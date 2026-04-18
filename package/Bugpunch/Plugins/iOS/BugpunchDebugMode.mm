// BugpunchDebugMode.mm — master coordinator for debug mode on iOS.
//
// Unity C# calls Bugpunch_StartDebugMode once with a config JSON. Native owns
// everything after that: crash handlers, log capture, shake detection,
// screenshot, upload queue. C# just calls Bugpunch_ReportBug when it has
// something to report.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <CoreMotion/CoreMotion.h>
#import <OSLog/OSLog.h>
#import <zlib.h>

// Symbols from sibling files
extern "C" {
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
    void BugpunchTouch_InjectTap(float x, float y);
    void BugpunchTouch_InjectSwipe(float x1, float y1, float x2, float y2, int durationMs);
}

// Log reader (same file for now — compact enough to inline).
@interface BPLogReader : NSObject
+ (void)startWithMaxEntries:(NSInteger)n;
+ (void)stop;
+ (NSString*)snapshotJson;
+ (void)pushEntryWithType:(NSString*)type message:(NSString*)message stackTrace:(NSString*)stackTrace;
@end

@interface BPShake : NSObject
+ (void)startWithThreshold:(double)t onShake:(void(^)(void))cb;
+ (void)stop;
@end

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
    if ([type isEqualToString:@"feedback"]) return @"/api/reports/feedback";
    if ([type isEqualToString:@"crash"])    return @"/api/crashes";
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
    };
    m[@"app"] = @{
        @"version":       d.metadata[@"appVersion"] ?: @"",
        @"bundleId":      d.metadata[@"bundleId"] ?: @"",
        @"unityVersion":  d.metadata[@"unityVersion"] ?: @"",
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

/** Gzip the logs JSON snapshot to a temp file. Returns the path, or nil. */
static NSString* BPWriteGzipLogs(void) {
    NSString* logsJson = [BPLogReader snapshotJson];
    if (!logsJson || logsJson.length < 3) return nil;  // "[]" = empty
    NSData* raw = [logsJson dataUsingEncoding:NSUTF8StringEncoding];
    if (!raw) return nil;
    NSString* caches = NSSearchPathForDirectoriesInDomains(
        NSCachesDirectory, NSUserDomainMask, YES).firstObject;
    NSString* path = [caches stringByAppendingPathComponent:
        [NSString stringWithFormat:@"bp_logs_%f.json.gz",
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
    NSString* url = [serverUrl stringByAppendingString:BPEndpointFor(type)];
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
    if (d.started) return true;

    NSDictionary* cfg = @{};
    if (configJson && *configJson) {
        NSData* data = [[NSString stringWithUTF8String:configJson]
            dataUsingEncoding:NSUTF8StringEncoding];
        id parsed = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
        if ([parsed isKindOfClass:[NSDictionary class]]) cfg = parsed;
    }
    d.config = cfg;

    NSDictionary* metaDict = cfg[@"metadata"];
    if ([metaDict isKindOfClass:[NSDictionary class]]) {
        for (NSString* k in metaDict) {
            id v = metaDict[k];
            d.metadata[k] = [v isKindOfClass:[NSString class]] ? v
                : [NSString stringWithFormat:@"%@", v];
        }
    }

    // Detect installer mode (App Store / TestFlight / sideload).
    NSURL* receiptURL = [[NSBundle mainBundle] appStoreReceiptURL];
    if (receiptURL) {
        d.metadata[@"installerMode"] = [[receiptURL lastPathComponent]
            isEqualToString:@"sandboxReceipt"] ? @"testflight" : @"store";
    } else {
        d.metadata[@"installerMode"] = @"sideload";
    }

    // Crash handlers — write crashes to caches dir. Install also drains the uploader.
    NSString* caches = NSSearchPathForDirectoriesInDomains(
        NSCachesDirectory, NSUserDomainMask, YES).firstObject;
    NSString* crashDir = [caches stringByAppendingPathComponent:@"bugpunch_crashes"];
    [[NSFileManager defaultManager] createDirectoryAtPath:crashDir
        withIntermediateDirectories:YES attributes:nil error:nil];
    Bugpunch_InstallCrashHandlers([crashDir UTF8String]);
    Bugpunch_SetCrashMetadata(
        [d.metadata[@"appVersion"] ?: @"" UTF8String],
        [d.metadata[@"bundleId"] ?: @"" UTF8String],
        [d.metadata[@"unityVersion"] ?: @"" UTF8String],
        [d.metadata[@"deviceModel"] ?: @"" UTF8String],
        [d.metadata[@"osVersion"] ?: @"" UTF8String],
        [d.metadata[@"gpu"] ?: @"" UTF8String]);

    // Metal backbuffer capture — swizzles presentDrawable to blit each frame
    // to a shared-memory texture. Near-zero CPU cost per frame. Used for:
    //   - ANR screenshots (background thread reads last frame)
    //   - Fast-path exception screenshots (no main-thread dependency)
    //   - Rolling 1/sec pre-capture (replaces drawViewHierarchyInRect timer)
    Bugpunch_InitBackbuffer();

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
            NSString* url = [[srv stringByTrimmingCharactersInSet:
                [NSCharacterSet characterSetWithCharactersInString:@"/"]]
                stringByAppendingString:@"/api/crashes"];
            extern const char* Bugpunch_GetPendingCrashFiles(void);
            extern const char* Bugpunch_ReadCrashFile(const char*);
            extern bool Bugpunch_DeleteCrashFile(const char*);
            extern void BugpunchUploader_EnqueueJson(const char*, const char*, const char*);

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
                    // Extract screenshot path for ANR reports.
                    NSString* shotPath = nil;
                    for (NSString* line in [rawStr componentsSeparatedByString:@"\n"]) {
                        if ([line hasPrefix:@"---"]) break;
                        if ([line hasPrefix:@"screenshot:"]) {
                            shotPath = [line substringFromIndex:@"screenshot:".length];
                            break;
                        }
                    }
                    BOOL hasShot = shotPath.length > 0 &&
                        [[NSFileManager defaultManager] fileExistsAtPath:shotPath];

                    NSError* e = nil;
                    NSData* bodyData = [NSJSONSerialization dataWithJSONObject:body options:0 error:&e];
                    if (bodyData) {
                        NSString* bodyStr = [[NSString alloc] initWithData:bodyData
                            encoding:NSUTF8StringEncoding];
                        if (hasShot) {
                            // Use multipart upload so the ANR screenshot is attached.
                            Bugpunch_EnqueueReport([url UTF8String], [key UTF8String],
                                [bodyStr UTF8String], [shotPath UTF8String], "", "");
                        } else {
                            BugpunchUploader_EnqueueJson([url UTF8String], [key UTF8String], [bodyStr UTF8String]);
                        }
                        Bugpunch_DeleteCrashFile([p UTF8String]);
                        NSLog(@"[Bugpunch] queued pending crash: %@ (screenshot=%@)", p, hasShot ? @"yes" : @"no");
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
    if (skipConsent) {
        dispatch_async(dispatch_get_main_queue(), ^{ BPStartRingFromConfig(); });
    } else {
        Bugpunch_PresentConsentSheet(^{ BPStartRingFromConfig(); });
    }
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

// ── Log reader (OSLogStore, iOS 15+) ──
//
// Keeps the first kStartupSize entries in a separate array so they're never
// evicted. Recent entries live in a capped ring buffer. On snapshot, emits:
//   [startup entries] + [breaker with skipped count] + [recent entries]

@implementation BPLogReader {
}

static const NSInteger kStartupSize = 2000;
static NSMutableArray* gStartupBuffer;
static NSMutableArray* gLogBuffer;
static NSInteger gMaxEntries = 2000;
static NSInteger gSkippedCount = 0;
static BOOL gStartupFull = NO;
static dispatch_source_t gLogTimer;
static NSDate* gLastFetch;

+ (void)startWithMaxEntries:(NSInteger)n {
    gMaxEntries = MAX(50, n);
    gStartupBuffer = [NSMutableArray array];
    gLogBuffer = [NSMutableArray array];
    gStartupFull = NO;
    gSkippedCount = 0;
    // Look back 60s on first poll to capture startup logs.
    gLastFetch = [NSDate dateWithTimeIntervalSinceNow:-60];

    // Poll OSLogStore every 2s. Too-frequent polling hurts battery; 2s is
    // fine given crash dumps happen on demand and buffer is just "recent
    // context".
    dispatch_queue_t q = dispatch_queue_create("com.oddgames.bugpunch.logreader", 0);
    gLogTimer = dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER, 0, 0, q);
    dispatch_source_set_timer(gLogTimer, DISPATCH_TIME_NOW,
        2 * NSEC_PER_SEC, 500 * NSEC_PER_MSEC);
    dispatch_source_set_event_handler(gLogTimer, ^{ [BPLogReader pullOnce]; });
    dispatch_resume(gLogTimer);
}

+ (void)stop {
    if (gLogTimer) { dispatch_source_cancel(gLogTimer); gLogTimer = nil; }
}

+ (void)pullOnce {
    if (@available(iOS 15.0, *)) {
        NSError* err = nil;
        OSLogStore* store = [OSLogStore storeWithScope:OSLogStoreCurrentProcessIdentifier error:&err];
        if (!store) return;
        OSLogPosition* pos = [store positionWithDate:gLastFetch];
        OSLogEnumerator* en = [store entriesEnumeratorWithOptions:0 position:pos predicate:nil error:&err];
        if (!en) return;
        for (OSLogEntryLog* e in en) {
            if (![e isKindOfClass:[OSLogEntryLog class]]) continue;
            NSString* type = [BPLogReader mapLevel:e.level];
            NSString* msg = e.composedMessage ?: @"";
            @synchronized (gLogBuffer) {
                // Dedup: check tail of ring buffer, then startup buffer.
                NSMutableDictionary* last = gLogBuffer.lastObject;
                if (!last && gStartupBuffer.count > 0) last = gStartupBuffer.lastObject;
                if (last && [last[@"type"] isEqualToString:type]
                         && [last[@"message"] isEqualToString:msg]) {
                    last[@"repeat"] = @([last[@"repeat"] integerValue] + 1);
                } else {
                    NSMutableDictionary* entry = [@{
                        @"time": @([e.date timeIntervalSince1970] * 1000),
                        @"type": type,
                        @"message": msg,
                        @"stackTrace": @"",
                        @"repeat": @1,
                    } mutableCopy];
                    // Fill startup buffer first.
                    if (!gStartupFull) {
                        [gStartupBuffer addObject:entry];
                        if ((NSInteger)gStartupBuffer.count >= kStartupSize) gStartupFull = YES;
                    } else {
                        [gLogBuffer addObject:entry];
                        while ((NSInteger)gLogBuffer.count > gMaxEntries) {
                            [gLogBuffer removeObjectAtIndex:0];
                            gSkippedCount++;
                        }
                    }
                }
            }
        }
        gLastFetch = [NSDate date];
    }
}

+ (NSString*)mapLevel:(OSLogEntryLogLevel)level API_AVAILABLE(ios(15.0)) {
    switch (level) {
        case OSLogEntryLogLevelFault:
        case OSLogEntryLogLevelError: return @"Error";
        case OSLogEntryLogLevelNotice: return @"Warning";
        default: return @"Log";
    }
}

+ (NSString*)snapshotJson {
    NSMutableArray* combined = [NSMutableArray array];
    @synchronized (gLogBuffer) {
        [combined addObjectsFromArray:gStartupBuffer];
        if (gSkippedCount > 0) {
            [combined addObject:@{
                @"type": @"Log",
                @"message": [NSString stringWithFormat:@"--- %ld log entries omitted ---", (long)gSkippedCount],
                @"stackTrace": @"",
            }];
        }
        [combined addObjectsFromArray:gLogBuffer];
    }
    if (combined.count == 0) return @"[]";
    NSData* d = [NSJSONSerialization dataWithJSONObject:combined options:0 error:nil];
    return d ? [[NSString alloc] initWithData:d encoding:NSUTF8StringEncoding] : @"[]";
}

+ (void)pushEntryWithType:(NSString*)type message:(NSString*)message stackTrace:(NSString*)stackTrace {
    if (!gLogBuffer) return;  // not started yet
    @synchronized (gLogBuffer) {
        // Dedup against tail (same as pullOnce).
        NSMutableDictionary* last = gLogBuffer.lastObject;
        if (!last && gStartupBuffer.count > 0) last = gStartupBuffer.lastObject;
        if (last && [last[@"type"] isEqualToString:type]
                 && [last[@"message"] isEqualToString:message]) {
            last[@"repeat"] = @([last[@"repeat"] integerValue] + 1);
            return;
        }
        NSMutableDictionary* entry = [@{
            @"time": @([[NSDate date] timeIntervalSince1970] * 1000),
            @"type": type ?: @"Log",
            @"message": message ?: @"",
            @"stackTrace": stackTrace ?: @"",
            @"repeat": @1,
        } mutableCopy];
        if (!gStartupFull) {
            [gStartupBuffer addObject:entry];
            if ((NSInteger)gStartupBuffer.count >= kStartupSize) gStartupFull = YES;
        } else {
            [gLogBuffer addObject:entry];
            while ((NSInteger)gLogBuffer.count > gMaxEntries) {
                [gLogBuffer removeObjectAtIndex:0];
                gSkippedCount++;
            }
        }
    }
}
@end

// ── Shake detector (CoreMotion) ──

@implementation BPShake
static CMMotionManager* gMotion;
static void (^gShakeCb)(void);

+ (void)startWithThreshold:(double)t onShake:(void(^)(void))cb {
    if (gMotion) return;
    gMotion = [CMMotionManager new];
    gShakeCb = [cb copy];
    if (!gMotion.accelerometerAvailable) return;
    gMotion.accelerometerUpdateInterval = 1.0 / 30.0;
    NSOperationQueue* q = [NSOperationQueue new];
    __block NSTimeInterval lastShake = 0;
    __block NSTimeInterval lastSpike = 0;
    __block int spikes = 0;
    [gMotion startAccelerometerUpdatesToQueue:q withHandler:^(CMAccelerometerData* d, NSError* e) {
        if (!d) return;
        double x = d.acceleration.x, y = d.acceleration.y, z = d.acceleration.z;
        // iOS accel is in G's; subtract 1 to remove gravity.
        double mag = sqrt(x * x + y * y + z * z) - 1.0;
        // Convert Gs → m/s² (×9.81) to match Android threshold semantics.
        if (mag * 9.81 < t) return;
        NSTimeInterval now = [NSDate timeIntervalSinceReferenceDate];
        if (now - lastSpike > 0.5) spikes = 0;
        spikes++;
        lastSpike = now;
        if (spikes >= 2 && now - lastShake > 2.0) {
            lastShake = now; spikes = 0;
            if (gShakeCb) gShakeCb();
        }
    }];
}

+ (void)stop {
    if (gMotion) [gMotion stopAccelerometerUpdates];
    gMotion = nil; gShakeCb = nil;
}
@end
