// BugpunchPerfMonitor.mm — iOS native performance monitor.
//
// Runs on a dedicated GCD serial queue, sampling FPS and memory every 1s.
// Fires full snapshots (screenshot + metadata) only on:
//   1. Memory pressure (didReceiveMemoryWarning from UIKit)
//   2. Consistently bad FPS (rolling window average below threshold)
//
// OOM detection: on startup, checks NSUserDefaults for clean exit flag.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <mach/mach.h>
#import <os/proc.h>
#import <sys/sysctl.h>

// Sibling file symbols
extern "C" {
    void Bugpunch_CaptureScreenshot(const char* requestId, const char* outputPath,
        int quality, void (*cb)(const char*, int, const char*));
    void Bugpunch_EnqueueReport(const char* url, const char* apiKey,
        const char* metadataJson, const char* screenshotPath, const char* videoPath,
        const char* annotationsPath);
    void BugpunchUploader_EnqueueJson(const char* url, const char* apiKey,
        const char* jsonBody);
}

// Cross-lane state — config / metadata / customData / fps live on BPRuntime
// (mirrors BugpunchRuntime.java + BugpunchRuntime.cs).
#import "BugpunchRuntime.h"

// ── State ──

static dispatch_queue_t sPerfQueue;
static dispatch_source_t sPerfTimer;
static BOOL sPerfStarted = NO;

static int sPerfFpsThreshold = 30;
static int sPerfReportInterval = 60;

// Rolling window (60s)
static const int kWindowSize = 60;
static float sFpsWindow[kWindowSize];
static float sMemWindow[kWindowSize];
static int sWindowIdx = 0;
static int sWindowFilled = 0;

// Aggregation
static float sFpsMin, sFpsMax, sFpsSum;
static float sMemPeak;
static int sSampleCount;
static NSString* sCurrentScene = @"unknown";
static NSTimeInterval sSceneStartTime;

// Throttle
static NSTimeInterval sLastFpsReportTime;
static NSTimeInterval sLastPeriodicTime;

// OOM
static NSString* const kPerfPrefsKey = @"bugpunch_perf";
static NSString* const kCleanExitKey = @"bugpunch_perf_clean_exit";
static NSString* const kLastMemMBKey = @"bugpunch_perf_last_mem_mb";
static NSString* const kLastSceneKey = @"bugpunch_perf_last_scene";

// ── Helpers ──

static float BPGetUsedMemoryMB(void) {
    struct task_basic_info info;
    mach_msg_type_number_t size = TASK_BASIC_INFO_COUNT;
    if (task_info(mach_task_self(), TASK_BASIC_INFO, (task_info_t)&info, &size) == KERN_SUCCESS)
        return info.resident_size / (1024.0f * 1024.0f);
    return 0;
}

static float BPGetAvailableMemoryMB(void) {
    if (@available(iOS 13.0, *))
        return os_proc_available_memory() / (1024.0f * 1024.0f);
    return 0;
}

static int BPGetTotalMemoryMB(void) {
    return (int)([NSProcessInfo processInfo].physicalMemory / (1024 * 1024));
}

static NSString* BPDeviceTier(void) {
    int memMB = BPGetTotalMemoryMB();
    int cpuCount = (int)[NSProcessInfo processInfo].activeProcessorCount;
    if (memMB >= 6144 && cpuCount >= 6) return @"high";
    if (memMB >= 3072 && cpuCount >= 4) return @"mid";
    return @"low";
}

static NSString* BPThermalState(void) {
    switch ([NSProcessInfo processInfo].thermalState) {
        case NSProcessInfoThermalStateNominal: return @"nominal";
        case NSProcessInfoThermalStateFair: return @"fair";
        case NSProcessInfoThermalStateSerious: return @"serious";
        case NSProcessInfoThermalStateCritical: return @"critical";
        default: return @"unknown";
    }
}

static void BPResetAggregation(void) {
    sFpsMin = 0; sFpsMax = 0; sFpsSum = 0; sMemPeak = 0; sSampleCount = 0;
}

static float BPComputeWindowAvg(void) {
    if (sWindowFilled == 0) return 0;
    float sum = 0;
    for (int i = 0; i < sWindowFilled; i++) sum += sFpsWindow[i];
    return sum / sWindowFilled;
}

static float BPComputeP5(void) {
    if (sWindowFilled == 0) return 0;
    float sorted[kWindowSize];
    memcpy(sorted, sFpsWindow, sWindowFilled * sizeof(float));
    for (int i = 0; i < sWindowFilled - 1; i++)
        for (int j = i + 1; j < sWindowFilled; j++)
            if (sorted[i] > sorted[j]) { float t = sorted[i]; sorted[i] = sorted[j]; sorted[j] = t; }
    int idx = (int)ceil(0.05 * sWindowFilled) - 1;
    if (idx < 0) idx = 0;
    return sorted[idx];
}

// ── Event building ──

static NSString* BPBuildEventJson(NSString* trigger) {
    BPRuntime* r = [BPRuntime shared];
    NSMutableDictionary* event = [NSMutableDictionary dictionary];

    event[@"trigger"] = trigger;
    event[@"scene"] = sCurrentScene ?: @"unknown";
    event[@"buildVersion"] = r.metadata[@"appVersion"] ?: @"";
    event[@"branch"] = r.metadata[@"branch"] ?: @"";
    event[@"changeset"] = r.metadata[@"changeset"] ?: @"";
    event[@"platform"] = @"iOS";
    event[@"deviceId"] = [[[UIDevice currentDevice] identifierForVendor] UUIDString] ?: @"";
    event[@"deviceName"] = [UIDevice currentDevice].name ?: @"";
    event[@"deviceModel"] = r.metadata[@"deviceModel"] ?: @"";
    event[@"deviceTier"] = BPDeviceTier();
    event[@"gpu"] = r.metadata[@"gpu"] ?: @"";

    CGSize screenSize = [UIScreen mainScreen].bounds.size;
    CGFloat scale = [UIScreen mainScreen].scale;
    event[@"screenSize"] = [NSString stringWithFormat:@"%.0fx%.0f", screenSize.width * scale, screenSize.height * scale];
    event[@"systemMemoryMB"] = @(BPGetTotalMemoryMB());

    if (sSampleCount > 0) {
        event[@"fpsAvg"] = @(roundf(sFpsSum / sSampleCount * 10.0f) / 10.0f);
        event[@"fpsMin"] = @(sFpsMin);
        event[@"fpsMax"] = @(sFpsMax);
        event[@"fpsP5"] = @(BPComputeP5());
    }

    float usedMB = BPGetUsedMemoryMB();
    event[@"memoryTotalMB"] = @(roundf(usedMB * 10.0f) / 10.0f);
    event[@"memoryPeakMB"] = @(roundf(sMemPeak * 10.0f) / 10.0f);
    event[@"memoryAvailableMB"] = @(roundf(BPGetAvailableMemoryMB() * 10.0f) / 10.0f);
    event[@"batteryLevel"] = @([UIDevice currentDevice].batteryLevel);
    event[@"thermalState"] = BPThermalState();

    // Tags from custom data
    if (r.customData.count > 0)
        event[@"tags"] = [r.customData copy];

    NSData* data = [NSJSONSerialization dataWithJSONObject:event options:0 error:nil];
    return data ? [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] : @"{}";
}

static void BPUploadEvent(NSString* jsonStr, NSString* screenshotPath) {
    BPRuntime* r = [BPRuntime shared];
    NSString* serverUrl = r.config[@"serverUrl"];
    NSString* apiKey = r.config[@"apiKey"];
    if (!serverUrl || !apiKey) return;

    NSString* url = [NSString stringWithFormat:@"%@/api/v1/perf/events", serverUrl];

    if (screenshotPath) {
        Bugpunch_EnqueueReport(url.UTF8String, apiKey.UTF8String,
            jsonStr.UTF8String, screenshotPath.UTF8String, NULL, NULL);
    } else {
        BugpunchUploader_EnqueueJson(url.UTF8String, apiKey.UTF8String, jsonStr.UTF8String);
    }
}

// ── Screenshot capture callback ──

static void BPPerfScreenshotCallback(const char* reqId, int ok, const char* path) {
    if (!ok || !path) return;
    NSString* json = BPBuildEventJson([NSString stringWithUTF8String:reqId]);
    BPUploadEvent(json, [NSString stringWithUTF8String:path]);
}

static void BPFirePerfEvent(NSString* trigger, BOOL withScreenshot) {
    if (withScreenshot) {
        NSString* dir = NSTemporaryDirectory();
        NSString* filename = [NSString stringWithFormat:@"bp_perf_%f.jpg", CACurrentMediaTime()];
        NSString* outPath = [dir stringByAppendingPathComponent:filename];
        Bugpunch_CaptureScreenshot(trigger.UTF8String, outPath.UTF8String, 80,
            BPPerfScreenshotCallback);
    } else {
        NSString* json = BPBuildEventJson(trigger);
        BPUploadEvent(json, nil);
    }
}

// ── Sampling (fires every 1s on serial queue) ──

static void BPDoSample(void) {
    if (!sPerfStarted) return;
    BPRuntime* r = [BPRuntime shared];

    float fps = (float)r.fps;
    float memMB = BPGetUsedMemoryMB();

    // Update rolling window
    sFpsWindow[sWindowIdx] = fps;
    sMemWindow[sWindowIdx] = memMB;
    sWindowIdx = (sWindowIdx + 1) % kWindowSize;
    if (sWindowFilled < kWindowSize) sWindowFilled++;

    // Aggregation
    if (sSampleCount == 0) { sFpsMin = fps; sFpsMax = fps; }
    else { if (fps < sFpsMin) sFpsMin = fps; if (fps > sFpsMax) sFpsMax = fps; }
    sFpsSum += fps;
    if (memMB > sMemPeak) sMemPeak = memMB;
    sSampleCount++;

    // Save memory watermark for OOM detection
    if (sSampleCount % 10 == 0) {
        NSUserDefaults* ud = [NSUserDefaults standardUserDefaults];
        [ud setFloat:memMB forKey:kLastMemMBKey];
        [ud setObject:sCurrentScene forKey:kLastSceneKey];
    }

    // Check consistently bad FPS
    if (sWindowFilled >= 10) {
        float windowAvg = BPComputeWindowAvg();
        NSTimeInterval now = CACurrentMediaTime();
        if (windowAvg < sPerfFpsThreshold && windowAvg > 0 &&
            now - sLastFpsReportTime > sPerfReportInterval) {
            sLastFpsReportTime = now;
            BPFirePerfEvent(@"fps_low", YES);
        }
    }

    // Periodic summary
    NSTimeInterval now = CACurrentMediaTime();
    if (now - sLastPeriodicTime >= sPerfReportInterval) {
        sLastPeriodicTime = now;
        BPFirePerfEvent(@"periodic", NO);
        BPResetAggregation();
    }
}

// ── OOM detection ──

static void BPCheckPreviousSessionOom(void) {
    NSUserDefaults* ud = [NSUserDefaults standardUserDefaults];
    BOOL cleanExit = [ud boolForKey:kCleanExitKey];
    float lastMemMB = [ud floatForKey:kLastMemMBKey];
    NSString* lastScene = [ud objectForKey:kLastSceneKey] ?: @"unknown";

    if (!cleanExit && lastMemMB > 100) {
        NSLog(@"[BugpunchPerf] Previous session may have OOM'd (memMB=%.0f, scene=%@)", lastMemMB, lastScene);
        NSString* savedScene = sCurrentScene;
        sCurrentScene = lastScene;
        BPFirePerfEvent(@"oom", NO);
        sCurrentScene = savedScene;
    }

    [ud setBool:NO forKey:kCleanExitKey];
}

// ── Memory pressure observer ──

static void BPOnMemoryWarning(void) {
    if (!sPerfStarted) return;
    NSLog(@"[BugpunchPerf] Memory warning received");
    dispatch_async(sPerfQueue, ^{ BPFirePerfEvent(@"memory_pressure", YES); });
}

// ── Scene change ──

void BugpunchPerfMonitor_OnSceneChange(NSString* newScene) {
    if (!sPerfStarted) return;
    dispatch_async(sPerfQueue, ^{
        NSString* oldScene = sCurrentScene;
        sCurrentScene = newScene ?: @"unknown";
        if (sSampleCount > 0) {
            NSString* saved = sCurrentScene;
            sCurrentScene = oldScene;
            BPFirePerfEvent(@"scene_change", NO);
            sCurrentScene = saved;
        }
        sSceneStartTime = CACurrentMediaTime();
        BPResetAggregation();
    });
}

// ── Clean exit marker ──

void BugpunchPerfMonitor_MarkCleanExit(void) {
    [[NSUserDefaults standardUserDefaults] setBool:YES forKey:kCleanExitKey];
}

// ── C entry point (called from BugpunchNative.cs via P/Invoke) ──

extern "C" void Bugpunch_StartPerfMonitor(const char* configJsonC) {
    if (sPerfStarted) return;

    // Parse config
    if (configJsonC) {
        NSString* json = [NSString stringWithUTF8String:configJsonC];
        NSData* data = [json dataUsingEncoding:NSUTF8StringEncoding];
        NSDictionary* cfg = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
        if (cfg) {
            sPerfFpsThreshold = [cfg[@"fpsThreshold"] intValue] ?: 30;
            sPerfReportInterval = [cfg[@"reportInterval"] intValue] ?: 60;
        }
    }

    // Check OOM from previous session
    BPCheckPreviousSessionOom();

    // Create serial queue + timer
    sPerfQueue = dispatch_queue_create("au.com.oddgames.bugpunch.perf", DISPATCH_QUEUE_SERIAL);
    sPerfTimer = dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER, 0, 0, sPerfQueue);
    dispatch_source_set_timer(sPerfTimer, dispatch_time(DISPATCH_TIME_NOW, 1 * NSEC_PER_SEC),
        1 * NSEC_PER_SEC, 100 * NSEC_PER_MSEC);
    dispatch_source_set_event_handler(sPerfTimer, ^{ BPDoSample(); });
    dispatch_resume(sPerfTimer);

    // Memory warning observer
    [[NSNotificationCenter defaultCenter] addObserverForName:UIApplicationDidReceiveMemoryWarningNotification
        object:nil queue:nil usingBlock:^(NSNotification* n) { BPOnMemoryWarning(); }];

    // Clean exit on termination
    [[NSNotificationCenter defaultCenter] addObserverForName:UIApplicationWillTerminateNotification
        object:nil queue:nil usingBlock:^(NSNotification* n) { BugpunchPerfMonitor_MarkCleanExit(); }];

    // Enable battery monitoring
    [UIDevice currentDevice].batteryMonitoringEnabled = YES;

    sSceneStartTime = CACurrentMediaTime();
    sLastPeriodicTime = CACurrentMediaTime();
    BPResetAggregation();
    sPerfStarted = YES;

    NSLog(@"[BugpunchPerf] Started — fpsThreshold=%d reportInterval=%d", sPerfFpsThreshold, sPerfReportInterval);
}
