# Bugpunch UITest — ActionExecutor

Orchestration layer above `Search` and `InputInjector`. Owns the public test API (`Click`, `Tap`, `Type`, `Swipe`, `Pinch`, `Rotate`, `Hold`, `Drag`, `Wait`, ...), the JSON action dispatcher used by the dashboard and AI test generator, and the cross-cutting concerns every action shares: main-thread marshalling, logging, screenshots, recorder hand-off, retry policy, lifecycle hooks.

Sits between user code and the engine:

```
       user / dashboard / AI / agent
                   │
                   ▼
          ActionExecutor.*       ← public API + JSON dispatcher  (this doc)
                   │
        ┌──────────┴──────────┐
        ▼                     ▼
     Search                InputInjector       ← engine ( INPUT_INJECTION.md )
```

For the user-facing API surface (filters, methods, examples) see `REFERENCE.md`. For the JSON action protocol see `JSON_ACTIONS.md`. For the engine internals it drives see `INPUT_INJECTION.md`.

---

## Files

`ActionExecutor` is split across partial classes:

| Partial | Owns |
|---|---|
| `ActionExecutor.cs` | Constants, defaults (`DefaultSearchTime`, `Interval`), config, hook registration. |
| `ActionExecutor.Pointers.cs` | `Click`, `Tap`, `DoubleTap`, `LongPress`, `Hold`, `Drag`. |
| `ActionExecutor.Gestures.cs` | `Swipe`, `Pinch`, `Rotate`, two-finger swipe. |
| `ActionExecutor.Input.cs` | `Type`, `Press`, `KeyDown`, `KeyUp`, `Clear`. |
| `ActionExecutor.Find.cs` | `Find<T>`, `FindAll<T>`, `Wait`, existence assertions. |
| `ActionExecutor.Logging.cs` | `RunAction`, `ActionScope`, `ActionScopeInner` — every public action goes through here. |
| `ActionParser.cs` | JSON dispatcher → routes to the partials above. |

---

## The action scope

Every public action follows the same shape:

```csharp
public async Task Click(Search search) {
    await using (await RunAction(scope => /* search.Find + injector.Tap */)) { ... }
}
```

`RunAction()` (`ActionExecutor.Logging.cs:44`) returns an `ActionScope`. The scope's awaiter:

1. **Marshals to main thread.** `Async.ToMainThread()` checks the captured `SynchronizationContext` from `[RuntimeInitializeOnLoadMethod]`; off-thread callers post; main-thread callers yield one frame.
2. **Logs START.** `TestReport.LogAction(START, ...)` with caller stack trace truncated at the first frame outside `ODDGames.Bugpunch`.
3. **Fires `BeforeAction`.** Hook (`ActionExecutor.cs:303`) — register a callback to dismiss popups, check preconditions, etc., before every action.
4. **Runs the body.**
5. On success: `TestReport.CaptureScreenshot()` + `LogAction(COMPLETE)`.
6. On failure: `TestReport.RecordFailure()` with screenshot + recorder video (if a `RecordingSession` is running) + scene name + matched-element snapshot.
7. **Interval delay** (default 50ms, `Interval` property) before yielding back.

This is why `await using` matters — disposal is where logging + screenshot + interval happen. Bypass the scope and you lose the diagnostics.

---

## Wait + retry policy

There is no per-action retry loop. `Search.Find()` already polls until match-stable (3 consecutive identical frames) and times out. `ActionExecutor` only sets the timeout:

| Mode | `DefaultSearchTime` | When |
|---|---|---|
| Normal | 10s | Test code, JSON actions. |
| Interactive | 1s | Bridge / live REPL — fail fast so the user can iterate. |

Timeouts use `Stopwatch`, not `Time.time`, so `timeScale = 0` (paused game, slow-motion) does not freeze them.

`Wait(Search, seconds)` and `WaitFor*` overloads in `ActionExecutor.Find.cs` are the explicit-wait API — same scope wrapper, same screenshot cadence as actions.

---

## Hooks

| Hook | Where | Purpose |
|---|---|---|
| `BeforeAction` | `ActionExecutor.cs:303` | Fires inside the scope before every action body. Use for popup dismissal, preconditions, light assertions. Multi-cast supported. |
| `RecordingSession` | `Recorder/RecordingSession.cs` | If a session is active when an action fails, the recording is attached to the failure bundle. Start/stop from your test setup; the executor doesn't manage the session itself. |
| `TestReport` | `TestReport.cs` | Static sink for log entries, screenshots, failure bundles. The dashboard reads these. |

---

## JSON action dispatcher

`ActionParser.Execute(string json)` (`ActionParser.cs:34`) parses a `JObject` and routes through `ActionJsonExecutor` to the matching partial.

```jsonc
{
  "action": "click",
  "name": "btn_login",      // filter sub-language
  "inside": { "name": "LoginPanel" }
}
```

Action grammar (`click`, `tap`, `type`, `swipe`, `scroll`, `drag`, `dropdown`, `pinch`, `rotate`, `hold`, `key`, `wait`, ...) and filter sub-language (`name`, `text`, `type`, `at`, `adjacent`, `inside`, ...) are documented in `JSON_ACTIONS.md`. Each JSON action is wrapped in the same `RunAction` scope as the C# API — same logging, same screenshot, same `BeforeAction`. Result:

```csharp
public sealed class ActionResult {
    public bool   Success;
    public string Error;        // exception message + truncated stack on failure
    public long   ElapsedMs;
    public string ScreenshotPath;
}
```

This is the hot path for AI-generated tests, dashboard "Run Action" buttons, the agent-controlled test runner, and the chat/IDE "drive the device" surface.

---

## Public surface

For the full API + filter reference see `REFERENCE.md`. Short list of what the partials expose:

| Partial | Main methods |
|---|---|
| `.Pointers` | `Click`, `Tap`, `DoubleTap`, `LongPress`, `Hold`, `Drag`, `DragTo`. |
| `.Gestures` | `Swipe`, `SwipeFrom`, `Pinch`, `PinchAt`, `Rotate`, `TwoFingerSwipe`. |
| `.Input` | `Type`, `TypeInto`, `Press`, `KeyDown`, `KeyUp`, `Clear`, `ClearAndType`. |
| `.Find` | `Find<T>`, `FindAll<T>`, `Exists`, `DoesNotExist`, `Wait`, `WaitForGone`. |

All methods are `async Task` and accept either a `Search` or a string shorthand (which builds `Name(...)` or `Text(...)` depending on context).

---

## Threading rules

- `ActionExecutor.*` is safe to `await` from any thread. The scope marshals.
- `Search.Find()` and `InputInjector.Inject*` directly are **main thread only** — the scope is the only sanctioned entry.
- Don't call into the SDK from `[InitializeOnLoad]` static constructors — main thread isn't established yet.
- `BeforeAction` callbacks run on the main thread inside the scope. Block too long and the action's frame budget shifts.

---

## Gotchas

- **Skipping the scope.** Calling `Search.Find()` + `InputInjector.Tap()` directly works but loses screenshots, START/COMPLETE logs, recorder linkage, and `BeforeAction`. Don't. Use `RunAction` if you need a custom action.
- **Long `BeforeAction` callbacks** delay every single action. If the callback does network or scene scans, gate it.
- **Multiple `RunAction` scopes nested in one action** double-log and double-screenshot. Build composite actions inside one scope, not by calling other public methods that each open their own scope.
- **`TestReport` is a static sink.** In test runs it pipes to disk + dashboard upload. In ad-hoc bridge use it just logs to console. Behaviour depends on whether a `TestRun` is active.
- **Domain reload mid-action** — `InputInjector` recovers, but the in-flight action will throw. Tests fail mid-flight; that's by design.

---

## See also

- `REFERENCE.md` — public API + filter reference.
- `JSON_ACTIONS.md` — JSON action protocol.
- `INPUT_INJECTION.md` — `Search` + `InputInjector` engine internals (what `ActionExecutor` drives).
