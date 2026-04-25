// BugpunchShake.h — CoreMotion-based shake detector.
//
// Two-spike debounced shake detection over the accelerometer, mirroring
// the Android SensorManager threshold semantics (threshold expressed in m/s²).

#import <Foundation/Foundation.h>

@interface BPShake : NSObject
+ (void)startWithThreshold:(double)t onShake:(void(^)(void))cb;
+ (void)stop;
@end
