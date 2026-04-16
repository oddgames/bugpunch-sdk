// BugpunchTouchRecorder.mm
// See BugpunchTouchRecorder.h for overview.
//
// Implementation notes:
// - Swizzle UIApplication.sendEvent: once. Inside the replacement, iterate
//   event.allTouches and push one record per touch on every UIEventTypeTouches
//   event. UITouch.timestamp is the same clock as ReplayKit PTS (host time
//   seconds since boot).
// - Pointer IDs: UITouch has no stable numeric id, but the pointer address is
//   stable for the lifetime of the touch. We map UITouch* -> small int id,
//   freeing the mapping on ended/cancelled.
// - Ring is a C++ std::deque under a spinlock-ish NSLock. Events are tiny
//   (40 bytes) so we can afford ~10k without stressing memory.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <objc/runtime.h>
#import <objc/message.h>

#include "BugpunchTouchRecorder.h"

#include <deque>
#include <unordered_map>

#define BPTLog(fmt, ...)  NSLog(@"[BugpunchTouch] " fmt, ##__VA_ARGS__)

namespace {

struct TouchRecord {
    double  t;         // UITouch.timestamp (seconds since boot)
    int     id_;
    uint8_t phase;     // 0 began, 1 moved, 2 stationary, 3 ended, 4 cancelled
    float   x;         // pixels
    float   y;         // pixels
};

static NSLock* gLock = nil;
static std::deque<TouchRecord>* gRing = nullptr;
static int gMaxEvents = 10000;
static std::atomic<bool> gRunning{false};
static std::atomic<bool> gSwizzled{false};

// Pointer-identity → small int id
static std::unordered_map<uintptr_t, int>* gIdMap = nullptr;
static int gNextId = 0;

// Capture size — populated first time we see a touch.
static std::atomic<int> gCaptureW{0};
static std::atomic<int> gCaptureH{0};

static inline uint8_t PhaseToByte(UITouchPhase p) {
    switch (p) {
        case UITouchPhaseBegan:      return 0;
        case UITouchPhaseMoved:      return 1;
        case UITouchPhaseStationary: return 2;
        case UITouchPhaseEnded:      return 3;
        case UITouchPhaseCancelled:  return 4;
        default:                     return 2;
    }
}

static inline const char* PhaseName(uint8_t p) {
    static const char* names[] = { "began", "moved", "stationary", "ended", "cancelled" };
    return (p < 5) ? names[p] : "stationary";
}

static void RecordTouches(UIEvent* event) {
    if (!gRunning.load()) return;
    if (event.type != UIEventTypeTouches) return;

    NSSet<UITouch*>* touches = event.allTouches;
    if (touches.count == 0) return;

    // Populate capture size once — use key window's screen.
    if (gCaptureW.load() == 0) {
        UIScreen* screen = UIScreen.mainScreen;
        CGSize sz = screen.bounds.size;
        CGFloat scale = screen.scale;
        gCaptureW.store((int)(sz.width * scale));
        gCaptureH.store((int)(sz.height * scale));
    }

    [gLock lock];
    for (UITouch* t in touches) {
        uintptr_t key = (uintptr_t)t;
        int id_;
        auto it = gIdMap->find(key);
        if (it == gIdMap->end()) {
            id_ = gNextId++;
            gIdMap->emplace(key, id_);
        } else {
            id_ = it->second;
        }

        // Convert point to pixel coordinates in the screen's native orientation.
        UIWindow* w = t.window;
        CGPoint pt = [t locationInView:nil]; // window coords
        CGFloat scale = (w ? w.screen.scale : UIScreen.mainScreen.scale);
        TouchRecord r;
        r.t     = t.timestamp;
        r.id_   = id_;
        r.phase = PhaseToByte(t.phase);
        r.x     = (float)(pt.x * scale);
        r.y     = (float)(pt.y * scale);
        gRing->push_back(r);

        if (t.phase == UITouchPhaseEnded || t.phase == UITouchPhaseCancelled) {
            gIdMap->erase(key);
        }
    }
    // Trim front
    while ((int)gRing->size() > gMaxEvents) {
        gRing->pop_front();
    }
    [gLock unlock];
}

// ── Swizzle ─────────────────────────────────────────────────────────────────
//
// Replace -[UIApplication sendEvent:] with a block that records then forwards.

static IMP gOriginalSendEvent = NULL;

static void BPSendEvent(id self, SEL _cmd, UIEvent* event) {
    @try { RecordTouches(event); } @catch (__unused id e) {}
    ((void(*)(id, SEL, UIEvent*))gOriginalSendEvent)(self, _cmd, event);
}

static void InstallSwizzleOnce(void) {
    if (gSwizzled.exchange(true)) return;

    Class cls = [UIApplication class];
    SEL sel = @selector(sendEvent:);
    Method m = class_getInstanceMethod(cls, sel);
    if (!m) {
        BPTLog(@"sendEvent: method not found, cannot swizzle");
        gSwizzled.store(false);
        return;
    }
    gOriginalSendEvent = method_getImplementation(m);
    method_setImplementation(m, (IMP)BPSendEvent);
    BPTLog(@"swizzled UIApplication.sendEvent:");
}

} // namespace

// ── C API ───────────────────────────────────────────────────────────────────

extern "C" {

void BugpunchTouch_Configure(int maxEvents) {
    if (maxEvents < 100) maxEvents = 100;
    if (maxEvents > 100000) maxEvents = 100000;
    gMaxEvents = maxEvents;
}

bool BugpunchTouch_Start(void) {
    if (!gLock)   gLock   = [NSLock new];
    if (!gRing)   gRing   = new std::deque<TouchRecord>();
    if (!gIdMap)  gIdMap  = new std::unordered_map<uintptr_t, int>();

    // Swizzle must run on the main thread to be safe against concurrent
    // method dispatch; dispatch_sync if we're elsewhere.
    if ([NSThread isMainThread]) {
        InstallSwizzleOnce();
    } else {
        dispatch_sync(dispatch_get_main_queue(), ^{ InstallSwizzleOnce(); });
    }
    gRunning.store(true);
    BPTLog(@"started (max %d events)", gMaxEvents);
    return true;
}

void BugpunchTouch_Stop(void) {
    gRunning.store(false);
    BPTLog(@"stopped");
}

bool BugpunchTouch_IsRunning(void) {
    return gRunning.load();
}

void BugpunchTouch_GetCaptureSize(int* outWidth, int* outHeight) {
    if (outWidth)  *outWidth  = gCaptureW.load();
    if (outHeight) *outHeight = gCaptureH.load();
}

const char* BugpunchTouch_SnapshotJson(double startHostTime, double endHostTime) {
    if (!gRing || !gLock) return NULL;
    if (endHostTime <= startHostTime) return NULL;

    // Copy matching records under the lock, then format outside.
    std::deque<TouchRecord> copy;
    [gLock lock];
    for (const auto& r : *gRing) {
        if (r.t >= startHostTime && r.t <= endHostTime) {
            copy.push_back(r);
        }
    }
    [gLock unlock];

    if (copy.empty()) return NULL;

    NSMutableString* s = [NSMutableString stringWithCapacity:copy.size() * 60];
    [s appendString:@"["];
    bool first = true;
    for (const auto& r : copy) {
        int tMs = (int)lround((r.t - startHostTime) * 1000.0);
        if (tMs < 0) tMs = 0;
        if (!first) [s appendString:@","];
        first = false;
        [s appendFormat:@"{\"t\":%d,\"id\":%d,\"phase\":\"%s\",\"x\":%.1f,\"y\":%.1f}",
            tMs, r.id_, PhaseName(r.phase), r.x, r.y];
    }
    [s appendString:@"]"];

    const char* utf8 = [s UTF8String];
    if (!utf8) return NULL;
    size_t len = strlen(utf8) + 1;
    char* out = (char*)malloc(len);
    if (!out) return NULL;
    memcpy(out, utf8, len);
    return out;
}

void BugpunchTouch_FreeJson(const char* json) {
    if (json) free((void*)json);
}

} // extern "C"
