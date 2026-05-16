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

/// Disk path for the periodic Metal backbuffer dump (context screenshot
/// for the next-launch crash drain). nil disables the periodic flush.
@property (nonatomic, copy, nullable) NSString* ctxShotDiskPath;

/// Player auth identity — set after a successful POST to
/// /api/v1/chat/auth/verify (driven from the C# Bugpunch.SetPlayerAuthSession
/// path). Mirrors the four fields on the Java + C# runtimes so chat HTTP
/// calls on every lane stamp the same X-Player-Auth-* / X-Player-Email
/// headers. nil when the player hasn't signed in yet.
@property (nonatomic, copy, nullable) NSString* playerAuthProvider;
@property (nonatomic, copy, nullable) NSString* playerAuthId;
@property (nonatomic, copy, nullable) NSString* playerEmail;
@property (nonatomic, copy, nullable) NSString* playerName;
/// Optional avatar URL set by the profile-picker flow on internal devices.
/// Empty / nil on the email-signin and public-player paths — the reporter
/// badge falls back to hash-coloured initials in those cases.
@property (nonatomic, copy, nullable) NSString* playerAvatarUrl;

/// Game-seeded email used to pre-populate BugpunchEmailEntry for public
/// testers. Set via `Bugpunch.SetPlayerEmail` (C#) → BugpunchNative.
/// Independent of `playerEmail` (which is the *verified* identity after
/// an email-signin round-trip).
@property (nonatomic, copy, nullable) NSString* prefillEmail;

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

+ (instancetype)shared;

/// Merge server-authored attachment rules into `config[@"attachmentRules"]`.
/// Idempotent across polls — entries with an `id` field replace any prior
/// server-authored entry; game-declared rules (no id) are kept untouched.
/// Tokens (`[PersistentDataPath]`, `[TemporaryCachePath]`, `[DataPath]`) in
/// the rule's `target` field are resolved against the runtime's `paths`
/// object before being persisted.
+ (void)mergeServerAttachmentRules:(NSArray*)serverRules;

/// Start the CADisplayLink frame tick. Drives FPS measurement and the
/// periodic backbuffer flush. Idempotent — called once from
/// `Bugpunch_StartDebugMode`; re-entry is a no-op.
- (void)startFrameTick;

/// Tear down the frame tick. Safe to call from `Bugpunch_StopDebugMode`
/// regardless of whether `startFrameTick` ran.
- (void)stopFrameTick;

@end

NS_ASSUME_NONNULL_END
