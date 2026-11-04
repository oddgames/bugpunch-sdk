# Bugpunch — Unity SDK

[![bugpunch.com](https://img.shields.io/badge/dashboard-bugpunch.com-2563eb)](https://bugpunch.com)
[![docs](https://img.shields.io/badge/docs-bugpunch.com%2Fdocs-f97316)](https://bugpunch.com/docs)

Crash reporting, bug capture, analytics and remote debugging for Unity
games. A drop-in UPM package with three platform lanes (Android Java + NDK,
iOS Obj-C++, C# for Editor + Standalone) that pairs with the Bugpunch
dashboard at [bugpunch.com](https://bugpunch.com).

📖 **Full documentation:** [bugpunch.com/docs](https://bugpunch.com/docs) —
getting started, the complete [API reference](https://bugpunch.com/docs#api-reference),
[configuration](https://bugpunch.com/docs#configuration), crash reporting,
the Remote IDE, and more.

## What it gives you

- **Crash reports** — native signal handlers (SIGSEGV / SIGABRT / SIGBUS /
  SIGFPE / SIGILL) on Android and iOS, Mach exceptions on iOS, and an ANR /
  main-thread-stall watchdog. These survive a dying Mono runtime: reports
  are queued to disk natively and retried on the next launch.
- **Exception reports** — managed C# exceptions caught through
  `Application.logMessageReceivedThreaded`, `AppDomain.UnhandledException`
  and `TaskScheduler.UnobservedTaskException`, and sent through the same
  upload queue.
- **Symbolication** — at build time the Editor uploads native symbols
  and IL2CPP method maps to the server, so native and IL2CPP frames resolve
  to C# methods.
- **Bug reports + feedback** — from the in-app debug widget, `Bugpunch.Report`,
  F12 in the Editor and desktop builds, or a shake / system-screenshot
  trigger on device. Each report carries a screenshot, scene, recent logs,
  tags and custom data, plus the gameplay video buffer when recording is
  on.
- **Remote IDE** — connect from the dashboard to a running build and drive
  it live: scene hierarchy, inspector, console stream, WebRTC game-view
  stream with touch/mouse input, C# script execution (needs the optional
  `au.com.oddgames.scripting` package — see below), file browser, data
  snapshots, memory snapshots and device info.
- **Performance** — FPS, memory, thermal and OOM tracking per build, with
  the dashboard flagging builds that breach thresholds.
- **Analytics + QA** — events, purchases, ads, progression, tags, QA
  goals and coverage, and server-driven game config variables.
- **UI test automation** — `ActionExecutor` (see below).

## Install

Add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "au.com.oddgames.bugpunch": "https://github.com/oddgames/bugpunch-sdk.git#v0.8.227"
  }
}
```

The repo is public, so you don't need a token or a scoped registry.

**Requirements:** Unity **6000.0** or newer. The package depends on
`com.unity.webrtc` 3.0.0, `com.unity.inputsystem` 1.7.0,
`com.unity.textmeshpro` 3.0.0, `com.unity.recorder` 5.0.0,
`com.unity.nuget.newtonsoft-json` 3.2.1, `com.unity.nuget.mono-cecil`
1.11.6 and `com.unity.ext.nunit` 2.0.5. All of these resolve from the Unity
Registry.

The Remote IDE's live video stream is the C# `WebRTCStreamer`, built on
`com.unity.webrtc`. It is the same on every lane, so there's no extra
native WebRTC library to vendor.

## Configure

1. Sign up at [bugpunch.com](https://bugpunch.com), create a project and
   copy its API key.
2. Importing the package creates `Assets/Resources/BugpunchConfig.asset`
   (or make one with **Create → ODD Games → Bugpunch Config**; it must
   live in a `Resources/` folder named `BugpunchConfig`). Paste the API
   key into it. The server URL defaults to `https://api.bugpunch.com`, and
   **Start Mode** (`Automatic` / `Forced` / `Disabled`) controls whether
   the SDK boots.
3. Build and run. Reports start flowing.

That's the whole integration. There's no `MonoBehaviour` to add and no
`Init()` call; the SDK boots itself after the first scene loads.

In the **Editor** Bugpunch is off until you turn on **Enable Bugpunch** in
the toolbar. When it's on, manual reports and the Remote IDE work, and
automatic error reports from the Editor are suppressed.

Dev builds also open a local Remote IDE server (port 47701+) so
[clibridge4unity](https://github.com/oddgames/clibridge4unity) can drive
the build over `adb forward` / `iproxy` without the cloud server. Release
builds never open it.

## Public API

```csharp
using System.Collections.Generic;
using ODDGames.BugpunchSdk;

// File a report from code. Silent by default: nothing appears in front of the
// player, it groups by title and has no cooldown. Safe from any thread. Pass
// type: "bug" only for a report the player asked to file: on device it opens
// the interactive report form. type: "exception" shares the automatic exception
// cooldown, so a repeat title inside it is dropped unless it carries attachments.
Bugpunch.Report("Player got stuck on level 3 boss", "Fell through the arena floor");
Bugpunch.Feedback("Loved the new gesture controls!");

// Attach files to ONE report — copied at the call, always uploaded (never
// sampled). A missing path shows on the report as skipped, not an error.
Bugpunch.Report("Save failed to load", "Slot 2 checksum mismatch", attachments: new[] {
    BugpunchAttachment.Path("[PersistentDataPath]/save.json"),
    BugpunchAttachment.Path("[PersistentDataPath]/profile/"),            // folder → profile.zip
    BugpunchAttachment.Path("[PersistentDataPath]/logs/*.txt", zip: true),
    BugpunchAttachment.Text("state.json", "{\"slot\":2}"),
});

// Tags for one report, and a hook over everything C# sends.
Bugpunch.Report("Save failed to load", "Slot 2", "error", null,
    new Dictionary<string, string> { { "phase_chain", "Garage>Paint" } });
Bugpunch.BeforeSend = report => report.Tags["level"] = "world1";

// A breadcrumb can carry key/value data; it stays with the breadcrumb on the report.
Bugpunch.Breadcrumb("store", "Purchase begin", new Dictionary<string, string> { { "sku", "coins_100" } });

// One-button entry for testers / players: Ask A Question (chat),
// Record A Bug (debug mode + recording), Request A Feature (feedback board).
Bugpunch.RequestHelp();
Bugpunch.RequestFeature();          // straight to the feedback board

// Hands-free reporting on device: shake, or take a system screenshot.
Bugpunch.SetReportTriggers(shake: true, screenshot: true);

// Context on every subsequent report.
Bugpunch.SetTag("playerLevel", 47);
Bugpunch.SetTag("subscription", "pro");
Bugpunch.Breadcrumb("flow", "Entered checkout");
Bugpunch.Trace("boss_spawned");

// Server-resolved config variable (per-device overrides supported).
// Typed getters: GetVariable (string), GetVariableBool / GetVariableInt / GetVariableFloat.
var spawnRate = Bugpunch.GetVariableFloat("spawnRate", 1.0f);

// Analytics.
Bugpunch.TrackEvent("tutorial_skipped");
Bugpunch.LogPurchase("coins_500", 4.99, "USD", "txn-123");
Bugpunch.LogScreen("Shop");

// Correlate several clients (co-op room, match) into one linked session.
Bugpunch.JoinLinkedSession("room-8841", new Dictionary<string, string> { ["role"] = "host" });

// Allow crash reports / "request more info" to pull matching files.
Bugpunch.AddAttachmentRule("saves", "[PersistentDataPath]/saves", "*.json", 512 * 1024);

UnityEngine.Debug.Log($"Bugpunch {Bugpunch.SdkVersion}, session {Bugpunch.SessionId}");
```

Most of the SDK runs on its own once configured. The rest of the surface
(`LogAd`, `LogResource`, `LogProgression`, `SetUserId`, `SetContext`,
memory and auth APIs, …) is in the
[API reference](https://bugpunch.com/docs#api-reference).

## QA Goals

The dashboard's **QA Goals** page (`/qa-goals`) shows a grid: rows are
goals, columns are platforms (Android, iOS, Steam, Editor, …), and each
cell shows % progress for that platform plus hit count and first/last
hitter.

Goals are declared in code with `Bugpunch.Goal(...)`. A build-time Cecil
scanner extracts the literal labels, so the dashboard sees every goal
before any tester runs. Labels and values must be string or numeric
literals for the scanner to find them.

```csharp
// Evaluator polled every ~10s, on scene change and on app focus.
// Fires and latches when the value matches.
Bugpunch.Goal("Reach level 5", 5, () => currentLevel);

// Mark a code path as exercised during a QA pass.
Bugpunch.MarkCoverage("tutorial_completed");

// Record a categorized value the dashboard aggregates across testers.
Bugpunch.RecordValue("weapon_used", "plasma_rifle");
```

See the [QA Goals guide](https://bugpunch.com/docs#analytics-goals) for
every `Goal` overload (including the fluent `Bugpunch.Goal("x").IsTrue()`
form), `AssertCoverage` and coverage tracking.

### Build identity

Every event carries a build hash: the SHA-1 of
`version|commit|builtAtUtc`, truncated to 12 hex characters and stamped at
build time. The QA Goals build picker shows `version · hash · date`, so two
builds with the same `Application.version` get separate entries.

## Supported targets

| Platform | Lane | Crash capture | Notes |
|---|---|---|---|
| Android player | Java + NDK | POSIX signals, ANR watchdog, managed exceptions | Native AAR shipped pre-built |
| iOS player | Obj-C++ | Mach exceptions, signals, hang watchdog, managed exceptions | xcframework shipped pre-built |
| Standalone (Win/Mac/Linux) | C# | Managed exceptions | No native crash handler |
| Nintendo Switch | C# (managed lane) | Managed exceptions | |
| Unity Editor | C# | Manual reports only | Off until the toolbar **Enable Bugpunch** toggle is on |

## Optional features

### Gameplay video buffer

On iOS and Android, the SDK can keep a rolling video buffer (default: the
last 90 seconds) that is attached to the next crash, ANR or bug report. That
way triage sees what led up to the failure.

Recording starts in one of two ways:

- **With consent:** a tester picks **Record A Bug** from
  `Bugpunch.RequestHelp()`, and a native consent sheet asks before
  anything records.
- **Without a prompt:** `Bugpunch.StartRecording()` / `StopRecording()`.
  Use this only in builds you control (e.g.
  `if (Debug.isDebugBuild) Bugpunch.StartRecording();`), because every
  player of a build containing the call is recorded.

Capture reads the game's own GPU output, so it needs no Android permission,
no foreground service and no MediaProjection prompt. `Bugpunch.IsRecording`
reports whether the buffer is running. Recording is a no-op in the Editor
and on Standalone.

### In-app purchases

If your game uses Unity Purchasing (`com.unity.purchasing` 4.x or 5.x), the
optional `ODDGames.Bugpunch.Purchasing` assembly compiles in automatically:

```csharp
// doc-check: skip — needs com.unity.purchasing, which the checker doesn't reference
using UnityEngine.Purchasing;
using ODDGames.BugpunchSdk;

// IAP 5.x
m_PurchaseService = UnityIAPServices.DefaultPurchase().WithBugpunch();
m_ProductService  = UnityIAPServices.DefaultProduct().WithBugpunch();

// IAP 4.x — in IStoreListener.OnInitialized, then BugpunchPurchasing.LogPurchase(args)
// from ProcessPurchase
controller.WithBugpunch();
```

`WithBugpunch()` declares the catalog SKUs for coverage and logs purchases
to analytics, and your own handlers run unchanged.
`WithBugpunchBlocking()` also adds a QA "IAP" debug tool that lets
internal testers complete or fail sandbox purchases without paying.

For raw StoreKit or Google Play Billing, call
`Bugpunch.LogPurchase(sku, price, currency, transactionId)` from your own
purchase handler.

Google Mobile Ads (`com.google.ads.mobile` 9+) gets a matching shim:
`rewardedAd.WithBugpunch(placement: "level_end")`.

### Scripting

Bugpunch's script features — the Remote IDE script panel, `script`
attachment rules, server debug-tool scripts and crash-directive scripts —
run on the separate scripting package. Without it they answer
"Scripting not installed". To turn them on, add it next to Bugpunch:

```json
{
  "dependencies": {
    "au.com.oddgames.scripting": "https://github.com/oddgames/scripting-sdk.git#v1.0.0"
  }
}
```

The optional `ODDGames.Bugpunch.Scripting` assembly then compiles in
automatically. See the scripting package's README for the language and
sandbox.

### Debug Tools

A panel of buttons, switches, sliders and fields for testers, opened from the
Bugpunch pill's overflow menu. Tools come from three places: attributes on
static members, code, and the dashboard.

```csharp
using ODDGames.BugpunchSdk;

public static class Cheats
{
    [DebugButton("Economy", "Give coins", "Adds 1000 coins")]
    static void GiveCoins() => UnityEngine.Debug.Log("coins");

    [DebugToggle("Player", "God mode")]
    static bool GodMode;

    [DebugSlider("Player", 0f, 4f, "Speed", adminOnly: true)]
    static float Speed = 1f;

    [DebugNumber("Economy", "Coins", min: 0f, max: 100000f)]
    static long Coins;

    [DebugString("Network", "Server override")]
    static string Server = "";
}
```

Attribute tools work on static methods (`[DebugButton]`) and on static fields
or read/write properties of type `bool` (`[DebugToggle]`), `float`
(`[DebugSlider]`), `int`, `long`, `float` or `double` (`[DebugNumber]`) and
`string` (`[DebugString]`). The panel reads each value when it opens.

For tools that belong to a screen or mode, or that need more than a static
member, use `Bugpunch.Tools`. A group is removed together when disposed:

```csharp
using System;
using ODDGames.BugpunchSdk;
using UnityEngine;

public class GarageScreen : MonoBehaviour
{
    float _zoom = 1f;
    string _plate = "";
    IDisposable _tools;

    void OnEnable()
    {
        _tools = Bugpunch.Tools.Group("Garage", debugBuildOnly: true)
            .Button("Refresh", () => Debug.Log("refresh"), description: "Reload the garage")
            .Toggle("Run in background", () => Application.runInBackground, on => Application.runInBackground = on)
            .Slider("Zoom", 0.5f, 3f, () => _zoom, v => _zoom = v)
            .Number("Target frame rate", () => Application.targetFrameRate, v => Application.targetFrameRate = (int)v)
            .Text("Plate", () => _plate, v => _plate = v);
    }

    void OnDisable() => _tools.Dispose();
}
```

- `adminOnly` shows a tool to internal testers only, and the SDK re-checks the
  role when the tool is used. Every attribute takes it, and so does
  `Bugpunch.Tools.Group`.
- `debugBuildOnly` adds nothing outside a development build.
- `order` sorts a code-registered tool within its category, lower first.
- `Bugpunch.Tools.Open()` opens the panel from your own button. Gate it yourself.
- Icons are Feather names. Every panel draws `play`, `pause`, `settings`,
  `search`, `close`, `warning`, `zap`, `eye`, `activity`, `tune`, `send`,
  `flag`, `clock`, `cpu`, `target`, `terminal`, `toggle-left`, `toggle-right`,
  `hash`, `wrench`, `info`, `check`, `refresh`, `trash`, `shield` and `layers`.
  An unknown name draws a circle.
- Register and dispose from the main thread. An open panel refreshes once on
  the next frame, however many tools changed.

`DebugToolsBridge.RegisterDynamicTool` still takes a button or toggle by id,
for code that manages each tool on its own.

### Roles on Standalone

Android and iOS keep the tester role on the device, and the Editor gets it from
a Bugpunch sign-in. A shipped Standalone (PC, Steam) player always reports
**Public**, so that an end user's desktop build can't reach debug tools.
Admin-only tools therefore never show there.

### What runs without debug mode

On Android and iOS the native SDK starts with the process in every build,
including store builds: crash handlers, the upload queue, and the analytics
`session_start` and `session_end` events. None of it waits for
`StartDebugMode`. Editor and Standalone don't emit session events.

Automatic exception reports are rate-limited to one per signature every 30
seconds, with a 5 second gap between any two. A distinct exception inside the
gap is sent late, never dropped, but a repeat of the same signature is dropped
and isn't counted. A continuous failure therefore shows as one event every 30
seconds. `Bugpunch.Report` and `Bugpunch.Feedback` aren't limited this way.

### Using Bugpunch with Firebase Crashlytics

`com.unity.webrtc` ships a prebuilt `libwebrtc.so` with no build ID and
malformed metadata. Crashlytics' `dump_syms` exits with an error on it, which
aborts the symbol upload for every library in the build. Android release builds
then send no native symbols, or fail if your CI treats the upload as required.

Leave `libwebrtc.so` out of the symbols folder you pass to Crashlytics. Copy the
folder without that file, or delete it from the copy, before running the upload.
Crash reports inside libwebrtc itself then won't symbolicate in Crashlytics; the
rest of the build's will.

The package also loads `libwebrtc` at startup in every player build, before any
Bugpunch code runs. Bugpunch's own streamer stays idle until a debug session
starts, but the library is already in the process.

### Old Input Manager projects

The package depends on the Input System, so Unity asks on each editor launch to
enable the new input backend in a project still on the old Input Manager.
Set **Active Input Handling** to **Both** to keep your game's input working and
stop the prompt. Remote taps and clicks from the dashboard are injected through
the Input System, so they can't drive a game that reads only the old Input
Manager.

### UI test automation

`ODDGames.BugpunchSdk.ActionExecutor` is a UI testing API: fluent element
search, clicks, typing, gestures (swipe / pinch / rotate / drag), waits,
and reports with screenshots and screen recording. It runs from NUnit
PlayMode tests, from JSON actions (Remote IDE, AI-generated tests) and
headless through [clibridge4unity](https://github.com/oddgames/clibridge4unity)
`UIACTION` / `UISESSION`.

- [REFERENCE.md](REFERENCE.md) — API reference
- [JSON_ACTIONS.md](JSON_ACTIONS.md) — JSON action protocol
- [ACTION_EXECUTOR.md](ACTION_EXECUTOR.md) / [INPUT_INJECTION.md](INPUT_INJECTION.md) — internals

## Versioning

Tags are SemVer (`v0.8.226`, …). Pin a specific tag in `manifest.json`;
Unity caches the package by tag, so you only update when you bump the pin.
See [CHANGELOG.md](CHANGELOG.md) for release notes.

## Issues + support

GitHub Issues on this repo. It's our internal tracker, but it's public so
you can see what's coming and what's known to be broken.

## License

See [LICENSE](LICENSE).
