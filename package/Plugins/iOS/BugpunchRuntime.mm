// =============================================================================
// LANE: iOS (Obj-C++)
//
// BugpunchRuntime — see BugpunchRuntime.h for the rationale. Implementation
// holds the singleton + the CADisplayLink frame tick that measures FPS and
// drives the periodic context-screenshot flush.
// =============================================================================

#import "BugpunchRuntime.h"

extern "C" {
    void Bugpunch_TickAnrWatchdog(void);
    bool Bugpunch_WriteBackbufferJPEG(const char* outputPath, float quality);
}

@interface BPRuntime ()
@property (nonatomic, strong, nullable) CADisplayLink* displayLink;
@property (nonatomic, assign) CFTimeInterval fpsWindowStart;
@property (nonatomic, assign) int frameCount;
@property (nonatomic, assign) int ctxShotFlushCounter;
@end

@implementation BPRuntime

+ (instancetype)shared {
    static BPRuntime* i; static dispatch_once_t once;
    dispatch_once(&once, ^{
        i = [BPRuntime new];
        i.metadata = [NSMutableDictionary new];
        i.customData = [NSMutableDictionary new];
    });
    return i;
}

- (void)startFrameTick {
    if (self.displayLink) return;
    self.fpsWindowStart = 0;
    self.frameCount = 0;
    dispatch_async(dispatch_get_main_queue(), ^{
        if (self.displayLink) return;
        self.displayLink = [CADisplayLink displayLinkWithTarget:self
                                                       selector:@selector(onFrame:)];
        [self.displayLink addToRunLoop:[NSRunLoop mainRunLoop]
                               forMode:NSRunLoopCommonModes];
    });
}

- (void)stopFrameTick {
    [self.displayLink invalidate];
    self.displayLink = nil;
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
