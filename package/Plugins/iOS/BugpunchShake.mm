// BugpunchShake.mm — CoreMotion shake detector.
//
// Watches accelerometer magnitude (gravity-removed, converted Gs→m/s²) for
// two spikes within 0.5 s, then fires the callback. 2 s lockout between
// fires prevents flurries from one shake.

#import "BugpunchShake.h"
#import <CoreMotion/CoreMotion.h>

// Defined in BugpunchDebugMode.mm. Forward-declared here so we can emit a
// `shake_fired` analytics event without pulling in the whole debug-mode
// header. Mirrors the Android side which calls BugpunchRuntime.trackEvent.
#ifdef __cplusplus
extern "C" {
#endif
void Bugpunch_TrackEvent(const char* name, const char* propertiesJson);
#ifdef __cplusplus
}
#endif

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
            Bugpunch_TrackEvent("shake_fired", "{\"platform\":\"ios\"}");
            if (gShakeCb) gShakeCb();
        }
    }];
}

+ (void)stop {
    if (gMotion) [gMotion stopAccelerometerUpdates];
    gMotion = nil; gShakeCb = nil;
}
@end
