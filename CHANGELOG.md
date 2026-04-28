# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

## [1.8.7] - 2026-04-29

### Fixed
- **Player builds no longer fail with `Failed to resolve assembly: 'nunit.framework'` during IL2CPP linking.** `UIAutomationTestFixture` was compiled into the runtime DLL via `UNITY_INCLUDE_TESTS`, which baked NUnit type references into the IL — fine for tests, fatal for player builds where `nunit.framework.dll` isn't shipped. The fixture has moved to `Tests/UIAutomationTestFixture.cs` under a new `ODDGames.Bugpunch.Tests` asmdef gated on `UNITY_INCLUDE_TESTS` with `nunit.framework.dll` as a precompiled reference, so it only compiles in the consumer's test-runner context. The runtime's `TestReport` now exposes an `internal SetOutcome(bool)` hook the fixture pushes NUnit's outcome through, keeping pass/fail semantics identical without the runtime DLL referencing NUnit.

## [1.8.6] - 2026-04-28

### Changed
- **SDK three-lane architecture is now structural, not aspirational.** Cross-lane class names align across Java + iOS + C# (`BugpunchClient`, `BugpunchRuntime`, `BugpunchDebugMode`, `BugpunchPoller`, `BugpunchCrashHandler`, …) so a feature owner can grep one identifier and find every implementation. C# `UnityExceptionForwarder` renamed to `BugpunchCrashHandler` to mirror its Java + iOS siblings (which handle native signals; the C# one handles managed exceptions). New `BugpunchPlatform.cs` is the lane router and logs the active lane on startup. iOS got a new `BugpunchRuntime.{h,mm}` that holds the cross-lane runtime state (config / metadata / customData / fps / started / lastAutoReport / reportInProgress) — `BPDebugMode` shrinks to just the consent + recording flow, mirroring Java's split. Cross-lane code on iOS now reads `[BPRuntime shared]` instead of `[BPDebugMode shared]`.
- **`DeviceConnect/` folder retired.** C# files that mirror native siblings (`BugpunchClient.cs`, `BugpunchDebugMode.cs`, `BugpunchPoller.cs`, `BugpunchRuntime.cs`, `BugpunchCrashHandler.cs`, `BugpunchNative.cs`, `BugpunchPlatform.cs`, `CrashDirectiveHandler.cs`, `BugpunchInputCapture.cs`, `BugpunchSurfaceRecorder.cs`, …) live at `package/` root — same flat layout as the Java side. The Remote IDE feature module moved to `package/RemoteIDE/`; `Config/` and `UI/` hoisted to package root. Namespaces collapse: `ODDGames.Bugpunch.DeviceConnect` → `ODDGames.Bugpunch`; `ODDGames.Bugpunch.RemoteIDE` for IDE; `ODDGames.Bugpunch.UI` for UI.
- **`BugpunchRuntime` is the only source of cross-lane shared state in C#.** `BugpunchClient.cs` is no longer where mirrors read config / suppression / host references from — they go through `BugpunchRuntime` exactly like Java's `BugpunchPoller.java` reads `BugpunchRuntime.getServerUrl()`. `BugpunchClient.PushSuppression()` still works; its counter just lives on `BugpunchRuntime` now.

### Added
- **`RemoteIDE/BugpunchUnity.cs` facade.** Single public entry point for the Remote IDE module — `BugpunchUnity.BuildServices(host)` constructs every IDE service and returns the wired `RequestRouter` + `SceneCameraService`. `BugpunchClient.BuildLazyServices` collapsed from 45 lines to one delegate call. Future work can drive remaining IDE service types to `internal` access so the module's public surface is just `BugpunchUnity` + the few types `BugpunchClient` still surfaces.
- **Preflight three-lane gates** (`/deploy-sdk` won't ship past these). New `three-lane` check verifies declared cross-lane mirrors exist on every lane they ship on (manifest pins Runtime / DebugMode / Poller / CrashHandler across all three lanes; Uploader / Tunnel / Screenshot for Java + iOS). New `cross-lane-client` check flags any `BugpunchClient.X` reference inside a C# mirror file — comments and `using` directives are skipped, the rule mirrors how Java/iOS siblings depend on `BugpunchRuntime` not on a "client" type.

### Added
- **"Send to Bugpunch" button on the SDK error overlay** — the expanded SDK-error card (`BugpunchSdkErrorOverlay`) now has a Send button alongside Clear/Dismiss. Tapping it bundles the captured ring buffer (source, message, count, stack for each entry) and enqueues it as an `"exception"` report through the standard upload pipeline. The most-recent entry's stack anchors the server-side fingerprint so recurring SDK errors group into one issue; all entries land in customData with flat `sdkError.entry.N.*` keys + a `bugpunchSdkError: true` flag for dashboard filtering. Disabled when the ring is empty; bypasses the exception-type cooldown since it's user-initiated. Wired on Android (`BugpunchSdkErrorOverlay.enqueueRingAsExceptionReport` → `BugpunchReportingService.reportBug`) and iOS (`-[BPSdkErrorOverlay onSendTap:]` → `Bugpunch_ReportBug`); new `Bugpunch_ResetAutoReportCooldown` C export added to `BugpunchDebugMode.mm` for the cooldown bypass.

### Added
- **Persistent chat-message banner** — when a dev replies in chat the SDK now surfaces a small top pill ("💬 N new messages from a dev") instead of force-opening the full chat board. Tap the pill to open the chat (which marks read); tap the X to snooze until a newer dev message arrives. Shares the visual style of the crash-upload banner, sits just below it so both can be visible at once.
  - Android: new `BugpunchChatBanner` (mirrors `BugpunchUploadStatusBanner`), driven via `BugpunchRuntime.showChatBanner(int)` / `hideChatBanner()`.
  - iOS: new `BugpunchTopBanner.mm` with two singletons (`BPUploadBanner`, `BPChatBanner`). Wires the iOS uploader to render an upload progress pill that previously only existed on Android — feature parity with `BugpunchUploadStatusBanner.java`. Driven via `Bugpunch_TopBanner_ShowChat` / `_HideChat`.
  - C#: `BugpunchClient.ChatReplyHeartbeat` now counts unread QA messages and calls `BugpunchNative.ShowChatBanner(unread)` instead of `UI.BugpunchChatBoard.Show()`. New `OnChatBannerDismissed` / `OnChatBannerOpened` UnitySendMessage receivers handle the snooze-until-newer behaviour.

### Fixed
- **Multi-minute video uploads on exception/crash reports.** The Android recorder's pause-time compensation (`mAccumPausedUs`) was accumulating across every pause/resume cycle (bug form, chat, annotate, tools panel each pause once), and the trim/dump cutoff subtracted it — so after several UI interactions the dump cutoff could drift back several minutes and the resulting video covered the entire session up to that point. The compensation now SETS (not adds) on each post-resume sample and is clamped at `windowSeconds`, so the dump duration is bounded at 2× the configured window even after a single long pause.

### Changed
- **Default `videoBufferSeconds` raised from 30 → 90.** Tier-based fallback (used when the inspector default isn't overridden) becomes 30/60/90s for low/mid/high. Affects in-RAM ring dumps for exception/bug reports; the crash-survivable native ring is still clamped to [30, 90]s.

### Added
- **Per-report log boundary marker.** After a report's logs are snapshotted for upload, the SDK injects a synthetic `Bugpunch.Boundary` line into the live log ring. The next report's log buffer carries that line, and the dashboard's log viewer collapses everything above the most recent boundary into a click-to-expand divider — so testers can see "since last report" at a glance without losing access to earlier context. Wired on Android (`BugpunchLogReader.markBoundary`) and iOS (`BPLogReader markBoundaryWithType:title:`); fires from both auto-reports (`reportBug`) and user-submitted reports (`submitReport` / `Bugpunch_SubmitReport`).

### Added
- **Crash video diagnostic.** When a crash event has no usable video the SDK now uploads a short `bugpunchDiag_video` text attachment with a stable reason token (`ring_missing`, `no_csd_yet`, `no_samples`, `ring_overwritten`, `no_keyframe`, `ring_bad_header`, `ring_truncated`, `remux_threw`) so the dashboard can show *why* video is unavailable instead of silently omitting it. Lands as a game attachment on the issue event — no server schema change. (Android only this round; iOS to follow.)

### Changed
- **Encoder primes with a synthetic black frame on every recorder start (buffer mode + projection mode).** Previously a crash within ~1 second of `EnterDebugMode` produced a video ring with no SPS/PPS (encoder hadn't emitted its first format yet) and the remuxer rejected it as `no_csd_yet`. Both startup paths now push one black frame the moment the encoder is started: buffer mode queues a cached NV12 array via `queueFrame`; projection mode draws onto the input Surface via `lockHardwareCanvas` *before* `createVirtualDisplay` attaches the screen mirror. Either way CSD + first IDR land in ~100-200 ms, so a startup-time crash still has playable video. Black frame slides out of the rolling window as soon as real content arrives.

### Fixed
- **`attachmentsAvailable` builder centralised** in `BugpunchUploader.collectAvailableRequires(attachments, deferred)` (Java) and `BPCollectAvailableRequires` + `BPInjectAttachmentsAvailable` (Obj-C++). Both platforms now patch the phase-1 JSON body with the union of every non-empty `requires` key on the attachments list before write. iOS was previously relying on the server's "absent → admit everything" legacy fallback; that worked at the time but would have broken silently the moment iOS gained any preflight attachment with a `requires` key. Contract is documented in `sdk/docs/upload-manifest.md`.
- **Crash events were uploading without logs / screenshots / breadcrumbs.** The new preflight/enrich flow filters phase-2 multipart fields by the server's `collect[]` response, and the server only includes a field in `collect[]` if the SDK advertised it in `attachmentsAvailable`. The SDK was building that list from `DeferredAttachment.requires` only — regular `FileAttachment.requires` keys (`logs`, `screenshot`, etc.) were never advertised, so every crash event uploaded with just the JSON body and was missing every heavy attachment. Exceptions weren't affected because they go through `BugpunchReportingService` which used the older one-stage path. Now `enqueuePreflight` walks both `attachments` and `deferred` when building `available`, so logs/screenshots come through for crashes too.
- **`startBufferMode` / `start` (projection) now block until the encoder publishes CSD into the ring.** A `CountDownLatch` released from `publishVideoFormat` gates EnterDebugMode return, with a 750 ms safety timeout. Combined with the black-frame primer from this same release, this closes the timing race where a fast crash (e.g. `Marshal.WriteInt32(IntPtr.Zero, 0)` on the first Update after EnterDebugMode) hit the signal handler before any encoded sample existed and produced a `no_csd_yet` ring. Slower crashes (stack overflow's recursion, abort) already worked because they spent enough cycles for CSD to land naturally.
- **Duplicate native crash events.** AEI synthesis was emitting `type:CRASH_NATIVE` while bp.c writes `type:NATIVE_SIGNAL`, so the merge step in `findMatchingCrashFile` never matched and every native crash uploaded twice (once from bp.c, once from AEI). Aligned `mapReasonToType` to bp.c's names so AEI augments the existing .crash with `---AEI---` metadata instead of synthesising a duplicate. Also fixes the dashboard's "Unknown: CRASH_NATIVE" labels — those rows were the duplicates the dashboard didn't recognise.

### Removed
- **Standalone SDK dashboard (`sdk/web/`) deleted.** The package no longer ships a separate React dashboard — `server/web` (bugpunch.com) is the only browser surface. `sdk/web/` had drifted from the server dashboard (its own `authFetch`, login flow, Vite + Tailwind config) without ever reaching parity. Anything that used to live there moves into `server/web` or the Remote IDE; the SDK itself stays focused on the package + native uploaders + CLI.

### Added
- **`sdk/docs/` cross-platform contracts.** New canonical specs that pin shared behavior across the Android Java and iOS Obj-C++ implementations:
  - `sdk/docs/upload-manifest.md` — the on-disk multipart manifest schema (single-stage + two-stage preflight + enrich), pinned constants (`MAX_ATTEMPTS`, timeouts, queue dir), and the field-by-field reference. Both `BugpunchUploader.java` and `BugpunchUploader.mm` link to this doc instead of duplicating the schema in their own headers.
  - `sdk/docs/shake-spec.md` — accelerometer-shake algorithm, pinned constants (500 ms spike window, 2 s cooldown, 2 spikes to fire), and the m/s² magnitude formula on each platform. Both shake detectors now emit a `shake_fired` analytics event with `{platform}` so dashboards surface drift if one side stops firing.

### Changed
- **`BugpunchJson` is now the only JSON-string escape path in the SDK.** Removed 14 redeclared `Esc` / `EscapeJson` / `EscJson` private helpers in service / editor / config files (they all delegate to `BugpunchJson.Esc` now). Dropped one minor behavior divergence: callsites that used to strip `\r` from output now emit a proper `\r` escape — JSON parsers see a literal CR instead of "the CR was here" silently disappearing. Also removed the public `RequestRouter.EscapeJson` API since `BugpunchJson.Esc` covers the same job; `BugpunchClient.cs` callers updated.
- **`BugpunchRetry.ExponentialBackoff(attempt, initialMs, capMs)`.** New helper at `sdk/package/DeviceConnect/BugpunchRetry.cs` replaces two divergent inline backoff formulas (`Math.Pow`-based in `IdeTunnel`, bitshift-based in `SymbolUploadClient`). One implementation, overflow-guarded, used by both call sites. Java/Obj-C++ uploaders pin to the same shape via `sdk/docs/upload-manifest.md`.
- **`Bugpunch.cs` dictionary serializer uses `BugpunchJson.Esc`.** `SerializeTags` / `SerializeDict` / `AppendJsonValue` previously had their own `EscJson` whose control-char output disagreed with the rest of the SDK; that's gone.

## [1.8.0] - 2026-04-26

### Changed
- **Unified ingest endpoint (breaking).** All issue submissions — crashes, exceptions, ANRs, bug reports, feedback items — now POST to a single `/api/issues/ingest` endpoint with a `type` field discriminator (`crash` | `exception` | `anr` | `bug_report` | `feedback_item`). The legacy `/api/crashes`, `/api/reports/bug`, and `/api/feedback` (POST) ingest paths are gone server-side. Two-phase crash uploads now hit `/api/issues/events/{id}/enrich`. Native uploaders (Android Java, iOS Obj-C++) and C# (Bugpunch.cs, FleetSimulator.cs, BugpunchFeedbackBoard.Http.cs) all updated. Legacy `category` field on crash payloads renamed to `type` (Android keeps `category` alongside for one release for backwards compat). Server runs the unified backend; this SDK release is the corresponding client.
- **Crash & bug-report status workflow is now per-project.** The hardcoded `new|open|resolved|ready_to_verify|ignored|regressed` enum is replaced with an admin-editable status pool on the dashboard (Settings → Issue workflow). The SDK doesn't see this directly — status moves are server/dashboard-only — but if you've automated dashboards against the legacy enum, switch to filtering by status `category` (`backlog|unstarted|started|completed|canceled`) instead.

## [1.7.46] - 2026-04-26

### Changed
- **Unread-chat badge on the floating button (#32).** When the dev team has chat replies the player hasn't seen, the in-game floating button shows a small accent-coloured `(N)` badge (`99+` when count >= 100). Android renders it on the recording overlay's red record button (top-right corner of a new `FrameLayout` wrapper, `BugpunchReportOverlay.setUnreadCount(int)`); iOS renders it on the floating debug widget (`BugpunchDebugWidget.mm`, exposed via `Bugpunch_SetUnreadCount(int)`). The count is fetched by the existing `BugpunchPoller` on every other tick (≈ 60s at the default 30s cadence — no second timer) via a new `GET /api/v1/chat/unread` endpoint that returns `{ count, lastFromQaAt }` for the device's thread, counting messages with `Sender == 'qa'` and `read_by_sdk_at IS NULL`. Opening the chat clears the badge instantly (`markRead` on both `BugpunchChatActivity.java` and `BugpunchChatViewController.mm` calls `setUnreadCount(0)` straight after `POST /api/v1/chat/read` so the player isn't waiting for the next poll). Server endpoint reuses the existing `requireAuth` + `X-Device-Id` SDK auth pattern; service layer adds `chatService.getUnreadForDevice(projectId, deviceId)`.
- **Native Android chat board (Phase A of #29).** The "Ask for help" surface is now a real native `BugpunchChatActivity` (`sdk/android-src/.../BugpunchChatActivity.java`) — Messenger-style header + bubble list + composer, HTTP and 5s polling all in Java. The previous flow bounced through `UnitySendMessage` and rendered in C# UI Toolkit on top of the Unity surface; the native version draws above it like every other Bugpunch overlay and matches the picker's look. Bubbles size asymmetrically (4dp tail), URLs autolink, timestamps live underneath. Composer has a `+` attach button that opens an inline pill with **Screenshot** / **Record video** options — screenshot is wired (uses `BugpunchScreenshot.captureThen`, returns to chat with a thumbnail chip in the composer); video shows a "coming soon" toast pending the projection-callback refactor (#30). Pending attachments render as removable chips above the input row and are uploaded multipart to `POST /api/v1/chat/upload` on send. Inline **Approve / Decline** buttons render on incoming `scriptRequest` / `dataRequest` bubbles per #31 — answering hits `POST /api/v1/chat/request/answer` and stamps the bubble with a final-state badge; full ScriptRunner JNI bridge is the follow-up. `AndroidDialog.ShowChatBoard` no longer round-trips to C# on Android; iOS / Editor / Standalone fallback path is unchanged.
- **Native iOS chat board (Phase B of #29).** Same surface ported to Obj-C++ as `BugpunchChatViewController` (`sdk/package/Plugins/iOS/BugpunchChatViewController.mm`), a full-screen `UIViewController` that mirrors the Android Activity — Messenger header (avatar circle + title + subtitle + close ✕), `UIScrollView` of bubble cells with asymmetric corner masks (`UIBezierPath` `byRoundingCorners:` per side), composer with `+` attach toggle, attach pill with **📷 Screenshot** / **🎥 Record video**, pending-attachment chips, inline **Approve / Decline** for `scriptRequest` / `dataRequest`, URL autolink via `NSDataDetector`, 5s polling on `NSTimer`, multipart upload via `NSURLSession`. `Bugpunch_ShowChatBoard` in `BugpunchReportOverlay.mm` no longer fires `UnitySendMessage("OnShowChatBoardRequested")` — it presents the new VC directly via `UIModalPresentationFullScreen`. Reuses the shared `[BPDebugMode shared].config` `serverUrl` / `apiKey` accessors (same pattern as `BugpunchPoller.mm` / `BugpunchDirectives.mm`), `Bugpunch_GetStableDeviceId` for the `X-Device-Id` header, and `Bugpunch_CaptureScreenshot` for the screenshot capture. Video stub mirrors Android — shows a "coming soon" alert pending the consent/recording refactor (#30). The C# `BugpunchChatBoard.cs` stays as the Editor / Standalone fallback.
- **Approved `scriptRequest` / `dataRequest` chat bubbles now actually run (#31).** Both native chat surfaces previously stamped the bubble approved and posted a hard-coded `"✓ Approved on device. Script runner bridge pending — full output will arrive once #31 is wired."` placeholder. The native `runScriptSafe` stub is gone (Android `BugpunchChatActivity.answerRequest` / iOS `BugpunchChatViewController -answerRequest:approved:`) — instead, on Approve native base64-encodes the body, builds `<messageId>|<base64>`, and `UnitySendMessage`s `BugpunchClient.OnApprovedScriptRequest` / `OnApprovedDataRequest`. The new C# receivers in `BugpunchClient.cs` decode the source, run it through the existing `ScriptRunner.Execute(...)` (returns the `{"ok":true,"output":"..."}` JSON envelope), and POST the result to `/api/v1/chat/message` with `inReplyTo: messageId` — picked up by the next native poll and rendered as a normal team-side bubble. `dataRequest` is wired the same way but stubbed for v1 (`"Got your data request: …\n\nTunnel-based collectors are next."`) — full collector pipeline is the next slice. No JNI bridge, no extra surface on `ScriptRunner` — UnitySendMessage is the only seam.
- **Chat video attachments (#30).** The `🎥 Record video` option in the chat composer's attach pill is now real on both platforms — previously a "coming soon" toast / alert. Tapping it dismisses the chat to the live game view, asks for screen-record consent, floats a red "Stop ⏺" pill anchored top-center on the host, and on Stop hands the resulting `.mp4` back to the chat as a pending attachment chip. On send it multipart-uploads to the existing `POST /api/v1/chat/upload` and rides through with the message. **Android:** generalised `BugpunchProjectionRequest` (`sdk/android-src/.../BugpunchProjectionRequest.java`) — added a `JavaProjectionCallback` interface + `requestForJavaCallback(Activity, JavaProjectionCallback)` overload so the consent result can flow to a native callback instead of always bouncing through `UnitySendMessage`; the legacy Unity-callback `request(...)` overload (used by the bug-report ring-buffer flow) is untouched and still does the buffer-mode fallback on denial. Added a single-segment write path to `BugpunchRecorder` (`startSegmentToPath(activity, resultCode, resultData, dpi, outputPath)` + `isLastSegmentValid()`) which lazily opens a `MediaMuxer` once the encoder reports its output format, pipes every keyframe-aligned sample to disk alongside the ring buffer, and finalises on `stop()`. `BugpunchProjectionService` learns a new `EXTRA_SEGMENT_OUTPUT_PATH` extra and routes to `startSegmentToPath` when present so the chat path reuses the existing FG-service plumbing (Android 14+ requirement) without forking. `BugpunchChatActivity.startVideoAttachment` does the actual orchestration — `moveTaskToBack(true)` to hide the chat, request consent, start the FG service in segment mode, show a `WindowManager`-overlaid Stop pill (`accentRecord` fill + white dot + "Stop ⏺", anchored top-center 48dp from top), and on Stop call `BugpunchRecorder.getInstance().stop()`, validate the file, re-launch the chat with `FLAG_ACTIVITY_REORDER_TO_FRONT`, and append the MP4 path as a pending attachment. **iOS:** chat VC video stub replaced — uses the existing `ODDRecorder` (`ODDRecorder.h` / `.mm`) which wraps `RPScreenRecorder.startCapture` (system consent prompt is built-in) and writes a single MP4 via `AVAssetWriter`. `BugpunchChatViewController -onStartVideo` dismisses the chat, starts the recorder targeting `NSTemporaryDirectory()/bp_chat_video_<ms>.mp4`, and floats a Stop pill on the key window with the same `accentRecord` styling as Android. On Stop tap, it calls `ODDRecorder_Stop`, validates the file, re-presents the chat VC, and adds the MP4 as a pending chip via the new `-appendPendingVideoAttachment:` hook. Bug-report video flow (`BugpunchRingRecorder` ring buffer) is untouched on iOS. **Server:** `chatService.ts` adds `video/mp4` to the `ALLOWED_ATTACHMENT_MIMES` map, introduces `MAX_VIDEO_ATTACHMENT_BYTES = 30 * 1024 * 1024`, and exposes `maxBytesForAttachmentMime(mime)` so the route layer can apply a per-MIME cap (multer ceiling stays at 30 MB; images are still gated at 5 MB). `AttachmentDescriptor.type` widens from `"image"` to `"image" | "video"`; `parseAttachmentsJson` accepts both kinds, and `parseInboundAttachments` in `chat.routes.ts` now allows `type: "video"` and cross-checks the type/mime pair to catch mismatches. The three chat-upload routes (`/api/v1/chat/upload` + the two dashboard variants) all enforce the per-MIME cap with a 413 response on overflow. `attachmentService.uploadAttachment` mirrors the per-MIME cap for any direct caller. The existing static `/api/files/*` middleware serves `.mp4` with the right `video/mp4` content-type out of the box. The C# Editor / Standalone fallback chat board (`BugpunchChatBoard.cs`) is untouched — video remains device-only for v1.
- **Native iOS feedback board (Phase B part 2 of #29).** Same surface ported to Obj-C++ as `BugpunchFeedbackViewController` (`sdk/package/Plugins/iOS/BugpunchFeedbackViewController.mm`), a full-screen `UIViewController` that mirrors the Android Activity — three views (List / Detail / Submit) swapped by replacing the body container's children, same vote-pill model (vertical pill on rows, horizontal "N votes" pill in the detail; 48pt+ tap target; accent fill when voted; pill's own `UITapGestureRecognizer` swallows the row tap so an upvote doesn't open detail), inline similarity prompt with **Vote for that** / **Post mine anyway** when `/api/feedback/similarity` returns a match scoring > 0.85. Detail view renders the body with `NSDataDetector`-underlined URLs (no markdown for v1, matching Android), image-thumb attachments (local-path bitmap or 📷 placeholder + tap-to-open for server URLs), comments list with author + staff badge, dense comment composer (📷 attach + `UITextView` + accent ➤ send circle). Submit view has title + description + screenshot attach; on send it hits `/api/feedback/similarity` first, then either creates directly or routes through the inline similarity prompt. HTTP, multipart upload, optimistic vote-flip and reconciliation all in `NSURLSession` — endpoints: `GET /api/feedback?sort=votes`, `POST /api/feedback`, `POST /api/feedback/similarity`, `POST /api/feedback/<id>/vote`, `GET|POST /api/feedback/<id>/comments`, multipart `POST /api/feedback/attachments`. `Bugpunch_ShowFeedbackBoard` in `BugpunchReportOverlay.mm` no longer falls through to the C# board — it presents the new VC directly via `UIModalPresentationFullScreen`. `IOSDialog.ShowFeedbackBoard` now calls the native extern instead of `BugpunchFeedbackBoard.Show()`. Reuses the shared `[BPDebugMode shared].config` `serverUrl` / `apiKey` accessors (same pattern as `BugpunchPoller.mm` / `BugpunchChatViewController.mm`), `Bugpunch_GetStableDeviceId` for the `X-Device-Id` header, `Bugpunch_CaptureScreenshot` for screenshot attach (with the same dismiss → snap → re-present dance the chat VC uses), and `BPTheme` / `BPStrings` for colours / fonts / strings. The C# `BugpunchFeedbackBoard.cs` stays as the Editor / Standalone fallback.
- **Native Android feedback board (Phase A part 2 of #29).** The "Request a feature" surface is now a real native `BugpunchFeedbackActivity` (`sdk/android-src/.../BugpunchFeedbackActivity.java`) with three views in one Activity: list (search field + vote pills + `+ New feedback` CTA), detail (back chevron, full body with `Linkify.WEB_URLS` autolinking — markdown rendering deferred per #29 v1 simplification — image-thumb attachments, comments list, dense single-line composer with screenshot attach), and submit (title / description / screenshot attach with similarity check on send). HTTP, similarity scoring, vote toggle, comments and multipart upload all live in Java — endpoints: `GET /api/feedback?sort=votes`, `POST /api/feedback`, `POST /api/feedback/similarity`, `POST /api/feedback/<id>/vote`, `GET|POST /api/feedback/<id>/comments`, multipart `POST /api/feedback/attachments`. Vote pill is a 48dp+ tap target that fills with `accentFeedback` when voted and stops row-click propagation so an upvote doesn't open the detail. Submit hits the similarity endpoint first; a match scoring > 0.85 routes through an inline prompt with **Vote for that** / **Post mine anyway** before creating a duplicate. Screenshot capture uses the existing `BugpunchScreenshot.captureThen`; video attachments are out of scope per #30. New `INativeDialog.ShowFeedbackBoard()` — Android launches the Activity via `BugpunchReportOverlay.showFeedbackBoard()`; iOS / Editor / Standalone keep falling back to the C# `BugpunchFeedbackBoard.Show()`. `BugpunchRequestHelpPicker.OnRequestFeature` and the recording-bar feedback shortcut now route through the dialog factory so the picker → native Activity wire-up is consistent across surfaces.

## [1.7.45] - 2026-04-25

### Added
- **`[Watch]` attribute drives the Watch panel.** New `WatchAttribute` (`sdk/package/WatchAttribute.cs`, global namespace) lets devs annotate any field/property on a `MonoBehaviour` with `[Watch]`, optional `group`, `min`/`max`, and a `WatchOwner` enum (`Self` / `Parent` / `Root`). `WatchService` scans loaded scenes on startup and after every `sceneLoaded`/`sceneUnloaded` event (rebuild is lazy on the next `/watch/poll` so there's no frame hitch on scene change), registering each annotated member as a "declared" watch entry. The Remote IDE Watch panel renders declared entries in collapsible sections grouped by `group ?? ownerName`, with sliders for ranged numerics and toggles for booleans, while user-pinned watches keep working below. New endpoint `POST /watch/rescan` for manual refresh; declared-watch metadata rides along the existing `/watch/poll` and `/watch/apply` pipelines so the polling cadence and write path are unchanged. `WatchAttribute` + `WatchOwner` are added to `link.xml` so IL2CPP doesn't strip them off user fields. `WatchService.ClearAll` now flags declared watches dirty so they reappear automatically on the next poll — clearing only wipes user-pinned entries from the user's POV.
- **Shader / Material Profiler in the Remote IDE.** New `ShaderProfilerService` (`sdk/package/DeviceConnect/Services/ShaderProfilerService.cs`) registered on `RequestRouter` under `/shader-profile/*`. It enumerates every `Renderer` in the loaded scenes (incl. inactive + DontDestroyOnLoad), groups them by shader name (or material instance), then runs a coroutine sweep that — with `Time.timeScale = 0` and `AudioListener.pause = true` to freeze spawners/animation/physics so the snapshot stays valid — hides everything outside the current group, samples `Time.unscaledDeltaTime` for N seconds, and ranks each group by added avg/p99 ms vs a baseline measurement. Endpoints: `GET /shader-profile/groups`, `POST /shader-profile/start`, `GET /shader-profile/status`, `GET /shader-profile/result`, `POST /shader-profile/cancel`, `POST /shader-profile/spotlight`. Restoration of renderer states + timeScale + audio pause is done in a `finally` block, on `Cancel`, on error, and on `OnDestroy` so the device is never left in a partially-disabled state. The matching web panel lives in the bottom tabset alongside Console / Script / Watch and offers an automatic "Run Auto Sweep" button plus a per-group "Spotlight" toggle for manual eyeballing.

### Changed
- **Hierarchy children include component types.** `HierarchyService.GetChildren` now emits a `components` array (e.g. `["Transform","Camera","AudioListener"]`) on every child node so the Remote IDE Hierarchy panel can search by component type ("Camera", "Rigidbody") in addition to GameObject name. The web search box gains an inline ✕ clear button (Esc also clears) and a `Match: any | name | type` segmented selector below it — choose the scope explicitly; the choice persists in `localStorage`. Placeholder text and search semantics adapt to the selected mode. Older SDK builds that don't ship the `components` field continue to fall back to name-only matching.

## [1.7.43] - 2026-04-25

### Changed
- **Snapshots no longer bundle SDK-internal folders.** `FileService.StartZipJob` accepts an `excludeDirPrefixes` query param; the server passes `bugpunch_` for snapshot creation so directories like `bugpunch_uploads`, `bugpunch_crashes`, `bugpunch_picks`, and `bugpunch_snapshots` are skipped. Restores mirror this — `UnzipToDirectory` now takes `preserveDirPrefixes`, and the server's `/restore` route passes the same prefix so `clearFirst` no longer wipes the live upload queue / crash spool. Snapshots taken before this version still restore correctly; they're just larger than they need to be.
- **Recording pill simplified.** Dropped the screenshot button from the floating debug widget on Android (`BugpunchDebugWidget.java`) and iOS (`BugpunchDebugWidget.mm`) — the report flow already grabs context shots automatically, the manual button was redundant. The Tools button now renders an icon (Android: new `toolbox` glyph in `BugpunchToolsActivity.FeatherIcon`; iOS: SF Symbol `wrench.and.screwdriver`) instead of a "Tools" text label, matching the report button's visual weight. Removed the now-orphaned `OnManualScreenshot` / `DrainManualScreenshots` plumbing from `DebugToolsBridge.cs`.

## [1.7.42] - 2026-04-25

### Fixed
- **ANRs now ship logs.** The Android ANR watchdog (`BugpunchCrashHandler.AnrWatchdog.writeAnrReport`) and the iOS ANR writer (`BugpunchCrashHandler.mm` `write_anr_report`) only wrote screenshots + thread stacks to the `.crash` file; the `logs:` field that the native signal handler emits was missing, so the drain had nothing to attach. Both watchdogs now snapshot the rolling log buffer (`BugpunchLogReader.snapshotText` / `[BPLogReader snapshotText]`) into a sibling file and add a `logs:` line. iOS drain at `BugpunchDebugMode.mm` was also updated to read the new field and queue the file as a `text/plain` `logs` attachment alongside the existing screenshot. Crashes already had this — ANRs were the odd one out.

## [1.7.41] - 2026-04-24

### Added
- **`Bugpunch.RequestHelp()` — new in-game picker (public API).** One call surfaces a modal with three large buttons so the game only has to wire a single "need help?" button: **Record a bug** (enters the existing debug-mode + consent + ring-buffer flow), **Ask for help** (opens the live chat board — see below), **Send feedback** (opens the in-game feedback board). Each row now carries its panel artwork (48pt icon) in the UIToolkit fallback and both native surfaces — PNGs live under `Resources/bugpunch-help-*.png`, `Plugins/iOS/bugpunch-help-*@{,2x,3x}.png`, and `android-src/.../res/drawable/bugpunch_help_*.png`, with a graceful fall-back to the previous coloured accent-dot if a resource is missing.
- **Native RequestHelp picker on Android + iOS.** Routes through `INativeDialog.ShowRequestHelp` (Android `BugpunchReportOverlay.showRequestHelp` — WindowManager overlay matching the welcome card's `#F0222222` / 16dp styling, with system-back + backdrop-tap cancel; iOS `Bugpunch_ShowRequestHelp` in `BugpunchReportOverlay.mm` — custom `UIView` backdrop + card mirroring the existing welcome-card look, with tap-outside cancel). UI Toolkit is the fallback for Editor + standalone platforms. Callback plumbing mirrors the existing `ShowReportWelcome` pattern — Android `UnitySendMessage` through `BugpunchReportCallback` (`OnRequestHelpChoice` / `OnRequestHelpCancel`); iOS `[MonoPInvokeCallback]` function pointers.
- **Feedback board (Canny-style, in-game).** `BugpunchFeedbackBoard` is pure UI Toolkit — three views in one card: searchable vote list, new-feedback submit form, similarity prompt. On submit the SDK calls the server's `/api/feedback/similarity` endpoint; if similarity passes the configured threshold the user is offered a "Vote for that instead" button (with a "Post mine anyway" escape hatch) before creating a duplicate. List pulls from `GET /api/feedback?sort=votes`, votes toggle via `POST /api/feedback/{id}/vote`. All HTTP uses `UnityWebRequest` with `Authorization: Bearer {apiKey}` + `X-Device-Id`.
- **Live chat board (`BugpunchChatBoard`).** "Ask for help" now opens a real threaded chat UI (list ↔ new-thread ↔ thread views) with 5-second polling while a thread is open, auto-mark-read on entry, off-hours banner driven by `GET /api/v1/chat/hours`, and two-tone bubbles (SDK user right/green, QA left/grey with name + timestamp). Routing: the native picker implementations on Android + iOS are thin shells that fire `UnitySendMessage("BugpunchReportCallback", "OnShowChatBoardRequested", "")` — `BugpunchClient` receives the message and shows the C# board, so chat is a single C# surface across all platforms. Server endpoints consumed: `POST /api/v1/chat/threads`, `POST /api/v1/chat/threads/:id/messages`, `GET /api/v1/chat/threads/mine`, `GET /api/v1/chat/threads/:id/messages?since=`, `POST /api/v1/chat/threads/:id/read`, `GET /api/v1/chat/hours`.
- **Chat reply popup heartbeat.** `BugpunchClient` now runs a 30-second `ChatReplyHeartbeat` coroutine that sums `UnreadCount` across `GET /api/v1/chat/threads/mine`. When the sum goes positive AND `SuppressInteractions` is currently `false`, the chat board auto-opens so the player sees the QA reply the moment it's safe to interrupt. A `_chatReplyPopupShown` guard prevents re-opening on every tick; the guard resets when the server reports zero unread.
- **Recording-bar shortcut icons.** While the recording overlay is live, two vector-drawn shortcut buttons stack above the record circle on Android + iOS: a blue speech-bubble (chat) and a green lightbulb (feedback). Taps bounce via `UnitySendMessage` to `ReportOverlayCallback` and open `BugpunchChatBoard.Show()` / `BugpunchFeedbackBoard.Show()`. Icons are drawn with Canvas / CoreGraphics — no new PNG assets.

### Changed
- **`SuppressInteractions` is now ref-counted (BREAKING for direct setters).** The old public setter on `BugpunchClient.SuppressInteractions` has been removed. Use `PushSuppression()` which returns an `IDisposable` — dispose to decrement. Read `SuppressInteractions` (bool) or `SuppressInteractionsCount` (int) to observe depth. This lets multiple systems (cutscenes, tutorials, QA reply popup, directive questions) stack suppression without stomping each other. Internal `SuppressInteractions = true/false` call sites have been migrated to `PushSuppression()` / token disposal.

## [1.7.40] - 2026-04-24

### Fixed
- **IAP sub-asmdef now references the right Unity asmdef name.** v1.7.38 referenced `Unity.Purchasing`, but `com.unity.purchasing` 4.0+ publishes its runtime under `UnityEngine.Purchasing.asmdef` (named to match the namespace). With the wrong name the IAP reference didn't resolve and consumers hit the same `CS0234: The type or namespace name 'Purchasing' does not exist in the namespace 'UnityEngine'` family of errors. Changed the reference to `UnityEngine.Purchasing`.

## [1.7.39] - 2026-04-24

### Fixed
- **PlayerPrefs Windows enumeration no longer needs a `Microsoft.Win32.Registry.dll` asmdef reference.** v1.7.37 added the DLL to `precompiledReferences` but Unity's resolution of that DLL varies by API compatibility level (.NET Framework bakes the types into mscorlib; .NET Standard 2.1 ships them separately and the BCL location differs between Editor and target platforms) — consumer projects kept hitting `CS1069`. `PlayerPrefsService.EnumerateWindows` now reaches `Microsoft.Win32.Registry` via reflection (`Type.GetType` with two probe names), so no compile-time type reference is required. Code path stays behind `#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN`, so Windows is still the only place it runs.

## [1.7.38] - 2026-04-24

### Fixed
- **Unity IAP integration moved to its own sub-asmdef.** The IAP file at `Purchasing/BugpunchPurchasing.cs` ships under `ODDGames.Bugpunch.Purchasing.asmdef`, gated by `defineConstraints: ["BUGPUNCH_HAS_UNITY_IAP"]` + `versionDefines` on `com.unity.purchasing >= 4.0`. Previously the file lived in the main asmdef and referenced `UnityEngine.Purchasing.*` types with only a `#if` guard — but the main asmdef had `overrideReferences: true` and no reference to `Unity.Purchasing`, so consumers that actually had IAP installed hit `CS0234: The type or namespace name 'Purchasing' does not exist in the namespace 'UnityEngine'` (and 16 similar). The sub-asmdef only compiles when IAP is present and declares the `Unity.Purchasing` reference locally — resolves cleanly when IAP is installed, silently inert otherwise.

## [1.7.37] - 2026-04-24

### Fixed
- **`Microsoft.Win32.Registry` reference restored on the UPM asmdef.** The SDK asmdef uses `overrideReferences: true` and had dropped `Microsoft.Win32.Registry.dll` from `precompiledReferences`, so `PlayerPrefsService.EnumerateWindows()` failed to compile in consumer projects with `CS1069: The type name 'Registry' could not be found in the namespace 'Microsoft.Win32'`. Re-added the reference — it's part of Unity's .NET Standard 2.1 extras and resolves cleanly on Windows Editor + standalone where the code path runs.
- **WebRTC live stream no longer renders the RT every game frame.** `WebRTCStreamer.RenderLoop()` was yielding on `WaitForEndOfFrame` and blitting every single frame regardless of the requested stream fps, so a 60 fps phone driving a 30 fps stream was doing 2× the `Graphics.Blit` / scene-camera `Camera.Render()` work for frames libwebrtc would never encode — which both cooked the device and let frames pile up inside the Unity-WebRTC binding's internal queue on bad networks. The loop now paces video blits via `Time.unscaledTime` against `1f / _fps` and catch-up bursts are clamped (next blit = `max(scheduled, now)`). Metadata sends are separately paced at 10 Hz (down from "every other rendered frame", which scaled with game fps). Net effect on a 60/30 setup: ~50% less streamer work, no added latency when the stream is healthy.

### Changed
- **`/files/zip` is now an async job with real progress.** The previous endpoint blocked the tunnel for the whole zip + base64 round-trip, which ran for minutes on big `persistentDataPath` trees and gave no feedback. Replaced with `/files/zip/start` (returns a `jobId` immediately), `/files/zip/progress?jobId=` (stage + `processedFiles/totalFiles` + `bytesWritten`), and `/files/zip/result?jobId=` (streams the base64 once done). C# version runs on `Task.Run`; Android version runs on a dedicated low-priority executor. `SnapshotsPanel` and `FileBrowserPanel` now show a live progress bar driven by these counters, and the snapshot-creation server route (`POST /api/snapshots`) likewise flipped to a job pattern (`GET /api/snapshots/jobs/:jobId`) so the dashboard never sits on a multi-minute HTTP request. Old `/files/zip` removed — pre-release, no backcompat needed.

## [1.7.36] - 2026-04-24

### Changed
- **Role model replaces per-feature pin toggles.** The three independent `alwaysLog` / `alwaysRemote` / `alwaysDebug` pins are gone. Each device now carries a single `tester_role` tag: `internal` (ambient log streaming + ambient Remote IDE tunnel accept + startup debug-mode consent prompt), `external` (startup debug-mode consent prompt only), or `public` (crash reporting only — the default for every new device). The server-signed handshake now ships a `roleConfig { role, issuedAt, payload, sig }` instead of `pinConfig`; `BugpunchTunnel` (Android + iOS) parses the role, SharedPreferences / Keychain caches the string, and existing feature gates fan out from `role == "internal"`. Startup debug-mode prompt wiring is a follow-up — `Bugpunch.EnterDebugMode()` still works as a manual entry point. Old server fields (`pinConfig`, `alwaysLog` etc.) are no longer emitted; shipped SDKs expecting them silently degrade to "no ambient features" which is the safe default.
- **SDK C# surface**: `PinState` → `RoleState` (`RoleState.Current`, `RoleState.IsInternal`, `RoleState.IsTester`). `BugpunchNative.PinAlwaysLog/Remote/Debug/PinConsent()` removed; `BugpunchNative.GetTesterRole()` added. `RequestRouter` Remote-IDE gate now `role == internal` on shipped builds (Editor + `Debug.isDebugBuild` still unrestricted).

### Fixed
- **Per-line compile errors in the Remote IDE script panel now render through Monaco's native marker API.** Previously errors only showed a whole-line red tint; they now light up the overview ruler, inline squiggles, hover cards, and the Problems list via `monaco.editor.setModelMarkers`. The script envelope now carries `{line, column, length}` when available so markers underline the exact token; runtime exceptions keep their line info too. Enabled `glyphMargin` so the gutter error-dot actually renders.

## [1.7.35] - 2026-04-24

### Added
- **Auto-emit `scene_change` analytics event on every scene transition.** `BugpunchSceneTick` now fires a `scene_change` event with `{from, to, duration_ms}` through the existing `Bugpunch.TrackEvent` pipe in addition to pushing the ambient scene name to native. The first activation fires with `from=null` so session-entry scenes are captured as a first-class signal (can't derive "what scene did the user open the app into" from ambient metadata alone). Same-name transitions (additive-load promote → unload sequences firing `activeSceneChanged` with unchanged active name) are suppressed analytically but still update native metadata. Events land in `analytics_events` with `event_name='scene_change'` — queryable for session paths, time-in-scene, and entry-point analytics.

## [1.7.34] - 2026-04-24

### Added
- **Unity IAP integration — one-line hookup.** If the game has `com.unity.purchasing` 4.0+ installed, wrapping the existing `IStoreListener` with the new `.WithBugpunch()` extension method auto-logs every successful purchase into Bugpunch analytics without duplicating the call inside `ProcessPurchase`. The wrapper is a transparent decorator — game's listener runs first unchanged, then Bugpunch side-channels `{sku, price, currency, transactionId}` to `LogPurchase`. Supports both `IStoreListener` and `IDetailedStoreListener`; forwards both old (reason-only) and new (detailed) failure callbacks. Gated behind a `BUGPUNCH_HAS_UNITY_IAP` version-define on `ODDGames.Bugpunch.asmdef` so the integration file compiles *only* when Unity IAP is present — zero reflection, zero runtime cost when absent, no effect on games that don't use IAP.

## [1.7.33] - 2026-04-24

### Fixed
- **Remote IDE stream now matches the device's live aspect ratio.** The WebRTC streamer was allocating a RenderTexture at the raw `width`/`height` sent from the dashboard (all three presets are 16:9: 480×270 / 960×540 / 1920×1080), then blitting the game screen into it with `Graphics.Blit` — so a portrait phone (e.g. 1080×2400) was being squashed into 16:9 and the Remote IDE displayed a distorted, black-barred frame. `WebRTCStreamer` now treats the requested W/H as a **long-edge pixel budget** and derives the actual RT dimensions from `Screen.width`/`Screen.height` every allocation. In scene mode, `SceneCameraService.SetAspect` pushes the dashboard's panel aspect into the streamer via the new `IStreamer.SetTargetAspect(w, h)` hook so the scene camera and the RT stay in lock-step. Quality changes and orientation drift hot-swap the RenderTexture in place via `RTCRtpSender.ReplaceTrack` — no SDP renegotiation, no browser reconnect. The `/webrtc-quality` response now reports `reconnectRequired:false` and the dashboard skips the WebRTCStream remount on quality change.
- **Device rotation no longer wedges the stream aspect.** The render loop now samples `Screen.width/Height` every ~30 frames while in game mode (no panel override active) and triggers a track-preserving resize the moment the aspect drifts — rotating the phone mid-session updates the stream within ~½ second instead of requiring a manual reconnect.

## [1.7.32] - 2026-04-24

### Changed
- **MediaProjection consent is re-asked every `EnterDebugMode()`.** 1.7.31 remembered a prior denial in SharedPreferences and skipped the OS dialog on subsequent opens. Dropped that short-circuit: each `EnterDebugMode()` call shows the consent dialog again, so a user who tapped Cancel once can still grant full-screen capture later without having to find a "clear denial" setting. The buffer-mode fallback still fires on denial — game surface video only, no further prompts for that session.
- **`BugpunchSurfaceRecorder` moves the RGBA→NV12 conversion to the GPU.** New compute shader at `Resources/BugpunchRgbaToNv12.compute` emits NV12 bytes into a `RWByteAddressBuffer`; `AsyncGPUReadback` then copies the already-converted bytes straight to JNI. Frees ~5% of one CPU core at 15 fps/720p. Falls back to the managed-thread CPU conversion on devices where `SystemInfo.supportsComputeShaders` is false.

## [1.7.31] - 2026-04-24

### Added
- **Android: Unity-surface fallback when MediaProjection consent is denied.** Previously, tapping "Cancel" on Android's system screen-recording consent dialog left us with no video at all — `BugpunchProjectionRequest` silently finished and `BugpunchRecorder` never started. Now: on denial we remember the refusal in SharedPreferences (`bp_projection_denied_at`) and spin the recorder up in buffer-input mode (`COLOR_FormatYUV420SemiPlanar`). A new `BugpunchSurfaceRecorder` MonoBehaviour on the Unity side mirrors the main camera's rendered output into a `RenderTexture` via a `CommandBuffer.Blit`, reads it back asynchronously through `AsyncGPUReadback` (no render-thread stall), converts RGBA32→NV12 on the managed thread, and pushes frames to the native encoder via `BugpunchNative.QueueVideoFrame`. The encoder, ring buffer, MP4 muxer, and crash-dump path are shared with the MediaProjection source — only the frame producer changed. Bug reports now ship ~15 fps game-only footage even when projection is denied, at ~4–6% CPU overhead. Next session after the denial, `BugpunchDebugMode.enter()` skips the OS dialog entirely via `BugpunchProjectionRequest.wasPreviouslyDenied()` and goes straight to buffer mode — no more repeated prompts.

## [1.7.30] - 2026-04-24

### Fixed
- **`CaptureScreen` no longer spams "attempting to ReadPixels outside of RenderTexture bounds!".** The old path called `ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0)` against whatever RT happened to be bound as active. That raced with the WebRTC render loop, which binds its own 960×540 RT during sampling — so a full-screen ReadPixels (e.g. `2254×…`) overflowed the 960×540 RT and Unity complained every frame. Capture now goes: `ScreenCapture.CaptureScreenshotIntoRenderTexture` into an owned full-size RT → GPU blit (with Y-flip) into a pre-scaled target RT → `ReadPixels` from that known target → CPU JPEG encode. Side benefit: the CPU readback is now of the already-downscaled image (960×540 instead of full screen), so one-shot screenshots are faster too.

## [1.7.29] - 2026-04-24

### Fixed
- **Unity Editor reappears in the Devices list after the tunnel split.** 1.7.28 moved the Editor to the managed-only `/api/devices/ide-tunnel`, but the dashboard's `GET /api/devices` was still sourcing its device list from `reportTunnelService.listTunnels()` + DB-poll-registered rows + agent devices — none of which the Editor populates (no native report tunnel, no HTTP register). Result: Editor connected and registered fine server-side but was invisible in the UI. `ideTunnelService` now exposes `listTunnels()`, and the devices route merges ide-tunnel-only connections into the response as `connectionMode: "debug"`. `IdeTunnel`'s register frame picks up `platform` + `appVersion` so Editor rows render with a proper platform badge.
- **Log flush backlog on hot logcat streams.** `BugpunchTunnel.java` could post thousands of `flushLogs` runnables before the worker thread got CPU — one per line once the buffer crossed the 16 KB threshold. The runnable now coalesces: one immediate + at most one delayed flush are in flight at a time. Re-enables `flushLogs` on the native path (the WebRTC debug `if (true) return;` short-circuit is removed).
- **iOS report tunnel now starts on `Bugpunch_StartDebugMode`.** Previously only Android auto-started the tunnel; iOS waited for something external to call `Bugpunch_StartTunnel`. BugpunchNative now calls it right after `StartDebugMode` succeeds.

### Changed
- **Pin config rides the poll path too.** The poll response can now carry a signed `pinConfig`; native applies it to the Keychain / SharedPreferences cache immediately and auto-starts the report tunnel if the device is pinned + consented. Release/internal devices get QA enrollment without waiting for a manual report-tunnel bring-up.
- **`useNativeTunnel` now true for `BuildChannel.Internal` too.** Previously only `Debug.isDebugBuild` / `EditorUserBuildSettings.development` unlocked the native tunnel; internal QA builds without the `development` flag stayed on poll-only. The two config bundlers (runtime + editor) converge on the same rule.
- **Register payload carries `stableDeviceId` + `buildChannel`.** Both Android + iOS poll registration send these now, so the server can reconcile reinstalls and apply channel-based pin policy. `ensureRegistered` refreshes once per process (not just once per installed token) so renamed apps / reissued keys refresh cleanly.
- **build-android.ps1 sets `ANDROID_USER_HOME` + `ANDROID_PREFS_ROOT` into a workspace-local dir.** Sandboxed / CI builds no longer need write access to the OS user profile.

## [1.7.28] - 2026-04-24

### Changed
- **Remote IDE and bug reporting now ride separate tunnels.** The single `/api/devices/tunnel` WebSocket is split into two: native SDK connects to `/api/devices/report-tunnel` (crashes / bugs / pin config / log sink / device actions), managed C# connects to `/api/devices/ide-tunnel` (hierarchy / inspector / script / webrtc-* / capture / scene-camera). Server-side: `tunnelService.ts` → `reportTunnelService.ts` + new `ideTunnelService.ts`; 10 callers updated. SDK-side: `TunnelClient.cs` → `IdeTunnel.cs` and loses the `#if UNITY_ANDROID || UNITY_IOS` platform splits — managed IDE tunnel runs on every platform. `TunnelBridge.cs` deleted (no abstraction needed once the split is clean). Native `BugpunchTunnel.java` / `BugpunchTunnel.mm` drop the `case "request":` request-routing block plus the `UnitySendMessage("OnTunnelRequest")` bridge — the report tunnel no longer carries Remote IDE traffic. Narrow native→C# bridge for queued `run_script` actions stays (`OnScriptAction` + `PostScriptResult`).
- **"Ask user for help" + "Enable debug tunnel on next boot" merged in the dashboard for device-targeted requests.** Single checkbox labelled "Request debug access" — accepting the in-app prompt opens the IDE tunnel on next boot. Crash-group-targeted requests keep the standalone "Ask user for help" semantics.

## [1.7.27] - 2026-04-24

### Fixed
- **Unity Editor no longer mis-detects itself as a mobile-native build.** When the Editor's build target was set to Android or iOS, the `#if UNITY_ANDROID || UNITY_IOS` preprocessor branches across `BugpunchClient` / `TunnelBridge` / `TunnelClient` / `DeviceIdentity` assumed a native `BugpunchTunnel` was running (it only runs on the real device player, never in the Editor). Result: the Editor never created a managed `TunnelClient`, never opened a WebSocket, never registered — it simply didn't appear on the server's Devices page. All 14 affected conditionals now read `(UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR` (or the complementary `|| UNITY_EDITOR` form) so the Editor always uses the managed C# tunnel path regardless of build target.

## [1.7.26] - 2026-04-24

### Changed
- **WebRTC streaming reverted to C# / `com.unity.webrtc` on Android.** N7's native `BugpunchStreamer.java` (PixelCopy / MediaProjection → ByteBuffer I420 → `libwebrtc`) is deleted. `/webrtc-*` requests now fall through `BugpunchTunnel` to `BugpunchClient`'s existing `WebRTCStreamer` path (same code used on iOS + Editor). The C# path is 100% GPU (`RenderTexture` → `VideoStreamTrack` → native encoder via Unity's graphics interop), supports camera switching (`SetCamera(Camera)`), a scene-camera mode driven by `SceneCameraService`, and a full game-view capture (including Screen-Space-Overlay UI) via `ScreenCapture.CaptureScreenshotIntoRenderTexture` when no target camera is set. Removed `libs/webrtc-classes.jar` (compile-only dep for the native streamer) and the `InitializeStreamerLazy` Android early-return.

### Added
- **Phase 6c — native log redaction.** `BugpunchConfig.logRedactionRules` is a list of regex patterns (name + pattern). The native tunnel compiles each rule once at startup (`Pattern.compile` on Android, `NSRegularExpression` on iOS) and applies them to every captured log line **before the line enters the batcher** — nothing matching a configured pattern ever leaves the process via the QA log sink. Each match is replaced with `[redacted:NAME]`. Bad patterns are logged and skipped so one typo can't take down the rest of the ruleset.
- **Phase 6b — `PinAuditPopover`.** Admin-only history icon next to `PinToggles` on the Devices page. On click, fetches the device's pin change log and shows who flipped what, when, and an optional reason. Diffs render as short human-readable flags (`alwaysLog on, alwaysDebug off`) with relative timestamps.
- **Phase 6d — `PinPolicyPopover`.** Admin-only shield icon on the Devices page header. Toggles the project's `allowAlwaysDebugOnProduction` flag via `PATCH /api/v1/projects/:id/pin-policy`. Together with the type-to-confirm dialog in `PinToggles`, this gives the project owner the full control loop for the production-channel alwaysDebug pin that Phase 6a unlocked.

## [1.7.24] - 2026-04-22

### Changed
- **Native tunnel N6 — `TunnelClient.IsConnected` and `.DeviceId` now read through to native on device.** `BugpunchNative.TunnelIsConnected()` / `.TunnelDeviceId()` (new JNI + P-Invoke accessors) are the single source of truth; the managed fields back the Editor path only. Every call site that checks `Tunnel.IsConnected` or reads `Tunnel.DeviceId` on a shipped build now sees the real native state instead of the stale managed value that never transitioned out of default (since `TunnelClient.Connect` has been a no-op on device since N3). Full class removal + OnConnected/OnDisconnected re-raising via `UnitySendMessage` is the remaining N6 follow-up.

## [1.7.23] - 2026-04-22

### Added
- **Native tunnel N3 — request-dispatch bridge.** Incoming Remote IDE `request` frames now arrive on the native WebSocket, are marshaled to C# via `UnityPlayer.UnitySendMessage("[Bugpunch Client]", "OnTunnelRequest", json)`, and the response ships back through `BugpunchNative.TunnelSendResponse`. The existing `RequestRouter` + every Hierarchy / Inspector / Script / SceneCamera / Watch / etc. service stays C# — they need Unity reflection. Only the transport moved.
- **Native tunnel N4 — native pin state.** Pin config (`{pins,consent,issuedAt,sig}`) is parsed + stored natively on both platforms:
  - Android — `SharedPreferences` file `bugpunch_pins`, reloaded on cold start.
  - iOS — Keychain entry `(au.com.oddgames.bugpunch, pin_state_v1)`, survives app reinstall.
  - C# `PinState` delegates to `BugpunchNative.PinAlwaysLog / Remote / Debug / Consent` on device; Editor keeps its PlayerPrefs-backed mirror for local dev flow.
  - HMAC verification of the server signature (`sig` field) against a bundled `pin_signing_secret` is deferred to N4.2 — current path trusts the tunnel (API-key + TLS), same guarantee the managed path had.
- **Native tunnel N5 — native log sink.** When `alwaysLog` is on and consent accepted, the native log readers (`BugpunchLogReader` on Android, `BPLogReader` on iOS) tee every captured line into a per-tunnel batcher (~100 ms / ~32 KB). Batches ship as `{type:"log",sessionId,text}` frames on the same WebSocket. **Zero C# involvement on the log path** — capture + transport are both native. Server-side `logSinkService` appends raw bytes to `data/logs/{sessionId}.log`, inserts the `log_sessions` DB row on first chunk, and finalizes on tunnel disconnect. S3 upload + 50-session-per-device retention are follow-up jobs.

### Changed
- **C# `TunnelClient.Connect` is a no-op on device.** Native `BugpunchTunnel` owns the WebSocket lifecycle end-to-end on Android + iOS. The managed `TunnelClient` still exists so existing call sites compile and the Editor keeps working with its managed ClientWebSocket for local dev; retired fully in a later phase (N6).
- **`TunnelClient.SendResponse` / `SendBinaryResponse`** route responses through `BugpunchNative.TunnelSendResponse` on device. Editor path unchanged.
- **`BugpunchLogReader` (Android)** tees each logcat line into the native sink; the crash-log ring buffer is unchanged.
- **`BPLogReader` (iOS)** tees each OSLog-derived line into the native sink; ring buffer unchanged.

## [1.7.22] - 2026-04-22

### Added
- **Native WebSocket tunnel — N1 (Android) + N2 (iOS)** of the native-tunnel migration. Replaces the C# `TunnelClient`'s dependence on a live Mono runtime with an OkHttp-backed socket on Android and a `URLSessionWebSocketTask` on iOS. The native tunnel comes up from the ContentProvider (Android) / `+load` bootstrap (iOS) — before Unity's mono runtime is alive — and survives managed crashes because nothing on the path touches the managed heap.
  - `sdk/android-src/bugpunch/src/main/java/au/com/oddgames/bugpunch/BugpunchTunnel.java` — connect + register (with stable device id + build channel) + 10-second heartbeat + exponential-backoff reconnect. Captures the signed pin config from the `registered` ack and live `pinUpdate` frames so the pin enforcement path can read it without further round-trips.
  - `sdk/package/Bugpunch/Plugins/iOS/BugpunchTunnel.mm` — iOS mirror using `NSURLSessionWebSocketDelegate`. Same register payload, same pin-config capture, same backoff. Exposes `Bugpunch_StartTunnel`, `Bugpunch_StopTunnel`, `Bugpunch_TunnelIsConnected`, `Bugpunch_GetLastPinConfig` for the later native pin handler (N4).
  - `sdk/android-src/bugpunch/build.gradle` picks up `com.squareup.okhttp3:okhttp:4.12.0`; resolved through the project's existing `google()` + `mavenCentral()` repositories.
- **Pin enforcement gate in `RequestRouter`** — interactive Remote IDE requests refuse with 403 on shipped builds unless `PinState.IsAlwaysRemote` is true. Editor + `Debug.isDebugBuild` keep the existing developer UX (no pin required).
- **`PinState` singleton** — receives pin config via `TunnelClient.OnPinConfig`, caches to PlayerPrefs for fast cold-start, exposes `IsAlwaysLog` / `IsAlwaysRemote` / `IsAlwaysDebug` to the rest of the SDK. Interim; to be retired when N4 moves pin handling fully native.

### Changed
- **`BuildChannel` field added to `BugpunchConfig`** (enum: `Internal` / `Beta` / `Production`). Declared per-build in the Inspector; serialised into the bundled config so the server can apply channel-appropriate pin guardrails. Defaults to `Internal`.
- **Tunnel register payload extended** — now includes `stableDeviceId` (Keychain UUID on iOS, `ANDROID_ID` on Android via the new `BugpunchIdentity` helper) and `buildChannel`. Server persists both on the `devices` row so pins survive app reinstalls.
- **Server tunnel handshake** returns a signed pin config (HMAC-SHA256 over `{ pins, consent, issuedAt }` keyed to each project's `pin_signing_secret`) in the `registered` ack, and pushes `pinUpdate` frames live when an admin toggles pins in the dashboard.

## [1.7.21] - 2026-04-21

### Added
- **Pre-Unity native bootstrap (Android + iOS)** — the Bugpunch SDK now installs crash handlers, the log ring, and the upload-queue drain *before* Unity boots. Previously the SDK waited for a C# `BugpunchNative.Start()` call from `[RuntimeInitializeOnLoadMethod]`, which meant crashes during Unity's own startup (mono boot, asset load, first-scene init) went uncaught. Now:
  - New Unity editor post-processor `BugpunchConfigBundle` serialises the static parts of `BugpunchConfig` into `bugpunch_config.json` and places it in the APK's `assets/` (Android) or the Xcode main bundle (iOS) at build time.
  - **Android** — new manifest-declared `BugpunchInitProvider` ContentProvider runs before `Application.onCreate()` returns (same pattern Firebase / Sentry use), reads the bundled config, and calls a new `BugpunchRuntime.startEarly(Context, String)` to install signal handlers / log ring / crash paths / upload drain. An `ActivityLifecycleCallbacks` bridge captures the first non-Bugpunch Activity and calls the new `BugpunchRuntime.attachActivity(Activity)` to wire up the Activity-bound pieces (overlay detector, SurfaceView cache, Choreographer frame tick, AEI scan, crash drain, shake detector).
  - **iOS** — new `BugpunchBootstrap.mm` uses an Obj-C `+load` method to read `bugpunch_config.json` from the main bundle and call `Bugpunch_StartDebugMode` before `main()` fires.
- **Idempotent config refresh** — both `BugpunchRuntime.start(Activity, String)` and `Bugpunch_StartDebugMode(const char*)` now safely re-run with a richer Unity-runtime config (Application.version, deviceId, persistentDataPath-resolved attachment rules). Config values are merged, crash handler metadata is refreshed, one-time init is skipped. The legacy C# path continues to work unchanged for projects that haven't picked up the new post-processor yet.

## [1.7.20] - 2026-04-21

### Added
- **ANR — OS-level Android data** — `BugpunchCrashHandler.scanApplicationExitInfo` (API 30+) now pulls `ActivityManager.getHistoricalProcessExitReasons` at next launch, merges new `REASON_ANR` / `REASON_CRASH_NATIVE` / `REASON_LOW_MEMORY` / etc. into matching on-disk `.crash` files by `(type, timestamp ±30s)`, or synthesizes standalone entries for exits we missed. Delivers the authoritative `/data/anr/traces.txt` blob alongside our in-process report — the two sources together give the richest ANR record available.
- **Main-thread stack sample ring** — `AnrWatchdog` now samples `Thread.getStackTrace()` on the main thread at 10 Hz **only** when the main thread has been missing ticks for ≥1 s (adaptive — zero overhead on healthy frames), and dumps the last 5 s of samples under `---STACK_SAMPLES---` in the ANR report. Shows whether the thread was pinned in one spot the whole hang or progressed through several frames before getting stuck — the #1 question when diagnosing ANRs.
- **Android `mapping.txt` upload** — new `BugpunchMappingUploader` editor post-build hook. When R8/ProGuard minification is enabled, locates the emitted `mapping.txt` (in `symbols.zip` or `Library/Bee/.../outputs/mapping/release/`), uploads it to the server via `POST /api/v1/mappings` keyed by `(bundleId, version, buildCode)`. Piggybacks on `symbolUploadEnabled`. Server retraces obfuscated Java frames in crash / ANR / AEI / stack-sample text at ingest time.
- **`app.buildCode` in crash payloads** — native Java now reads Android `versionCode` from `PackageInfo` at startup and includes it in every crash/exception/feedback report so the server can look up the right mapping.

### Changed
- **`BugpunchDebugMode` split into four focused classes** — the old 1500-line coordinator mixed always-on crash/ANR/exception reporting with opt-in video recording, making it ambiguous whether the SDK was privacy-safe for all users. Now:
  - `BugpunchRuntime` — always-on lifecycle coordinator (crash init, ANR watchdog, AEI scan, log reader, metadata, analytics, frame tick, public accessors). Entry point renamed `startDebugMode` → `start`.
  - `BugpunchCrashDrain` — previous-session `.crash` file drain + payload builder + attachment snapshotting.
  - `BugpunchReportingService` — in-process bug / feedback / exception report building + upload orchestration.
  - `BugpunchDebugMode` (shrunk to ~240 lines) — opt-in video recording coordinator only. `enterDebugMode` → `enter`, `stopDebugMode` → `stop`.
- **C# — `BugpunchNative.StartDebugMode` → `BugpunchNative.Start`** to match the Java rename. JNI `AndroidJavaClass` routing updated throughout.

## [1.7.19] - 2026-04-20

### Changed
- **Native debug-tools button press feedback** — `BugpunchToolsActivity` (Android) and `BugpunchToolsPanel.mm` (iOS) now give instant visual feedback on tap. Android gets a `StateListDrawable` with a darkened pressed state plus an alpha flash; iOS gets a scale-down + dim on touch-down and a "Running…" title flash after click. The callback to Unity fires immediately on touch-up so the game runs the tool with zero added delay — the animation is purely for the user's sake, running in parallel with the click dispatch.

## [1.7.18] - 2026-04-20

### Fixed
- **Post-build uploads skipped on `BuildAndRun`** — Unity's `BuildAndRun` pipeline leaves `BuildReport.summary.result == Unknown` when `IPostprocessBuildWithReport` runs; the `Succeeded` marker is only written after the hook returns. The previous `result != Succeeded` check treated `Unknown` as failure, so build artifact + type DB + symbol uploads all silently no-op'd even on successful builds. Now we only bail on explicit `Failed` / `Cancelled` and let the `File.Exists` check on the output path be the authority.

## [1.7.17] - 2026-04-20

### Changed
- **Symbol upload streams through disk** — `BugpunchSymbolUploader` no longer buffers `.so` bytes in the managed heap. Scan pass extracts each zip entry to a temp file and reads only the ELF header + PT_NOTE segment (~1KB) to recover the GNU build-ID. Upload pass builds the multipart body on disk and streams via `UploadHandlerFile`. Peak Editor RAM during symbol upload drops from ~1GB (sum of all `libil2cpp.sym.so` across ABIs) to effectively zero. Temp files cleaned up in a `finally` so failed uploads don't leak.

## [1.7.16] - 2026-04-20

### Changed
- **Symbol upload on by default** — `BugpunchConfig.symbolUploadEnabled` now defaults to `true`. Android IL2CPP builds will upload unstripped `.so` symbols from Unity's `symbols.zip` (requires Player Settings → Publishing Settings → Symbols enabled). Prior default was held off pending server storage + symbolicator RAM work; both are resolved on the Lightsail host (80GB disk, 4GB RAM), so the gate is removed. Crash frames will now resolve to source:line on the dashboard.

## [1.7.15] - 2026-04-19

### Removed
- **Dead OS-level injection code** — dropped `BugpunchTouchRecorder.injectTap / injectSwipe / injectPointer*` (Android Java), `RequestRouter.NativeInjectTap / NativeInjectSwipe / NativeInjectPointer` (C#), and the iOS `BugpunchTouch_InjectTap / InjectSwipe` stubs. All Remote IDE input lives in `InputInjector` (Unity Input System) after the previous release. Touch *recording* paths are unaffected.

## [1.7.14] - 2026-04-19

### Changed
- **Remote IDE input goes through Unity Input System only** — dropped the Android native `Instrumentation.sendPointerSync` injection from `/input/tap`, `/input/swipe`, `/input/pointer`. WebRTC captures the Unity render texture, not the OS screen, so OS-level touches would land outside what the dashboard can see. Injection now uniformly uses `InputInjector` across Android, iOS, and Editor.
- **`#if ENABLE_INPUT_SYSTEM` guards** on `InjectPointerDown/Move/Up/Cancel` so the code compiles even when a project has only the legacy Input Manager enabled (though injection is a no-op in that configuration).

## [1.7.13] - 2026-04-19

### Added
- **Pointer lifecycle on iOS + Editor** — `InputInjector.InjectPointerDown/Move/Up/Cancel` use the Unity Input System's `TouchState` (touch devices) and `Mouse` state events (desktop) to hold persistent pointer state across calls. The `/input/pointer` route now routes to this on iOS and Editor builds so the Remote IDE drag / press-and-hold works on every platform.

## [1.7.12] - 2026-04-19

### Added
- **Pointer lifecycle input injection** (Android) — new `/input/pointer` endpoint accepts `{ action: down|move|up|cancel, x, y }` with persistent per-pointer state in `BugpunchTouchRecorder`. Lets the dashboard drive press-and-hold + drag gestures naturally instead of synthesising whole swipes at mouseup. iOS falls back to the existing tap/swipe primitives.

## [1.7.11] - 2026-04-18

### Fixed
- **Don't tear down WebRTC on transient ICE Disconnected** — ICE `Disconnected` is recoverable per the spec; the prior handler eagerly teared the peer connection down on that state, which killed the stream when switching to scene camera (brief main-thread hang tripped ICE keepalives). Now we only clean up on `Failed` / `Closed`.

## [1.7.10] - 2026-04-18

### Changed
- **Scene camera skip-ahead** — if the device-side camera lags more than 15m or 90° behind the dashboard's target (fast drag, teleport, snap), it jumps to within that range before lerping the remainder. Keeps big moves responsive while preserving the smooth feel for small deltas.

## [1.7.9] - 2026-04-18

### Added
- **WebRTC bitrate cap** — outgoing video sender now caps at 2.5 Mbps / 30 fps via `RTCRtpSender.SetParameters`. Previously Unity.WebRTC could push 8+ Mbps which collapsed on weak cellular; the cap gives GCC a sane target and keeps quality smooth on mobile links. `WebRTCStreamer.SetVideoMaxBitrate(bps, fps)` exposed for dynamic tuning (dashboard can signal bandwidth changes later).

## [1.7.8] - 2026-04-18

### Changed
- **Looser scene camera lerp** — `LerpSpeed` dropped 12 → 8 so the device-side camera eases into position with a softer trailing feel instead of snapping hard to the dashboard's drag target.

## [1.7.7] - 2026-04-18

### Changed
- **Debug widget: remove elapsed-time counter** — the widget was showing a ticking `0:00` timer, but the recording is a rolling ring buffer, not a session. The number didn't represent anything the user could act on. Widget now shows only the blinking red dot + Report / Screenshot / Tools buttons (Android + iOS).
- **Log inner exception on tunnel connect failure** — surface the wrapped `SocketException` / `AuthenticationException` etc. so mobile-carrier connectivity issues can be diagnosed instead of just seeing the generic `WebSocketException: Unable to connect to the remote server`.

## [1.7.6] - 2026-04-18

### Changed
- **Log connect failures** — surface the actual exception type + message when the tunnel WebSocket fails to connect, instead of firing a silent `OnError` event nobody subscribes to. Makes "stuck retrying" situations (mobile carrier blocking, TLS issues, DNS) diagnosable.

## [1.7.5] - 2026-04-18

### Fixed
- **Faster reconnect on network switch** — on app resume (e.g. WiFi↔mobile swap), proactively drop the tunnel socket so `ConnectLoop` reconnects immediately instead of waiting 1–2 min for Android's TCP stack to notice the half-open socket.

## [1.7.4] - 2026-04-18

### Added
- **Scene Camera Aspect Ratio Sync** — new `/scene-camera/aspect` endpoint lets the dashboard update the scene camera's aspect ratio when the panel resizes, keeping game and scene views properly framed.

## [1.7.3] - 2026-04-18

### Added
- **Live Touch Overlay** — native OS touch data streamed via WebRTC data channel for real-time visualization in Remote IDE. Shows finger circles with swipe tails, frame-synced to the video stream.
- **Native Touch Injection (Android)** — tap and swipe commands from the dashboard inject at the OS level via `Instrumentation.sendPointerSync`, captured by the native touch recorder for round-trip visualization.
- **`/input/touches` endpoint** — poll native touch state via tunnel proxy (Android JNI, iOS P/Invoke, Editor mouse fallback).

## [1.7.2] - 2026-04-18

### Added
- **PlayerPrefs Service** — enumerate, read, edit, and delete PlayerPrefs from Remote IDE. Platform-specific key discovery: Windows registry, Android XML, macOS plist.
- **Memory Snapshot Service** — trigger Unity Memory Profiler snapshots on-device, download .snap files for analysis in Unity's Memory Profiler window. Live memory stats with asset breakdown (textures, meshes, audio, animation, materials).

## [1.7.1] - 2026-04-18

### Added
- **Scene Camera Smooth Lerp** — scene camera now smoothly interpolates toward target position/rotation instead of snapping.
- **Watch Service** — live variable watching: search, pin, poll values, apply changes from Remote IDE.
- **Texture Service** — browse, thumbnail, and full-size capture of in-game textures.
- **Material Service** — browse materials, view shader properties, preview textures.

## [1.7.0] - 2026-04-17

### Added
- **Scene Camera Collider Streaming** — Remote IDE scene camera overlay now streams actual collider geometry (box, sphere, capsule, mesh) instead of renderer bounding boxes. Colliders load progressively near-to-far via Physics.OverlapSphere.
- **Tiered Transform Updates** — dynamic rigidbody colliders update at 150ms, kinematic at 500ms, static colliders sent once. Transform-only polls are lightweight (7 numbers per object).
- **Debug Tools Bridge** — new DebugToolsBridge, DebugToolAttributes for in-game debug tool registration and native debug widget (Android BugpunchToolsActivity, iOS BugpunchToolsPanel).
- **iOS Backbuffer Capture** — new BugpunchBackbuffer.mm for Metal backbuffer screenshot path.

### Changed
- **Inspector Service** — improvements to component inspection.
- **Script Runner** — updates to script execution service.
- **Crash Handler** — Android crash handler improvements, iOS crash handler updates.
- **Report Form** — updated native bug report form (Android BugpunchReportActivity, iOS BugpunchReportForm).
- **Uploader** — Android and iOS uploader improvements.
- **Exception Forwarding** — updated UnityExceptionForwarder.

## [1.6.0] - 2026-04-16

### Added
- **Game Config Variables** — new `Bugpunch.GetVariable`, `GetVariableBool`, `GetVariableInt`, `GetVariableFloat` API. Variables are fetched from the server on startup with device-matched overrides (GPU, memory, screen size, platform) automatically resolved.
- **Native Performance Monitor** — new `BugpunchPerfMonitor` on Android (Java) and iOS (Obj-C). Enabled via server config, samples FPS and memory natively on a background thread, fires perf events on memory pressure or sustained low FPS.
- **ANR Screenshots** — Android ANR reports now include a screenshot captured via PixelCopy from a cached SurfaceView (works even with the main thread stuck).
- **iOS Log Push** — Unity `Debug.Log` entries are now pushed to the native iOS log buffer via P/Invoke. Android already captures these via logcat.
- **Installer Mode Detection** — Android and iOS detect store/testflight/sideload at startup, included in crash metadata and device registration.

### Changed
- **ANR Cooldown** — 60s cooldown between ANR reports to prevent duplicate firing during a single hang.
- **Crash Category Field** — crash payloads now include a `category` field (`crash`/`anr`) for server-side Issues page tab routing.

## [1.5.23] - 2026-04-16

### Changed
- **Startup log buffer raised to 2000** — matches the ring buffer size so the full boot sequence is captured even on noisy devices.

## [1.5.22] - 2026-04-16

### Changed
- **Log buffer 500 → 2000** — native log ring buffer on both Android and iOS now keeps 2000 entries (was 500). Since crash collection is budgeted to 10 per fingerprint per version, larger buffers have negligible bandwidth impact.
- **Gzip log compression** — logs are now gzip'd and sent as a separate multipart file instead of embedded in the metadata JSON. Reduces upload size ~5×.
- **Startup log preservation** — first 200 log entries are kept in a permanent buffer that's never evicted. When entries between startup and recent are dropped, a "--- N log entries omitted ---" breaker is inserted so you always see how the app started.
- **SDK-level log dedup** — consecutive identical log messages (same level + tag + message) are collapsed into a single entry with a `repeat` count. Eliminates buffer-wasting spam (e.g. Vulkan per-frame logs).
- **iOS startup log lookback** — OSLogStore now queries 60 seconds before init on its first poll, capturing logs written before BugpunchDebugMode started.

### Fixed
- **Android AAR local build** — `build.gradle` updated for local build with Unity's bundled SDK/NDK (compileSdk 36, build-tools 36, Java 11 source compat, `ndkPath` instead of env-only).

## [1.5.21] - 2026-04-16

### Fixed
- **iOS Xcode build break in v1.5.20** — `extern "C"` declarations are only valid at file scope in Objective-C++; the v1.5.20 fix put one inside a method body, which clang rejects with `expected unqualified-id`. Hoisted the forward declaration of `BPDirectives_OnUploadResponse` to the top of `BugpunchUploader.mm`. Downstream games on v1.5.20 will fail to archive — upgrade to v1.5.21.

## [1.5.20] - 2026-04-16

### Fixed
- **iOS link error** — `BPDirectives_OnUploadResponse` forward declaration in `BugpunchUploader.mm` now uses `extern "C"` so Obj-C++ matches the C symbol defined in `BugpunchDirectives.mm`. Without this, crash upload responses fail to dispatch directives on iOS builds.

## [1.5.19] - 2026-04-15

### Added
- **"Request More Info" crash directives** — QA can queue per-fingerprint data-gathering from the dashboard. Three action types: `attach_files` (native glob within a game-declared allow-list), `run_script` (runtime diagnostics via the IL2CPP-safe script runner), and `ask_user_for_help` (friendly post-upload consent dialog that auto-enables debug mode on accept and persists "never ask again" per fingerprint on decline). Directive output is POSTed to `/api/crashes/events/:id/enrich` via the existing retry/app-kill-resistant upload queue. Script failures file a companion `DirectiveError:Script` event under the same crash group so they surface in the issue detail.
- **`BugpunchConfig.attachmentRules[]`** — Inspector-editable allow-list of files Bugpunch is permitted to read. Server directives can only reference paths that match a rule here, so games stay in control of what leaves the device. Supports Unity path tokens `[PersistentDataPath]`, `[TemporaryCachePath]`, `[DataPath]`.
- **`Bugpunch.AddAttachmentRule(name, path, pattern, maxBytes)`** — runtime escape hatch for dynamic attachment paths; merged with config rules at startup.
- **Per-(fingerprint, appVersion) sample budget** — crash group `SampleCountByVersion` tracks collection quota per app version so a v2.0 regression can't be masked by v1.x having already exhausted the global counter.

### Changed
- **Android plugin now ships as a pre-built AAR.** Source moved from `package/Bugpunch/Plugins/Android/BugpunchPlugin.androidlib/` (which forced end-game builds to run Gradle + NDK) to `android-src/bugpunch/` (outside the UPM package). CI rebuilds `BugpunchPlugin.aar` on every push to `main` touching `android-src/**` and commits it back. Downstream games consume only the AAR — no JDK / Android SDK / NDK required during their player build. Local rebuild via `sdk/build-android.ps1` for devs with the toolchain; iOS stays as source (Xcode compiles per player build).
- **Directive dispatch is native-heavy.** C# shrunk to one managed-only concern (script execution, which requires the Mono/IL2CPP runtime). Native owns upload-response parsing, fingerprint matching, file globs, consent dialogs, and per-fingerprint denial prefs on both Android (`BugpunchDirectives.java` + `SharedPreferences`) and iOS (`BugpunchDirectives.mm` + `NSUserDefaults`).

## [1.5.18] - 2026-04-15

### Fixed
- **iOS build error — `use of undeclared identifier 'BPOverlayActions'`** — `BugpunchReportOverlay.mm` referenced the class via `[BPOverlayActions class]` inside the C-linkage setup functions, but its `@interface` sat at the bottom of the file. Moved the interface to the top (implementation stays near its helpers) so clang sees the declaration before the usages.
- **Android CMake path-length warning on deep Jenkins workspaces** — `CMakeFiles/bugpunch_crash.dir/bugpunch_crash.c.o` pushed the object path to 243/250 chars on `C:/jenkinsagents/.../MTD/...`, risking a silent build failure. Renamed the native source to `bp.c` and the CMake target to `bp`; `OUTPUT_NAME bugpunch_crash` preserves `libbugpunch_crash.so` so `System.loadLibrary("bugpunch_crash")` still works unchanged.

## [1.5.17] - 2026-04-15

### Fixed
- **False ANR detections every 5s after the native-first C# refactor** — the Android ANR watchdog relied on `BugpunchCrashHandler.tickWatchdog()` being called from the C# `Update()` loop. The refactor to `UnityExceptionForwarder` (static, no MonoBehaviour) removed that tick, so the watchdog thought the main thread was permanently stuck and wrote a new ANR crash file every 5s. Replaced with a self-tick `Runnable` posted to Android's main `Handler` every 1s — if the main thread is healthy it runs and updates the timestamp; if it's genuinely stuck the ticks stop and the watchdog fires a real ANR. No C# coordination required.

## [1.5.16] - 2026-04-15

### Fixed
- **iOS build failure** — `Bugpunch_EnqueueReport` called `Bugpunch_EnqueueReportWithTraces` before its definition appeared in the translation unit. Added a forward declaration inside the `extern "C"` block so clang sees both symbols. No functional change.

## [1.5.15] - 2026-04-15

### Added
- **`Bugpunch.Trace(label)` / `Bugpunch.Trace(label, tags)`** — mark a moment in the session with a label + optional string tags dict. Queued in a bounded ring buffer (max 50); all accumulated events ship with the next bug report as a `traces` multipart field.
- **`Bugpunch.TraceScreenshot(label)` / `TraceScreenshot(label, tags)`** — same as Trace but also captures a screenshot at call time (native `PixelCopy` on Android, `drawViewHierarchyInRect` on iOS) and attaches it as a separate `trace_N.jpg` multipart part. Server exposes these as `TraceMarker` Parse rows linked to the bug report; dashboard renders them in a timeline with thumbnails.
- **`autoStart` flag on `BugpunchConfig`** — when false, the client won't auto-connect and the game must call `BugpunchClient.StartConnection()` manually (e.g. behind a debug-build feature toggle).

### Changed
- **Bug report uploader signature grew optional trace params** — existing `BugpunchUploader.enqueue(...)` 7-arg overload kept for backwards compat. New 9-arg overload (Java) / `Bugpunch_EnqueueReportWithTraces` (iOS) carry the traces JSON path + per-trace screenshot paths.

## [1.5.14] - 2026-04-15

### Fixed
- **CMake path not found** — `externalNativeBuild { cmake.path '../jni/CMakeLists.txt' }` resolved to `unityLibrary/jni/` in Unity's generated Gradle project, which doesn't exist (the `jni/` sibling folder wasn't copied). Moved the NDK source into the androidlib at `src/main/cpp/` so Unity copies it alongside the Java sources; updated `cmake.path` to `src/main/cpp/CMakeLists.txt`.
- **AGP `ndkVersion` / `ndkPath` mismatch** — read the actual version from Unity's bundled NDK `source.properties` and set `ndkVersion` to match `ndkPath`, avoiding [CXX1100].
- **Type database uploaded on failed Player builds** — dropped the `compilationFinished` trigger which fired on every Editor recompile (including the cascade after a failed build). TypeDB now only uploads on successful post-build and on client-connect during play mode.

## [1.5.13] - 2026-04-15

### Fixed
- **AGP still checked NDK license by version** even when matching `unityLibrary.ndkVersion` — Unity's bundled NDK isn't registered as a managed SDK package, so AGP thinks it needs to install it. Switched to `ndkPath` (from `unityLibrary.ndkPath` or `ANDROID_NDK_ROOT`) which points Gradle straight at Unity's bundled NDK and bypasses the license check entirely.

## [1.5.12] - 2026-04-15

### Fixed
- **AGP demanded NDK 27.0.12077973 license not accepted** when building the native `.so`. The androidlib Gradle config now inherits `ndkVersion` from Unity's `:unityLibrary` (already installed + licensed in the Unity Hub SDK tree).

## [1.5.11] - 2026-04-15

### Fixed
- **`libbugpunch_crash.so` not found at runtime** — the androidlib Gradle config never included `externalNativeBuild`, so the NDK `.c` file in `Plugins/Android/jni/` was never compiled into a `.so`. Added `externalNativeBuild { cmake { path '../jni/CMakeLists.txt' } }` to `BugpunchPlugin.androidlib/build.gradle` plus `ndk.abiFilters` for `arm64-v8a` + `armeabi-v7a`. Native POSIX signal handlers (SIGABRT / SIGSEGV / SIGBUS / SIGFPE / SIGILL) now actually install at startup instead of failing silently with `dlopen failed`.

## [1.5.10] - 2026-04-15

### Fixed
- **`MPEG4Writer Stop() called but track is not started`** still fired in v1.5.9 because `muxer.release()` internally stops the muxer when it was started without samples. `BugpunchRecorder.dump` now pre-counts real (non-codec-config) samples BEFORE opening the `MediaMuxer`. If zero, it logs the reason and returns false — no muxer instantiation, no framework warning, no 0-byte MP4.

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
- **Script errors carry line numbers** — `ScriptRunner` returns structured `{line, message}` entries from the script engine's diagnostics instead of a single blob string, so the script panel can underline the offending line.

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
