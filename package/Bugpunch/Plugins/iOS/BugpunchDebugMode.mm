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

// Symbols from sibling files
extern "C" {
    bool Bugpunch_InstallCrashHandlers(const char* crashDir);
    void Bugpunch_SetCrashMetadata(const char* appVersion, const char* bundleId,
        const char* unityVersion, const char* deviceModel,
        const char* osVersion, const char* gpuName);
    void Bugpunch_CaptureScreenshot(const char* requestId, const char* outputPath,
        int quality, void (*cb)(const char*, int, const char*));
    void Bugpunch_EnqueueReport(const char* url, const char* apiKey,
        const char* metadataJson, const char* screenshotPath, const char* videoPath,
        const char* annotationsPath);
    void Bugpunch_DrainUploadQueue(void);
    void Bugpunch_PresentReportForm(const char* screenshotPath,
        const char* title, const char* description);
    // Ring recorder (iOS):
    bool BugpunchRing_HasFootage(void);
    bool BugpunchRing_Dump(const char* outputPath);
    void BugpunchRing_Configure(int width, int height, int fps, int bitrate, int windowSeconds);
    bool BugpunchRing_Start(void);
    bool BugpunchRing_IsRunning(void);
}

// Log reader (same file for now — compact enough to inline).
@interface BPLogReader : NSObject
+ (void)startWithMaxEntries:(NSInteger)n;
+ (void)stop;
+ (NSString*)snapshotJson;
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
        @"version":      d.metadata[@"appVersion"] ?: @"",
        @"bundleId":     d.metadata[@"bundleId"] ?: @"",
        @"unityVersion": d.metadata[@"unityVersion"] ?: @"",
        @"scene":        d.metadata[@"scene"] ?: @"",
        @"fps":          @(d.fps),
    };
    NSMutableDictionary* custom = [d.customData mutableCopy];
    if (extra) [custom addEntriesFromDictionary:extra];
    m[@"customData"] = custom;

    NSString* logsJson = [BPLogReader snapshotJson];
    if (logsJson.length > 0) {
        NSData* logData = [logsJson dataUsingEncoding:NSUTF8StringEncoding];
        id parsedLogs = [NSJSONSerialization JSONObjectWithData:logData options:0 error:nil];
        if (parsedLogs) m[@"logs"] = parsedLogs;
    }

    NSData* out = [NSJSONSerialization dataWithJSONObject:m options:0 error:nil];
    return out ? [[NSString alloc] initWithData:out encoding:NSUTF8StringEncoding] : @"{}";
}

static NSString* BPDumpRingIfRunning(void) {
    if (!BugpunchRing_HasFootage()) return nil;
    NSString* caches = NSSearchPathForDirectoriesInDomains(
        NSCachesDirectory, NSUserDomainMask, YES).firstObject;
    NSString* path = [caches stringByAppendingPathComponent:
        [NSString stringWithFormat:@"bp_vid_%f.mp4", [NSDate timeIntervalSinceReferenceDate]]];
    if (!BugpunchRing_Dump([path UTF8String])) return nil;
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
    NSString* shotPath = [caches stringByAppendingPathComponent:
        [NSString stringWithFormat:@"bp_shot_%f.jpg", [NSDate timeIntervalSinceReferenceDate]]];
    Bugpunch_CaptureScreenshot([[NSString stringWithFormat:@"rb_%f",
        [NSDate timeIntervalSinceReferenceDate]] UTF8String],
        [shotPath UTF8String], 85, NULL);

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
    NSString* metadataJson = BPBuildMetadataJson(type, title, description, extra, nil, nil);
    NSString* videoPath = BPDumpRingIfRunning();

    Bugpunch_EnqueueReport([url UTF8String], [apiKey UTF8String],
        [metadataJson UTF8String], [shotPath UTF8String],
        videoPath ? [videoPath UTF8String] : "", "");
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

    NSInteger logSize = [cfg[@"logBufferSize"] integerValue];
    if (logSize < 50) logSize = 500;
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

void Bugpunch_UpdateScene(const char* scene) {
    BPDebugMode* d = [BPDebugMode shared];
    if (scene) d.metadata[@"scene"] = [NSString stringWithUTF8String:scene];
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

    NSString* metadataJson = BPBuildMetadataJson(@"bug", nsTitle, nsDesc, nil, nsEmail, nsSev);
    NSString* videoPath = BPDumpRingIfRunning();

    Bugpunch_EnqueueReport([url UTF8String], [apiKey UTF8String],
        [metadataJson UTF8String],
        screenshotPath ? screenshotPath : "",
        videoPath ? [videoPath UTF8String] : "",
        annotationsPath ? annotationsPath : "");
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

}

// ── Log reader (OSLogStore, iOS 15+) ──

@implementation BPLogReader {
}

static NSMutableArray* gLogBuffer;
static NSInteger gMaxEntries = 500;
static dispatch_source_t gLogTimer;
static NSDate* gLastFetch;

+ (void)startWithMaxEntries:(NSInteger)n {
    gMaxEntries = MAX(50, n);
    gLogBuffer = [NSMutableArray array];
    gLastFetch = [NSDate date];

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
            NSDictionary* entry = @{
                @"time": @([e.date timeIntervalSince1970] * 1000),
                @"type": [BPLogReader mapLevel:e.level],
                @"message": e.composedMessage ?: @"",
                @"stackTrace": @"",
            };
            @synchronized (gLogBuffer) {
                [gLogBuffer addObject:entry];
                while (gLogBuffer.count > gMaxEntries)
                    [gLogBuffer removeObjectAtIndex:0];
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
    NSArray* copy;
    @synchronized (gLogBuffer) { copy = [gLogBuffer copy]; }
    if (!copy) return @"[]";
    NSData* d = [NSJSONSerialization dataWithJSONObject:copy options:0 error:nil];
    return d ? [[NSString alloc] initWithData:d encoding:NSUTF8StringEncoding] : @"[]";
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
