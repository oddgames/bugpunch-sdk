# Bugpunch UI Automation — Search + InputInjector

Engine internals for the two layers below `ActionExecutor`: how a screen-target query resolves to a `GameObject`, and how synthetic input events reach Unity's `EventSystem`.

For the orchestration layer above (public API, scope/logging, JSON dispatcher) see `ACTION_EXECUTOR.md`. For the test-authoring API see `REFERENCE.md`. For the JSON action protocol see `JSON_ACTIONS.md`.

```
ActionExecutor (ACTION_EXECUTOR.md)
        │
        ▼
Search.Find()  →  GameObject  →  screen position  →  StateEvent  →  InputSystem.QueueEvent  →  EventSystem
   (Search.cs)                    (UIUtility.cs)     (InputInjector.cs)                       (Unity)
```

| Layer | Files | Job |
|---|---|---|
| **Search** (public) | `Search.cs` + partials `.Chainable`, `.Spatial`, `.OrderingFiltering`, `.Matching`, `.Helpers`, `.GameObject`, `.StaticPath`, `.Inverse`; `SearchCandidateCache.cs` | Find the right `GameObject` from a fluent query. |
| **InputInjector** (internal) | `InputInjector.cs` + `Input/InputInjector.{Mouse,Touch,Gesture,Keyboard}.cs` | Turn screen coordinates + durations into Input System events. |

`InputInjector` is `internal` to the SDK assembly — game and test code drive it through `ActionExecutor`. `UIUtility` (screen positions, raycast hits) is public.

---

## Search engine

### Query model

A `Search` is a list of conditions (`Func<GameObject, bool>`) plus optional post-processing (ordering, `First`/`Skip`/`Take`, spatial scoring) that needs the whole match set.

```csharp
var close = Name("btn_close*|btn_x")       // wildcard + |-OR, case-insensitive
    .Type<Button>()                         // has component
    .HasAncestor(Name("HUD"))               // tree relation with a sub-Search
    .Not.Name("*_disabled")                 // Not negates the next filter
    .Interactable()                         // Selectable.interactable
    .Visible()                              // centre inside screen + ScrollRect viewport
    .RequiresReceiver(typeof(UnityEngine.EventSystems.IPointerClickHandler));
```

`Find()` (`Search.Matching.cs`) works like this:

- **Candidates** come from `SearchCandidateCache`, not a fresh `FindObjectsByType` per check. Discovery is incremental (a budget of 2048 objects per frame, held by weak reference) and re-runs at most every 0.25 s. A search that finds nothing asks for rediscovery.
- **On-screen filter** — screen bounds must be non-zero and overlap the screen. Partially visible objects count. `IncludeOffScreen()` turns this off.
- **Stability** — the match set must be identical for **3 consecutive frames** before `Find` returns, so animations, fade-ins and post-load layouts settle first.
- **Default order** — by depth: UI by root canvas `sortingOrder` (later siblings first), world objects by `Camera.main` distance, with UI winning ties. Any explicit ordering (`OrderBy*`, `Near`, `Randomize`, …) replaces this.
- **Polling** — every frame while there are candidates, otherwise every 100 ms, until the timeout (Stopwatch time, so `timeScale = 0` doesn't stall it).
- **Diagnostic snapshots** — every 2 s during a wait, `TestReport.CaptureSearchSnapshot` records the search and its partial matches when a session is active.

### Surface area

| Filters | Partial | Notes |
|---|---|---|
| `Name`, `Text`, `Type`/`Type<T>`, `Texture`, `Path`, `Tag`, `Any`, `With<T>`, `Where` | `.Chainable` | Patterns: `*` wildcard, `\|` OR, case-insensitive. `Text` checks only the element's own TMP/legacy `Text`. `Tag` is exact (`CompareTag`). |
| `HasParent`, `HasAncestor`, `HasChild`, `HasDescendant`, `HasSibling` | `.Chainable` | Take a sub-`Search` or a name pattern. |
| `GetParent<T>`, `GetChild<T>` | `.Chainable` | Filters: keep elements that have a `T` ancestor / descendant. |
| `InRegion` | `.Chainable` | Screen-region filter. |
| `Adjacent(text, Direction)`, `Near(text, Direction?)` | `.Spatial` | `Direction` is `Right`/`Below`/`Left`/`Above`. These replace the query's target. |
| `Visible`, `Interactable`, `RequiresReceiver(types)` | `.OrderingFiltering` | State filters. |
| `First`, `Last`, `Skip`, `Take`, `OrderBy<T>`, `OrderByDescending<T>`, `OrderByPosition`, `OrderByName(Descending)`, `OrderByInstanceId(Descending)`, `Randomize` | `.OrderingFiltering` | Post-processing over the match set. |
| `Or`, `GetParent()`, `GetChild(int)`, `GetSibling(int)` | `.OrderingFiltering` | Combine or navigate from the result. |
| `IncludeInactive`, `IncludeOffScreen` | `Search.cs` | Scope opt-ins. Non-interactable elements always match; filter them with `Interactable()`. |
| `Not` | prefix property | Negates the next filter. |

`RequiresReceiver` raycasts (`EventSystem.RaycastAll`) at the candidate's screen position and passes if any hit object has one of the given handler components. It is a "something clickable is here" check, not a "this candidate is on top" check.

### Canvas + camera handling

`UIUtility.GetScreenPosition()` handles:

1. **ScreenSpaceOverlay** — centre of the `RectTransform`'s world corners, already in screen pixels.
2. **ScreenSpaceCamera / WorldSpace canvas** — `RectTransformUtility.WorldToScreenPoint(canvas.worldCamera ?? Camera.main, centre)`.
3. **No canvas** — `Renderer` bounds centre, else `Collider` bounds centre, else `transform.position`, projected through `Camera.main`.

Positions are computed per element, so mixing canvas types in one test is fine. Cameras that render into a RenderTexture need an explicit `canvas.worldCamera`.

### What Search can't see

- **IMGUI** (`OnGUI`) has no `Transform` and can't be searched.
- **Occlusion** — there is no z-test. A covered button is still found by name; the tap lands on whatever is on top.
- **`EventSystem.current == null`** — raycasts return nothing, so `RequiresReceiver` never matches and clicks reach nothing. No warning is logged.

---

## InputInjector

### Virtual devices

`InputInjector.Setup()` (called by the PlayMode fixtures and the AI runner):

- snapshots `InputSettings.backgroundBehavior` and `editorInputBehaviorInPlayMode`, then sets `IgnoreFocus` and `AllDeviceInputAlwaysGoesToGameView`, so injection works when the Game view isn't focused;
- adds `UIAutomation_Mouse`, `UIAutomation_Keyboard` and `UIAutomation_Touchscreen`;
- disables hardware devices so real input doesn't interfere.

Without `Setup()`, each virtual device is created lazily the first time an action needs it, and hardware stays enabled. Teardown restores the settings and re-enables hardware.

Domain reload is hostile to virtual devices. `OnDomainReload()` re-enables hardware and clears stale references, and `CleanupOrphanedVirtualDevices()` removes `UIAutomation_*` devices left from a previous run. During `ExitingPlayMode` the injector makes no `InputSystem` calls, because they trigger native asserts in some Unity versions.

### Mouse vs touch

`ShouldUseTouchInput()` picks the path. It is always touch in iOS/Android builds (compile-time). Elsewhere it is touch only when `Mouse.current == null` and `Touchscreen.current != null`.

| Path | Tap sequence |
|---|---|
| **Mouse** | move to position (wait 2 frames) → press (4 frames) → release (2 frames). The leading move makes `EventSystem` set up pointer-enter before the click. |
| **Touch** | `Began` → 4 frames → `Ended` → 2 frames. |

Double/triple taps put `Async.Delay(2 frames, 0.05 s)` between taps (both thresholds must pass) so they land inside the double-click window.

Each event is a full device snapshot (`StateEvent.From` / `QueueStateEvent`) queued through `InputSystem`.

### Drags and gestures

| Gesture | Implementation |
|---|---|
| **Drag / swipe** | Short settle, press, hold `holdTime`, then interpolate start→end over **at least 10 frames** or `duration` (Stopwatch), whichever is longer. Uses touch or mouse per `ShouldUseTouchInput`. |
| **Swipe on an element** (`ActionExecutor.Swipe(search, …)`) | Goes through `InputInjector.Swipe(GameObject, …)`, which **always injects a mouse drag**, even on touch devices. Swipe by coordinates (`SwipeAt`) uses the touch/mouse routing above. |
| **Pinch** | Two touches about a centre; spacing scales from start to `start × scale` over `max(5, duration × 60)` frames. The element overload needs a `RectTransform`. |
| **Rotate** | Two touches orbit the centre by `degrees`; `fingerDistance` (normalized) or `radiusPixels` sets the radius. |
| **Two-finger swipe** | Two parallel touches translate together; `fingerSpacing` sets their gap. |

Every injection is reported to `InputVisualizer`, so the debug overlay (`ActionExecutor.ShowInputOverlay`) shows where synthetic input went.

### Keyboard (`Input/InputInjector.Keyboard.cs`)

- `PressKey(Key)` queues a `KeyboardState` press, also sends a legacy-Input key event so `Input.GetKeyDown` sees it, waits 3 frames, then releases.
- `HoldKey` / `HoldKeys` keep the key(s) down for a duration. `PressKeyWithModifier`, `SelectAllText` (Ctrl+A) and `DeleteAllText` build on these.
- `TypeText(string)` queues one `InputSystem.QueueTextEvent` per character, one per frame. That reaches `Keyboard.onTextInput` listeners, **not** `TMP_InputField`.
- `TypeIntoField(go, text, clearFirst, pressEnter)` taps the field, then sets `TMP_InputField.text` / `InputField.text` directly (replace or append), re-activates the field, moves the caret to the end and optionally presses Enter. `ActionExecutor.TextInput` / `Type` use this path.

### Frame stepping

`Async` (in `InputInjector.cs`) provides the primitives, all `Task`-based (no coroutines):

- `Async.ToMainThread()` — post to the Unity `SynchronizationContext`; on the main thread, wait one frame.
- `Async.DelayFrames(n)` — wait until `Time.frameCount` advances by `n`.
- `Async.Delay(minFrames, minSeconds)` — wait until both the frame count and the Stopwatch time have passed.

---

## Recorder

`ODDGames.Recorder.MediaRecorder.StartAsync(RecorderSettings)` returns a `RecordingSession` (`IsRecording`, `StopAsync()` → output path, `Dispose()`). The backend is chosen by platform:

| Platform | Backend |
|---|---|
| Editor | Unity Recorder (`com.unity.recorder`) via `EditorRecorder` in the Editor assembly. |
| Android player | Native `MediaCodecBridge` (`au.com.oddgames.recorder`). |
| Windows / macOS player | Native `ODDRecorder` plugin. |
| iOS player | Not supported — no-op backend. |
| Anything else | No-op backend, with a warning. |

`TestReport.StartSession` starts a recording automatically (1280×720, 15 fps) and attaches it to the session report. There is no replay engine; recordings are diagnostic artifacts only.

This test recorder is separate from the crash-reporting video ring buffer, which is native-owned on Android/iOS (see `README.md`).

---

## Threading rules

- `Search.Find()` / `Matches()` and all injection are **main thread only**.
- `ActionExecutor` marshals for you (see `ACTION_EXECUTOR.md`). Call `Search` directly only when you're already on the main thread.
- The main-thread context is captured at `BeforeSceneLoad`; don't drive the SDK from an `[InitializeOnLoad]` static constructor.

---

## Gotchas

- **IMGUI** — invisible to Search.
- **No EventSystem** — raycast-based checks silently fail. Make sure the scene has one.
- **RenderTexture cameras** — set `canvas.worldCamera` explicitly; the `Camera.main` fallback may not match.
- **Occluded UI** — still found by name; the tap goes to whatever is on top.
- **TMP input** — `TypeText` won't reach `TMP_InputField`; use `TextInput` / `Type` on the field.
- **Element swipes on mobile** — `Swipe(search, …)` injects a mouse drag; use `SwipeAt` for a real touch swipe.
- **Domain reload mid-test** — the injector recovers its devices, but the running test fails.
- **`Time.timeScale == 0`** — injection timing is Stopwatch-based and keeps working, but UI that animates on scaled time won't.

---

## File map

| Area | Path (under `csharp-src/Bugpunch/Sources/`) |
|---|---|
| Search | `Search.cs` + `Search.*.cs` partials, `SearchCandidateCache.cs` |
| Injection | `InputInjector.cs` + `Input/InputInjector.{Mouse,Touch,Gesture,Keyboard}.cs` |
| Overlay | `InputVisualizer.cs` |
| Geometry / hits | `UIUtility.cs` |
| Recorder | `Recorder/MediaRecorder.cs`, `Recorder/RecordingSession.cs`, `Recorder/Internal/` |

## See also

- `ACTION_EXECUTOR.md` — orchestration layer above this engine.
- `REFERENCE.md` — user-facing test-authoring API.
- `JSON_ACTIONS.md` — JSON action protocol.
