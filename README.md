# Bugpunch — Unity SDK

[![bugpunch.com](https://img.shields.io/badge/dashboard-bugpunch.com-2563eb)](https://bugpunch.com)

Crash reporting, bug capture, and remote debugging for Unity games. Drop-in
UPM package, three platform lanes (Android NDK + iOS Obj-C++ + C#), pairs
with the Bugpunch dashboard at [bugpunch.com](https://bugpunch.com).

## What it gives you

- **Crash reports** — native signal handlers (SIGSEGV / SIGABRT / SIGBUS)
  on Android NDK + iOS Mach exceptions; ANR / main-thread-stall detection
  via watchdog. Survives a dying Mono runtime — reports queue to disk
  natively and ship on the next launch if the process died mid-upload.
- **Exception reports** — managed C# exceptions caught via
  `AppDomain.UnhandledException` and `Application.logMessageReceivedThreaded`,
  routed through the same upload queue. Stack traces deobfuscate against
  uploaded IL2CPP method maps.
- **User-initiated bug reports + feedback** — shake to report (or the
  in-app debug widget). Auto-attaches a screenshot, scene name, recent
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
- **Custom data + analytics** — `Bugpunch.SetCustomData("level", "boss-3")`
  attaches to every report. `Bugpunch.LogPurchase(...)` for IAP analytics
  (auto-wired if you use Unity Purchasing — see below).

## Install

Add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "au.com.oddgames.bugpunch": "https://github.com/oddgames/bugpunch-sdk.git#v1.8.6"
  }
}
```

The repo is public; no token, no scoped registry. Unity Package Manager
fetches the tag and treats the repo as the package root.

## Configure

1. Sign up at [bugpunch.com](https://bugpunch.com), create a project, copy
   the API key.
2. In Unity: **Create → Bugpunch → Config**. Paste your API key + server
   URL into the ScriptableObject. Drop the asset somewhere under
   `Resources/` so the SDK loads it on startup.
3. Build + run. The first launch logs `[Bugpunch] Active lane: …` so you
   know which platform path is active. Reports start flowing.

That's the entire integration. No `MonoBehaviour` to drop into a scene,
no `Bugpunch.Init()` call — the SDK boots itself before scene load.

## Public API

```csharp
using ODDGames.Bugpunch;

// Manually file a bug or feedback.
Bugpunch.Report("Player got stuck on level 3 boss");
Bugpunch.Feedback("Loved the new gesture controls!");

// Tag every subsequent report with extra context.
Bugpunch.SetCustomData("playerLevel", 47);
Bugpunch.SetCustomData("subscription", "pro");

// Pull a server-resolved config variable (per-device overrides supported).
var spawnRate = Bugpunch.GetVariable("spawnRate", 1.0f);

// IAP analytics (no-op if Unity Purchasing isn't installed).
Bugpunch.LogPurchase("coins_500", 4.99m, "USD", txnId);
```

The full surface is small — most of the SDK runs autonomously once
configured.

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

If your game uses Unity Purchasing (`com.unity.purchasing` 4.0+):

```csharp
using UnityEngine.Purchasing;
using ODDGames.Bugpunch;

UnityPurchasing.Initialize(myStoreListener.WithBugpunch(), builder);
```

`.WithBugpunch()` wraps your `IStoreListener` and side-channels every
successful purchase to analytics — your `ProcessPurchase` still runs
first, untouched. Compiles only when Unity Purchasing is installed; zero
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

### UI test automation

The SDK ships a UI testing framework (`ODDGames.Bugpunch.ActionExecutor`)
for record-and-replay tests, gesture injection, AI-driven test
generation, and headless test execution via the
[clibridge4unity](https://github.com/oddgames/clibridge4unity) bridge.
Documentation at [bugpunch.com/docs](https://bugpunch.com).

## Versioning

Tags are SemVer (`v1.8.6`, etc.). Pin a specific tag in `manifest.json`
— Unity caches the package by tag hash, so consumers update only when
they explicitly bump the pin. See [CHANGELOG.md](CHANGELOG.md) for
release notes.

## Issues + support

GitHub Issues on this repo. Internal tracker, but visible publicly so
you can see what's coming and what's known-broken.

## License

See [LICENSE](LICENSE).
