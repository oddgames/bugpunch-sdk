// =============================================================================
// LANE: iOS (Obj-C++)
//
// BugpunchRuntime — process-wide shared state for the iOS lane. Mirrors
// `BugpunchRuntime.java` (Android) and `BugpunchRuntime.cs` (Editor +
// Standalone) by name and purpose. Cross-lane code (chat poller, perf
// monitor, directives, chat view controller, …) reads its inputs from
// here, NOT from `BPDebugMode`.
//
// `BPDebugMode` keeps only the opt-in debug-mode (consent + ring recorder)
// state — everything else (config, metadata, custom data, fps, started
// flag, last-auto-report timestamp, report-in-progress guard) lives here.
// =============================================================================

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

@interface BPRuntime : NSObject

/// Resolved config blob handed in by C# `Bugpunch_StartDebugMode`. Holds
/// `serverUrl`, `apiKey`, `buildChannel`, `metadata`, `video`, etc.
@property (nonatomic, strong, nullable) NSDictionary* config;

/// Live metadata (deviceModel, osVersion, gpu, scene, …). Seeded from
/// `config.metadata`; scene is pushed by C# on scene change. Mutable so
/// the SDK can update fields like installerMode at startup.
@property (nonatomic, strong) NSMutableDictionary<NSString*, NSString*>* metadata;

/// Game-supplied tags (key/value). Set via `Bugpunch.SetTag` and
/// stamped onto every report. Drives the dashboard Impact view's
/// "which devices share this state" diagnosis.
@property (nonatomic, strong) NSMutableDictionary<NSString*, NSString*>* tags;

/// SystemInfo blob pushed by C# (`BugpunchSystemInfo.CaptureFull` at init,
/// `CaptureVolatile` on each scene change). Stamped onto every uploaded
/// crash / exception / bug-report manifest under the top-level
/// `systemInfo` key. Stored as the raw merged dict so `NSJSONSerialization`
/// embeds it as nested JSON without re-parsing.
@property (nonatomic, strong, nullable) NSMutableDictionary<NSString*, id>* systemInfo;

/// Has Bugpunch_StartDebugMode completed?
@property (nonatomic, assign) BOOL started;

/// Cooldown bookkeeping for auto exception reports — written by
/// BPFireReport, read to enforce the per-cooldown interval.
@property (nonatomic, assign) NSTimeInterval lastAutoReport;

/// Guards against a second report being filed while the form / consent
/// UI is already open.
@property (nonatomic, assign) BOOL reportInProgress;

/// Current FPS measured by the CADisplayLink frame tick.
@property (nonatomic, assign) int fps;

/// Player identity — populated by the native SSO sign-in flow
/// (BugpunchProfilePicker → BugpunchPostSsoSignIn). Mirrors the four
/// fields on the Java + C# runtimes so chat HTTP calls and upload
/// manifests on every lane stamp the same reporter snapshot. nil when
/// the player hasn't signed in yet.
@property (nonatomic, copy, nullable) NSString* playerAuthProvider;
@property (nonatomic, copy, nullable) NSString* playerAuthId;
@property (nonatomic, copy, nullable) NSString* playerEmail;
@property (nonatomic, copy, nullable) NSString* playerName;
/// Optional avatar URL set by the SSO profile-picker flow. Empty / nil
/// on the public-player path — the reporter badge falls back to
/// hash-coloured initials in those cases.
@property (nonatomic, copy, nullable) NSString* playerAvatarUrl;

/// Game-supplied auxiliary account identities (Parse, Steam, GameCenter,
/// PlayFab, …) added via `Bugpunch.SetAccount(provider, username, email)`.
/// Deduped by provider — a second SetAccount for the same provider replaces
/// the prior entry in place, preserving insertion order. Stamped onto the
/// upload manifest under `extraAccounts` only when the device is in tester
/// mode (role != public) — public-role devices never forward this PII.
/// Each entry is `{provider, username, email?}` with `email` omitted when
/// empty / nil.
@property (nonatomic, strong) NSMutableArray<NSMutableDictionary*>* extraAccounts;

/// Video capture status — set when video is unavailable for a
/// known reason so the upload manifest can surface a placeholder
/// card on the dashboard instead of a silent miss. Cleared on
/// successful recorder start. Mirrors `BugpunchRuntime.setVideoStatus`
/// on Android. `videoStatus` is one of: "declined" (user dismissed
/// ReplayKit permission), "init_error" (compression session /
/// startCapture failed). `videoStatusMessage` is a human-readable
/// summary.
@property (nonatomic, copy, nullable) NSString* videoStatus;
@property (nonatomic, copy, nullable) NSString* videoStatusMessage;

/// ICE servers for the native WebRTC streamer (`BugpunchStreamer`). Folded
/// into the poll bootstrap (`bootstrap.iceServers`) and stashed here by
/// `BugpunchPoller` so the streamer can build its `RTCConfiguration` without
/// a per-session HTTP fetch. Each entry is `{urls, username?, credential?}`.
/// Mirrors Android, where the streamer reads `BugpunchRuntime.getConfig()
/// .iceServers`. nil until the first poll lands; the streamer falls back to a
/// default STUN server in that window. `atomic`: written on the poll queue,
/// read on the streamer queue — atomic keeps the strong-pointer get/set
/// retain-safe across threads (the value itself is an immutable NSArray).
@property (atomic, strong, nullable) NSArray<NSDictionary*>* iceServers;

+ (instancetype)shared;

/// Mark / clear an SDK self-instrumentation source. `source` is a stable key
/// ("record" for the video ring recorder, "stream" for the live IDE WebRTC
/// stream); `active` adds or removes it. While any source is active
/// `isInstrumented` returns YES and the perf monitor pauses FPS sampling —
/// frames captured under that load are a debugging artefact, not the player
/// experience, so they'd trip false "Low FPS" problems on tester devices.
/// Keyed so overlapping sources (record + stream) clear independently.
/// Thread-safe. Mirrors `BugpunchRuntime.setInstrumentation` (Java) + the
/// managed `BugpunchRuntime.SetInstrumentation` (C#).
- (void)setInstrumentation:(NSString*)source active:(BOOL)active;

/// YES while the SDK is itself loading the GPU/CPU (video record or live IDE
/// stream). Read by the perf monitor to skip unrepresentative FPS samples.
- (BOOL)isInstrumented;

/// Merge server-authored attachment rules into `config[@"attachmentRules"]`.
/// Idempotent across polls — entries with an `id` field replace any prior
/// server-authored entry; game-declared rules (no id) are kept untouched.
/// Tokens (`[PersistentDataPath]`, `[TemporaryCachePath]`, `[DataPath]`) in
/// the rule's `target` field are resolved against the runtime's `paths`
/// object before being persisted.
+ (void)mergeServerAttachmentRules:(NSArray*)serverRules;

/// Stash the project id (from the poll bootstrap) into `metadata[@"projectId"]`
/// so project-scoped SDK endpoints (profile-picker credential sign-in) can
/// build their URLs before the user is authenticated. Mirrors
/// `BugpunchRuntime.setProjectId` on Android. No-op on empty input.
+ (void)setProjectId:(nullable NSString*)projectId;

/// Merge the server-pushed SSO client ids (`gameConfig.sso`) into `config` so
/// Google/Apple sign-in can read `googleClientIdIos`, `googleWebClientId`,
/// `appleBundleId`, `appleServicesId`, `appleRedirectUri`. These are
/// configured per-project in the dashboard and arrive on every poll, not in
/// the build-time config blob. Mirrors `BugpunchRuntime.mergeServerSsoConfig`
/// on Android. Best-effort; only non-empty string values are copied.
+ (void)mergeServerSsoConfig:(nullable NSDictionary*)sso;

/// Start the CADisplayLink frame tick. Drives FPS measurement and the
/// periodic backbuffer flush. Idempotent — called once from
/// `Bugpunch_StartDebugMode`; re-entry is a no-op.
- (void)startFrameTick;

/// Tear down the frame tick. Safe to call from `Bugpunch_StopDebugMode`
/// regardless of whether `startFrameTick` ran.
- (void)stopFrameTick;

/// Insert / overwrite an auxiliary account identity. Provider keyed; a
/// second call with the same provider REPLACES the prior entry in place
/// (preserving insertion order). Trims provider/username/email to 200
/// chars; no-op when provider or username is empty after trim. Email may
/// be nil or empty (omitted from the entry in that case).
- (void)setAccount:(NSString*)provider username:(NSString*)username email:(nullable NSString*)email;

/// Drop every account previously inserted by `setAccount:`. Safe to call
/// when none have been set.
- (void)clearAccounts;

@end

NS_ASSUME_NONNULL_END
