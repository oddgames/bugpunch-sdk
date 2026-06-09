// Shared in-game capture pill — iOS mirror of Java BugpunchCapturePill (#30).
//
// Dismisses the owner view controller to reveal the live game, floats a small
// pill on the key window (📷 capture + ✕ cancel for a screenshot, or a red
// "Stop ⏺" pill for a video), captures via Bugpunch_CaptureScreenshot /
// ODDRecorder, re-presents the SAME owner instance (draft state intact), then
// fires the completion with the captured file path.
//
// Chat, the feedback / feature-request composer, and the issue-detail reply
// composer all call this — one capture flow, three callers. `instruction`,
// when set, shows a dev-request card above the pill (used by the dashboard
// screenshot/video request flow); pass nil for a player-initiated attach.

#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

@interface BugpunchCapturePill : NSObject

/// Capture a screenshot of the game. `onComplete` gets the JPEG path, or nil if
/// the player cancelled. Always invoked on the main thread after the owner is
/// back on screen.
+ (void)captureScreenshotFromOwner:(UIViewController*)owner
                       instruction:(nullable NSString*)instruction
                        onComplete:(void (^)(NSString* _Nullable path))onComplete;

/// Record a video of the game (ReplayKit via ODDRecorder; the OS shows its own
/// consent prompt the first time). `onComplete` gets the MP4 path + a validity
/// flag (NO when cancelled, declined, or the clip was empty). Main thread.
+ (void)captureVideoFromOwner:(UIViewController*)owner
                  instruction:(nullable NSString*)instruction
                   onComplete:(void (^)(NSString* _Nullable path, BOOL valid))onComplete;

@end

NS_ASSUME_NONNULL_END
