# Bugpunch — Unity SDK

[![bugpunch.com](https://img.shields.io/badge/dashboard-bugpunch.com-2563eb)](https://bugpunch.com)
[![docs](https://img.shields.io/badge/docs-bugpunch.com%2Fdocs-f97316)](https://bugpunch.com/docs)

Crash reporting, bug capture, and remote debugging for Unity games. Drop-in
UPM package, three platform lanes (Android NDK + iOS Obj-C++ + C#), pairs
with the Bugpunch dashboard at [bugpunch.com](https://bugpunch.com).

📖 **Full documentation:** [bugpunch.com/docs](https://bugpunch.com/docs) —
getting started, the complete [API reference](https://bugpunch.com/docs#api-reference),
[configuration](https://bugpunch.com/docs#configuration), crash reporting,
the Remote IDE, automated UI testing, and more.

## What it gives you

- **Crash reports** — native signal handlers (SIGSEGV / SIGABRT / SIGBUS)
  on Android NDK + iOS Mach exceptions; ANR / main-thread-stall detection
  via watchdog. Survives a dying Mono runtime — reports queue to disk
  natively and ship on the next launch if the process died mid-upload.
- **Exception reports** — managed C# exceptions caught via
  `AppDomain.UnhandledException` and `Application.logMessageReceivedThreaded`,
  routed through the same upload queue. Stack traces deobfuscate against
  uploaded IL2CPP method maps.
- **User-initiated bug reports + feedback** — the in-app debug widget
  (or `Bugpunch.Report` / F12). Auto-attaches a screenshot, scene name, recent
  logs, custom data, and (opt-in) the last 90 seconds of gameplay video.
- **Remote IDE** — connect from the web dashboard to a running build and
  drive it live: scene hierarchy, component inspector, console log
  stream, screenshot capture, scripted commands, WebRTC video stream of
  the game view, file browser, snapshot save/restore, device info. Same
  surface you'd get from Unity's own profiler, plus C# script execution.
- **Performance monitoring** — sampled FPS, memory, frame time; alerts
  fire when thresholds break. Pre-crash storyboard captures the last few
  seconds of UI presses + screenshots so triage gets context, not just a
  stack frame.
- **Tags + analytics** — `Bugpunch.SetTag("level", "boss-3")` attaches
  indexed segmentation data to every report. The dashboard's Impact view
  groups events by tag value so you can see which devices share a state
  when an issue hits. `Bugpunch.LogPurchase(...)` for IAP analytics
  (auto-wired if you use Unity Purchasing — see below).

## Install

Add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "au.com.oddgames.bugpunch": "https://github.com/oddgames/bugpunch-sdk.git#v0.8.76"
  }
}
```

The repo is public; no token, no scoped registry. Unity Package Manager
fetches the tag and treats the repo as the package root.

Dependencies (`com.unity.webrtc`, `com.unity.inputsystem`, etc. declared in
`package.json`) resolve from the default Unity Registry. The Android-
native pieces of the SDK (Remote IDE live stream peer) reuse the
`libwebrtc.aar` that `com.unity.webrtc` already ships in your APK — no
extra dependency manager and no extra `.aar` to vendor in your project.

## Configure

1. Sign up at [bugpunch.com](https://bugpunch.com), create a project, copy
   the API key.
2. In Unity: **Create → ODD Games → Bugpunch Config**. Paste your API key + server
   URL into the ScriptableObject. Drop the asset somewhere under
   `Resources/` so the SDK loads it on startup.
3. Build + run. The first launch logs `[Bugpunch] Active lane: …` so you
   know which platform path is active. Reports start flowing.

That's the entire integration. No `MonoBehaviour` to drop into a scene,
no `Bugpunch.Init()` call — the SDK boots itself before scene load.

## Public API

```csharp
using ODDGames.BugpunchSdk;

// Manually file a bug or feedback.
Bugpunch.Report("Player got stuck on level 3 boss");
Bugpunch.Feedback("Loved the new gesture controls!");

// Tag every subsequent report with extra context.
Bugpunch.SetTag("playerLevel", 47);
Bugpunch.SetTag("subscription", "pro");

// Pull a server-resolved config variable (per-device overrides supported).
// Typed getters: GetVariable (string), GetVariableBool / GetVariableInt / GetVariableFloat.
var spawnRate = Bugpunch.GetVariableFloat("spawnRate", 1.0f);

// IAP analytics (no-op if Unity Purchasing isn't installed).
Bugpunch.LogPurchase("coins_500", 4.99m, "USD", txnId);
```

The full surface is small — most of the SDK runs autonomously once
configured.

## QA Goals

The dashboard's **QA Goals** page (`/coverage`, under the *Tasks* group)
shows a grid: rows = goals you authored, columns = platforms (Android,
iOS, Steam, Editor, …), cells = % progress per platform. Each cell also
carries hit count + first/last hitter so QA can see who's been driving
the work.

Goals are programmatic — game code declares them via the
`Bugpunch.Goal(...)` runtime API, a build-time Cecil scanner extracts the
literal labels so the dashboard sees every goal before any tester runs, and
the runtime fires the match when the criteria are met. Both label and value
must be string / numeric literals so the scanner can extract them.

```csharp
// Declare a goal with an evaluator the SDK polls (every ~10s + on scene
// change + on app focus). Fires and latches when the value matches.
Bugpunch.Goal("Reach level 5", 5, () => currentLevel);

// Mark a code path as exercised during a QA pass.
Bugpunch.MarkCoverage("tutorial_completed");

// Record a categorized value the dashboard aggregates across testers.
Bugpunch.RecordValue("weapon_used", "plasma_rifle");
```

See the full [QA Goals guide](https://bugpunch.com/docs#analytics-goals) for
every `Goal` overload plus `AssertCoverage` and coverage tracking.

### Manual QA tasks — tested by hand

For visual / locale / "vibes" checks that no event captures cleanly,
declare the goal as `manual` in `BugpunchGoals.asset` (*Assets →
Create → Bugpunch → Goals*). QA ticks them off from the dashboard.

### Build identity

Every event the SDK ships carries a stable build hash —
`sha1(Application.version + git commit + UTC build time)` truncated to
12 hex chars, stamped at editor build time. The QA Goals page picker
shows `version · hash · YYYY-MM-DD` so two builds with the same
`Application.version` (e.g. an Android rebuild after a tiny code fix)
get separate entries instead of conflating their data.

## Supported targets

| Platform           | Lane         | Crash handler             | Notes                                 |
|--------------------|--------------|---------------------------|---------------------------------------|
| Android player     | Java + NDK   | POSIX signals, ANR watchdog | Native AAR shipped pre-built          |
| iOS player         | Obj-C++      | Mach exceptions, signals  | xcframework shipped pre-built         |
| Unity Editor       | C#           | Managed exceptions only   | Useful for crash dialog, IDE testing  |
| Standalone (Win/Mac/Linux) | C#  | Managed exceptions only   | Native crash handlers TBD             |

Unity 6000.0+ tested. Unity 2022.3 LTS works for the runtime; the Editor
windows assume Unity 6.

## Optional features

### In-app purchases analytics

If your game uses Unity Purchasing (`com.unity.purchasing` 5.0+):

```csharp
using UnityEngine.Purchasing;
using ODDGames.BugpunchSdk;

m_PurchaseService = UnityIAPServices.DefaultPurchase().WithBugpunch();
```

`.WithBugpunch()` subscribes to `OnPurchaseConfirmed` and side-channels
every confirmed purchase to analytics — your own event handlers run
unchanged. Compiles only when Unity Purchasing 5.0+ is installed; zero
runtime cost when absent.

For raw StoreKit (iOS) or direct Google Play Billing (Android), call
`Bugpunch.LogPurchase(sku, price, currency, transactionId)` from your
own purchase handler.

### Pre-crash video buffer (opt-in)

Tester builds can call `Bugpunch.EnterDebugMode()` from a menu to start a
rolling video buffer (default: last 90 seconds, h264, ~2 Mbps). On the
next crash / ANR / bug report, the recording attaches automatically so
triage sees what happened *before* the failure, not just the moment of.

The first call shows a native consent sheet; on Android it then chains
to the OS-level MediaProjection prompt. Nothing records until both are
accepted, and there's no way for the SDK to start recording without the
explicit `EnterDebugMode()` call.

**Android Play Store publishing.** The Bugpunch AAR declares
`FOREGROUND_SERVICE` and `FOREGROUND_SERVICE_MEDIA_PROJECTION` in its
own manifest — Unity's manifest merger pulls them into your host app
automatically, no editing required.

If your app targets `targetSdkVersion` 34+ and you ship to Play, fill
out the **Foreground Service Permissions** form once in Play Console
(Policy → App content → Foreground Service Permissions). Pick *Media
projection* and use this justification:
*"Bugpunch SDK's opt-in QA video recording. Captures a short rolling
buffer of the device screen so the dev team can review what the user
saw immediately before a crash. Only runs after the user explicitly
taps Start Recording on an in-app consent dialog, then accepts the OS
MediaProjection dialog."*
Upload a short clip showing the consent flow.

If you target `targetSdkVersion` 33 or below, the Play Console form
isn't triggered.

### UI test automation

The SDK ships a UI testing framework (`ODDGames.BugpunchSdk.ActionExecutor`)
for record-and-replay tests, gesture injection, AI-driven test
generation, and headless test execution via the
[clibridge4unity](https://github.com/oddgames/clibridge4unity) bridge.
Full guide: [bugpunch.com/docs#automated-testing](https://bugpunch.com/docs#automated-testing).

## Versioning

Tags are SemVer (`v0.8.76`, etc.). Pin a specific tag in `manifest.json`
— Unity caches the package by tag hash, so consumers update only when
they explicitly bump the pin. See [CHANGELOG.md](CHANGELOG.md) for
release notes.

## Issues + support

GitHub Issues on this repo. Internal tracker, but visible publicly so
you can see what's coming and what's known-broken.

## License

See [LICENSE](LICENSE).