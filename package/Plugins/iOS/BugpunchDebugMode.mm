// BugpunchDebugMode.mm — master coordinator for debug mode on iOS.
//
// Unity C# calls Bugpunch_StartDebugMode once with a config JSON. Native owns
// everything after that: crash handlers, log capture, shake detection,
// screenshot, upload queue. C# just calls Bugpunch_ReportBug when it has
// something to report.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <zlib.h>

#import "BugpunchTheme.h"
#import "BugpunchStrings.h"
#import "BugpunchLogReader.h"
#import "BugpunchShake.h"

// Symbols from sibling files
extern "C" {
    void Bugpunch_SetSdkErrorOverlay(int enabled);
    bool Bugpunch_InstallCrashHandlers(const char* crashDir);
    void Bugpunch_SetCrashMetadata(const char* appVersion, const char* bundleId,
        const char* unityVersion, const char* deviceModel,
        const char* osVersion, const char* gpuName);
    void Bugpunch_StartAnrWatchdog(int timeoutMs);
    void Bugpunch_StopAnrWatchdog(void);
    void Bugpunch_TickAnrWatchdog(void);
    bool Bugpunch_InitBackbuffer(void);
    bool Bugpunch_BackbufferReady(void);
    bool Bugpunch_WriteBackbufferJPEG(const char* outputPath, float quality);
    void Bugpunch_ShutdownBackbuffer(void);
    void Bugpunch_CaptureScreenshot(const char* requestId, const char* outputPath,
        int quality, void (*cb)(const char*, int, const char*));
    void Bugpunch_EnqueueReport(const char* url, const char* apiKey,
        const char* metadataJson, const char* screenshotPath, const char* videoPath,
        const char* annotationsPath);
    void Bugpunch_EnqueueReportWithTraces(const char* url, const char* apiKey,
        const char* metadataJson, const char* screenshotPath, const char* videoPath,
        const char* annotationsPath, const char* tracesJsonPath,
        const char* traceScreenshotPathsCsv);
    void Bugpunch_EnqueueReportFull(const char* url, const char* apiKey,
        const char* metadataJson, const char* screenshotPath, const char* videoPath,
        const char* annotationsPath, const char* tracesJsonPath,
        const char* traceScreenshotPathsCsv, const char* logsGzPath);
    void Bugpunch_EnqueueReportWithContext(const char* url, const char* apiKey,
        const char* metadataJson, const char* screenshotPath,
        const char* contextScreenshotPath,
        const char* videoPath, const char* annotationsPath,
        const char* tracesJsonPath, const char* traceScreenshotPathsCsv,
        const char* logsGzPath);
    void Bugpunch_DrainUploadQueue(void);
    void Bugpunch_PresentReportForm(const char* screenshotPath,
        const char* title, const char* description);
    // Ring recorder (iOS):
    bool BugpunchRing_HasFootage(void);
    bool BugpunchRing_Dump(const char* outputPath);
    void BugpunchRing_Configure(int width, int height, int fps, int bitrate, int windowSeconds);
    bool BugpunchRing_Start(void);
    void BugpunchRing_Stop(void);
    bool BugpunchRing_IsRunning(void);
    double BugpunchRing_GetLastDumpStartHostTime(void);
    double BugpunchRing_GetLastDumpEndHostTime(void);
    // Touch recorder (iOS):
    void BugpunchTouch_Configure(int maxEvents);
    bool BugpunchTouch_Start(void);
    void BugpunchTouch_Stop(void);
    bool BugpunchTouch_IsRunning(void);
    const char* BugpunchTouch_SnapshotJson(double startHostTime, double endHostTime);
    void BugpunchTouch_FreeJson(const char* json);
    void BugpunchTouch_GetCaptureSize(int* outWidth, int* outHeight);
    const char* BugpunchTouch_GetLiveTouches(int trailMs);
}

// BPLogReader (log ring + OSLogStore pull) lives in BugpunchLogReader.{h,mm}.
// BPShake (CoreMotion shake detector) lives in BugpunchShake.{h,mm}.
//
// Zero device-side parsing: each OSLog entry becomes one raw text line
// (logcat-ish format so the server's parser handles it the same way as
// Android). The SDK never builds structured JSON — the server owns that.

@interface BPDebugMode : NSObject
@property (nonatomic, strong) NSMutableDictionary<NSString*, NSString*>* metadata;
@property (nonatomic, strong) NSMutableDictionary<NSString*, NSString*>* customData;
@property (nonatomic, strong) NSDictionary* config;
@property (nonatomic, assign) NSTimeInterval lastAutoReport;
@property (nonatomic, assign) BOOL started;
@property (nonatomic, assign) BOOL reportInProgress;
@property (nonatomic, assign) int fps;
@property (nonatomic, strong) CADisplayLink* displayLink;
@property (nonatomic, assign) CFTimeInterval fpsWindowStart;
@property (nonatomic, assign) int frameCount;
@property (nonatomic, assign) int ctxShotFlushCounter;
@property (nonatomic, copy) NSString* ctxShotDiskPath;
// Cache-driven debug-mode auto-prompt state.
//  - autoPromptShown: launch-time prompt fired (or skipped because cache
//    wasn't internal); guards against re-prompting in the same session.
//  - startedFromCachedPrompt: ring buffer is running because the cached
//    prompt's consent was accepted; tunnel role-change → non-internal
//    tears it down. Manual Bugpunch_EnterDebugMode never sets this.
@property (nonatomic, assign) BOOL autoPromptShown;
@property (nonatomic, assign) BOOL startedFromCachedPrompt;
+ (instancetype)shared;
- (void)onFrame:(CADisplayLink*)link;
@end

@implementation BPDebugMode
+ (instancetype)shared {
    static BPDebugMode* i; static dispatch_once_t once;
    dispatch_once(&once, ^{ i = [BPDebugMode new];
        i.metadata = [NSMutableDictionary new];
        i.customData = [NSMutableDictionary new];
    });
    return i;
}
- (void)onFrame:(CADisplayLink*)link {
    // Tick the ANR watchdog from the main thread — this proves the main
    // thread is alive and processing display link callbacks.
    Bugpunch_TickAnrWatchdog();

    CFTimeInterval now = link.timestamp;
    if (self.fpsWindowStart == 0) { self.fpsWindowStart = now; self.frameCount = 0; return; }
    self.frameCount++;
    CFTimeInterval elapsed = now - self.fpsWindowStart;
    if (elapsed >= 1.0) {
        self.fps = (int)(self.frameCount / elapsed);
        self.frameCount = 0;
        self.fpsWindowStart = now;

        // Every ~3 seconds, persist the Metal backbuffer to disk so native
        // crash reports (SIGSEGV etc.) have a context screenshot on next launch.
        if (self.ctxShotDiskPath && ++self.ctxShotFlushCounter >= 3) {
            self.ctxShotFlushCounter = 0;
            dispatch_async(dispatch_get_global_queue(QOS_CLASS_UTILITY, 0), ^{
                Bugpunch_WriteBackbufferJPEG([self.ctxShotDiskPath UTF8String], 0.75f);
            });
        }
    }
}
@end

// ── Trace ring buffer ──
//
// Bounded list (max 50). Events carry timestamp, label, optional tags JSON
// string, optional screenshot path. TraceScreenshot captures async; the event
// lands in the buffer immediately and the screenshot path is filled in on
// completion.

@interface BPTraceEvent : NSObject
@property (nonatomic, assign) uint64_t timestampMs;
@property (nonatomic, copy) NSString* label;
@property (nonatomic, copy) NSString* tagsJson;         // nullable
@property (nonatomic, copy) NSString* screenshotPath;   // nullable
@end
@implementation BPTraceEvent
@end

static const NSInteger kBPTraceMax = 50;
static NSMutableArray<BPTraceEvent*>* gTraces;
static NSObject* gTraceLock;

static void BPEnsureTraceInit(void) {
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        gTraces = [NSMutableArray array];
        gTraceLock = [NSObject new];
    });
}

static void BPAddTrace(NSString* label, NSString* tagsJson, NSString* shotPath) {
    if (label.length == 0) return;
    BPEnsureTraceInit();
    BPTraceEvent* ev = [BPTraceEvent new];
    ev.timestampMs = (uint64_t)([[NSDate date] timeIntervalSince1970] * 1000.0);
    ev.label = label;
    ev.tagsJson = tagsJson;
    ev.screenshotPath = shotPath;
    @synchronized (gTraceLock) {
        [gTraces addObject:ev];
        while (gTraces.count > kBPTraceMax) [gTraces removeObjectAtIndex:0];
    }
}

/**
 * Drain the trace buffer to disk. Returns @[jsonPath, @[shotPaths]] or nil.
 */
static NSArray* BPPrepareTraceAttachments(void) {
    BPEnsureTraceInit();
    NSArray<BPTraceEvent*>* evs;
    @synchronized (gTraceLock) {
        if (gTraces.count == 0) return nil;
        evs = [gTraces copy];
        [gTraces removeAllObjects];
    }
    NSMutableArray* arr = [NSMutableArray array];
    NSMutableArray<NSString*>* shots = [NSMutableArray array];
    NSFileManager* fm = [NSFileManager defaultManager];
    for (BPTraceEvent* ev in evs) {
        NSMutableDictionary* o = [NSMutableDictionary dictionary];
        o[@"ts"] = @(ev.timestampMs);
        o[@"label"] = ev.label ?: @"";
        if (ev.tagsJson.length > 0) {
            NSData* d = [ev.tagsJson dataUsingEncoding:NSUTF8StringEncoding];
            id parsed = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
            if ([parsed isKindOfClass:[NSDictionary class]]) o[@"tags"] = parsed;
        }
        if (ev.screenshotPath.length > 0 && [fm fileExistsAtPath:ev.screenshotPath]) {
            NSDictionary* attrs = [fm attributesOfItemAtPath:ev.screenshotPath error:nil];
            if ([attrs[NSFileSize] unsignedLongLongValue] > 0) {
                o[@"screenshotIndex"] = @(shots.count);
                [shots addObject:ev.screenshotPath];
            }
        }
        [arr addObject:o];
    }
    NSData* data = [NSJSONSerialization dataWithJSONObject:arr options:0 error:nil];
    if (!data) return nil;
    NSString* caches = NSSearchPathForDirectoriesInDomains(
        NSCachesDirectory, NSUserDomainMask, YES).firstObject;
    NSString* path = [caches stringByAppendingPathComponent:
        [NSString stringWithFormat:@"bp_traces_%f.json",
            [NSDate timeIntervalSinceReferenceDate]]];
    if (![data writeToFile:path atomically:YES]) return nil;
    return @[ path, shots ];
}

static NSString* BPEndpointFor(NSString* type) {
    // exception/crash/anr go through the two-phase preflight path and never
    // hit this function. Only user-initiated bug reports and feedback do.
    if ([type isEqualToString:@"feedback"]) return @"/api/reports/feedback";
    return @"/api/reports/bug";
}

static NSString* BPJsonEscape(NSString* s) {
    if (!s) return @"";
    NSData* d = [NSJSONSerialization dataWithJSONObject:@[s] options:0 error:nil];
    if (!d) return @"";
    NSString* j = [[NSString alloc] initWithData:d encoding:NSUTF8StringEncoding];
    // Strip the surrounding ["..."]
    if (j.length < 4) return @"";
    return [j substringWithRange:NSMakeRange(2, j.length - 4)];
}

static NSString* BPBuildMetadataJson(NSString* type, NSString* title,
                                     NSString* description, NSDictionary* extra,
                                     NSString* reporterEmail, NSString* severity) {
    BPDebugMode* d = [BPDebugMode shared];
    NSMutableDictionary* m = [NSMutableDictionary dictionary];
    m[@"type"] = type ?: @"bug";
    if (title) m[@"title"] = title;
    if (description) m[@"description"] = description;
    if (reporterEmail.length) m[@"reporterEmail"] = reporterEmail;
    if (severity.length) m[@"severity"] = severity;
    m[@"timestamp"] = [[NSISO8601DateFormatter new] stringFromDate:[NSDate date]];

    m[@"device"] = @{
        @"model":    d.metadata[@"deviceModel"] ?: @"",
        @"os":       d.metadata[@"osVersion"] ?: @"",
        @"platform": @"iOS",
        @"gpu":      d.metadata[@"gpu"] ?: @"",
        @"deviceId": d.metadata[@"deviceId"] ?: @"",
    };
    m[@"app"] = @{
        @"version":       d.metadata[@"appVersion"] ?: @"",
        @"bundleId":      d.metadata[@"bundleId"] ?: @"",
        @"unityVersion":  d.metadata[@"unityVersion"] ?: @"",
        @"branch":        d.metadata[@"branch"] ?: @"",
        @"changeset":     d.metadata[@"changeset"] ?: @"",
        @"scene":         d.metadata[@"scene"] ?: @"",
        @"fps":           @(d.fps),
        @"installerMode": d.metadata[@"installerMode"] ?: @"unknown",
    };
    NSMutableDictionary* custom = [d.customData mutableCopy];
    if (extra) {
        // Hoist structured sidecar fields (video metadata, touch events) to
        // the top level — they aren't arbitrary user-provided custom data.
        for (NSString* k in extra) {
            if ([k isEqualToString:@"videoMeta"] || [k isEqualToString:@"touches"]) {
                m[k] = extra[k];
            } else {
                custom[k] = extra[k];
            }
        }
    }
    m[@"customData"] = custom;

    NSData* out = [NSJSONSerialization dataWithJSONObject:m options:0 error:nil];
    return out ? [[NSString alloc] initWithData:out encoding:NSUTF8StringEncoding] : @"{}";
}

// Dumps the video ring (if running) and harvests touch events aligned to the
// dumped clip. On success, writes the MP4 path to *outVideoPath and, if any
// touches landed in the video window, adds `touches` (array rebased to video
// t=0) and `video` (width/height/durationMs) into the caller's extras dict.
static void BPDumpRingAndTouches(NSString** outVideoPath, NSMutableDictionary* extras) {
    if (outVideoPath) *outVideoPath = nil;
    if (!BugpunchRing_HasFootage()) return;

    NSString* caches = NSSearchPathForDirectoriesInDomains(
        NSCachesDirectory, NSUserDomainMask, YES).firstObject;
    NSString* path = [caches stringByAppendingPathComponent:
        [NSString stringWithFormat:@"bp_vid_%f.mp4", [NSDate timeIntervalSinceReferenceDate]]];
    if (!BugpunchRing_Dump([path UTF8String])) return;
    if (outVideoPath) *outVideoPath = path;

    // Align touches to the clip's host-time window.
    double startHost = BugpunchRing_GetLastDumpStartHostTime();
    double endHost   = BugpunchRing_GetLastDumpEndHostTime();
    if (!extras || endHost <= startHost) return;

    int capW = 0, capH = 0;
    BugpunchTouch_GetCaptureSize(&capW, &capH);

    NSMutableDictionary* videoMeta = [NSMutableDictionary dictionary];
    videoMeta[@"durationMs"] = @((int)lround((endHost - startHost) * 1000.0));
    if (capW > 0 && capH > 0) {
        videoMeta[@"width"]  = @(capW);
        videoMeta[@"height"] = @(capH);
    }
    // Key is `videoMeta` (not `video`) — the multipart uploader uses `video`
    // for the MP4 binary; the server's middleware would clobber this object.
    extras[@"videoMeta"] = videoMeta;

    const char* cjson = BugpunchTouch_SnapshotJson(startHost, endHost);
    if (cjson) {
        NSString* js = [NSString stringWithUTF8String:cjson];
        BugpunchTouch_FreeJson(cjson);
        NSData* data = [js dataUsingEncoding:NSUTF8StringEncoding];
        id parsed = data ? [NSJSONSerialization JSONObjectWithData:data options:0 error:nil] : nil;
        if ([parsed isKindOfClass:[NSArray class]]) {
            extras[@"touches"] = parsed;
        }
    }
}

/** Gzip the raw log ring snapshot to a temp file. Returns the path, or nil
 *  on I/O failure. Empty snapshots still round-trip — the server synthesizes
 *  a stack-trace fallback when the parsed entry list is empty so the Logs
 *  tab is never blank when there's something useful to show. */
static NSString* BPWriteGzipLogs(void) {
    NSString* text = [BPLogReader snapshotText];
    if (!text) text = @"";
    NSData* raw = [text dataUsingEncoding:NSUTF8StringEncoding];
    if (!raw) return nil;
    NSString* caches = NSSearchPathForDirectoriesInDomains(
        NSCachesDirectory, NSUserDomainMask, YES).firstObject;
    NSString* path = [caches stringByAppendingPathComponent:
        [NSString stringWithFormat:@"bp_logs_%f.log.gz",
            [NSDate timeIntervalSinceReferenceDate]]];
    gzFile gz = gzopen([path fileSystemRepresentation], "wb");
    if (!gz) return nil;
    int written = gzwrite(gz, raw.bytes, (unsigned)raw.length);
    gzclose(gz);
    if (written <= 0) { [[NSFileManager defaultManager] removeItemAtPath:path error:nil]; return nil; }
    return path;
}

static void BPFireReport(NSString* type, NSString* title, NSString* description,
                         NSDictionary* extra) {
    BPDebugMode* d = [BPDebugMode shared];
    if (!d.started) { NSLog(@"[Bugpunch] reportBug before start"); return; }

    if ([type isEqualToString:@"exception"]) {
        NSTimeInterval cooldown = [d.config[@"autoReportCooldownSeconds"] doubleValue];
        if (cooldown <= 0) cooldown = 30.0;
        NSTimeInterval now = [NSDate timeIntervalSinceReferenceDate];
        if (now - d.lastAutoReport < cooldown) return;
        d.lastAutoReport = now;
    }

    NSString* caches = NSSearchPathForDirectoriesInDomains(
        NSCachesDirectory, NSUserDomainMask, YES).firstObject;
    NSTimeInterval now = [NSDate timeIntervalSinceReferenceDate];

    // Context screenshot — last GPU-rendered frame from the Metal backbuffer.
    // This is what was on screen at the moment of the event (or just before).
    NSString* ctxShotPath = nil;
    long long ctxShotTs = (long long)([[NSDate date] timeIntervalSince1970] * 1000);
    if (Bugpunch_BackbufferReady()) {
        ctxShotPath = [caches stringByAppendingPathComponent:
            [NSString stringWithFormat:@"bp_ctx_%f.jpg", now]];
        if (!Bugpunch_WriteBackbufferJPEG([ctxShotPath UTF8String], 0.85f)) {
            ctxShotPath = nil;
        }
    }

    // Event screenshot — also from the backbuffer (fast path, no main-thread dependency).
    NSString* shotPath = [caches stringByAppendingPathComponent:
        [NSString stringWithFormat:@"bp_shot_%f.jpg", now]];
    long long eventShotTs = (long long)([[NSDate date] timeIntervalSince1970] * 1000);
    Bugpunch_WriteBackbufferJPEG([shotPath UTF8String], 0.85f);

    // User-initiated bug reports show the form; silent reports enqueue directly.
    BOOL showForm = [type isEqualToString:@"bug"];
    if (showForm) {
        if (d.reportInProgress) {
            NSLog(@"[Bugpunch] report already in progress, ignoring");
            return;
        }
        d.reportInProgress = YES;
        Bugpunch_PresentReportForm([shotPath UTF8String],
            [(title ?: @"") UTF8String], [(description ?: @"") UTF8String]);
        return;
    }

    NSString* serverUrl = d.config[@"serverUrl"] ?: @"";
    NSString* apiKey = d.config[@"apiKey"] ?: @"";
    if (serverUrl.length == 0 || apiKey.length == 0) {
        NSLog(@"[Bugpunch] serverUrl/apiKey not configured — skipping report");
        return;
    }
    while ([serverUrl hasSuffix:@"/"])
        serverUrl = [serverUrl substringToIndex:serverUrl.length - 1];
    NSString* videoPath = nil;
    NSMutableDictionary* mergedExtra = extra ? [extra mutableCopy] : [NSMutableDictionary dictionary];
    BPDumpRingAndTouches(&videoPath, mergedExtra);

    // Attach timestamped screenshots array to metadata so the dashboard can
    // show prev/next navigation between context and event screenshots.
    NSMutableArray* screenshotsMeta = [NSMutableArray array];
    if (ctxShotPath) {
        [screenshotsMeta addObject:@{
            @"type": @"context", @"timestampMs": @(ctxShotTs), @"field": @"context_screenshot"
        }];
    }
    [screenshotsMeta addObject:@{
        @"type": @"event", @"timestampMs": @(eventShotTs), @"field": @"screenshot"
    }];
    mergedExtra[@"screenshots"] = screenshotsMeta;

    NSString* metadataJson = BPBuildMetadataJson(type, title, description, mergedExtra, nil, nil);
    NSString* logsGzPath = BPWriteGzipLogs();

    NSArray* traceAttach = BPPrepareTraceAttachments();
    NSString* tracesPath = traceAttach ? (NSString*)traceAttach[0] : nil;
    NSArray<NSString*>* traceShots = traceAttach ? (NSArray*)traceAttach[1] : nil;

    // Exception / crash / ANR go through the two-phase preflight path. Phase
    // 1 POSTs the lightweight metadata JSON to /api/crashes; server replies
    // with eventId + collect[] naming which heavy attachments it wants.
    // Phase 2 multipart-POSTs those attachments to .../events/:id/enrich.
    // bug / feedback keep the existing single-phase multipart since they're
    // user-initiated and rare.
    BOOL useTwoPhase = [type isEqualToString:@"exception"]
        || [type isEqualToString:@"crash"]
        || [type isEqualToString:@"anr"];

    if (useTwoPhase) {
        NSString* preflightUrl = [serverUrl stringByAppendingString:@"/api/crashes"];
        NSString* enrichTemplate = [serverUrl
            stringByAppendingString:@"/api/crashes/events/{id}/enrich"];
        NSMutableArray* attach = [NSMutableArray array];
        if (ctxShotPath) {
            [attach addObject:@{ @"field": @"context_screenshot",
                @"filename": @"context_screenshot.jpg", @"contentType": @"image/jpeg",
                @"path": ctxShotPath, @"requires": @"context_screenshot" }];
        }
        if (shotPath) {
            [attach addObject:@{ @"field": @"screenshot",
                @"filename": @"screenshot.jpg", @"contentType": @"image/jpeg",
                @"path": shotPath, @"requires": @"screenshot" }];
        }
        if (videoPath) {
            [attach addObject:@{ @"field": @"video",
                @"filename": @"video.mp4", @"contentType": @"video/mp4",
                @"path": videoPath, @"requires": @"video" }];
        }
        if (logsGzPath) {
            [attach addObject:@{ @"field": @"logs",
                @"filename": @"logs.log.gz", @"contentType": @"application/gzip",
                @"path": logsGzPath, @"requires": @"logs" }];
        }
        if (tracesPath) {
            [attach addObject:@{ @"field": @"traces",
                @"filename": @"traces.json", @"contentType": @"application/json",
                @"path": tracesPath, @"requires": @"traces" }];
        }
        if (traceShots) {
            for (NSUInteger i = 0; i < traceShots.count; i++) {
                NSString* p = traceShots[i];
                if (p.length == 0) continue;
                [attach addObject:@{
                    @"field": [NSString stringWithFormat:@"trace_%lu", (unsigned long)i],
                    @"filename": [NSString stringWithFormat:@"trace_%lu.jpg", (unsigned long)i],
                    @"contentType": @"image/jpeg",
                    @"path": p,
                    @"requires": @"traces" }];
            }
        }
        NSData* attachJsonData = [NSJSONSerialization dataWithJSONObject:attach options:0 error:nil];
        NSString* attachJson = attachJsonData
            ? [[NSString alloc] initWithData:attachJsonData encoding:NSUTF8StringEncoding]
            : @"[]";
        extern void Bugpunch_EnqueuePreflight(const char*, const char*, const char*, const char*, const char*);
        Bugpunch_EnqueuePreflight([preflightUrl UTF8String], [enrichTemplate UTF8String],
            [apiKey UTF8String], [metadataJson UTF8String], [attachJson UTF8String]);
        return;
    }

    NSString* url = [serverUrl stringByAppendingString:BPEndpointFor(type)];
    NSString* traceShotsCsv = traceShots.count ? [traceShots componentsJoinedByString:@","] : nil;
    Bugpunch_EnqueueReportWithContext([url UTF8String], [apiKey UTF8String],
        [metadataJson UTF8String], [shotPath UTF8String],
        ctxShotPath ? [ctxShotPath UTF8String] : "",
        videoPath ? [videoPath UTF8String] : "", "",
        tracesPath ? [tracesPath UTF8String] : NULL,
        traceShotsCsv ? [traceShotsCsv UTF8String] : NULL,
        logsGzPath ? [logsGzPath UTF8String] : NULL);
}

// ── C API exposed to Unity / native callers ──

extern "C" {

bool Bugpunch_StartDebugMode(const char* configJson) {
    BPDebugMode* d = [BPDebugMode shared];

    // Always re-parse and merge config + metadata so a second call from C#
    // after the early +load bootstrap refreshes Unity-runtime values
    // (Application.version, deviceId, persistentDataPath-resolved attachment
    // rules, etc.) over what was seeded from the bundled JSON.
    NSDictionary* cfg = @{};
    if (configJson && *configJson) {
        NSData* data = [[NSString stringWithUTF8String:configJson]
            dataUsingEncoding:NSUTF8StringEncoding];
        id parsed = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
        if ([parsed isKindOfClass:[NSDictionary class]]) cfg = parsed;
    }
    if (cfg.count > 0 || !d.config) d.config = cfg;

    // Push the native-overlay theme into BPTheme so every surface built
    // downstream (welcome card, request-help picker, recording overlay,
    // consent sheet, crash/bug dialogs) reads its colours, radii and font
    // sizes from one place. Nil / empty theme block falls back to defaults.
    [BPTheme applyFromJson:cfg[@"theme"]];

    // Apply user-visible strings + locale overrides — every overlay
    // resolves its labels through BPStrings text:fallback: so a missing
    // / empty block falls back to the caller's hardcoded English.
    [BPStrings applyFromJson:cfg[@"strings"]];

    // SDK self-diagnostic banner — visible on-screen pill that surfaces
    // internal SDK failures so the dev/QA isn't in the dark when the SDK
    // itself swallows an error. Default ON; disabled via
    // BugpunchConfig.showSdkErrorOverlay or runtime SetSdkErrorOverlay.
    {
        id flag = cfg[@"sdkErrorOverlay"];
        BOOL on = (flag == nil) ? YES : [flag boolValue];
        Bugpunch_SetSdkErrorOverlay(on ? 1 : 0);
    }

    NSDictionary* metaDict = cfg[@"metadata"];
    if ([metaDict isKindOfClass:[NSDictionary class]]) {
        for (NSString* k in metaDict) {
            id v = metaDict[k];
            d.metadata[k] = [v isKindOfClass:[NSString class]] ? v
                : [NSString stringWithFormat:@"%@", v];
        }
    }

    // Installer mode — same result either call, set once.
    if (!d.metadata[@"installerMode"]) {
        NSURL* receiptURL = [[NSBundle mainBundle] appStoreReceiptURL];
        if (receiptURL) {
            d.metadata[@"installerMode"] = [[receiptURL lastPathComponent]
                isEqualToString:@"sandboxReceipt"] ? @"testflight" : @"store";
        } else {
            d.metadata[@"installerMode"] = @"sideload";
        }
    }

    // Refresh crash handler metadata every call so a late refresh from C#
    // picks up Unity-runtime values for any crash that hits after Unity boots.
    Bugpunch_SetCrashMetadata(
        [d.metadata[@"appVersion"] ?: @"" UTF8String],
        [d.metadata[@"bundleId"] ?: @"" UTF8String],
        [d.metadata[@"unityVersion"] ?: @"" UTF8String],
        [d.metadata[@"deviceModel"] ?: @"" UTF8String],
        [d.metadata[@"osVersion"] ?: @"" UTF8String],
        [d.metadata[@"gpu"] ?: @"" UTF8String]);

    // Everything below is one-time init — crash handler install, backbuffer,
    // watchdog, log reader, shake, display link, crash drain.
    if (d.started) return true;

    NSString* caches = NSSearchPathForDirectoriesInDomains(
        NSCachesDirectory, NSUserDomainMask, YES).firstObject;
    NSString* crashDir = [caches stringByAppendingPathComponent:@"bugpunch_crashes"];
    [[NSFileManager defaultManager] createDirectoryAtPath:crashDir
        withIntermediateDirectories:YES attributes:nil error:nil];
    Bugpunch_InstallCrashHandlers([crashDir UTF8String]);

    // Metal backbuffer capture — swizzles presentDrawable to blit each frame
    // to a shared-memory texture. Near-zero CPU cost per frame. Used for:
    //   - ANR screenshots (background thread reads last frame)
    //   - Fast-path exception screenshots (no main-thread dependency)
    //   - Rolling 1/sec pre-capture (replaces drawViewHierarchyInRect timer)
    Bugpunch_InitBackbuffer();

    // Set disk path for periodic context screenshot persistence. On native crash
    // (SIGSEGV etc.) the process dies — this file on disk is the only way to get
    // a context screenshot on next launch.
    d.ctxShotDiskPath = [crashDir stringByAppendingPathComponent:@"context_screenshot.jpg"];

    // ANR watchdog — background thread checks that the main thread ticks.
    int anrMs = [cfg[@"anrTimeoutMs"] intValue];
    if (anrMs <= 0) anrMs = 5000;
    Bugpunch_StartAnrWatchdog(anrMs);

    NSInteger logSize = [cfg[@"logBufferSize"] integerValue];
    if (logSize < 50) logSize = 2000;
    [BPLogReader startWithMaxEntries:logSize];

    NSDictionary* shake = cfg[@"shake"];
    if ([shake isKindOfClass:[NSDictionary class]] && [shake[@"enabled"] boolValue]) {
        double threshold = [shake[@"threshold"] doubleValue];
        if (threshold <= 0) threshold = 2.5;
        [BPShake startWithThreshold:threshold onShake:^{
            BPFireReport(@"bug", @"Shake report", @"Triggered by shake gesture", nil);
        }];
    }

    // Frame tick for native FPS — CADisplayLink on main runloop.
    dispatch_async(dispatch_get_main_queue(), ^{
        d.displayLink = [CADisplayLink displayLinkWithTarget:d selector:@selector(onFrame:)];
        [d.displayLink addToRunLoop:[NSRunLoop mainRunLoop] forMode:NSRunLoopCommonModes];
    });

    d.started = YES;
    NSLog(@"[Bugpunch] debug mode started");

    // Drain any .crash files left by the signal / Mach handler on a prior
    // launch. Raw text is sent as `stackTrace` — server's crashSymbolicator
    // parses the ---STACK--- + ---BUILD_IDS--- sections. Same shape as
    // Android so the ingest contract is one path on the server.
    @try {
        NSString* srv = d.config[@"serverUrl"] ?: @"";
        NSString* key = d.config[@"apiKey"] ?: @"";
        if (srv.length > 0 && key.length > 0) {
            NSString* base = [srv stringByTrimmingCharactersInSet:
                [NSCharacterSet characterSetWithCharactersInString:@"/"]];
            NSString* preflightUrl = [base stringByAppendingString:@"/api/crashes"];
            NSString* enrichTemplate = [base
                stringByAppendingString:@"/api/crashes/events/{id}/enrich"];
            extern const char* Bugpunch_GetPendingCrashFiles(void);
            extern const char* Bugpunch_ReadCrashFile(const char*);
            extern bool Bugpunch_DeleteCrashFile(const char*);
            extern void Bugpunch_EnqueuePreflight(const char*, const char*, const char*, const char*, const char*);

            const char* listC = Bugpunch_GetPendingCrashFiles();
            if (listC && *listC) {
                NSArray* paths = [[NSString stringWithUTF8String:listC]
                    componentsSeparatedByString:@"\n"];
                for (NSString* p in paths) {
                    if (p.length == 0) continue;
                    const char* raw = Bugpunch_ReadCrashFile([p UTF8String]);
                    if (!raw || !*raw) {
                        Bugpunch_DeleteCrashFile([p UTF8String]);
                        continue;
                    }
                    NSMutableDictionary* body = [NSMutableDictionary dictionary];
                    NSString* rawStr = [NSString stringWithUTF8String:raw];
                    body[@"stackTrace"] = rawStr;
                    body[@"platform"] = @"ios";
                    // Extract first-line header fields for display. Mirrors
                    // the Java side — anything we miss still lives in stackTrace.
                    NSString* signal = nil, *faultAddr = nil, *type = nil;
                    for (NSString* line in [rawStr componentsSeparatedByString:@"\n"]) {
                        if ([line hasPrefix:@"---"]) break;
                        NSRange c = [line rangeOfString:@":"];
                        if (c.location == NSNotFound) continue;
                        NSString* k = [line substringToIndex:c.location];
                        NSString* v = [line substringFromIndex:c.location + 1];
                        if ([k isEqualToString:@"signal"]) signal = v;
                        else if ([k isEqualToString:@"fault_addr"]) faultAddr = v;
                        else if ([k isEqualToString:@"type"]) type = v;
                        else if ([k isEqualToString:@"app_version"]) body[@"buildVersion"] = v;
                        else if ([k isEqualToString:@"device_model"]) body[@"deviceName"] = v;
                    }
                    if ([type isEqualToString:@"NATIVE_SIGNAL"]) {
                        body[@"errorMessage"] = [NSString stringWithFormat:@"%@%@",
                            signal ?: @"NATIVE", faultAddr ? [@" at " stringByAppendingString:faultAddr] : @""];
                        body[@"category"] = @"crash";
                    } else if ([type isEqualToString:@"MACH_EXCEPTION"]) {
                        body[@"errorMessage"] = @"Mach exception";
                        body[@"category"] = @"crash";
                    } else if ([type isEqualToString:@"ANR"]) {
                        body[@"errorMessage"] = @"ANR — main thread unresponsive";
                        body[@"category"] = @"anr";
                    } else {
                        body[@"errorMessage"] = type ?: @"iOS crash";
                        body[@"category"] = @"crash";
                    }
                    // branch / changeset aren't written into the crash file —
                    // pull from current runtime metadata (they don't change
                    // within a build's lifetime so drain-time == crash-time).
                    BPDebugMode* cur = [BPDebugMode shared];
                    NSString* br = cur.metadata[@"branch"];
                    if (br.length) body[@"branch"] = br;
                    NSString* cs = cur.metadata[@"changeset"];
                    if (cs.length) body[@"changeset"] = cs;
                    // Extract screenshot + logs paths for ANR reports.
                    NSString* shotPath = nil;
                    NSString* logsPath = nil;
                    for (NSString* line in [rawStr componentsSeparatedByString:@"\n"]) {
                        if ([line hasPrefix:@"---"]) break;
                        if (!shotPath && [line hasPrefix:@"screenshot:"]) {
                            shotPath = [line substringFromIndex:@"screenshot:".length];
                        } else if (!logsPath && [line hasPrefix:@"logs:"]) {
                            logsPath = [line substringFromIndex:@"logs:".length];
                        }
                        if (shotPath && logsPath) break;
                    }
                    BOOL hasShot = shotPath.length > 0 &&
                        [[NSFileManager defaultManager] fileExistsAtPath:shotPath];
                    // For native crashes without an embedded screenshot, check the
                    // rolling buffer's persisted context screenshot on disk.
                    NSString* ctxShotPath = nil;
                    if (!hasShot) {
                        NSString* ctxPath = [crashDir stringByAppendingPathComponent:@"context_screenshot.jpg"];
                        if ([[NSFileManager defaultManager] fileExistsAtPath:ctxPath]) {
                            ctxShotPath = ctxPath;
                        }
                    }

                    NSError* e = nil;
                    NSData* bodyData = [NSJSONSerialization dataWithJSONObject:body options:0 error:&e];
                    if (bodyData) {
                        NSString* bodyStr = [[NSString alloc] initWithData:bodyData
                            encoding:NSUTF8StringEncoding];
                        // Previous-launch native crashes use the same preflight
                        // path — server gets device info up-front, decides
                        // whether the screenshot is worth uploading before the
                        // SDK ships it.
                        NSMutableArray* attach = [NSMutableArray array];
                        if (hasShot) {
                            [attach addObject:@{ @"field": @"screenshot",
                                @"filename": @"screenshot.jpg",
                                @"contentType": @"image/jpeg",
                                @"path": shotPath,
                                @"requires": @"screenshot" }];
                        }
                        if (ctxShotPath) {
                            [attach addObject:@{ @"field": @"context_screenshot",
                                @"filename": @"context_screenshot.jpg",
                                @"contentType": @"image/jpeg",
                                @"path": ctxShotPath,
                                @"requires": @"context_screenshot" }];
                        }
                        if (logsPath.length > 0 &&
                            [[NSFileManager defaultManager] fileExistsAtPath:logsPath]) {
                            [attach addObject:@{ @"field": @"logs",
                                @"filename": @"logs.log",
                                @"contentType": @"text/plain",
                                @"path": logsPath,
                                @"requires": @"logs" }];
                        }
                        NSData* attachJsonData = [NSJSONSerialization dataWithJSONObject:attach
                            options:0 error:nil];
                        NSString* attachJson = attachJsonData
                            ? [[NSString alloc] initWithData:attachJsonData encoding:NSUTF8StringEncoding]
                            : @"[]";
                        Bugpunch_EnqueuePreflight([preflightUrl UTF8String],
                            [enrichTemplate UTF8String], [key UTF8String],
                            [bodyStr UTF8String], [attachJson UTF8String]);
                        Bugpunch_DeleteCrashFile([p UTF8String]);
                        NSLog(@"[Bugpunch] queued pending crash: %@ (screenshot=%@, context=%@)",
                            p, hasShot ? @"yes" : @"no", ctxShotPath ? @"yes" : @"no");
                    } else {
                        NSLog(@"[Bugpunch] failed to serialize crash payload: %@", e);
                        // File stays — retry next launch.
                    }
                }
            }
        }
    } @catch (NSException* ex) {
        NSLog(@"[Bugpunch] crash drain failed: %@", ex);
    }

    // Cache-driven debug-mode auto-prompt. If the last roleConfig the
    // server delivered was "internal", fire the consent sheet now — don't
    // wait for the tunnel handshake to return. The tunnel will tear down
    // any speculative recording if the server now says non-internal.
    extern void Bugpunch_MaybeAutoPromptDebugModeOnLaunch(void);
    Bugpunch_MaybeAutoPromptDebugModeOnLaunch();

    return true;
}

extern "C" void Bugpunch_PresentConsentSheet(void (^onStart)(void));

static void BPStartRingFromConfig(void) {
    BPDebugMode* d = [BPDebugMode shared];
    NSDictionary* v = d.config[@"video"];
    int fps = [v isKindOfClass:[NSDictionary class]] ? [v[@"fps"] intValue] : 0;
    int bitrate = [v isKindOfClass:[NSDictionary class]] ? [v[@"bitrate"] intValue] : 0;
    int windowSec = [v isKindOfClass:[NSDictionary class]] ? [v[@"bufferSeconds"] intValue] : 0;
    if (fps <= 0) fps = 30;
    if (bitrate <= 0) bitrate = 2000000;
    if (windowSec <= 0) windowSec = 30;
    CGSize sz = UIScreen.mainScreen.bounds.size;
    CGFloat scale = UIScreen.mainScreen.scale;
    BugpunchRing_Configure((int)(sz.width * scale), (int)(sz.height * scale),
        fps, bitrate, windowSec);
    BugpunchRing_Start();
    // Touch recorder shares the same window; sized generously so a
    // multi-finger session over 30s never drops events.
    BugpunchTouch_Configure(windowSec * 600);
    BugpunchTouch_Start();
    // Show the floating debug widget (recording indicator + tools).
    extern void Bugpunch_ShowDebugWidget(void);
    Bugpunch_ShowDebugWidget();

}

void Bugpunch_EnterDebugMode(int skipConsent) {
    BPDebugMode* d = [BPDebugMode shared];
    if (!d.started) return;
    if (BugpunchRing_IsRunning()) return;
    // Manual entry — clear the cache-driven flag so a subsequent server
    // role of non-internal doesn't tear down a user-initiated recording.
    d.startedFromCachedPrompt = NO;
    if (skipConsent) {
        dispatch_async(dispatch_get_main_queue(), ^{ BPStartRingFromConfig(); });
    } else {
        Bugpunch_PresentConsentSheet(^{ BPStartRingFromConfig(); });
    }
}

// Cache-driven debug-mode auto-prompt key.
//   "internal" | "external" | "user" | absent (no cache yet → wait for server)
static NSString* const kBPLastTesterRoleKey = @"bugpunch.last_tester_role";

// Tunnel hooks: read/write the cache used by the launch-time auto-prompt.
// Implementations live here so the cache lives next to the prompt logic;
// BugpunchTunnel.mm calls these from cacheRoleConfig: when the server's
// roleConfig arrives.
extern "C" void Bugpunch_WriteLastTesterRoleCache(const char* normalizedRole) {
    if (!normalizedRole || !*normalizedRole) return;
    @autoreleasepool {
        NSString* v;
        if (strcmp(normalizedRole, "internal") == 0)      v = @"internal";
        else if (strcmp(normalizedRole, "external") == 0) v = @"external";
        else                                              v = @"user";
        [[NSUserDefaults standardUserDefaults] setObject:v forKey:kBPLastTesterRoleKey];
    }
}

extern "C" const char* Bugpunch_ReadLastTesterRoleCache(void) {
    NSString* v = [[NSUserDefaults standardUserDefaults] stringForKey:kBPLastTesterRoleKey];
    if (![v isKindOfClass:[NSString class]] || v.length == 0) return NULL;
    return [v UTF8String];
}

static void BPRunAutoPromptOnMain(void) {
    BPDebugMode* d = [BPDebugMode shared];
    if (d.autoPromptShown) return;
    if (!d.started) return;
    if (BugpunchRing_IsRunning()) return;

    NSString* cached = [[NSUserDefaults standardUserDefaults] stringForKey:kBPLastTesterRoleKey];
    if (![cached isEqualToString:@"internal"]) {
        // First-ever launch (nil) or last-known non-internal → no prompt.
        return;
    }
    d.autoPromptShown = YES;
    NSLog(@"[Bugpunch] cached role=internal — auto-prompting consent on launch");
    Bugpunch_PresentConsentSheet(^{
        [BPDebugMode shared].startedFromCachedPrompt = YES;
        BPStartRingFromConfig();
    });
}

/// Cache-driven launch flow. If the last roleConfig was "internal", show
/// the consent sheet immediately so the video ring can come up before the
/// tunnel handshake returns. Other cached values (and an empty cache)
/// wait for the server. Called once from Bugpunch_StartDebugMode after
/// debug mode is started.
extern "C" void Bugpunch_MaybeAutoPromptDebugModeOnLaunch(void) {
    // Defer to main queue. From +load this guarantees we run after main()
    // boots and the runloop is up. If UIApplication still has no scenes
    // by the time main fires (e.g. +load → main → before scene init), we
    // wait for UIApplicationDidFinishLaunchingNotification — the consent
    // sheet's keyWindow lookup needs a window to attach to.
    dispatch_async(dispatch_get_main_queue(), ^{
        BOOL hasScene = UIApplication.sharedApplication.connectedScenes.count > 0;
        if (!hasScene) {
            __block id obs = [[NSNotificationCenter defaultCenter]
                addObserverForName:UIApplicationDidFinishLaunchingNotification
                            object:nil queue:[NSOperationQueue mainQueue]
                        usingBlock:^(NSNotification* n) {
                BPRunAutoPromptOnMain();
                if (obs) [[NSNotificationCenter defaultCenter] removeObserver:obs];
                obs = nil;
            }];
            return;
        }
        BPRunAutoPromptOnMain();
    });
}

/// Tunnel callback: server-delivered roleConfig flipped TO "internal".
/// If the cached path didn't already prompt, prompt now.
extern "C" void Bugpunch_OnRoleBecameInternal(void) {
    BPDebugMode* d = [BPDebugMode shared];
    if (d.autoPromptShown) return;
    if (!d.started) return;
    if (BugpunchRing_IsRunning()) return;
    d.autoPromptShown = YES;
    NSLog(@"[Bugpunch] role flipped to internal — prompting consent mid-session");
    Bugpunch_PresentConsentSheet(^{
        [BPDebugMode shared].startedFromCachedPrompt = YES;
        BPStartRingFromConfig();
    });
}

/// Tunnel callback: server-delivered roleConfig flipped AWAY from "internal".
/// If the ring buffer is running because of the speculative cached prompt,
/// tear it down. Manual Bugpunch_EnterDebugMode recordings (which clear
/// startedFromCachedPrompt) are left alone.
extern "C" void Bugpunch_OnRoleLeftInternal(void) {
    BPDebugMode* d = [BPDebugMode shared];
    if (!d.startedFromCachedPrompt) return;
    NSLog(@"[Bugpunch] role left internal — tearing down speculative recording");
    d.startedFromCachedPrompt = NO;
    dispatch_async(dispatch_get_main_queue(), ^{
        if (BugpunchRing_IsRunning()) BugpunchRing_Stop();
        BugpunchTouch_Stop();
        extern void Bugpunch_HideDebugWidget(void);
        Bugpunch_HideDebugWidget();
    });
}

void Bugpunch_StopDebugMode(void) {
    BPDebugMode* d = [BPDebugMode shared];
    if (!d.started) return;
    [BPShake stop];
    [BPLogReader stop];
    BugpunchTouch_Stop();
    Bugpunch_StopAnrWatchdog();
    Bugpunch_ShutdownBackbuffer();
    [d.displayLink invalidate]; d.displayLink = nil;
    d.started = NO;
}

void Bugpunch_SetCustomData(const char* key, const char* value) {
    if (!key) return;
    NSString* k = [NSString stringWithUTF8String:key];
    BPDebugMode* d = [BPDebugMode shared];
    if (!value) [d.customData removeObjectForKey:k];
    else d.customData[k] = [NSString stringWithUTF8String:value];
}

extern void BugpunchPerfMonitor_OnSceneChange(NSString* newScene);

void Bugpunch_UpdateScene(const char* scene) {
    BPDebugMode* d = [BPDebugMode shared];
    NSString* s = scene ? [NSString stringWithUTF8String:scene] : nil;
    if (s) d.metadata[@"scene"] = s;
    BugpunchPerfMonitor_OnSceneChange(s);
}

const char* Bugpunch_GetInstallerMode(void) {
    NSString* m = [BPDebugMode shared].metadata[@"installerMode"] ?: @"unknown";
    return strdup([m UTF8String]);
}

void Bugpunch_SubmitReport(const char* title, const char* description,
                           const char* reporterEmail, const char* severity,
                           const char* screenshotPath, const char* annotationsPath) {
    BPDebugMode* d = [BPDebugMode shared];
    if (!d.started) return;
    NSString* serverUrl = d.config[@"serverUrl"] ?: @"";
    NSString* apiKey = d.config[@"apiKey"] ?: @"";
    if (serverUrl.length == 0 || apiKey.length == 0) return;
    while ([serverUrl hasSuffix:@"/"])
        serverUrl = [serverUrl substringToIndex:serverUrl.length - 1];
    NSString* url = [serverUrl stringByAppendingString:@"/api/reports/bug"];

    NSString* nsTitle = title ? [NSString stringWithUTF8String:title] : nil;
    NSString* nsDesc = description ? [NSString stringWithUTF8String:description] : nil;
    NSString* nsEmail = reporterEmail ? [NSString stringWithUTF8String:reporterEmail] : nil;
    NSString* nsSev = severity ? [NSString stringWithUTF8String:severity] : nil;

    NSString* videoPath = nil;
    NSMutableDictionary* mergedExtra = [NSMutableDictionary dictionary];
    BPDumpRingAndTouches(&videoPath, mergedExtra);
    NSString* metadataJson = BPBuildMetadataJson(@"bug", nsTitle, nsDesc,
        mergedExtra.count ? mergedExtra : nil, nsEmail, nsSev);
    NSString* logsGzPath = BPWriteGzipLogs();

    NSArray* traceAttach = BPPrepareTraceAttachments();
    NSString* tracesPath = traceAttach ? (NSString*)traceAttach[0] : nil;
    NSArray<NSString*>* traceShots = traceAttach ? (NSArray*)traceAttach[1] : nil;
    NSString* traceShotsCsv = traceShots.count ? [traceShots componentsJoinedByString:@","] : nil;

    Bugpunch_EnqueueReportFull([url UTF8String], [apiKey UTF8String],
        [metadataJson UTF8String],
        screenshotPath ? screenshotPath : "",
        videoPath ? [videoPath UTF8String] : "",
        annotationsPath ? annotationsPath : "",
        tracesPath ? [tracesPath UTF8String] : NULL,
        traceShotsCsv ? [traceShotsCsv UTF8String] : NULL,
        logsGzPath ? [logsGzPath UTF8String] : NULL);
}

void Bugpunch_ClearReportInProgress(void) {
    [BPDebugMode shared].reportInProgress = NO;
}

void Bugpunch_ReportBug(const char* type, const char* title, const char* description,
                        const char* extraJson) {
    NSString* t = type ? [NSString stringWithUTF8String:type] : @"bug";
    NSString* ti = title ? [NSString stringWithUTF8String:title] : nil;
    NSString* de = description ? [NSString stringWithUTF8String:description] : nil;
    NSDictionary* ex = nil;
    if (extraJson && *extraJson) {
        NSData* d = [[NSString stringWithUTF8String:extraJson]
            dataUsingEncoding:NSUTF8StringEncoding];
        id parsed = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
        if ([parsed isKindOfClass:[NSDictionary class]]) ex = parsed;
    }
    BPFireReport(t, ti, de, ex);
}

void Bugpunch_Trace(const char* label, const char* tagsJson) {
    if (!label || !*label) return;
    NSString* l = [NSString stringWithUTF8String:label];
    NSString* tj = (tagsJson && *tagsJson) ? [NSString stringWithUTF8String:tagsJson] : nil;
    BPAddTrace(l, tj, nil);
}

// ── Custom product-analytics events ──────────────────────────────────
//
// In-memory ring; flushed every kBPAnalyticsFlushSeconds or at
// kBPAnalyticsFlushSize, whichever comes first. Flush goes through
// BugpunchUploader_EnqueueJson so disk persistence + retry match the
// other upload paths.

static const NSInteger kBPAnalyticsBufferMax = 500;
static const NSInteger kBPAnalyticsFlushSize = 50;
static const NSTimeInterval kBPAnalyticsFlushSeconds = 15.0;

static NSMutableArray<NSDictionary*>* gAnalyticsBuffer;
static NSObject* gAnalyticsLock;
static dispatch_source_t gAnalyticsTimer;
static NSString* gAnalyticsSessionId;

extern "C" void BugpunchUploader_EnqueueJson(const char*, const char*, const char*);

static void BPFlushAnalytics(void);

static void BPEnsureAnalyticsInit(void) {
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        gAnalyticsBuffer = [NSMutableArray array];
        gAnalyticsLock = [NSObject new];
        gAnalyticsSessionId = [[NSUUID UUID] UUIDString];
        gAnalyticsTimer = dispatch_source_create(
            DISPATCH_SOURCE_TYPE_TIMER, 0, 0,
            dispatch_get_global_queue(QOS_CLASS_UTILITY, 0));
        uint64_t interval = (uint64_t)(kBPAnalyticsFlushSeconds * NSEC_PER_SEC);
        dispatch_source_set_timer(gAnalyticsTimer,
            dispatch_time(DISPATCH_TIME_NOW, interval), interval, interval / 10);
        dispatch_source_set_event_handler(gAnalyticsTimer, ^{ BPFlushAnalytics(); });
        dispatch_resume(gAnalyticsTimer);
    });
}

void Bugpunch_TrackEvent(const char* name, const char* propertiesJson) {
    if (!name || !*name) return;
    BPEnsureAnalyticsInit();

    NSString* evName = [NSString stringWithUTF8String:name];
    NSDictionary* props = nil;
    if (propertiesJson && *propertiesJson) {
        NSData* d = [[NSString stringWithUTF8String:propertiesJson]
            dataUsingEncoding:NSUTF8StringEncoding];
        id parsed = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
        if ([parsed isKindOfClass:[NSDictionary class]]) props = parsed;
    }

    BPDebugMode* dbg = [BPDebugMode shared];
    NSString* buildVersion = dbg.config[@"appVersion"] ?: @"";
    NSString* deviceId = dbg.config[@"deviceId"] ?: @"";
    NSString* scene = dbg.config[@"scene"] ?: @"";
    NSString* userId = dbg.customData[@"userId"];

    NSMutableDictionary* ev = [NSMutableDictionary dictionary];
    ev[@"name"] = evName;
    if (props) ev[@"properties"] = props;
    ev[@"timestamp"] = [[NSISO8601DateFormatter new] stringFromDate:[NSDate date]];
    ev[@"platform"] = @"ios";
    ev[@"deviceId"] = deviceId;
    ev[@"sessionId"] = gAnalyticsSessionId;
    ev[@"buildVersion"] = buildVersion;
    ev[@"branch"] = dbg.metadata[@"branch"] ?: @"";
    ev[@"changeset"] = dbg.metadata[@"changeset"] ?: @"";
    ev[@"scene"] = scene;
    if (userId) ev[@"userId"] = userId;

    BOOL shouldFlush = NO;
    @synchronized (gAnalyticsLock) {
        if (gAnalyticsBuffer.count >= kBPAnalyticsBufferMax) {
            [gAnalyticsBuffer removeObjectAtIndex:0];
        }
        [gAnalyticsBuffer addObject:ev];
        shouldFlush = (NSInteger)gAnalyticsBuffer.count >= kBPAnalyticsFlushSize;
    }
    if (shouldFlush) {
        dispatch_async(dispatch_get_global_queue(QOS_CLASS_UTILITY, 0), ^{
            BPFlushAnalytics();
        });
    }
}

static void BPFlushAnalytics(void) {
    NSArray* drained;
    @synchronized (gAnalyticsLock) {
        if (gAnalyticsBuffer.count == 0) return;
        drained = [gAnalyticsBuffer copy];
        [gAnalyticsBuffer removeAllObjects];
    }
    BPDebugMode* dbg = [BPDebugMode shared];
    NSString* serverUrl = dbg.config[@"serverUrl"] ?: @"";
    NSString* apiKey = dbg.config[@"apiKey"] ?: @"";
    if (serverUrl.length == 0 || apiKey.length == 0) return;
    while ([serverUrl hasSuffix:@"/"])
        serverUrl = [serverUrl substringToIndex:serverUrl.length - 1];
    NSString* url = [serverUrl stringByAppendingString:@"/api/v1/analytics/events"];

    NSDictionary* body = @{ @"events": drained };
    NSError* err = nil;
    NSData* data = [NSJSONSerialization dataWithJSONObject:body options:0 error:&err];
    if (!data) { NSLog(@"[Bugpunch] analytics serialize failed: %@", err); return; }
    NSString* bodyStr = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    BugpunchUploader_EnqueueJson([url UTF8String], [apiKey UTF8String], [bodyStr UTF8String]);
}

void Bugpunch_TraceScreenshot(const char* label, const char* tagsJson) {
    if (!label || !*label) return;
    NSString* l = [NSString stringWithUTF8String:label];
    NSString* tj = (tagsJson && *tagsJson) ? [NSString stringWithUTF8String:tagsJson] : nil;

    NSString* caches = NSSearchPathForDirectoriesInDomains(
        NSCachesDirectory, NSUserDomainMask, YES).firstObject;
    NSString* shotPath = [caches stringByAppendingPathComponent:
        [NSString stringWithFormat:@"bp_trace_%f.jpg",
            [NSDate timeIntervalSinceReferenceDate]]];

    // Reserve the slot immediately for ordering; fill path on success.
    BPEnsureTraceInit();
    BPTraceEvent* ev = [BPTraceEvent new];
    ev.timestampMs = (uint64_t)([[NSDate date] timeIntervalSince1970] * 1000.0);
    ev.label = l;
    ev.tagsJson = tj;
    @synchronized (gTraceLock) {
        [gTraces addObject:ev];
        while (gTraces.count > kBPTraceMax) [gTraces removeObjectAtIndex:0];
    }

    Bugpunch_CaptureScreenshot(
        [[NSString stringWithFormat:@"tr_%f", [NSDate timeIntervalSinceReferenceDate]] UTF8String],
        [shotPath UTF8String], 80,
        NULL);
    // The screenshot writes synchronously-or-async depending on the native
    // impl; if it lands by the time a report fires, the file check in
    // BPPrepareTraceAttachments will include it. Tag the event with the
    // expected path so we can check on drain.
    ev.screenshotPath = shotPath;
}

/// Push a Unity C# log entry into the native log buffer. Called from
/// BugpunchSceneTick via P/Invoke — ensures Unity Debug.Log output
/// appears in crash report logs even when OSLogStore misses them.
void Bugpunch_PushLogEntry(const char* type, const char* message,
                           const char* stackTrace) {
    if (!message) return;
    NSString* nsType = type ? [NSString stringWithUTF8String:type] : @"Log";
    NSString* nsMsg = [NSString stringWithUTF8String:message];
    NSString* nsStack = (stackTrace && *stackTrace)
        ? [NSString stringWithUTF8String:stackTrace] : @"";
    [BPLogReader pushEntryWithType:nsType message:nsMsg stackTrace:nsStack];
}

}

