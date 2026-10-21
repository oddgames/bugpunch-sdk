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

**Requirements:** Unity **6000.3** or newer. The package depends on
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

// File a bug or feedback.
Bugpunch.Report("Player got stuck on level 3 boss", "Fell through the arena floor");
Bugpunch.Feedback("Loved the new gesture controls!");

// Attach files to ONE report — copied at the call, always uploaded (never
// sampled). A missing path shows on the report as skipped, not an error.
// type: "error" files silently (no report form) and groups by title.
Bugpunch.Report("Save failed to load", "Slot 2 checksum mismatch", type: "error", attachments: new[] {
    BugpunchAttachment.Path("[PersistentDataPath]/save.json"),
    BugpunchAttachment.Path("[PersistentDataPath]/profile/"),            // folder → profile.zip
    BugpunchAttachment.Path("[PersistentDataPath]/logs/*.txt", zip: true),
    BugpunchAttachment.Text("state.json", "{\"slot\":2}"),
});

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
