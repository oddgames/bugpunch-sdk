# Bugpunch UI Automation — ActionExecutor

Orchestration layer above `Search` and `InputInjector`. `ActionExecutor` owns the public test API (`Click`, `Type`, `Swipe`, `Pinch`, `Rotate`, `Hold`, `Drag`, `Wait`, …), the JSON action dispatcher used by the Remote IDE, the AI test runner and the CLI bridge, and what every action shares: main-thread marshalling, START/COMPLETE logging, diagnostics screenshots and the post-action interval.

```
       test code / Remote IDE / AI runner / clibridge4unity
                   │
                   ▼
          ActionExecutor.*       ← public API + JSON dispatcher  (this doc)
                   │
        ┌──────────┴──────────┐
        ▼                     ▼
     Search                InputInjector       ← engine ( INPUT_INJECTION.md )
```

For the user-facing API (filters, methods, examples) see `REFERENCE.md`. For the JSON action protocol see `JSON_ACTIONS.md`. For the engine it drives see `INPUT_INJECTION.md`.

---

## Files

`ActionExecutor` is one `static partial class`:

| Partial | Owns |
|---|---|
| `ActionExecutor.cs` | Configuration (`DefaultSearchTime`, `Interval`, `PollInterval`, `DebugMode`, `ShowInputOverlay`, `BeforeAction`), button-click tracking, unobserved-exception capture, random seed. Also `StaticPath`, `SwipeDirection`, `ItemContainer`, `SimpleExploreResult`. |
| `ActionExecutor.Logging.cs` | `RunAction` / `ActionScope` — the scope every public action runs in. |
| `ActionExecutor.SearchShortcuts.cs` | `Name`, `Text`, `Type`, `Texture`, `Path`, `Tag`, `Any`, `Adjacent`, `Near`, `Reflect`. |
| `ActionExecutor.Pointers.cs` | `Click`, `ClickAt`, `DoubleClick(At)`, `TripleClick(At)`, `Hold(At)`, `Drag`, `DragTo`, `DragFromTo`. |
| `ActionExecutor.Gestures.cs` | `Swipe(At)`, `Pinch(At)`, `Rotate(At)`, `RotateAtPixels`, `TwoFingerSwipe(At)`. |
| `ActionExecutor.Input.cs` | `TextInput`, `Type`, `TypeText`, `PressKey`, `PressKeys`, `HoldKey(s)`, `KeyCodeToKey`, `CharToKey`. |
| `ActionExecutor.Scrolling.cs` | `Scroll`, `ScrollAt`, `ScrollTo`, `ScrollToAndClick`, `ClickSlider`, `DragSlider`, `SetSlider`, `SetScrollbar`. |
| `ActionExecutor.UI.cs` | `ClickDropdown`, `FindItems`, `RandomClick(Except)`, `AutoExplore*`. |
| `ActionExecutor.Find.cs` | `Find<T>`, `FindAll<T>`, search-failure messages. |
| `ActionExecutor.WaitMethods.cs` | `WaitFor(Search)`, `WaitFor(Search, text)`, `WaitForNot`, `WaitFor(path)`, `WaitFor<T>(path, expected)`, static-path resolver. |
| `ActionExecutor.WaitOperations.cs` | `Wait(seconds)`, `Wait(Search)`, `WaitFor(Func<bool>)`, `WaitFramerate`. |
| `ActionExecutor.SceneManagement.cs` | `LoadTestData`, `WaitForStableFrameRate`, `SceneChange`, `NavigateToMainMenu`. |
| `ActionExecutor.GameObject.cs` | `Disable`, `Enable`, `Freeze`, `Teleport`, `NoClip`, `Clip`. |
| `ActionExecutor.GetValue.cs` | `GetValue<T>(Search)`, `GetValue<T>(path)`, `Static(path)`. |
| `ActionExecutor.Diagnostics.cs` | `Screenshot`, `Snapshot`. |
| `ActionExecutor.Locators.cs` | `LocatorsFor` — suggested searches that resolve back to a GameObject. |
| `ActionParser.cs` | `Execute(json)` + the JSON dispatcher (`ActionJsonExecutor`). |

---

## The action scope

Every public action has the same shape:

```csharp
// doc-check: skip — RunAction is private; this is the internal pattern
public static async Task Click(Search search, float searchTime = -1f, int index = 0)
{
    await using var action = await RunAction($"Click({search})");
    var element = await search.Find(ResolveSearchTime(searchTime), index);
    if (element == null) { action.Fail(BuildSearchFailureMessage(search, searchTime)); return; }
    await InputInjector.InjectPointerTap(InputInjector.GetScreenPosition(element));
    action.SetResult($"'{element.name}'");
}
```

`RunAction(name)` returns an awaitable `ActionScope`:

1. **Awaiting it marshals to the main thread.** `Async.ToMainThread()` posts through the Unity `SynchronizationContext` captured at `BeforeSceneLoad`; on the main thread it waits one frame instead.
2. **START is logged** through `TestReport.LogAction`, tagged with the caller's file/line — the first stack frame outside `ODDGames.BugpunchSdk`, `System` and `Microsoft`. When a diagnostics session is active, START also queues a screenshot.
3. **The body runs** and reports through the scope:
   - `SetResult(text)` — queues a `<action>_success` screenshot and logs `COMPLETE: <action> -> text`.
   - `Warn(reason)` — logs `WARN`, does not throw.
   - `Fail(reason)` — marks the session failed, logs `FAILED` (with a failure screenshot) and throws `BugpunchTestFailureException("<action> failed: <reason>")`.
4. **Disposal** marshals back to the main thread, sleeps `Interval` ms (×`DebugIntervalMultiplier` in `DebugMode`), and logs a plain `COMPLETE` if the body neither set a result nor failed.

`await using` matters: disposal is where the interval and the closing log happen. Calling `Search.Find()` and the injector directly works but loses the START/COMPLETE trail, screenshots and failure reporting.

`TestReport` only writes a session folder (log, screenshots, hierarchy snapshots, and a screen recording started with the session) when `TestReport.StartSession` has been called — the PlayMode fixtures and `UISESSION` do this. Without a session, actions still log to the Unity console.

---

## Wait + retry policy

There is no per-action retry loop. `Search.Find()` already polls until the match set is stable for 3 consecutive frames, or times out. `ActionExecutor` only chooses the timeout:

| Caller | `DefaultSearchTime` |
|---|---|
| Test code, Remote IDE `/action`, AI runner | 10 s (default) |
| `clibridge4unity UIACTION` | 1 s — fail fast so the caller can retry. Set globally; it stays at 1 s for the Play session. |

Methods take `searchTime = -1` to mean "use `DefaultSearchTime`"; a few older overloads (`Swipe(search)`, `Pinch(search)`, `Rotate(search)`, `ScrollToAndClick`) default to a fixed value instead.

Timeouts use `Stopwatch`, not `Time.time`, so `timeScale = 0` doesn't freeze them. While a search is waiting, a diagnostics screenshot is captured every 2 s.

---

## Hooks and options

| Member | Purpose |
|---|---|
| `BeforeAction` | A `Func<string, Task>` awaited on the main thread at the start of every action scope (`RunAction`), before the START log and element search, receiving the action description. Nested scopes (e.g. the `Find<T>` inside `Swipe`) run it too. Actions the hook itself runs don't re-trigger it (`AsyncLocal` guard in `PrepareActionAsync`); an exception it throws fails the action. Typical use: dismiss popups. |
| `CaptureUnobservedExceptions` / `CapturedExceptions` / `ClearCapturedExceptions()` | Collect unobserved `Task` exceptions during a test instead of losing them. |
| `EnableButtonClickTracking()` / `LastClickedButton` | Adds an `onClick` listener to every `Button` to log which one actually fired. |
| `ShowInputOverlay` | Draws injected input (clicks, drags, scrolls, keys) on screen via `InputVisualizer`. |
| `SetRandomSeed(seed)` | Deterministic `RandomClick` / `AutoExplore`. |

---

## JSON action dispatcher

`ActionExecutor.Execute(string json)` (`ActionParser.cs`) parses a `JObject`, routes on the lower-cased `"action"` field to the matching public method, then always takes a `Screenshot()`.

```json
{
  "action": "click",
  "name": "btn_login",
  "path": "*/LoginPanel/*"
}
```

Verbs (`click`, `type`, `swipe`, `scroll`, `drag`, `dropdown`, `pinch`, `rotate`, `hold`, `key`, `waitfor`, …) and target fields (`text`, `name`, `type`, `tag`, `path`, `near`, `adjacent`, `any`, plus `at`) are documented in `JSON_ACTIONS.md`. Because each verb calls the public C# method, JSON actions run inside the same scope — same logging, screenshots and failure reporting. Normalized JSON coordinates are converted to screen pixels before calling the pixel-space methods (`DoubleClickAt`, `HoldAt`, `ScrollAt`, `DragFromTo`, `Drag(Vector2)`).

```csharp
public class ActionResult
{
    public bool Success;
    public string Action;          // the JSON that ran
    public string ScreenshotPath;  // post-action PNG in persistentDataPath
    public string Error;           // exception message on failure
    public float ElapsedMs;
}
```

Callers: the Remote IDE device route `POST /action` (returns `{ok, error?, elapsedMs}`) and the `UIACTION` bridge command.

---

## Threading rules

- `ActionExecutor.*` can be awaited from any thread — the scope marshals to the main thread.
- `Search.Find()` / `FindAll()` and the injector are **main thread only**; reach them through `ActionExecutor` unless you're already on the main thread.
- The main-thread context is captured at `RuntimeInitializeLoadType.BeforeSceneLoad`; calls made before that (e.g. from `[InitializeOnLoad]` static constructors) can't marshal.

---

## Gotchas

- **Skipping the scope.** Calling `Search.Find()` + injector methods directly loses START/COMPLETE logs, screenshots and failure reporting.
- **Nested public calls double-log.** A composite action built from other public actions opens one scope per call (e.g. `NavigateToMainMenu` logs every `Click` it makes). Expected, but noisy in reports.
- **`DefaultSearchTime` is global.** Anything that changes it (the bridge sets 1 s) affects every later action in the session.
- **Domain reload mid-action** — the injector re-enables hardware devices and sweeps orphaned virtual devices, but the in-flight action is lost.

---

## See also

- `REFERENCE.md` — public API + filter reference.
- `JSON_ACTIONS.md` — JSON action protocol.
- `INPUT_INJECTION.md` — `Search` + `InputInjector` engine internals.
