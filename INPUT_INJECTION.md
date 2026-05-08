# Bugpunch UITest — Search + InputInjector

Engine internals for the two layers below `ActionExecutor`: how a screen-target query resolves to a `GameObject`, and how synthetic input events reach Unity's `EventSystem`.

For the orchestration layer above (public API, scope/logging, JSON dispatcher) see `ACTION_EXECUTOR.md`. For the test-authoring API see `REFERENCE.md`. For the JSON action protocol see `JSON_ACTIONS.md`.

```
ActionExecutor (ACTION_EXECUTOR.md)
        │
        ▼
Search.Find()  →  GameObject  →  screen position  →  StateEvent  →  InputSystem.QueueEvent  →  EventSystem
   (Search.cs)                    (UIUtility.cs)     (InputInjector.cs)                       (Unity)
```

Two pieces:

| Layer | File(s) | Job |
|---|---|---|
| **Search** | `Search.cs` + `Search.Matching.cs` + `Search.Chainable.cs` + `Search.Spatial.cs` + `Search.OrderingFiltering.cs` | Find the right `GameObject` from a fluent query. |
| **InputInjector** | `InputInjector.cs` + `Input/InputInjector.{Mouse,Touch,Gesture}.cs` | Turn coords + duration into platform input events. |

---

## Search engine

### Query model

Every `Search` is a chained list of conditions over `GameObject`. Conditions are `Func<GameObject, bool>` plus a few spatial / structural filters that need the whole match set.

```csharp
Name("btn_*")            // name pattern (wildcards + |-OR)
    .Type<Button>()       // component type
    .Inside(Name("HUD"))  // ancestor query
    .Not.Disabled()       // negated state filter
    .Visible()            // on-screen + non-zero rect
    .RequiresReceiver()   // EventSystem raycast hits us
```

`Find()` (`Search.Matching.cs`) scans `Object.FindObjectsByType<Transform>()` once per check, filters through the condition list, then enforces:

- **On-screen filter** — corner-based `Rect` overlap with the screen bounds, zero-size rejected.
- **Stability** — match set must be identical for **3 consecutive frames** before returning. Animations, fade-ins, and post-load layouts all settle before we click them.
- **Depth ordering** — UI elements (have a `Canvas`) win ties; world-space objects rank by camera distance.
- **Diagnostic snapshots** — every 2s during a wait, dump a screenshot + matched-set summary to `TestReport`.

### Surface area

| Filter | Where | Notes |
|---|---|---|
| `Name`, `Text`, `Type`, `Tag`, `Texture`, `Path` | `Search.Chainable.cs` | Patterns: `*` wildcard, `|` OR. |
| `HasParent`, `HasChild`, `HasAncestor`, `Inside` | `Search.Chainable.cs` | Tree relations (with sub-`Search`). |
| `Adjacent`, `Near` | `Search.Spatial.cs` | Direction enum (`Left`/`Right`/`Above`/`Below`); distance scoring. |
| `RequiresReceiver` | `Search.Matching.cs` + `UIUtility.cs:129` | EventSystem raycast hit must include the candidate. |
| `Active`, `Visible`, `Interactable`, `Enabled` | `Search.Chainable.cs` | State filters. |
| `IncludeInactive`, `IncludeDisabled`, `IncludeOffScreen` | `Search.cs` | Scope opt-ins. |
| `Skip`, `Take`, `OrderBy`, `First`, `Last`, `At(index)` | `Search.OrderingFiltering.cs` | Set ops. |
| `Not` | prefix on any filter | Negation. |

### Canvas + camera handling

`UIUtility.GetScreenPosition()` (line 17–53) handles four cases:

1. **ScreenSpaceOverlay** — `RectTransform` corner → screen pixel directly.
2. **ScreenSpaceCamera** — `RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, ...)`. If `worldCamera` is null, falls back to `Camera.main`.
3. **WorldSpace canvas** — same pipeline as 3D world via the canvas's camera.
4. **3D world (no canvas)** — `Renderer` bounds or `Collider` bounds projected via `Camera.main`.

Mixing canvas types in one test is fine; the per-element resolution handles each correctly. Render textures need an explicit `canvas.worldCamera`.

### What Search can't see

- **IMGUI** (`OnGUI`) has no `Transform` and is not searchable.
- **Prefab Editor** finds objects but they may not render or receive events as expected — test in Play mode.
- **Occlusion** — there is no z-test culling. A button covered by another element is still found by name. Add `RequiresReceiver()` if you need topmost-only.
- **`EventSystem.current == null`** silently fails raycast queries. The injector logs a warning at setup if no EventSystem is in the scene.

---

## InputInjector

### Virtual devices

On `Setup()` (`InputInjector.cs:207`) the injector creates three virtual devices via the new Input System:

- `UIAutomation_Mouse` (`Mouse`)
- `UIAutomation_Keyboard` (`Keyboard`)
- `UIAutomation_Touchscreen` (`Touchscreen`)

Hardware devices are **disabled** while a test runs so the Editor doesn't double-process events. Settings snapshotted: `backgroundBehavior`, `editorInputBehaviorInPlayMode`. Restored on teardown.

Domain reload is hostile to virtual devices — `OnDomainReload()` (line 172) re-enables hardware and clears stale refs. `CleanupOrphanedVirtualDevices()` (line 380) sweeps any `UIAutomation_*` device left from a previous run.

Play mode exit is the other hazardous moment: the injector avoids **all** `InputSystem` API calls during `ExitingPlayMode` (line 297) because they trigger native asserts in some Unity versions.

### Mouse path (`InputInjector.Mouse.cs`)

A single `Click` is three events scheduled across **8 frames** total:

| Step | Event | Frame delay after |
|---|---|---|
| 1 | move to position (button=0) | 2 |
| 2 | press (button=1) | 4 |
| 3 | release (button=0) | 2 |

Each event is a full device snapshot via `StateEvent.From(mouse, out var ptr)` queued through `InputSystem.QueueEvent`. The leading move-to-position step exists because `EventSystem` will not raise pointer-enter/click on a stale position.

### Touch path (`InputInjector.Touch.cs`)

`TouchState { phase = Began, position, pressure = 1f, touchId }` queued via `InputSystem.QueueStateEvent(touchscreen, state)`. Multi-tap (double / triple) inserts a `Async.Delay(2 frames, 0.05s)` gap — both frame and wall-clock thresholds — to land inside the system double-click window.

`ShouldUseTouchInput()` (`InputInjector.Gesture.cs:24`) auto-routes to touch when running on iOS or Android, or when there is no Mouse but there is a Touchscreen.

### Gestures (`InputInjector.Gesture.cs` + `ActionExecutor.Gestures.cs`)

| Gesture | Implementation |
|---|---|
| **Swipe** | One pointer drag from start → end across `max(5, duration*60fps)` interpolated steps. |
| **Pinch** | Two symmetrical touches; offsets scale from `startOffset → startOffset * scale`. |
| **Rotate** | Two fingers orbit a center point by an angle. `fingerDistance` (0..1) sets radius. |
| **Two-finger swipe** | Two parallel touches translate together; `fingerSpacing` controls width. |

All gestures call `InputVisualizer.Record*` so the on-screen debug overlay shows where synthetic input went.

### Keyboard

`InputInjector.Type(string)` walks characters → key state events. Modifier keys (shift, ctrl) are pressed/released around the affected character. `ActionExecutor.Input.cs` exposes `Type`, `Press`, `KeyDown`, `KeyUp`.

### Frame stepping

Two helpers, both in `InputInjector.cs`:

- `Async.DelayFrames(n)` — yields until `Time.frameCount` advances by `n`.
- `Async.Delay(minFrames, minSeconds)` — both thresholds must clear (Stopwatch + frame count). Used wherever the injector needs real wall-clock spacing (multi-tap, animations).

Coroutines are not used — pure `Task` / `async` / `await` with frame-yield primitives.

---

## Recorder

`Recorder/RecordingSession.cs` is a thin facade over an `IRecorderBackend`:

- `Start()` begins capture.
- `StopAsync()` returns the output file path.
- `IsRecording` for status.

Backends:

- **Android** — native `MediaProjection` ring buffer (in `BugpunchPlugin.aar`).
- **iOS** — `VideoToolbox` ring buffer (`BugpunchRingRecorder.mm`).
- **Editor / Standalone** — `Texture2D.ReadPixels` readback per frame.

There is no replay engine. Recordings are diagnostic artifacts only — `TestReport` attaches them to failure bundles uploaded to the dashboard.

---

## Threading rules of thumb

- `Search.Find()` and `InputInjector.Inject*` are **main thread only** — Unity object access requires it.
- `Search.Matches()` (the predicate evaluator) is also main-thread for the same reason.
- The orchestration layer (`ActionExecutor`) marshals for you — see `ACTION_EXECUTOR.md`. Drive `Search` / `InputInjector` directly only when you've already ensured main thread.
- Don't call into the SDK from a `[InitializeOnLoad]` static ctor — main thread isn't established yet.

---

## Gotchas

- **IMGUI** — invisible to Search.
- **`EventSystem.current == null`** — raycast filters silently return nothing. Drop a default EventSystem in the scene.
- **Render texture cameras** — set `canvas.worldCamera` explicitly; `Camera.main` fallback may not match.
- **Camera-overlay vs World-space canvas** — coords are computed per-element so mixing is fine, but a shared screen-position assumption isn't.
- **Occluded UI** — found by name; not blocked. Add `RequiresReceiver()` for topmost-only.
- **Domain reload mid-test** — the injector handles it (`OnDomainReload` re-enables hardware, sweeps orphaned virtual devices), but tests will fail mid-flight.
- **Double-click windows** — system threshold ~300ms; the injector enforces a minimum gap, but app-level handlers may need tighter timing.
- **`Time.timeScale == 0`** — pause screens don't break injection (Stopwatch-based timing) but Unity animations / Animator state machines that gate UI on time will. Use unscaled-time UI or bump the scale.

---

## File map

| Area | Path |
|---|---|
| Search | `Search.cs` + partials (`.Matching.cs`, `.Chainable.cs`, `.Spatial.cs`, `.OrderingFiltering.cs`) |
| Injection | `InputInjector.cs` + `Input/InputInjector.{Mouse,Touch,Gesture}.cs` |
| Geometry / hits | `UIUtility.cs` |
| Recorder facade | `Recorder/RecordingSession.cs` |

## See also

- `ACTION_EXECUTOR.md` — orchestration layer above this engine (public API, scope/logging, JSON dispatcher).
- `REFERENCE.md` — user-facing test-authoring API.
- `JSON_ACTIONS.md` — JSON action protocol.
