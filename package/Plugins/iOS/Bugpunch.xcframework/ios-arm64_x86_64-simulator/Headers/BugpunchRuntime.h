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

/// Game-supplied custom data (key/value). Set via `Bugpunch.SetCustomData`
/// and stamped onto every report.
@property (nonatomic, strong) NSMutableDictionary<NSString*, NSString*>* customData;

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

+ (instancetype)shared;

/// Start the CADisplayLink frame tick. Drives FPS measurement and the
/// periodic backbuffer flush. Idempotent — called once from
/// `Bugpunch_StartDebugMode`; re-entry is a no-op.
- (void)startFrameTick;

/// Tear down the frame tick. Safe to call from `Bugpunch_StopDebugMode`
/// regardless of whether `startFrameTick` ran.
- (void)stopFrameTick;

@end

NS_ASSUME_NONNULL_END
