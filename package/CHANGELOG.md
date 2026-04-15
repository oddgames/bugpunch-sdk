# Changelog

All notable changes to this project will be documented in this file.

## [1.5.9] - 2026-04-15

### Fixed
- **Screenshot captured the report form instead of the game** — `BugpunchScreenshot.capture` was async (PixelCopy fires on a HandlerThread); `BugpunchReportActivity.launch` fired in the same instant, covering Unity before the copy ran. New `captureThen(path, quality, Runnable)` chains the form launch into the PixelCopy completion callback so the screenshot is of the actual game state.
- **Empty/0-byte video MP4** — `BugpunchRecorder.dump` now counts media samples written and returns false when only codec-config samples were in the buffer (recorder hadn't captured a real frame yet). Eliminates `MPEG4Writer Stop() called but track is not started` warnings and prevents uploading invalid MP4s.
- **`dumpRingIfRunning` verifies file size** after dump — logs clearly whether the video was attached (`video dump ok: … (N bytes)`) or skipped (`no video dump — recorder not running`), deletes zero-byte files.
- **Nav bar covering annotate + report form buttons** — bottom toolbar (Annotate) and form Send/Cancel (Report) applied `setOnApplyWindowInsetsListener` to pad above system bars. Required on Android 15+ which is edge-to-edge regardless of theme. Activities switched from `Theme.NoTitleBar.Fullscreen` to `Theme.NoTitleBar`.

### Changed
- **Dashboard media layout** — screenshot constrained to `max-w-md` (no longer stretches full panel width); 2-column layout on `md+`; placeholder card when no video is attached explaining *why* ("Tester didn't tap Enter Debug before reporting, or recorder hadn't captured a keyframe yet").

## [1.5.8] - 2026-04-15

Major refactor: **native-first architecture**. Everything that doesn't strictly need Unity now lives in Java/Obj-C++; C# is a thin adapter (4 files: facade, JNI/P-Invoke bridge, scene tick, managed-exception forwarder). Survives a dying Mono runtime, owns a persistent retry queue across launches, no Unity dep on the crash path.

### Added
- **`Bugpunch.EnterDebugMode(bool skipConsent = false)`** — opt-in screen recording. Default shows a custom native consent sheet (Start Recording / Cancel) over a blurred backdrop. `skipConsent: true` for debug/alpha builds. Android still shows OS-level MediaProjection consent; that's mandatory.
- **`Bugpunch.Report(title, description, type)`** — opens a native bug-report form (screenshot preview, email, description, severity dropdown, Send/Cancel). Tap the screenshot to open a fullscreen annotation canvas (pen, undo, clear) — annotations submit as a transparent PNG layer the dashboard can toggle.
- **`Bugpunch.Feedback(message)`**, **`Bugpunch.SetCustomData(k, v)`**, **`Bugpunch.ClearCustomData(k)`** — facade methods.
- **Native log capture** — Android tails its own logcat (own-app filter automatic on API 25+), iOS reads `OSLogStore` (iOS 15+). Bounded ring buffer, snapshotted into report metadata at send time.
- **Native shake detection** — `SensorManager` (Android) / `CoreMotion` (iOS). Disabled by default; flip `shake.enabled` in config to opt in.
- **Native screenshot** — Android `PixelCopy` on Unity's `SurfaceView`, iOS `drawViewHierarchyInRect` on key `UIWindow`. No permissions, no user prompt.
- **Native upload queue** — disk-backed manifest queue at `{cacheDir}/bugpunch_uploads/`. Multipart POST via `HttpURLConnection` (Android) / `NSURLSession` (iOS). Retry up to 10 attempts across app launches; terminal HTTP statuses (400/401/403) drop immediately.
- **Native FPS** — `Choreographer` (Android) / `CADisplayLink` (iOS). C# no longer pushes fps.
- **Bugpunch logo PNG assets** — bundled at multiple densities for Android (`drawable/`) and iOS (`@1x`/`@2x`/`@3x`). Currently unused in dialogs but ready for future UI.
- **Concurrent-report guard** — second `Bugpunch.Report()` while the form is open is ignored (logged, returns silently).

### Changed
- **C# surface shrunk to 4 files**: `Bugpunch.cs`, `BugpunchNative.cs` (+ `BugpunchSceneTick`), `UnityExceptionForwarder.cs`, plus the `BugpunchClient.Setup()` startup hook.
- **`UnityExceptionForwarder`** (was `NativeCrashHandler`) is no longer a `MonoBehaviour` — static, hooks `AppDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException` and forwards directly to `BugpunchNative.ReportBug("exception", …)`.
- **Crash endpoint** corrected from `/api/reports/crash` (didn't exist server-side) to `/api/crashes`. Crash JSON now matches `crashService.ingestCrash` shape.
- **Wire format** is now `multipart/form-data` for all reports: `metadata` text field + `screenshot.jpg` / `video.mp4` / `annotations.png` file parts. Server middleware accepts both new multipart and legacy JSON.
- **Editor is now a no-op** — no in-editor send path; device-only.

### Fixed
- **Android 14+ MediaProjection crash** — `MediaProjection.Callback` is now registered via `registerCallback()` before `createVirtualDisplay`. Required by API 34; previously threw `IllegalStateException`.
- **Activity not declared** — `BugpunchReportActivity` and `BugpunchAnnotateActivity` added to `AndroidManifest.xml`.
- **Android Java compile** — three new files (`BugpunchDebugMode`, `BugpunchReportActivity`, `BugpunchScreenshot`) were importing `com.unity3d.player.UnityPlayer` directly. Replaced with the existing reflection helper pattern (`BugpunchUnity.currentActivity()` + `BugpunchUnity.sendMessage()`).
- **Test scene buttons unresponsive** — `DebugModeButton.cs` now spawns an `EventSystem` + `InputSystemUIInputModule` if the scene doesn't already have one.

### Removed
- `BugReporter.cs`, `NativeScreenshot.cs`, `BugpunchUpload.cs`, `NativeCrashHandler.cs` — all replaced by the native coordinator + thin C# adapter.
- `Bugpunch.RecordBug()` / `Bugpunch.QuickReport()` — replaced by `Bugpunch.Report()`.

## [1.5.7] - 2026-04-15

### Added
- **Simplified static API** — `ODDGames.Bugpunch.Bugpunch` facade: `Bugpunch.RecordBug()`, `Bugpunch.QuickReport()`, `Bugpunch.Feedback()`, etc. Replaces deep `BugpunchClient.Instance.Reporter.StartReport()` calls.
- **Record-and-report flow** — friendly native welcome card ("We'll record your screen...") followed by a floating draggable red report button. Button stays for the whole session; shake/F12/`Bugpunch.RecordBug()` all trigger the flow.
- **Native overlay UIs** — new `BugpunchReportOverlay.java` (Android `WindowManager` views) and `BugpunchReportOverlay.mm` (iOS `UIView` on root window) with vector-drawn camera + video icons.

### Fixed
- **Android build failure** — `BugpunchReportOverlay.java` was importing `com.unity3d.player.UnityPlayer` directly. The `.androidlib` doesn't have Unity on the classpath, so this broke Gradle compile. Replaced with reflection-based `UnitySendMessage` call (same pattern as `BugpunchCrashActivity` and `BugpunchProjectionRequest`).

## [1.5.6] - 2026-04-14

### Fixed
- **iOS linker error — 25 duplicate symbols** — `ODDRecorderMac.mm` was being included in iOS Xcode exports despite `.meta` restricting it to Standalone. Added `#if TARGET_OS_OSX` guard so it compiles to nothing on iOS. The `ODDRecorderImpl` class is already provided by `ODDRecorder.mm` on iOS.

## [1.5.5] - 2026-04-14

### Fixed
- **iOS build error (Xcode 16.2 / iOS 18.2 SDK)** — renamed `VTCompressionOutputCallback` to avoid name collision with VideoToolbox SDK typedef. Fixed incorrect `CFMutableDictionaryRef` cast of `CMSampleBufferGetSampleAttachmentsArray` return (should be `CFArrayRef`). Added missing `#include <unistd.h>` for `usleep()`.

## [1.5.4] - 2026-04-14

### Fixed
- **Android build failure** — `.androidlib/build.gradle` now inherits `buildToolsVersion` from `unityLibrary` in addition to `compileSdkVersion`. Prevents Gradle from trying to download missing build-tools from a read-only SDK location.

## [1.5.3] - 2026-04-14

### Fixed
- **Android build failure** — `.androidlib/build.gradle` now inherits `compileSdkVersion`, `minSdkVersion`, and `targetSdkVersion` from Unity's `unityLibrary` project (same pattern as OneSignal). Fixes `compileSdkVersion is not specified` Gradle error.

## [1.5.2] - 2026-04-14

### Fixed
- **iOS build error** — added forward declaration of `handleEncodedSampleBuffer:` in BugpunchRingRecorder.mm to fix compile error in Objective-C++ mode (Xcode 16.2)
- **WebRTC black screen** — game view now uses `ScreenCapture.CaptureScreenshotIntoRenderTexture` to capture exactly what's on screen (all cameras, UI, post-fx). Fixes multi-camera setups where Camera.main was a UI overlay.
- **WebRTC stream killed on scene switch** — `SetCamera` no longer calls `StopStreaming()`. One persistent stream, camera just swaps.
- **Scene camera stop returns to game view** — `SetCamera(null)` instead of `SetCamera(Camera.main)` which was incorrectly selecting the UICamera.
- **ProGuard consumer rules** — added `build.gradle` + `consumer-proguard.pro` to `.androidlib` so R8 keeps `org.webrtc.**` classes automatically in consumer projects (fixes `ClassNotFoundException` in release builds).
- **WebRTC `webrtc-stop` handling** — ignored during streaming to prevent dashboard mode switches from killing the connection.
- **Scene camera orientation** — all 6 snap directions corrected.

## [1.5.1] - 2026-04-14

### Fixed
- **Android manifest merging** — moved Java plugins + manifest into `BugpunchPlugin.androidlib/` structure (same pattern as Google Ads, Firebase). Fixes `ActivityNotFoundException` for `BugpunchProjectionRequest` and `BugpunchCrashActivity`.
- **WebRTC on Android IL2CPP** — removed separate asmdef approach (stripped by IL2CPP). WebRTCStreamer now in main assembly with direct `AddComponent<WebRTCStreamer>()` in try/catch. Added `link.xml` preserve rules.
- **Scene camera orientation** — all 6 snap directions were inverted, now correct.
- **WebRTC shutdown crash** — skip cleanup during `OnApplicationQuit`.
- **ProGuard stripping** — added `proguard-bugpunch.txt` keeping `org.webrtc.**` Java classes.
- **Plugin .meta files** — 12 files fixed with correct `PluginImporter` platform restrictions.

### Added
- **`SuppressDebugRequests`** — games can block debug session prompts during gameplay.
- **`EnableVideoCapture()`** — video ring buffer is now opt-in, not auto-starting.
- **Scene camera viewport sizing** — renders at dashboard panel dimensions, not device screen.
- **`IStreamer` interface** — decouples BugpunchClient from WebRTCStreamer concrete type.

### Changed
- **Lazy WebRTC loading** — WebRTC only initializes on first `webrtc-offer` from dashboard, not at startup or WebSocket connect.
- **Debug builds use WebSocket directly** — release builds poll, debug builds connect immediately.
- **Simplified BugpunchConfig** — just `serverUrl` + `apiKey`, removed `projectId` and feature toggles.

## [1.5.0] - 2026-04-13

### Added
- **Production crash reporting** — all-user crash reporting comparable to Instabug/Bugsee/Sentry
- **Native crash handlers** — POSIX signal handlers (SIGABRT, SIGSEGV, SIGBUS, SIGFPE, SIGILL) on Android (NDK) and iOS (Mach exceptions). Async-signal-safe, writes crash file to disk, uploads on next launch.
- **ANR detection** — watchdog timer on Android detects main thread hangs, captures all thread stacks
- **C# exception handlers** — `AppDomain.CurrentDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` for exceptions that bypass Unity's log system
- **Native crash overlay** — full-screen Activity (Android) / ViewController (iOS) / IMGUI (desktop) with video playback, stack trace display, user input fields for title/description/severity
- **iOS ring buffer recorder** — always-on H.264 circular buffer using ReplayKit + VideoToolbox, matching the existing Android implementation. 30s window, hardware-accelerated, minimal CPU impact.
- **Unified RingBufferRecorder** — cross-platform C# bridge replacing Android-only `AndroidScreenRecorder`
- **NativeCrashHandler** — C# bridge integrating all native handlers, crash file format (`BUGPUNCH_CRASH_V1`), next-launch detection and upload

### Fixed
- **WebRTC track disposal ordering** — video track now removed from peer connection before disposal, preventing native crashes
- **WebRTC RenderTexture lifecycle** — changing camera mid-stream now safely stops streaming instead of replacing RT while track references it
- **WebRTC.Update() coroutine lifecycle** — guard against multiple coroutine instances, proper cleanup on destroy
- **WebRTC connection state cleanup** — disconnected/failed connections now clean up resources on main thread
- **WebRTC Camera.main caching** — render loop no longer calls Camera.main every frame
- **WebRTC data channel disposal** — explicit Dispose() after Close()
- **WebRTC offer timeout** — 30s deadline prevents hanging on malformed SDP
- **WebRTC dead code** — removed unused HandleAnswer method

### Changed
- **Crash overlay wired to BugReporter** — `INativeDialog.ShowCrashReport()` now callable with exception context + video path
- **`_streaming` flag is volatile** — thread-safe for WebRTC connection state callbacks

### Removed
- **Standalone recorder package** — `au.com.oddgames.recorder` removed, all code already merged into Bugpunch SDK. Eliminates duplicate `ODDRecorderImpl` symbols on iOS builds.

## [1.4.9] - 2026-04-13

### Fixed
- **Android 15 crash on startup** — Updated `com.unity.webrtc` from `3.0.0-pre.7` to `3.0.0` (stable). The pre.7 native library (`libwebrtc.so`) called `abort()` during `JNI_OnLoad` on Android 15 (API 35) devices due to missing 16KB page size support. Fixes SIGABRT crash that prevented apps from launching on Honor and other Android 15 devices.

### Changed
- **Minimum Unity version raised to 6000.0** — Required by `com.unity.webrtc` 3.0.0.

## [1.4.8] - 2026-04-13

### Fixed
- **Native plugin platform conflicts** — `ODDRecorder.h` headers in iOS/Windows had `DefaultImporter` (no platform restriction), causing Android build collisions. `ODDRecorderMac.mm` lacked explicit iOS/Android exclusions, causing duplicate `ODDRecorderImpl` symbols on iOS builds. All native plugin `.meta` files now have correct `PluginImporter` entries with explicit platform targets and exclusions.

## [1.4.6] - 2026-04-13

### Changed
- **Removed `UNITY_INCLUDE_TESTS` file-level gates** — All SDK classes (InputInjector, ActionExecutor, Search, ElementFinder, ActionParser, UIUtility, InputVisualizer, GeminiTestClient, GeminiTestRunner, TestReport) are now compiled unconditionally. Only `UIAutomationTestFixture` (genuine NUnit base class) retains the gate. Replaced NUnit `AssertionException` with `BugpunchTestFailureException` and gated TestReport's NUnit `TestContext` usage inline.

## [1.4.4] - 2026-04-11

### Changed
- **TypeDB upload is gzipped** — `TypeDatabaseExporter` now gzips the JSON before upload (~10× smaller on the wire) and sends metadata (`X-Unity-Version`, `X-App-Version`, `X-Type-Count`, `X-Namespace-Count`) via headers. Storage format on the server is now `typedb.json.gz`.
- **TypeDB auto-uploads on compile and connect** — `BuildHooks` now uploads the TypeDB after every script compilation (debounced by SHA1 content hash stored in `EditorPrefs` — unchanged content skips the upload) and also whenever `BugpunchClient` connects via the new `OnAnyConnected` static event. Remote IDE IntelliSense stays fresh without manual exports or builds.
- **New `ExportAndUploadIfChanged()` API** — cheap to call from hot paths; the menu item `ODD Games → Export Type Database` still forces a full upload.

## [1.4.3] - 2026-04-11

### Added
- **WebRTC camera metadata data channel** — `WebRTCStreamer` now creates an `RTCDataChannel` ("camera-metadata") alongside the video track and sends scene camera pose (`px/py/pz/rx/ry/rz`) every other frame. Lets the web dashboard overlay world-space gizmos that stay aligned with the streamed view.
- **`/resolve-element-type` route** — `RequestRouter` exposes `InspectorService.ResolveElementType` for the Remote IDE to resolve the element type of a collection/array chain (e.g. `transform.GetComponentsInChildren<Renderer>()` → `Renderer`).
- **Generic type args in member chain resolution** — `InspectorService.ResolveChain` now parses `Foo.FindFirstObjectByType<Renderer>` and resolves to `Renderer`, so IntelliSense works on generic method calls.

### Changed
- **Inspector member enumeration includes inherited members** — properties, fields, and methods now use `BindingFlags.FlattenHierarchy`, deduped by name so overrides don't appear twice. Methods are grouped by name with an overload count (`(+N overloads)`) instead of listing every signature.
- **PaxScript errors carry line numbers** — `PaxScriptRunner` returns structured `{line, message}` entries from `PaxScripter.Error_List` instead of a single blob string, so the script panel can underline the offending line.

### Fixed
- **Missing Android Plugins .meta files** — v1.4.0 shipped `BugpunchProjectionRequest.java`, `BugpunchProjectionService.java`, `BugpunchRecorder.java`, and `AndroidManifest.xml` without their folder `.meta` companions, so Unity dropped the entire `Plugins/Android` tree on package import in consumer projects.

## [1.4.2] - 2026-04-11

### Fixed
- **`GeminiTestRunner` preprocessor errors in projects without test framework** — the file is wrapped in `#if UNITY_INCLUDE_TESTS`, and the C# preprocessor scans skipped regions for `#` directives without parsing string literals. The `SystemPrompt` verbatim string contained markdown headings (`##`, `###`) at line start, which Roslyn treated as invalid preprocessor directives and failed with CS1024 in consumer projects that don't define `UNITY_INCLUDE_TESTS`. Replaced the `##`/`###` line prefixes with `==`/`--` so they no longer look like directives.

## [1.4.1] - 2026-04-11

### Fixed
- **Missing `AndroidScreenRecorder.cs.meta`** — v1.4.0 shipped the new `AndroidScreenRecorder.cs` without its Unity meta file, so consuming projects hit `CS0246: AndroidScreenRecorder not found` in `BugReporter.cs` when importing the git package. Unity skips .cs files without a .meta companion during package import.

## [1.4.0] - 2026-04-11

### Added
- **Native Android screen recorder** — `BugpunchRecorder.java` (MediaCodec + MediaProjection + MediaMuxer) maintains a rolling 30-second ring buffer of encoded H.264 samples on the native side. On uncaught exception, `BugReporter` dumps a trimmed MP4 (starting at the oldest keyframe within the window) and POSTs it with the crash report. Replaces the old Unity-side JPEG frame capture.
- **Auto bug reports on exceptions** — `BugReporter.autoReportExceptions` (default on). When a `LogType.Exception` is logged, a report is filed automatically with a configurable cooldown (`autoReportCooldownSeconds`, default 30s) to prevent flooding on tight exception loops.
- **`BugpunchProjectionService`** — foreground service of type `mediaProjection`, required by Android 10+ to hold the MediaProjection token.
- **`BugpunchClient.StartConnection()`** — static opt-in entry point for release builds. Auto-init is now gated on `Debug.isDebugBuild || Application.isEditor`; release builds must call this explicitly.
- **HTTP POST bug report path** — `BugReporter` now POSTs to `/api/reports/bug` via `UnityWebRequest` with `X-Api-Key` auth. Works even when the live tunnel isn't connected, so crash reports ship from production players with no debug session open.

### Changed
- **`BugReporter` video config** — old `videoScale` + `videoQuality` replaced with `videoBitrate` (H.264 bps) and `videoMaxDimension` (cap on the shorter screen axis). Default video FPS raised from 10 → 30 now that encoding is hardware-accelerated.
- **`BugReporter.OnLog` is thread-safe** — `Application.logMessageReceivedThreaded` can fire on worker threads, so the log timestamp now uses `Stopwatch` instead of `Time.realtimeSinceStartup` (which is main-thread only).
- **Bug reports are deduped server-side** — the server collapses identical stacks (hashed on title + first stack frame) within a 1-hour window, so a buggy release doesn't flood Parse with thousands of duplicate rows.

## [1.3.2] - 2026-02-15

### Fixed
- **Version bump to invalidate stale Unity package cache** — CI builds were failing with CS0246 (`VideoTimestampTracker` not found) because the v1.3.1 tag was force-updated after initial creation, and Unity's global git package cache retained the old resolution missing the `.meta` file.

## [1.3.1] - 2026-02-13

### Added
- **Video timestamp sidecar (`video_timestamps.csv`)** — Per-game-frame session timestamps captured during video recording via `VideoTimestampTracker`. Enables accurate video-to-log synchronization even when the encoder drops frames or stalls.
- **`videoStartOffset` in session JSON** — Seconds between session start and recording start, used by the web viewer to map between session timeline and video playback position.
- **`videoTimestampsFile` in session JSON** — Reference to the sidecar CSV filename in the diagnostic zip.

### Changed
- **Web viewer video sync uses offset mapping** — `VideoPanel` now maps between session time and video time via `sessionToVideo()`/`videoToSession()` functions instead of assuming 1:1 alignment. Correctly handles recordings that start after the session begins and detects when the timeline position is past video coverage.

## [1.3.0] - 2026-02-13

### Added
- **`HierarchyNode.instanceId`** — `GameObject.GetInstanceID()` for unique identification of duplicate-named objects in hierarchy snapshots.
- **`HierarchyNode.depth`** — Camera distance for front-to-back rendering order (lower = closer). 3D objects use camera Z-distance; UI elements use negative canvas sort order + hierarchy order.
- **`HierarchyNode.worldBounds`** — Raw world-space AABB `[cx, cy, cz, ex, ey, ez]` for 3D objects with visible Renderers. Projected to screen-space client-side.
- **`HierarchySnapshot.cameraMatrix`** — 4x4 view-projection matrix (row-major) for client-side 3D→screen coordinate projection.
- **Unfocused play mode** — `Application.runInBackground = true` is set automatically before tests start (`RunStarted`) and restored after tests finish (`RunFinished`), ensuring tests continue running when the Unity editor loses focus.

### Changed
- **3D bounds use client-side projection** — Hierarchy snapshots store raw world-space AABBs and the camera VP matrix instead of projecting per-object with `WorldToScreenPoint`. Eliminates expensive per-object native interop calls in `BuildHierarchy`.
- **`GetHierarchyOrder()` simplified** — Uses `depth * 1000 + siblingIndex` (zero-allocation) instead of root-to-leaf walk with `Transform[]` allocation per call.
- **Test run creation deferred** — `RunBatchCallbacks` creates server runs on the first actual test method (`TestStarted`) instead of `RunStarted`, preventing empty "0 tests" runs from Test Runner scans and recompilations.
- **Upload timeout unlimited** — `HttpClient.Timeout` set to `InfiniteTimeSpan` for diagnostic zip uploads, preventing timeouts on large video recordings over slow connections.

## [1.2.9] - 2026-02-13

### Added
- **`TestReport` diagnostic system** — Automatic test session capture with video recording, screenshots, hierarchy snapshots, and structured console logs. Sessions are packaged as `.zip` archives in `temporaryCachePath/UIAutomation_Diagnostics/`.
- **Cross-platform video recorder (`MediaRecorder`)** — Integrated under `Recorder/` and `Plugins/`. Supports Windows (Media Foundation), Android (MediaCodec), iOS/macOS (AVAssetWriter). Editor uses Unity Recorder backend at 1280x720, 15 FPS.
- **Server integration** — Auto-upload diagnostic sessions to a centralized ASP.NET Core server. Configurable via **Edit > Project Settings > UI Automation** (server URL, API key, auto-upload toggle, upload passes toggle).
- **Test run lifecycle** — `RunBatchCallbacks` hooks Unity Test Runner to create/finish test runs on the server. Sessions are grouped by run.
- **Upload queue** — `AutoUploadHook` queues uploads on thread pool instead of blocking per-test. `DrainUploadQueue()` waits for all uploads before the test batch exits.
- **`ActionExecutor.Snapshot()`** — Captures detailed hierarchy snapshot with full component properties (text, colors, sizes, interactable states, layout settings).
- **Console log capture** — `Application.logMessageReceived` hooked during sessions. All Unity console output stored as `LogEntry` with extended logTypes: Screenshot (5), Snapshot (6), ActionStart (7), ActionSuccess (8), ActionWarn (9), ActionFailure (10).
- **Code location tracking** — `DiagEvent` captures `callerFile`, `callerLine`, `callerMethod` via stack walking to identify test source code.
- **Hierarchy snapshots capture all GameObjects** — Uses `FindObjectsByType<Transform>` for full scene capture. 3D objects get annotations for Camera, Light, MeshRenderer, Collider, Rigidbody, Animator, AudioSource, ParticleSystem.
- **Persistent data capture** — On test failure, `Application.persistentDataPath` contents are optionally included in diagnostic zips (max 50 MB, 3 levels deep). Toggle via Project Settings.
- **Custom metadata** — `TestReport.MetadataCallback` for project-specific metadata. `TestReport.AutoDetectVCS` for Git/Plastic SCM auto-detection.
- **`com.unity.nuget.newtonsoft-json` dependency** — For hierarchy JSON serialization (no depth limit).

### Changed
- **`FailureDiagnostics` renamed to `TestReport`** — Better reflects that it captures all test results, not just failures.
- **`UIAutomationTestFixture.TearDown()` is now `async Task`** — Awaits session end and upload before next test starts.
- **`UIAutomationTestFixture.SetUp()` creates fallback camera** — Auto-creates depth -100 camera when none exists, ensuring recordings/screenshots always have a depth buffer.
- **Hierarchy snapshots use nested `roots`/`children` format** — Serialized with Newtonsoft.Json instead of JsonUtility (no 10-level depth cap).
- **`InputInjector` saves/restores individual settings** — Saves `backgroundBehavior` and `editorInputBehaviorInPlayMode` individually instead of cloning the entire `InputSettings` ScriptableObject.
- **`InputVisualizer` icons have transparent backgrounds** — Cursor icons (mouse, click, touch, scroll) converted from black to transparent backgrounds.
- **`InputVisualizer` scales by screen resolution** — All sizes scale by `Screen.height / 1080f`. Base cursor size reduced from 48px to 32px at 1080p.
- **`BuildFailureReport` returns short assertion message** — Full report in `FAILURE_REPORT.txt`, not the exception message.
- **Diagnostic zips use `CompressionLevel.NoCompression`** — Faster packaging.
- **Screenshots captured via `WaitForEndOfFrame` coroutine** — Replaces mid-frame `ScreenCapture.CaptureScreenshotAsTexture()` that caused gray/empty captures.

### Fixed
- **`InputInjector.OnPlayModeStateChanged`** — No longer touches InputSystem APIs during `ExitingPlayMode`, preventing native assertion spam.
- **`InputInjector.TearDown()`** — Device cleanup wrapped in try/catch to prevent assertion crashes.
- **`Near()` and `Adjacent()` spatial filters** — Now use `UIUtility.GetScreenBounds()` for proper screen-space coordinates on all canvas types.
- **`FindBestAdjacentElement()`** — Iterates `Selectable` components directly instead of walking `RectTransform` hierarchies.
- **`Near()` direction filter** — Changed from strict edge-based to center-based check, matching `Adjacent()` behavior.
- **Native crash in complex scenes** — `Search.Find()`/`FindAll()` now do `await Task.Delay(1)` before first `FindObjectsByType` call to break out of the `ExecuteTasks` pipeline.
- **Screenshots missing from zips** — PNG writes now tracked in `_pendingFileWrites` and awaited before zipping.
- **Action events logged twice** — Removed duplicate `Log()` calls from `ActionExecutor`; `TestReport.LogAction` handles all console output.
- **Video recording stall** — `EditorRecorderBackend` uses `FrameRatePlayback.Variable` to avoid Media Foundation rate control backpressure.
- **Audio capture always enabled** — Uses `MovieRecorderSettings.CaptureAudio` property setter instead of `AudioInputSettings.PreserveAudio`.
- **Double `.mp4.mp4` extension** — `OutputPath` now passes filename without extension.
- **Video playback stopping early** — `GetDuration()` prefers actual `VideoPlayer.length` over session metadata.
- **Encoder odd resolution crash** — Resolution rounded to even values with `& ~1`.

### Removed
- **`DiagnosticsViewerWindow`** — Built-in Unity EditorWindow viewer replaced by web-based viewer.
- **`ClearScreen()` from `SetUp()`** — `Camera.Render()` during SetUp could corrupt native state.

### Breaking
- **`com.unity.recorder` is now a required dependency** (was previously optional).

## [1.2.8] - 2026-02-11

### Changed
- **`Search.Texture()` dynamic shader property discovery** — Material texture matching now dynamically enumerates all texture properties declared by the shader via `Shader.GetPropertyCount()`/`GetPropertyType()`, replacing the previous hardcoded list of 16 property names. Works with any render pipeline (Standard, URP, HDRP) and custom shaders without maintenance.

## [1.2.7] - 2026-02-11

### Fixed
- **Player build IL2CPP linker errors** — All source files in the main assembly are now wrapped in `#if UNITY_INCLUDE_TESTS`, so the entire API only compiles in editor and test player builds. Regular player builds no longer fail from NUnit references.
- **`ThrowTestFailure` exception type mismatch** — Now throws `NUnit.Framework.AssertionException` instead of `UnityEngine.Assertions.AssertionException`. Test `catch (AssertionException)` blocks (which resolve to the NUnit type via `using NUnit.Framework`) now correctly catch action failures.
- **`AllPublicAsyncMethods_UseRunAction` test source file not found** — Uses `[CallerFilePath]` to locate `ActionExecutor.cs` instead of `Application.dataPath`, which returns temp paths when the package is imported via `file:` reference.

### Added
- **`link.xml`** — Preserves `nunit.framework` from IL2CPP managed code stripping in test player builds.
- **`com.unity.ext.nunit` package dependency** — Provides AOT-compatible `nunit.framework.dll` for player builds.

### Changed
- **asmdef uses explicit NUnit reference** — `overrideReferences: true` with `precompiledReferences: ["nunit.framework.dll"]` ensures the NUnit DLL is included in test player builds.

## [1.2.6] - 2026-02-10

### Fixed
- **Mouse position teleport causes camera jumps** — Replaced `DeltaStateEvent` with full `StateEvent` in `InjectPointerTap`, `InjectPointerDoubleTap`, `InjectPointerTripleTap`, `InjectPointerHold`, and `InjectScroll`. Every mouse event now explicitly sets position, delta=zero, and button state, preventing stale state from triggering game camera controllers between actions.
- **Visualizer icon pivot offsets** — `DrawIcon` now uses per-icon hotspot offsets. Mouse-click starburst centers on click point, touch finger aligns fingertip to position, scroll icons center on scroll point.
- **Consecutive drags bleed into each other** — Increased settle time to 4 frames after button release (was 2) and 2 frames before drag start (was 1).

### Added
- **`ClampToScreen()` helper** — Keeps all injected screen positions at least 1px inside screen edges. Applied to all drag, pinch, rotate, and two-finger gesture methods.

### Changed
- **Removed all manual `InputSystem.Update()` calls** — Events are now queued and processed by Unity's natural update cycle, ensuring all scripts (including those checking `wasPressedThisFrame`/`wasReleasedThisFrame`) see each state change correctly.
- **Realistic input timing** — `InjectPointerTap` spreads press/release over ~8 frames (~133ms at 60fps). `InjectTouchTap` spans ~6 frames. All keyboard, gesture, and drag methods use similar realistic spacing.
- **Minimum drag frames increased** — Drag interpolation uses 10 minimum frames (was 5), ~167ms at 60fps for more realistic drag speed.

## [1.2.5] - 2026-02-10

### Fixed
- **Hardware input not restored after test crash** — Added `EditorApplication.playModeStateChanged` hook to restore hardware devices, remove virtual devices, and reset input settings when exiting play mode. Prevents keyboard/mouse from staying disabled if TearDown didn't run.
- **Domain reload leaves hardware devices disabled** — `OnDomainReload` now re-enables all disabled hardware devices (previously only cleaned up virtual devices and cleared stale references).

## [1.2.4] - 2026-02-10

### Fixed
- **Player build failure: IL2CPP linker cannot resolve `nunit.framework`** — Removed `#if UNITY_INCLUDE_TESTS` guards and `overrideReferences`/`precompiledReferences` from the asmdef. NUnit is now provided unconditionally via auto-reference, requiring a Custom NUnit package (or equivalent) that ships `nunit.framework.dll` in player builds.

### Changed
- **`UIAutomationTestFixture` no longer conditional** — The test fixture class is always compiled, removing the `#if UNITY_INCLUDE_TESTS` wrapper.
- **`ThrowTestFailure` always throws `AssertionException`** — No longer falls back to `InvalidOperationException` in non-test builds.

## [1.2.3] - 2026-02-10

### Added
- **`Search.InvokeAsync()` and `Search.InvokeAsync<T>()`** — Await async methods invoked via Reflect paths. `await Search.Reflect("ParseAccountAPI").InvokeAsync("LogoutAsync")` calls the method and awaits the returned Task. `InvokeAsync<T>` returns the typed result from `Task<T>`.

### Changed
- **`Search.Invoke()` now supports static methods** — When `Reflect()` resolves to a type name (e.g., `Search.Reflect("PlayerPrefs")`), `Invoke` uses `BindingFlags.Static` to call static methods directly without an instance.
- **`Search.Invoke()` smart method resolution** — Method matching now handles optional/default parameters (omitted args filled with defaults), implicit numeric conversions (int→long, float→double, etc.), and type-compatible arguments. Exact type matches are preferred over widening conversions.
- **`Search.Invoke()` error messages list available signatures** — When no matching method is found, the error message now includes all available overloads with parameter types and names, making it easier to diagnose mismatched arguments.

## [1.2.2] - 2026-02-10

### Fixed
- **Player build failure: "UnityEditor.dll assembly is referenced by user code"** — Root cause was `UnityEngine.TestRunner` in the main assembly's asmdef references, which chains to `UnityEditor.CoreModule`. Removed the reference entirely; NUnit is now provided via `precompiledReferences: ["nunit.framework.dll"]`.

### Changed
- **`editorInputBehaviorInPlayMode` no longer wrapped in `#if UNITY_EDITOR`** — This setting is in the `UnityEngine.InputSystem` namespace (runtime assembly), not `UnityEditor`. Removed the unnecessary guard.

### Removed
- **`IgnoreErrorLogs` virtual property** — Removed from `UIAutomationTestFixture`. This property set `LogAssert.ignoreFailingMessages` which required `UnityEngine.TestRunner`. If needed, set `UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true` directly in your test's `SetUp()`.
- **`EditorApplication.playModeStateChanged` safety hook** — Removed from `InputInjector`. The `Application.quitting` callback and `InputSystem.settings` restore in `TearDown()` provide sufficient cleanup.
- **All `#if UNITY_EDITOR` / `using UnityEditor` from `InputInjector`** — No editor code remains in the runtime assembly.

## [1.2.1] - 2026-02-10

### Added
- **`UIUtility` public static class** — Single source of truth for screen-space utility methods: `GetScreenPosition(GameObject)`, `GetScreenPosition(Component)`, `GetScreenBounds()`, `GetHitsAtPosition()`, `GetReceiversAtPosition()`. Use this instead of `InputInjector` (which is now internal).

### Changed
- **`InputInjector` screen utilities delegate to `UIUtility`** — `GetScreenPosition`, `GetScreenBounds`, `GetHitsAtPosition`, `GetReceiversAtPosition` are still available on `InputInjector` internally but now delegate to `UIUtility`.
- **`Search` screen utilities delegate to `UIUtility`** — `GetScreenCenter`, `GetScreenBounds`, and private `GetScreenPosition` eliminated duplicate implementations.

## [1.2.0] - 2026-02-10

### Breaking Changes
- **`UITestBase` renamed to `UIAutomationTestFixture`** — Class renamed and moved from `ODDGames.UIAutomation.Testing` namespace to `ODDGames.UIAutomation`. Update: `using ODDGames.UIAutomation;` and change class name in test declarations.
- **`ODDGames.UIAutomation.Testing` assembly removed** — Test fixture now lives in the main `ODDGames.UIAutomation` assembly. Remove `ODDGames.UIAutomation.Testing` from asmdef references.
- **`InputInjector` is now `internal`** — No longer accessible from external assemblies. Use `UIAutomationTestFixture` base class instead, which handles input setup/teardown automatically.
- **`InputInjector.DisableHardwareInput()` / `EnableHardwareInput()` removed** — Replaced by `InputInjector.Setup()` / `TearDown()` lifecycle (called automatically by `UIAutomationTestFixture`).

### Changed
- **`InputInjector` follows `InputTestFixture` pattern** — Explicit `Setup()`/`TearDown()` lifecycle instead of eager initialization on domain reload. `OnDomainReload()` now only cleans up orphaned devices.
- **`InputInjector.Setup()` saves and restores `InputSystem.settings`** — Settings are snapshotted before modification and restored in `TearDown()`, preventing permanent clobbering of game settings like `backgroundBehavior`.
- **`UIAutomationTestFixture` handles full input lifecycle** — `SetUp()` calls `InputInjector.Setup()` (virtual devices, hardware isolation, settings). `TearDown()` calls `InputInjector.TearDown()` (restore everything).
- **All test classes extend `UIAutomationTestFixture`** — Standardized input setup across all tests, removing duplicate manual mouse reset and EventSystem cleanup boilerplate.
- **Consolidated to two assemblies** — `ODDGames.UIAutomation` (runtime) and `ODDGames.UIAutomation.Editor` (editor). Removed AI, Recording, VisualBuilder, and Testing assemblies.

### Removed
- **AI Testing module** — Entire `AI/` directory (actions, models, navigation, screen analysis, strategy, debug, editor panels)
- **Recording module** — `Recording/` directory (input interceptor, recorder, generator, settings)
- **Visual Builder module** — `VisualBuilder/` directory (element selector, visual blocks, runtime compiler, editor panels)
- **Auto Explorer** — `AutoExplorer.cs`
- **Editor hooks** — `InputInjectorEditorHooks.cs`, `UITestLiveRecorder.cs`

## [1.1.43] - 2026-02-09

### Fixed
- **`InjectScroll()` uses `DeltaStateEvent` for correct scroll injection** — Full-state `QueueStateEvent` with `MouseState` was clobbering delta controls that reset each frame. Now uses `DeltaStateEvent.From(mouse.scroll)` matching Unity's `InputTestFixture.Set()` pattern. Scroll event no longer calls `InputSystem.Update()` manually, letting the player loop process it so the scroll delta isn't reset before `InputSystemUIInputModule.Process()` reads it.
- **`ScaleToGameViewWindow()` removed from all input injection** — Was transforming coordinates to Game View render-target space but Input System expects `Screen.width/height` space, causing clicks, scrolls, and gestures to miss their targets.
- **Search ordering preserved** — `Near()`, `OrderBy()`, `OrderByPosition()` etc. were being overwritten by a depth-sort applied unconditionally after post-processing. Depth-sort now only applies as fallback when no custom ordering is set.
- **`EnsureGameViewFocusAsync()` on all injection entry points** — `InjectPointerTap`, `Swipe`, `TypeText`, `InjectPinch`, `InjectTwoFingerDrag`, `InjectRotatePixels` now ensure Game View focus before injecting events.
- **`LogAssert.Expect` added to tests** catching expected `AssertionException` from `ActionScope.Fail()`.

### Changed
- **Input injection uses `DeltaStateEvent` consistently** — Mouse click, hold, and scroll methods now use `DeltaStateEvent.From(control)` for individual controls instead of full `MouseState` structs, matching Unity's `InputTestFixture` pattern and avoiding delta control clobbering.
- **Touch tap simplified to `TouchState` struct** — `InjectTouchTap` now uses `InputSystem.QueueStateEvent(touchscreen, new TouchState{...})` (one line per event) instead of 6-line `WriteValueIntoEvent` calls, matching Unity's `InputTestFixture.SetTouch()` pattern.
- **Click speed dramatically improved** — Press+release now happen in consecutive frames instead of being spread across ~160ms of delays. Single click: ~3 frames (was ~7 frames + 160ms). Double/triple click use compact loop patterns.
- **All internal delays reduced to frame-based waits** — pointerEnter gap reduced from 2 frames + 50ms to 1 frame. Key press/release, modifier combos, and touch taps all use `DelayFrames(1)` instead of `Async.Delay(frames, seconds)`.
- **`ActionExecutor.Interval` default reduced from 200ms to 50ms** — 4x faster waits between high-level test actions.
- **Default drag/swipe/gesture duration halved from 0.3s to 0.15s** — All drag, swipe, pinch, rotate, and two-finger gesture defaults are now twice as fast.
- **Inter-click gap reduced to 50ms** — Double/triple click inter-click delay reduced from 120ms to 50ms (still within multi-click speed threshold).
- **`EnsureGameViewFocusAsync()` caches Game View reference** — Avoids reflection lookup on every call (runs in every delay loop iteration).
- **Virtual input devices created eagerly in `OnDomainReload`** — All hardware devices disabled at init so `<Mouse>/position` etc. can only resolve to virtual devices, eliminating `.current` race conditions.
- **`editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView`** — Routes all virtual device input to Game View during play mode (matches Unity's `InputTestFixture`).
- **Test search timeouts reduced to 0.5s** — All test `searchTime`, `timeout`, and `seconds` parameters reduced from 2s to 0.5s (or 1s for delayed-element tests) to prevent slow hangs on expected failures.

### Added
- **Percentage parameter validation (0-1 range)** on all public API methods — `ClickAt`, `SwipeAt`, `PinchAt`, `RotateAt`, `TwoFingerSwipeAt`, `ClickSlider`, `DragSlider`, `SetSlider`, `SetScrollbar`, `Scroll(direction)`, `Search.InRegion`; throws `ArgumentOutOfRangeException` for values outside 0-1.
- **Full gesture visualization** in InputVisualizer (pinch, rotate, two-finger drag, key presses, text input).
- **3D object click tests** (with and without collider) in InputInjectorTests.

## [1.1.42] - 2026-02-09

### Fixed
- **`InjectScroll()` now actually scrolls** - Added `ScaleToGameViewWindow()` for correct mouse positioning in the Editor (matching `InjectPointerTap`), and wait frames after moving mouse so the EventSystem establishes `pointerEnter` before sending scroll events. Previously scroll events were silently dropped because the mouse was at wrong coordinates and `ProcessPointerScroll()` requires `pointerEnter` to be set.
- **`LoadTestData()` now clears PlayerPrefs** - Clear all PlayerPrefs before restoring test data, while preserving Unity-internal keys (Addressables, Unity Services, IAP). Previously leftover PlayerPrefs from prior test runs would persist and pollute test state.

## [1.1.41] - 2026-02-09

### Fixed
- **Player build failures from test-only assemblies** - Removed `versionDefines` that self-defined `UNITY_INCLUDE_TESTS` from all 6 asmdefs. The define was always active (test-framework is always installed in editor), causing NUnit/LogAssert/TestRunner code to compile in player builds where those assemblies don't exist.

### Changed
- **Testing assembly is now editor-only** - Added `includePlatforms: ["Editor"]` to `ODDGames.UIAutomation.Testing.asmdef` since it depends on `UnityEngine.TestRunner` and `nunit.framework.dll` which are editor-only.
- **AITestDiscovery guard changed to `#if UNITY_EDITOR`** - The entire file uses NUnit and AssetDatabase, so `UNITY_EDITOR` is the correct guard (not `UNITY_INCLUDE_TESTS`).
- **VisualTestRunner no longer depends on NUnit directly** - Uses `Assert.Fail()` via `#if UNITY_EDITOR` for PlayMode test integration, falls back to `InvalidOperationException` in player builds. This allows VisualTestRunner to work on-device.
- **Removed `defineConstraints: ["UNITY_INCLUDE_TESTS"]`** from Editor, VisualBuilder, VisualBuilder.Editor, and AI.Editor asmdefs. Editor asmdefs are already protected by `includePlatforms: ["Editor"]`. VisualBuilder runtime assembly needs to compile in player builds.

## [1.1.40] - 2026-02-07

### Fixed
- **`LogAssert` guard changed from `UNITY_INCLUDE_TESTS` to `UNITY_EDITOR`** - `UNITY_INCLUDE_TESTS` is defined via `versionDefines` even in player builds (when test-framework package is installed), but `LogAssert` lives in `UnityEngine.TestRunner` which is editor-only. Using `#if UNITY_EDITOR` correctly excludes the call from player builds.

## [1.1.39] - 2026-02-07

### Fixed
- **Testing assembly compiles in player builds** - Wrapped `LogAssert.ignoreFailingMessages` in `#if UNITY_INCLUDE_TESTS` guard so `UITestBase` compiles when test-framework is unavailable (e.g. production builds)
- **Testing assembly uses `versionDefines` without `defineConstraints`** - Self-defines `UNITY_INCLUDE_TESTS` when test-framework is present, but always compiles regardless

## [1.1.38] - 2026-02-07

### Fixed
- **Testing assembly no longer uses `defineConstraints`** - Removed `defineConstraints: ["UNITY_INCLUDE_TESTS"]` and set `autoReferenced: false` to prevent the Testing assembly from being skipped by Unity's constraint evaluation. Consumer assemblies reference it explicitly by name instead.

## [1.1.37] - 2026-02-06

### Fixed
- **Testing assembly now self-defines `UNITY_INCLUDE_TESTS`** - Added `versionDefines` to detect `com.unity.test-framework` and satisfy its own `defineConstraints`, fixing `UITestBase` not found errors

## [1.1.36] - 2026-02-06

### Fixed
- **Removed `LogAssert` dependency from core assembly** - Replaced `LogAssert.ignoreFailingMessages` in AITestRunner/AITestDiscovery with existing custom log handler, eliminating the need for `UnityEngine.TestRunner` reference in the core assembly
- **Testing assembly now references `UnityEngine.TestRunner`** - Ensures `LogAssert` works properly in UITestBase

## [1.1.35] - 2026-02-06

### Fixed
- **Removed deprecated `optionalUnityReferences` from all assemblies** - This Unity 6 deprecated field was silently preventing the core assembly from compiling in some projects, causing `ODDGames` namespace not found errors in downstream test assemblies

## [1.1.34] - 2026-02-06

### Fixed
- **Testing assembly now supports PlayMode tests** - Removed `includePlatforms: ["Editor"]` restriction
  - Testing assembly can now be referenced by PlayMode test assemblies
  - Uses `defineConstraints: ["UNITY_INCLUDE_TESTS"]` to ensure exclusion from player builds
  - Fixes "ODDGames namespace not found" errors when referencing UITestBase from PlayMode tests

## [1.1.30] - 2026-02-06

### Fixed
- **Testing assembly now Editor-only** - Added `includePlatforms: ["Editor"]` to Testing assembly
  - Prevents LogAssert/NUnit errors when Test Framework not fully configured in consuming projects
  - Testing assembly only compiles in Editor context where Test Framework is guaranteed

## [1.1.29] - 2026-02-06

### Changed
- **`AITestRunner` and `AITestDiscovery` moved to Testing assembly** - These files now in `ODDGames.UIAutomation.Testing`
  - Fixes build errors when Test Framework not installed (LogAssert/NUnit references)
  - Core runtime assembly (`ODDGames.UIAutomation`) now has zero test dependencies
  - Works correctly in player builds without Test Framework

## [1.1.28] - 2026-02-06

### Changed
- **BREAKING: `UITestBase` moved to separate assembly** - Now in `ODDGames.UIAutomation.Testing`
  - User test assemblies must add reference to `ODDGames.UIAutomation.Testing`
  - Testing assembly only compiles when Test Framework is installed (via `defineConstraints`)
  - Core runtime (`ODDGames.UIAutomation`) no longer has any test dependencies
  - `CaptureUnobservedExceptions` moved to always-available in core assembly (no longer conditional)

## [1.1.27] - 2026-02-06

### Fixed
- **Assembly definitions now auto-define `UNITY_INCLUDE_TESTS`** - Added `versionDefines` to detect Test Framework
  - All assemblies with `defineConstraints: ["UNITY_INCLUDE_TESTS"]` now have matching `versionDefines`
  - Fixes "namespace 'ODDGames' could not be found" errors when Test Framework is installed but define was missing
  - Unity's `UNITY_INCLUDE_TESTS` is not globally defined - must be set per-assembly via `versionDefines`

## [1.1.26] - 2026-02-06

### Fixed
- **All test-related assemblies excluded from builds without Test Framework** - Added `defineConstraints: ["UNITY_INCLUDE_TESTS"]` to:
  - `ODDGames.UIAutomation.Editor` (main editor assembly)
  - `ODDGames.UIAutomation.VisualBuilder` (visual test builder)
  - `ODDGames.UIAutomation.VisualBuilder.Editor` (visual builder editor)
  - `ODDGames.UIAutomation.AI.Editor` (AI test editor)
  - Previously these assemblies would fail to compile when Test Framework was not installed
  - Core runtime assembly (`ODDGames.UIAutomation`) still compiles without tests for runtime-only usage

## [1.1.25] - 2026-02-06

### Fixed
- **UITestBase now compiles in builds without Test Framework** - Wrapped entire class in `#if UNITY_INCLUDE_TESTS`
  - Previously `UITestBase.cs` would cause build errors when Test Framework package was not installed
  - Assembly definition now uses `optionalUnityReferences: ["TestAssemblies"]` for conditional test support

## [1.1.24] - 2026-02-06

### Added
- **`InputVisualizer` cursor fade-out** - Cursor icon now gradually fades over 1.5 seconds after actions complete
  - Configurable via `InputVisualizer.CursorFadeDuration`
  - Previously cursor disappeared instantly

### Changed
- **Realistic input injection timing** - All input methods now simulate realistic user interaction speeds
  - Single click: ~160ms total (80ms button down, 30ms release)
  - Double/Triple click: ~120ms between clicks (was 50ms)
  - Touch tap: ~110ms total (80ms contact, 30ms release)
  - Key press: ~110ms total (80ms down, 30ms release)
  - Scroll: ~160ms total (80ms move to position, 50ms scroll, 30ms reset)
  - Previously timings were 20-50ms which was too fast for realistic simulation

### Fixed
- **`InjectScroll` no longer triggers clicks** - Scroll methods now use explicit `MouseState` struct
  - Previously `StateEvent.From(mouse)` could inherit button state from prior operations
  - Now explicitly sets all button states to released during scroll

## [1.1.23] - 2026-02-06

### Fixed
- **Failure messages now logged before throwing** - `ActionScope.Fail()` logs error message before throwing `AssertionException`
  - Failure reason is now visible in Unity console: `[UIAutomation] FAILED: Click(Text("Button")) failed: Element not found within 10s`
  - Previously the failure message was only in the exception, which could be truncated in test results

## [1.1.22] - 2026-02-06

### Changed
- **All failures now throw `NUnit.Framework.AssertionException`** - Simplified exception handling
  - Removed `UIAutomationException`, `UIAutomationTimeoutException`, `UIAutomationNotFoundException` custom exception classes
  - All action failures (element not found, timeouts, invalid paths) now throw `AssertionException` directly
  - Ensures proper test failure reporting in Unity Test Runner without exception swallowing

## [1.1.21] - 2026-02-06

### Fixed
- **`ActionScope.Fail()` now uses NUnit `Assert.Fail()`** when `UNITY_INCLUDE_TESTS` is defined
  - Previously only threw `UIAutomationTimeoutException` which could be swallowed by async continuations
  - Now properly reports test failures in Unity Test Runner with correct stack traces
  - Non-test contexts still throw `UIAutomationTimeoutException`

## [1.1.20] - 2026-02-06

### Added
- **`InputVisualizer` PNG icon support** - Visual feedback now uses PNG icons loaded from Resources folder
  - Icons: mouse-cursor, mouse-click, touch, touch-tap, scroll-up, scroll-down
  - White icons with black shadows for contrast on any background
- **`Async.Delay(int minFrames, float minSeconds)`** - Wait for both minimum frames AND minimum time
  - Used for timing-critical operations where both frame processing and real-time thresholds matter

### Changed
- **`InputVisualizer` uses OnGUI rendering** - Works without cameras, regardless of render pipeline
  - Previously used GL rendering which required a camera
  - Now renders cursor icons, click ripples, and trails via Unity's immediate mode GUI
- **Improved input injection timing reliability** - All tap/click methods now use `Async.Delay(frames, seconds)`
  - `InjectPointerTap` uses 2 frames + 20ms per step
  - `InjectPointerDoubleTap`/`TripleTap` use 2 frames + 20ms per step, 4 frames + 50ms between clicks
  - `InjectTouchTap` uses 2 frames + 20ms for began/ended phases
  - `InjectTouchDoubleTap`/`TripleTap` use 4 frames + 50ms between taps
  - Fixes sporadic test failures from insufficient timing between input events

## [1.1.19] - 2026-02-06

### Added
- **`InputInjector.GetHitsAtPosition(Vector2)`** - Gets all raycast hits at a screen position using EventSystem.RaycastAll
- **`InputInjector.GetReceiversAtPosition(Vector2, params Type[])`** - Gets GameObjects with specified handler interfaces (IPointerClickHandler, IDragHandler, etc.) at a screen position
- **`Search.RequiresReceiver(params Type[])`** - Filter to elements where receivers with specified handler types exist at the element's screen position
- **`Search.Receivers` property** - Returns the receivers found by the last RequiresReceiver evaluation

### Changed
- **Receiver logging on action completion** - Click, DoubleClick, TripleClick, Hold, Drag, DragTo, Scroll, Type methods now log receiver hierarchy paths when actions complete, showing what GameObjects at the target position have event handler interfaces
- **Optional receiver logging for "At" methods** - ClickAt, DoubleClickAt, TripleClickAt, HoldAt, DragFromTo, Drag(Vector2) methods now have optional `requireReceivers` parameter (default false) to enable receiver logging

## [1.1.18] - 2026-02-05

### Removed
- **`EnsureSceneLoaded()`** from ActionExecutor - use `LoadScene()` (UITestBase) instead
- **`RestoreGameState()`** from UITestBase - use `LoadTestData()` (ActionExecutor) directly
- **`LogPersistentDataState()`** and `FormatFileSize()` private helpers from UITestBase (unused after RestoreGameState removal)

## [1.1.17] - 2026-02-05

### Changed
- **Case-insensitive `Name()` and `Text()` matching** - Both methods now default to `ignoreCase: true`
  - `Name("MainMenu")` matches "mainmenu", "MAINMENU", etc.
  - `Text("Play")` matches "play", "PLAY", etc.
  - Opt out with `ignoreCase: false` for exact-case matching
- **Stability-checked `Search.Find()` and `Search.FindAll()`** - Results must be consistent for 3 consecutive frames before returning
  - Replaces the old synchronous `FindFirst()`/`FindAll()` + separate `Resolve()` pattern
  - Results are ordered: on-screen UI elements first, then on-screen non-UI, then off-screen
  - Logs matched elements with positions on first match for debugging
- **Simplified `ActionExecutor` internal search** - Removed `ResolveSearch()`, `ResolveSearchAll()`, `ResolveDropdown()`, `ResolveDropdownByLabel()`, `ResolveSearch<T>()` recovery-polling methods
  - All search logic now delegates to `Search.Find()`/`Search.FindAll()` with built-in stability checking
  - `Click()`, `Find<T>()`, `FindAll<T>()` are simpler and more reliable
- **`ActionScope.Fail()` uses NUnit `Assert.Fail()`** instead of throwing `UIAutomationTimeoutException` when `UNITY_INCLUDE_TESTS` is defined
  - Provides proper test failure reporting in Unity Test Runner
- **`Exists()` is now async** - `Search.Exists(float timeout)` returns `Task<bool>` with timeout support
- **`Value` property restricted to Reflect paths** - For UI element searches, use `await search.Find()` instead
- **Removed typed value properties from UI element searches** - `StringValue`, `BoolValue`, `FloatValue`, `IntValue`, `Vector3Value`, `Vector2Value`, `ColorValue`, `QuaternionValue` on UI searches removed; use `GetValue<T>()` with `await Find()` instead (Reflect paths still support `GetValue<T>()`)
- **`EnsureSceneLoaded()` waits for stable frame rate** - New `minFps` parameter (default 20 FPS) ensures scene is ready before continuing
- **`LoadScene()` waits for stable frame rate** - New `minFps` parameter (default 20 FPS) on `UITestBase.LoadScene()`
- **Removed `Search.Validate()`** - Use `Exists()` or `Find()` instead

### Added
- **`Search.OrderByName()`** - Sort matches alphabetically by GameObject name (A-Z)
- **`Search.OrderByNameDescending()`** - Sort reverse-alphabetically by name (Z-A)
- **`Search.OrderByInstanceId()`** - Sort by Unity instance ID (ascending)
- **`Search.OrderByInstanceIdDescending()`** - Sort by instance ID (descending)
- **`WaitForStableFrameRate()`** - Wait until frame rate stabilizes at target FPS
  - Configurable `minFps` (default 20), `stableFrames` (default 5), `timeout` (default 10s)
- **`InputInjector.GetClearClickPosition()`** - Placeholder for future occlusion-aware click targeting
- **`UITestBase.IgnoreErrorLogs`** - Virtual property (default: `true`) to ignore error/exception logs during tests via `LogAssert.ignoreFailingMessages`
- **Play mode exit safety** - `InputInjector` re-enables hardware input devices when exiting play mode if they were disabled during testing
- **URP/HDRP `InputVisualizer` support** - Visualization now renders on both built-in and scriptable render pipelines via `RenderPipelineManager.endCameraRendering`
- **`InputVisualizer` early-exit optimization** - Skips rendering when no events to draw
- **Test Runner compact mode** - Toolbar buttons adapt to narrow window widths (< 450px)

### Fixed
- **Input click reliability** - `InjectPointerTap` and `InjectTouchTap` now hold each state for 2 frames instead of 1, improving click registration on complex UIs
- **Camera fallback for world-space canvas** - `GetScreenPosition()` properly falls back to `Camera.main` when `canvas.worldCamera` is null
- **Detected recovery flow** - `RunDetectedFlows()` now uses async `Exists(0.5f)` instead of synchronous check

## [1.1.16] - 2026-02-02

### Changed
- **TimeScale-independent waits** - All timeout and polling operations now use `System.Diagnostics.Stopwatch` instead of Unity's `Time.realtimeSinceStartup`
  - Enables running tests at higher game speeds (e.g., `Time.timeScale = 3`) while keeping real-time waits
  - `ActionExecutor` wait loops use new `Now` property based on `Stopwatch.GetTimestamp()`
  - `Search.Resolve()` timeout checks use `Stopwatch` for consistent behavior
- **Virtual device naming** - Virtual input devices now use `UIAutomation_` prefix for easier identification
  - `UIAutomation_Mouse`, `UIAutomation_Keyboard`, `UIAutomation_Touchscreen`
  - Prevents accumulation of orphaned devices between test runs

### Fixed
- **Domain reload cleanup** - `InputInjector.OnDomainReload()` now cleans up orphaned virtual devices
- **Input timing at high timeScale** - `InputInjector` now uses `Time.unscaledDeltaTime` for frame timing

## [1.1.15] - 2026-02-02

### Added
- **`InputVisualizer`** - GL-based input visualization overlay that works over entire screen
  - Shows clicks, drags, scrolls, and key presses with visual feedback
  - Enable via `ActionExecutor.ShowInputOverlay = true`
  - Works regardless of canvas setup, UI layers, or camera configuration
- **`DialogDismissal`** - Fast recovery handler for dismissing common dialog patterns
  - Looks for buttons matching patterns like "Close", "Cancel", "OK", "Skip", etc.
  - Use as standalone or chain with other recovery handlers
  - Configurable button name and text patterns
- **`ActionExecutor.ChainRecoveryHandlers()`** - Combine multiple recovery handlers in waterfall pattern
  - Each handler tried in order until one succeeds
  - Example: `ChainRecoveryHandlers(DialogDismissal.CreateHandler(), AINavigator.CreateRecoveryHandler())`
- **`ActionExecutor.DetectedRecoveryHandler()`** - Register handlers that run when a Search is detected (preemptive recovery)
- **`ActionExecutor.FailureRecoveryHandler()`** - Register handlers that run when actions fail (fallback recovery)
- **Button click tracking** - Debug which buttons receive click events
  - `ActionExecutor.EnableButtonClickTracking()` / `DisableButtonClickTracking()`
  - `ActionExecutor.LastClickedButton` - Name of last clicked button
- **Virtual input devices** - InputInjector creates virtual devices when hardware unavailable
  - `GetMouse()`, `GetKeyboard()`, `GetTouchscreen()` create devices on demand
  - `InputInjector.CleanupVirtualDevices()` - Remove virtual devices when done
- **Hardware input isolation** - Prevent real input from interfering with tests
  - `InputInjector.DisableHardwareInput()` - Disable all hardware devices
  - `InputInjector.EnableHardwareInput()` - Re-enable hardware devices
- **`TestDataCapture`** - Editor window to capture game state for test fixtures
  - Captures persistent data files and PlayerPrefs to a zip
  - Menu: `Window > UI Automation > Capture Test Data`

### Changed
- **`UITestBase` simplified** - Removed automatic scene loading, now uses explicit `LoadScene()` and `RestoreGameState()` calls
  - `DisableHardwareInput` property (default: true) controls whether hardware input is disabled during tests
  - `TryRecover` method for custom recovery logic
  - `TestMustExpectAllLogs(false)` attribute applied automatically
- **VisualTestRunner refactored** - Significant simplification and cleanup

## [1.1.14] - 2026-01-30

### Fixed
- **Frame-based timing for multi-click operations** - Double/triple click and tap now use `DelayFrames` instead of `Task.Delay` for more reliable input processing
  - `InjectPointerDoubleTap` uses `DelayFrames(3)` between clicks
  - `InjectTouchDoubleTap` uses `DelayFrames(3)` between taps
  - `InjectPointerTripleTap` uses `DelayFrames(2)` between clicks
  - `InjectTouchTripleTap` uses `DelayFrames(2)` between taps
- **Drag operation timing stability** - Added initial frame delay before starting drags to ensure previous input state settles
  - `InjectMouseDrag` now waits one frame before positioning mouse
  - `InjectTouchDrag` now waits one frame before starting touch
  - Fixes flaky tests when consecutive drags occur (e.g., `Swipe_Diagonal`, sequential swipes)

## [1.1.13] - 2026-01-29

### Added
- **No-code AI tests via ScriptableObjects** - Create AI tests as assets that automatically appear in Unity's Test Runner
  - Create via `+ AI Test` button in Test Runner toolbar or `Create > UITest > AI Test`
  - Tests appear under `AI > TestName` in the Test Runner
  - Each `AITest` asset is a ScriptableObject with prompt, knowledge, and configuration
- **Test Runner toolbar buttons** - Injected buttons for AI test workflow
  - `↻` Refresh - Triggers domain reload to refresh test discovery
  - `+ AI Test` - Creates new AITest asset
  - `Edit` - Opens selected AITest asset in Inspector
- **AITestInspector** - Custom inspector showing test discovery status with visual icon
- **`AINavigator`** - Multi-turn conversation recovery for stuck AI tests
  - Automatically engages when AI reports being stuck or taking no action
  - Uses focused recovery prompts to help AI find alternative approaches
  - Configurable max recovery attempts (default: 3)

### Removed
- **`AIPromptTest`** - Script-based test class removed in favor of ScriptableObject-only approach
  - All AI tests should now be created as `AITest` assets
  - Simplifies test creation to pure no-code workflow

### Changed
- **AI test class structure** - Renamed from `AITestDiscovery` to `AI` for cleaner Test Runner display
  - Tests now appear as `AI > TestName` instead of `AITestDiscovery > RunAITest(TestName)`

## [1.1.12] - 2026-01-28

### Removed
- `IgnoreErrors` property - use `LogAssert.ignoreFailingMessages` directly instead (Unity's API doesn't work outside test context)

## [1.1.11] - 2026-01-28

### Added
- `CaptureUnobservedExceptions` property - set to `true` to capture fire-and-forget Task exceptions
- `CapturedExceptions` read-only list and `ClearCapturedExceptions()` method for inspecting captured exceptions
- Test runner assembly reference for test-only features (guarded by `UNITY_INCLUDE_TESTS`)

### Removed
- `UITestAttribute` - unused metadata attribute removed

## [1.1.10] - 2026-01-28

### Changed
- **BREAKING**: `ClickAt(float, float)` and `ClickAt(Vector2)` now interpret values as percentages (0-1) instead of pixel coordinates
  - `ClickAt(0.5f, 0.5f)` clicks at screen center (50%, 50%)
  - Consistent with `PinchAt`, `SwipeAt`, and other percentage-based methods
- All public async methods in `ActionExecutor` now use unified `RunAction` pattern for consistent START/COMPLETE logging
- `ActionScope` refactored to support async awaiter pattern: `await using var action = await RunAction(...)`
- `ActionScope` now ensures main thread before logging START (via `Async.ToMainThread()`)

### Fixed
- Focus stealing during multi-click operations now handled properly
  - `InjectPointerDoubleTap` ensures Game View focus before second click
  - `InjectPointerTripleTap` ensures Game View focus before each click
  - Prevents flaky tests when user steals focus during test execution

## [1.1.9] - 2026-01-28

### Removed
- **BREAKING**: Removed implicit string to Search conversion - `Click("text")` no longer compiles
  - Use explicit search methods: `Click(Text("text"))` or `Click(Name("name"))`
  - This prevents confusion where `"ToggleDebug"` would search for text content, not GameObject name

## [1.1.8] - 2026-01-28

### Added
- `UIAutomationException`, `UIAutomationTimeoutException`, `UIAutomationNotFoundException` exception classes for better error identification
- `LogStart()`, `LogComplete()`, `LogFail()` helper methods for consistent action logging
- `ActionLog` disposable struct for cleaner START/COMPLETE logging pattern
- All public ActionExecutor methods now log START before action and COMPLETE/FAILED after

### Changed
- Log prefix changed from `[UITEST]` to `[UIAutomation]`
- All exceptions now use `UIAutomationException` family instead of generic `TimeoutException`/`InvalidOperationException`
- Default `holdTime` reduced from 0.5s to 0.05s for all drag operations - most drags don't need a long hold before movement
- Default gesture `duration` reduced from 1.0s to 0.3s (Drag, Swipe, Pinch, Rotate, TwoFingerSwipe)
- `ScrollTo` searchTime default increased from 5s to 10f for consistency
- `ClickDropdown` maxWaitTime for options increased from 0.5s to 2s for slow UI
- `InjectMouseDrag` and `InjectTouchDrag` now `internal` - use `InjectPointerDrag` instead
- Removed unused `UITEST_AI` version define from Editor asmdef

### Fixed
- `SwipeAt` now passes `holdTime: 0f` - swipes are immediate drag motions without hold delay
- Swipe/SwipeAt nested logging - internal method now used to prevent double START/COMPLETE logs
- `InjectMouseDrag`/`InjectTouchDrag` timing reliability:
  - Now uses minimum 5 frames for drag motion regardless of timing
  - Yields one frame when `holdTime=0` to ensure initial position registers
  - Yields after final position event before releasing button/touch to ensure delta is processed
  - Fixes flaky swipe tests that failed when timing raced ahead of frame processing
- `SwipeAtInternal` now uses `InjectPointerDrag` (ensures Game View focus)
- Added `EnsureGameViewFocusAsync` to `InjectTwoFingerSwipe`, `InjectPinch`, `InjectTwoFingerDrag`, `InjectRotate`

## [1.1.7] - 2026-01-28

### Fixed
- **CRITICAL**: Fixed missing method errors (`ClickAtAsync`, `TypeTextAsync`, etc.) - AIActionExecutor now uses correct method names
- **CRITICAL**: Fixed `DebugMode` reference in InputInjector to use `ActionExecutor.DebugMode`
- All package source files now properly committed (v1.1.5 and v1.1.6 were incomplete)

### Changed
- Improved package.json `description` field with detailed feature explanation
- Updated root README.md with installation instructions and repo structure
- `/deploy` command now ensures ALL package files are staged before commit

### Added
- `license` field to package.json
- Root README.md with installation guide and private repo token instructions

## [1.1.6] - 2026-01-28

### Added
- `repository`, `changelogUrl`, `documentationUrl` fields to package.json for better Unity Package Manager integration
- `_commitHash` field to package.json for cache invalidation assistance

## [1.1.5] - 2026-01-28

### Changed
- **BREAKING**: Removed `UIAutomation` facade class - use `ActionExecutor` directly
  - `using static ODDGames.UIAutomation.ActionExecutor;` replaces `using static ODDGames.UIAutomation.UIAutomation;`
  - All methods now in single `ActionExecutor` class
- **BREAKING**: `UITestBehaviour` base class completely removed - use standard NUnit `[TestFixture]` pattern
- `ScrollTo()` now uses drag-based input injection instead of direct normalizedPosition manipulation
  - Automatically detects horizontal vs vertical scrolling based on ScrollRect configuration
  - Smart direction detection based on target position relative to viewport center
  - Supports diagonal scrolling for 2D scroll views (both horizontal and vertical enabled)
- Updated all documentation to reflect new `ActionExecutor` API

### Added
- `ScrollToAndClick()` - Scroll to target element and click it in one action

## [1.1.4] - 2026-01-27

### Changed
- Internal classes (`ActionExecutor`, `ElementFinder`, `StaticPath`) now use `InternalsVisibleTo` for test access
- Merged AI assembly into main `ODDGames.UIAutomation` assembly (removed separate `ODDGames.UIAutomation.AI.asmdef`)
- Removed toggle-specific `WaitFor(Search, bool, float)` overload - use `GetValue<bool>()` with standard assertions

## [1.1.3] - 2026-01-27

### Added
- `WaitFor<T>(string path, T expected, float timeout)` - Wait for static path to equal expected value

## [1.1.2] - 2026-01-27

### Changed
- Updated README and documentation to use async Task pattern (no more UniTask.ToCoroutine)
- Added explanation of `using static ODDGames.UIAutomation.UIAutomation;` pattern
- Removed UniTask as a documented dependency (framework uses native async)
- Updated display name to "ODD Games UI Automation"

## [1.1.1] - 2026-01-27

### Changed
- **BREAKING**: Package ID changed from `com.oddgames.uitest` to `au.com.oddgames.uiautomation`
  - Update your `manifest.json` to use the new package ID

## [1.1.0] - 2026-01-27

### Changed
- **BREAKING**: Package folder renamed from `UITest` to `UIAutomation` - clearer naming
- **BREAKING**: New static `UIAutomation` class replaces `UITestBehaviour` base class
  - Use `using static ODDGames.UIAutomation.UIAutomation;` for access to all helpers
  - Tests are now plain `[TestFixture]` classes using `[UnityTest]` with `IEnumerator` and `UniTask.ToCoroutine()`
  - No more inheriting from MonoBehaviour - cleaner, more idiomatic Unity test pattern
- `UITestBehaviour` removed (was marked obsolete)

### Fixed
- **Near() spatial search bounds calculation** - Fixed incorrect text bounds when canvas has non-identity scale
  - Now uses `RectTransform.GetWorldCorners()` instead of `TransformVector()` for reliable world-space bounds
  - Fixes direction checks (Above, Below, Left, Right) failing on scaled canvases

## [1.0.53] - 2026-01-27

### Fixed
- Actually renamed `Search.Static()` to `Search.Reflect()` across all code, tests, and documentation (code changes were missing in v1.0.52)

## [1.0.52] - 2026-01-27

### Changed
- **BREAKING**: `Search.Static()` renamed to `Search.Reflect()` - clearer naming for reflection-based access
  - `Search.Reflect("GameManager.Instance.Score").IntValue`
  - `Reflect("GameManager.Instance")` helper available in test classes

### Added
- **`Property()` dot notation** - Navigate nested properties with a single call
  - `Property("a.b.c")` is equivalent to `Property("a").Property("b").Property("c")`
  - Example: `Reflect("GameModeDrag.Instance").Property("loadedLevel.player.racingLine")`
- **`Deserialize(string json)`** - Deserialize JSON (Newtonsoft) and set as property value
  - Example: `Reflect("GameManager.Instance").Property("PlayerData").Deserialize(@"{ ""Name"": ""Test"" }")`
- **`Serialize(bool indented = false)`** - Serialize current value to JSON string (Newtonsoft)
  - Example: `var json = Reflect("Player.Instance").Property("Stats").Serialize(true)`
- **`Search.New<T>(string json = null)`** - Create new instance, optionally from JSON
  - Example: `var player = Search.New<PlayerData>(@"{ ""Name"": ""Test"", ""Health"": 100 }")`
- **`Search.New(string typeName, string json = null)`** - Create instance by type name (no generic needed)
  - Example: `var player = Search.New("PlayerData", @"{ ""Name"": ""Test"" }")`

## [1.0.51] - 2026-01-17

### Changed
- **BREAKING**: GameObject manipulation methods are now async with timeout support
  - `Disable()`, `Enable()`, `Freeze()`, `Teleport()`, `NoClip()`, `Clip()` now return `UniTask<T>` and require `await`
  - All methods wait up to 10 seconds (configurable via `searchTime` parameter) for elements to appear
  - Throws `TimeoutException` instead of `InvalidOperationException` when element not found
  - Consistent with other action methods like `Click()`, `ClickDropdown()`, etc.

## [1.0.50] - 2026-01-16

### Added
- **GameObject manipulation methods on Search** - Direct methods on Search class for game object control:
  - `.Disable()` - Deactivate a GameObject (returns `ActiveState`)
  - `.Enable()` - Activate a GameObject, can find inactive objects (returns `ActiveState`)
  - `.Freeze(includeChildren)` - Zero velocity and set kinematic on Rigidbodies (returns `FreezeState`)
  - `.Teleport(Vector3)` - Move transform to world position (returns `PositionState`)
  - `.NoClip(includeChildren)` - Disable all colliders (returns `ColliderState`)
  - `.Clip(includeChildren)` - Enable all colliders (returns `ColliderState`)
- **Restoration tokens** - All manipulation methods return state objects that can restore original state:
  - `state.Restore()` - Manually restore to original state
  - `state.Count` - Number of affected components
  - Implements `IDisposable` for automatic restoration with `using()` pattern
  - Example: `using (Name("Player").NoClip()) { /* colliders disabled */ } // auto-restored`
- **Works on both UI searches and static paths**:
  - `new Search().Name("Player").Freeze()`
  - `Search.Static("GameManager.Instance.player").NoClip()`

## [1.0.49] - 2026-01-16

### Added
- **`SetValue(object)` method** - Set property/field values via reflection:
  - Works with `.Property()` chain: `Static("...").Property("field").SetValue(value)`
  - Supports chained property access: `.Property("A").Property("B").SetValue(x)`
  - Works with indexers: `Static("...").Index(0).Property("field").SetValue(value)`
  - Example: `Static("GameManager.Instance").Property("isKinematic").SetValue(true)`

## [1.0.48] - 2026-01-16

### Removed
- **`Search()` helper method** - Removed from UITestBehaviour to allow direct access to `Search.Static()`. Use specific helpers instead: `Name()`, `Text()`, `Type()`, `Static()`, etc., or `new Search()` for advanced chaining.

## [1.0.47] - 2026-01-16

### Added
- **Indexer support for static paths and property navigation**:
  - `Search.Index(int)` method: `.Property("Items").Index(0)`
  - `Search.Index(string)` method: `.Property("Players").Index("Player1")`
  - C# indexer syntax: `.Property("Items")[0]` or `.Property("Dict")["key"]`
  - Inline indexer syntax in paths: `Search.Static("Game.Players[0].Name")` or `"Items[\"key\"]"`
  - Supports chained indexers: `Items[0][1]` or `Players["team"]["player"]`
- **Dot syntax for nested types** - Use `Search.Static("OuterClass.InnerClass.Property")` instead of `"OuterClass+InnerClass.Property"` for more readable paths
- **Right/middle mouse button support for drag operations**:
  - New `PointerButton` enum: `Left`, `Right`, `Middle`
  - All drag methods now accept optional `button` parameter (defaults to `Left`)
  - Example: `await Drag(new Vector2(100, 0), button: PointerButton.Right)`

## [1.0.46] - 2026-01-16

### Changed
- **`Sprite()` replaced with `Texture()`** - More comprehensive texture matching across all visual components:
  - Matches `Image.sprite.name`, `RawImage.texture.name`, `SpriteRenderer.sprite.name`
  - Matches all Renderer types: MeshRenderer, SkinnedMeshRenderer, ParticleSystemRenderer, LineRenderer, TrailRenderer, etc.
  - Searches material textures: `mainTexture`, `_BaseMap`, `_NormalMap`, `_EmissionMap`, and other common shader properties
  - Uses cached Shader property IDs for efficient texture property lookups
- **`Search.FindAll()` now searches all GameObjects** - Changed from RectTransform-only to Transform-based search, enabling search of non-UI objects like SpriteRenderers and MeshRenderers

### Removed
- **`Sprite()` search method** - Use `Texture()` instead, which provides the same functionality plus support for all renderer types

## [1.0.45] - 2026-01-16

### Added
- **Unity type value shortcuts** - New properties for common Unity structs:
  - `Vector3Value` - get Vector3 from static path or element transform
  - `Vector2Value` - get Vector2 from static path or RectTransform
  - `ColorValue` - get Color from static path or Image/Text color
  - `QuaternionValue` - get Quaternion from static path or transform rotation

- **Spatial positioning helpers** - Methods for checking element positions:
  - Screen-space: `ScreenCenter`, `ScreenBounds`, `IsAbove()`, `IsBelow()`, `IsLeftOf()`, `IsRightOf()`, `DistanceTo()`, `Overlaps()`, `Contains()`, `IsHorizontallyAligned()`, `IsVerticallyAligned()`
  - World-space: `WorldPosition`, `WorldBounds`, `WorldDistanceTo()`, `WorldIntersects()`, `WorldContains()`, `IsInFrontOf()`, `IsBehind()`

### Changed
- **`GetValue<T>()` now supports Unity types** - Direct cast for Vector3, Color, Quaternion, etc. before falling back to Convert.ChangeType

## [1.0.44] - 2026-01-16

### Added
- **`Invoke()` and `Invoke<T>()` methods** - Call methods via reflection on static instances or UI element components
  - `Search.Static("GameManager.Instance").Invoke("StartGame")` - void method
  - `Search.Static("Player.Instance").Invoke("TakeDamage", 10f)` - with arguments
  - `Search.Static("Validator").Invoke<bool>("Validate", data)` - typed return value
  - Works on UI elements: `new Search().Name("Dialog").Invoke("Close")`

### Changed
- **Reflection methods now throw exceptions on failure** - `Property()`, `Invoke()`, `GetValue<T>()`, `StringValue`, `BoolValue`, `FloatValue`, `IntValue` now throw `InvalidOperationException` with informative error messages instead of silently returning null/default values. This ensures tests fail immediately with clear diagnostics when reflection operations fail.

## [1.0.43] - 2026-01-16

### Fixed
- **`Click(GameObject)` clicking at wrong position** - Added `Click(GameObject)`, `DoubleClick(GameObject)`, `TripleClick(GameObject)` overloads to fix incorrect click position when clicking GameObjects directly (e.g., in `ClickScrollItems`)

### Changed
- **Drag operations now hold at start position** - All drag methods hold for 0.5s before moving (configurable via `holdTime` parameter)
- **Default drag duration increased to 1.0s** - Up from 0.3-0.5s for more reliable input processing
- **Default gesture duration increased to 1.0s** - Swipe, Pinch, Rotate, TwoFingerSwipe now default to 1.0s duration

### Added
- **`holdTime` parameter on all drag methods** - `DragAsync`, `DragToAsync`, `DragFromToAsync`, `DragSliderAsync` now expose `holdTime` parameter (default 0.5s)

## [1.0.42] - 2026-01-16

### Added
- **`GetValue<T>(search)`** - Get values from UI elements or static paths
  - `GetValue<string>(search)` - text from TMP_Text, Text, InputField
  - `GetValue<bool>(search)` - toggle isOn state
  - `GetValue<float>(search)` - slider/scrollbar value
  - `GetValue<int>(search)` - slider as int, dropdown index, or text parsed as int
  - `GetValue<string[]>(search)` - dropdown options list
  - Also supports static paths: `GetValue<int>("GameManager.Instance.Score")`

## [1.0.41] - 2026-01-16

### Added
- **TestBuilder redesigned target editor** - Inline dropdown + text field instead of element picker popup
  - Search type dropdown: Text, Name, Type, Path, Adjacent, Near, Sprite, Tag, Any
  - Direction dropdown appears for Adjacent/Near search types
  - Suggestions dropdown (▼) shows available elements from scene filtered by search type
  - Target updates live as you type with visual indicator showing match location
- **TargetOverlay** - Visual indicator showing where clicks will occur while editing blocks
  - Crosshair with bounding box rendered via GL in Game view
  - Updates in real-time as target selector changes
- **SearchQuery factory methods**: `Path()`, `Sprite()`, `Tag()`, `Any()`, `Near()`
- **ElementSelector factory methods**: `ByPath()`, `BySprite()`, `ByTag()`, `ByAny()`, `NearTo()`

### Fixed
- **VisualTestRunner block execution** for non-Selectable elements - Now creates ElementInfo on-the-fly
- **Focus stealing in TestBuilder** - Editing fields no longer loses focus on value changes

## [1.0.40] - 2026-01-15

### Added
- **`ClickDropdownItems()`** - Click each dropdown option sequentially
  - Automatically iterates through all options in a Dropdown/TMP_Dropdown
  - Optional `delayBetween` parameter for delay between clicks
  - Example: `await ClickDropdownItems(Name("Dropdown"), delayBetween: 500);`
- **`ClickScrollItems()`** - Click each item in a ScrollRect sequentially
  - Scrolls to each item before clicking to ensure visibility
  - Finds all Selectable children in the scroll view content
  - Example: `await ClickScrollItems(Name("Scroll View"));`

### Fixed
- **Null reference in TestBuilder** when chain item is null in VisualBuilder editor

## [1.0.39] - 2026-01-15

### Changed
- **InputInjector verbose logs now gated behind DebugMode** - Reduces console noise during normal test execution
  - Logs like "InjectPointerTap at (x,y)", "Using mouse input", "MouseDrag start/end" now only appear when `UITestBehaviour.DebugMode = true`
  - Warning logs for missing devices remain visible at all times

## [1.0.38] - 2026-01-15

### Changed
- **`Text()` now matches only the element with the text component** - Breaking change from v1.0.37
  - Previously `Text("Play")` would match a Button that had a child TMP_Text with "Play"
  - Now `Text("Play")` only matches the actual TMP_Text/Text element itself
  - Use `HasChild(Text("Play"))` or `HasDescendant(Text("Play"))` to match parent containers
  - Example migration: `new Search().Text("Submit").Type<Button>()` → `new Search().HasChild(new Search().Text("Submit")).Type<Button>()`
- **Action `Interval` reverted to 200ms** - Was 500ms in v1.0.37, now back to 200ms for faster test execution

## [1.0.37] - 2026-01-15

### Fixed
- **`Text()` now matches only the element with the text** - Previously matched ancestors that contained text in children
  - `Text("Australia")` now only matches the TMP_Text/Text element itself, not parent ScrollArea
  - Fixes incorrect matches where `ClickAny(Text("X"))` would click on ancestor containers

### Changed
- **ActionExecutor logging now shows search query and element name** - Better debugging output
  - Format: `[ActionExecutor] Click(Text("Button")) -> 'ButtonText' at (450, 300)`
  - Shows what was searched for, what was found, and where the action occurred
- **Separated action delay from search polling** - `Interval` (200ms) for action delay, `PollInterval` (100ms) for search loops
  - Prevents flaky tests from actions being too fast while keeping searches responsive
- **Removed verbose debug logging from Adjacent search** - Cleaner console output

## [1.0.36] - 2026-01-15

### Fixed
- **`Adjacent()` no longer matches label itself** - Excludes source text and its parent hierarchy from matching
- **`Adjacent()` uses alignment-aware scoring** - Prefers elements aligned with the label in the perpendicular axis
- **Flaky drag tests** - Added extra frame yield after drag operations for UI to process events
- **Unity serialization depth error** - Added `[SerializeReference]` to `SearchQuery.chain` and `SearchChainItem.search` to fix recursive serialization

### Changed
- **`Adjacent()` finds single nearest element** - Matches only the best-scoring adjacent element based on distance and alignment
  - Combined with other filters like `.Name()` or `.Type()`, requires both the adjacent match AND the filter to be true
  - Use `Near()` for filtering multiple elements in a direction

## [1.0.35] - 2026-01-14

### Fixed
- **`Near()` no longer checks parent/child relationships** - Only uses screen-space positions for matching
  - Elements that contain the anchor text as a child now properly match based on screen position

## [1.0.34] - 2026-01-14

### Added
- **`Near()` static helper in UITestBehaviour** - Can now use `Near()` as a starting point like `Text()` and `Name()`
  - `await Click(Near("Center Flag", Direction.Below).Text("Texture"));`
  - `await Click(Near("Settings"));`
- **Debug logging for `Near()` searches** - Logs element positions and direction checks to help diagnose search issues

## [1.0.33] - 2026-01-14

### Fixed
- **Fixed `Near()` with direction filtering** - Now correctly matches all elements in the specified direction
  - `Near("Center Flag", Direction.Below)` matches ALL elements below the anchor text, ordered by distance
  - The first result from `Find()` is always the closest element
  - Combined with `Text()`, enables reliable selection of duplicates: `Near("Center Flag", Direction.Below).Text("Mask")`
  - Fixes regression from v1.0.32 where `Near()` was completely broken

### Changed
- **`Near()` behavior clarified** - Matches all elements in direction, orders by distance
  - `Matches()` returns true for ALL elements in the specified direction (not just the closest)
  - `Find()` returns results ordered by distance, so the closest element comes first
  - Use `First()` to ensure only the closest is selected: `Near("Label", Direction.Below).First()`

## [1.0.32] - 2026-01-14

### Changed
- **Simplified `Near()` implementation** - BROKEN (fixed in v1.0.33)
  - Removes complex `IsNearestElementToText` check that was causing incorrect filtering
  - Results are ordered by distance to anchor text (closest first)
  - When combined with other filters like `Text()`, the closest matching element is returned first
  - Example: `Text("Mask").Near("Right Flag", Direction.Below)` returns all "Mask" elements below "Right Flag", closest first

## [1.0.31] - 2026-01-14

### Added
- **`ActionExecutor` class** - Unified action execution layer for all test runners
  - UITestBehaviour, VisualTestRunner, and AIActionExecutor now share the same action execution code
  - Ensures consistent behavior across all test execution paths
  - Static async methods for all input actions (Click, Type, Drag, etc.)

### Fixed
- **`Near()` and `Adjacent()` with duplicate text labels** - Correctly handles UI with repeated text
  - When multiple text labels match (e.g., "Mask" under both "Center Flag" and "Right Flag" sections)
  - Now finds the closest matching anchor text to the candidate element first
  - Then verifies the element is the nearest to that specific anchor
  - Both query orderings work: `Near("Right Flag").Text("Mask")` and `Text("Mask").Near("Right Flag")`

### Removed
- **`SearchMethodTests.cs`** - Moved all tests to PlayMode tests in `SearchPlayModeTests.cs`

## [1.0.30] - 2026-01-13

### Changed
- **Extracted Search class to separate file** - `Search.cs` is now a standalone file for better organization
  - All Search functionality remains the same
  - Reduces UITestBehaviour.cs file size for easier maintenance

### Fixed
- **Two-finger gesture reliability** - Added extra frame yields after ending touch gestures
  - Fixes flaky `PinchAt` and `RotateAt` tests at off-center positions
  - Input System now has more time to process touch end states between gestures

## [1.0.29] - 2026-01-12

### Added
- **`Search.Near()`** - Distance-based proximity search for finding elements near text labels
  - `Search.Near("Center Flag")` - finds closest interactable to text using Euclidean distance
  - `Search.Near("Center Flag", Direction.Below)` - optionally filter by direction
  - More lenient than `Adjacent` - works with diagonal/offset layouts
- **`Search.HasSibling()`** - Filter elements that have a sibling matching criteria
  - `Search.ByType<Button>().HasSibling(Search.ByText("Label"))` - find buttons with a sibling containing "Label"
  - `Search.ByType<Button>().HasSibling("HeaderText")` - shorthand for name pattern

### Changed
- **Renamed `ByAdjacent` to `Adjacent`** - Removed "By" prefix for consistency
  - `Search.ByAdjacent("Label:")` → `Search.Adjacent("Label:")`
- **Renamed `Adjacent` enum to `Direction`** - Clearer naming, avoids conflict with method
  - `Adjacent.Right` → `Direction.Right`, `Adjacent.Below` → `Direction.Below`, etc.
- **Renamed target transform methods** - Added "Get" prefix for clarity
  - `Parent()` → `GetParent()` - transforms result to parent
  - `Child(index)` → `GetChild(index)` - transforms result to child at index
  - `Sibling(offset)` → `GetSibling(offset)` - transforms result to sibling at offset
  - These are distinct from filter methods like `HasParent()`, `HasChild()`, `HasSibling()`

## [1.0.28] - 2026-01-12

### Added
- **Visual Test Builder** - Scratch-like visual test creator with drag-and-drop blocks:
  - Block types: Click, Type, Wait, Scroll, KeyHold, Assert, Log, Screenshot
  - Assert conditions: ElementExists, TextEquals, TextContains, ToggleIsOn/Off, SliderValue, DropdownIndex, DropdownText, InputValue, CustomExpression
  - RunCode block - Execute custom C# code at runtime (interpreted)
  - VisualScript block - Trigger Unity Visual Scripting graph events
  - Visual block editor with color-coded blocks, drag reordering, and collapsible groups
  - Test assets stored as ScriptableObjects (`.asset` files)
- **RuntimeCodeCompiler** - Sandboxed C# interpreter for runtime code execution:
  - Supports: Debug.Log, PlayerPrefs (Get/Set/Delete), GameObject.Find().SetActive()
  - Boolean expression evaluation for custom asserts
- **AI Test Generation** - Gemini-powered test generation from natural language:
  - AITestRunner for step-by-step test execution with screenshots
  - ConversationManager for maintaining context across actions
  - GeminiProvider for API communication
  - AIScreenCapture for capturing game state
- **InputInjector public utility class** - Extracted from UITestBehaviour for reuse:
  - `GetScreenPosition(GameObject)` - Get screen position of UI or world objects
  - `GetScreenBounds(GameObject)` - Get screen-space bounding box
  - `InjectPointerTap/Hold/Drag` - Cross-platform pointer input (mouse/touch)
  - `InjectTouchTap/Hold/Drag` - Mobile touch input injection
  - `InjectMouseDrag` - Mouse-specific drag injection
  - `InjectScroll` - Scroll wheel input
  - `TypeText` - Character-by-character text input
  - `PressKey/HoldKey/HoldKeys` - Keyboard input injection
- **Keys fluent builder** for complex key sequences:
  - `Keys.Hold(Key.W).For(2f)` - Hold key for duration
  - `Keys.Hold(Key.W, Key.A).For(2f)` - Hold multiple keys simultaneously
  - `Keys.Hold(Key.W).For(1f).Then(Key.A).For(0.5f)` - Sequential key holds
  - `Keys.Press(Key.Space).Then(Key.W).For(2f)` - Tap then hold
- **KeyHoldIndicator** sample component for visualizing held keys

### Fixed
- **Keyboard input injection** - Keys now properly re-queue state each frame during hold for reliable detection
- **Mouse/touch hold injection** - Added missing `InputSystem.Update()` calls for consistent event processing
- All input injection methods now call `InputSystem.Update()` after state changes

### Changed
- Input injection methods moved from internal UITestBehaviour to public `InputInjector` static class
- Improved consistency across all hold/drag methods with frame-by-frame state updates

## [1.0.27] - 2026-01-10

### Added
- **Random Click methods** for monkey testing within UITestBehaviour:
  - `SetRandomSeed(seed)` - Set seed for deterministic random clicks
  - `RandomClick(filter)` - Click a random clickable element
  - `RandomClickExcept(exclude)` - Click random element excluding patterns
  - `AutoExplore(condition, value, seed)` - Auto-explore UI with stop conditions
  - `AutoExploreForSeconds(seconds, seed)` - Explore for specified duration
  - `AutoExploreForActions(count, seed)` - Explore for N actions
  - `AutoExploreUntilDeadEnd(seed)` - Explore until no new elements
  - `TryClickBackButton()` - Attempt to click back/exit/close buttons
- **AutoExplorer static class** for CI/batch mode and runtime exploration:
  - `AutoExplorer.StartExploration(settings)` - Start exploration from any script
  - `AutoExplorer.StopExploration()` - Stop current exploration
  - `AutoExplorer.IsExploring` - Check if exploration is running
  - `AutoExplorer.OnExploreComplete` / `OnActionPerformed` - Events for monitoring
  - `AutoExplorer.RunBatch()` - Entry point for Jenkins/CI batch mode
  - Command line args: `-exploreSeconds`, `-exploreActions`, `-exploreSeed`, `-exploreDelay`, `-exploreUntilDeadEnd`
- **AutoExplorer runtime component** - Add to GameObject for game loop integration
  - Inspector settings for duration, actions, seed, auto-start
  - Context menu to start/stop exploration
- **Auto-Explore in Test Explorer window** - Dropdown in toolbar with time/action/dead-end options
- **Smarter AutoExplorer** with configurable behavior:
  - **Exclusion patterns** - Skip dangerous buttons (Logout, Delete, Purchase, Quit, etc.)
    - `ExcludePatterns` - Name patterns like `"*Logout*"`, `"*Delete*"`
    - `ExcludeTexts` - Text content like `"Buy Now"`, `"Confirm Delete"`
  - **Action variety** - Interact appropriately with different UI elements:
    - Sliders: Drag to random position
    - Input fields: Type test strings (configurable via `TestInputStrings`)
    - Dropdowns: Open and select random option
    - ScrollRects: Scroll up/down
  - **Priority scoring** - Prefer certain elements:
    - New (unseen) elements get highest priority
    - Modal/popup elements get bonus
    - Buttons > Toggles > Dropdowns > Sliders
    - Center of screen bonus
  - All features enabled by default, configurable via `ExploreSettings`

### Changed
- Consolidated UI Automation menu - removed separate menu items, all functionality now in Test Explorer window
- Test Explorer toolbar now includes Auto-Explore dropdown and Stop button

## [1.0.26] - 2026-01-09

### Added
- **Component overloads** for all interaction methods - enables iterating over `FindAll` results:
  - `Click(Component)` - Click a component directly
  - `Hold(Component, seconds)` - Hold/long-press a component
  - `DoubleClick(Component)` - Double-click a component
  - `Drag(Component, direction)` - Drag a component in a direction
  - `DragTo(Component, Component)` - Drag one component to another (drag and drop)
  - `DragTo(Component, Vector2)` - Drag a component to a screen percentage position
  - `DragTo(Search, Vector2)` - Drag a found element to a screen percentage position
  - `DragTo(string, Vector2)` - Drag a named element to a screen percentage position
  - `Swipe(Component, direction)` - Swipe gesture on a component
  - `Pinch(Component, scale)` - Pinch gesture on a component
  - `TwoFingerSwipe(Component, direction)` - Two-finger swipe on a component
  - `Rotate(Component, degrees)` - Rotation gesture on a component
- **`FindItems(containerSearch, itemSearch)`** - Find container items for iteration
  - Returns `ItemContainer` with `(Container, Item)` pairs
  - Supports: ScrollRect, Dropdown, TMP_Dropdown, VerticalLayoutGroup, HorizontalLayoutGroup, GridLayoutGroup
  - Enables patterns like `foreach (var (list, item) in await FindItems("InventoryList"))`

### Changed
- README.md: Removed "Supported Projects" section
- README.md: Added comprehensive "Best Practices" section with examples for finding elements, interacting with controls, iterating containers, gestures, waiting, and search method selection

## [1.0.25] - 2026-01-09

### Added
- **`Search.First()`** - Take only the first matching element by screen position (top-left to bottom-right)
- **`Search.Last()`** - Take only the last matching element by screen position
- **`Search.Skip(n)`** - Skip the first N matching elements
- **`Search.OrderBy<T>(selector)`** - Order matches by a component property value
- **`Search.OrderByDescending<T>(selector)`** - Order matches by a component property value (descending)
- **`Search.OrderByPosition()`** - Explicitly order by screen position
- **`Search.GetParent<T>()`** - Find component in parent hierarchy (with optional predicate)
- **`Search.GetChild<T>()`** - Find component in children hierarchy (with optional predicate)
- **`Search.InRegion(ScreenRegion)`** - Filter elements by screen region (TopLeft, Center, BottomRight, etc.)
- **`ScrollTo(scrollViewSearch, targetSearch)`** - Auto-scroll a ScrollRect until target element is visible
- **`ScreenRegion` enum** - TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight
- PlayMode tests for new Search methods in `test/Assets/Tests/PlayMode/`
- UITest Explorer window for browsing and running tests (`Window/Analysis/UI Automation/Test Explorer`)

### Fixed
- Input System event processing in batch test mode - added `InputSystem.Update()` calls to force event processing
- Test cleanup between runs - Input System state now properly reset in SetUp/TearDown
- ScrollRect drag direction - fixed inverted scroll direction in `ScrollTo` method
- Mouse drag injection now properly sets delta values for ScrollRect compatibility

### Changed
- All input injection methods (`InjectMouseDrag`, `InjectTouchDrag`, `InjectPointerTap`, `InjectTouchTap`, `InjectMouseScroll`) now call `InputSystem.Update()` after queueing events to ensure reliable event processing in test environments

## [1.0.24] - 2026-01-09

### Changed
- **`Search.WithPredicate()`** renamed to **`Search.With()`** for API consistency
  - `With<T>(predicate)` for component predicates (unchanged)
  - `With(predicate)` for GameObject predicates (renamed from `WithPredicate`)

## [1.0.23] - 2026-01-08

### Added
- **`Search.ByAdjacent()`** - Find interactables by adjacent text labels using spatial proximity
  - `Search.ByAdjacent("Username:", Adjacent.Right)` finds input field to the right of "Username:" text
  - Supports all four directions: `Right`, `Left`, `Below`, `Above`
  - Uses spatial proximity scoring algorithm, not hierarchy-based
- **`Search.IncludeInactive()`** - Chainable method to include inactive (SetActive=false) GameObjects in search
- **`Search.IncludeDisabled()`** - Chainable method to include disabled/non-interactable components in search
- NUnit tests for `Search.ByAdjacent()` in `test/Assets/Tests/Editor/SearchAdjacentTests.cs`
- Runtime sample tests for Adjacent search in `SearchMethodTests.cs`
- Change History section in CLAUDE.md for tracking API changes

### Removed
- **`Availability` enum** - Replaced with chainable `Search.IncludeInactive()` and `Search.IncludeDisabled()` methods
- `Availability` parameter from `Find`, `FindAll`, `Click`, `ClickAny`, `Hold` methods

### Changed
- Default search behavior unchanged (active and enabled only), but now configured via Search instead of method parameter
- `ComprehensiveSampleTest` updated to use correct Search patterns (`Search.ByName()` for panels, text for buttons)
- Form inputs now use `Search.ByAdjacent()` to find fields by their label text

### Migration Guide
```csharp
// Before (old API):
await Find<T>(search, true, 10, Availability.Active | Availability.Enabled);

// After (new API):
await Find<T>(search, true, 10);  // Default: active and enabled only
await Find<T>(search.IncludeInactive(), true, 10);  // Include inactive
await Find<T>(search.IncludeDisabled(), true, 10);  // Include disabled
```

## [1.0.22] - 2026-01-07

### Fixed
- Ambiguous `CompressionLevel` reference (System.IO.Compression vs UnityEngine)
- Runtime/Editor assembly boundary issue in `UITestRecorder` (now uses EditorPrefs directly)

## [1.0.21] - 2026-01-07

### Added
- **UITestSettings** - Centralized settings provider accessible via `Edit > Project Settings > UITest`
  - Gemini API key configuration with validation
  - Model selection (gemini-2.0-flash, gemini-1.5-flash, gemini-1.5-pro, etc.)
  - Quick actions for test generation and sample scene creation
- Automatic migration of old `TOR.*` EditorPrefs keys to new `ODDGames.UITest.*` prefix

### Fixed
- `UITestInputEvents` sibling counting now correctly counts only siblings with the same name (matches `UITestInputInterceptor` behavior)

### Changed
- All recording components now use centralized `UITestSettings` instead of scattered EditorPrefs
- Standardized all EditorPrefs keys to use `ODDGames.UITest.*` prefix
- Removed hardcoded Gemini model - now configurable via settings

## [1.0.20] - 2026-01-07

### Fixed
- Menu item "Create Test Recorder" moved to `Window/Analysis/UI Automation/` for consistency with other menu items

## [1.0.19] - 2026-01-07

### Fixed
- TouchPhase injection now uses enum values directly (fixed `ArgumentException: Expecting control of type 'UInt16'`)
- Gesture methods (`Pinch`, `Rotate`, `TwoFingerSwipe`) now use `Find<Transform>` to work with both UI and 3D objects
- `PanelSwitcher` now finds inactive 3D objects using `Resources.FindObjectsOfTypeAll<Transform>()`
- `DoubleClick` signature simplified with consistent parameter ordering

### Changed
- Standardized debug logging across all gesture methods (removed verbose internal logs)
- `RotateAt` refactored to remove redundant calculations
- Sample `GestureCube` added for 3D gesture demonstrations
- Sample `GestureTargetUI` added for UI gesture demonstrations

## [1.0.18] - 2026-01-07

### Fixed
- `Find<RectTransform>` and other non-Behaviour component types now work correctly
- Drag and drop operations can now find elements properly
- `ClickDropdown` now supports both legacy `Dropdown` and `TMP_Dropdown` components
- `ClickDropdown` uses more robust item finding (handles different Unity naming patterns)

### Added
- `DragTo(source, target)` - Drag one element to another for drag-and-drop testing
- `ClickDropdown(name, index)` - Select dropdown option by index using realistic clicks
- `ClickDropdown(name, label)` - Select dropdown option by label text using realistic clicks
- `ClickSlider(name, percent)` - Click slider at percentage position (0-1)
- `DragSlider(name, fromPercent, toPercent)` - Drag slider between positions
- `DoubleClick(name)` - Double-click an element
- `Scroll(name, delta)` - Scroll wheel input on an element
- `Swipe(name, direction)` - Swipe gesture helper (Left, Right, Up, Down)
- `Pinch(name, scale)` - Two-finger pinch gesture (scale < 1 = pinch in, scale > 1 = pinch out)
- `TwoFingerSwipe(name, direction)` - Two-finger swipe gesture
- `Rotate(name, degrees)` - Two-finger rotation gesture
- `DraggableUI` and `DropZoneUI` sample components for drag-and-drop demos
- `PanelSwitcher` component for sample scene navigation

### Changed
- Simplified `ComprehensiveSampleTest` for faster execution
- Removed EzGUI support (moved to separate package)
- Removed verbose iteration logging from Find method
- Gesture methods (`Swipe`, `Pinch`, `TwoFingerSwipe`, `Rotate`) now use screen-relative percentages instead of fixed pixels for device independence
- Added `fingerDistance` and `fingerSpacing` parameters to gesture methods for customization

## [1.0.17] - 2025-01-07

### Fixed
- `TextInput` now works with TMP_InputField by using direct text property injection (character-by-character)
- Sample scenes use TMP_InputField instead of legacy InputField for proper Input System compatibility

## [1.0.16] - 2025-01-07

### Breaking Changes
- **Removed EZ GUI support**: The `HAS_EZ_GUI` conditional compilation and all EZ GUI/AnB UI SDK integrations have been removed

### Changed
- `TextInput` now uses Input System injection (click to focus, type characters, optional Enter)
- Reduced debug logging verbosity (removed per-frame Find/injection logs)
- Simplified test runner context menus (removed popup dialog)

### Added
- `KeyPressTest` sample for keyboard input testing
- Context menu options: Run Test, Run Test (Clear Data), Run Test with Data Folder/Zip

## [1.0.15] - 2025-01-07

### Breaking Changes
- **Requires Unity Input System**: Projects must now use the Input System Package (New) - legacy Input Manager is no longer supported

### Changed
- Restructured repository: package content now in `package/` subfolder for proper UPM git URL references
- Click/Hold/Drag now use Unity Input System injection for reliable UI event handling
- Sample scenes now use `InputSystemUIInputModule` instead of `StandaloneInputModule`
- Replaced Odin Inspector dependency with `[ContextMenu]` attribute

### Added
- Test Unity project in `test/` folder for framework development
- `ClickFeedback` component for visual click confirmation in samples
- Input System as package dependency (required)
- PressKey/PressKeys methods for keyboard input simulation

### Fixed
- Input System clicks not reaching UI elements (was using wrong input module)

## [1.0.7] - 2025-01-05

### Added
- Sample tests demonstrating UITest framework capabilities
  - BasicNavigationTest - menu navigation patterns
  - ButtonInteractionTest - clicks, toggles, indexes, repeats, availability checks
  - FormInputTest - text input, dropdowns, sliders, form submission
  - DragAndDropTest - scrolling, swiping, drag-and-drop
  - PerformanceTest - framerate monitoring, scene load timing
- SampleSceneGenerator editor tool (UITest > Samples > Generate All Sample Scenes)
- Assembly definitions for Samples module

## [1.0.0] - 2024-12-16

### Added
- Initial package release
- Core UITestBehaviour base class with async test support
- UITestAttribute for test metadata (Scenario, Feature, Story, Severity, etc.)
- Recording system for capturing UI interactions
- Test generator for creating test code from recordings
- Editor toolbar integration for recording
- UITestRunner for batch test execution
- Assembly definitions for proper code organization
- README documentation

### Supported Projects
- MTD (Monster Truck Destruction)
- TOR (Trucks Off Road)
