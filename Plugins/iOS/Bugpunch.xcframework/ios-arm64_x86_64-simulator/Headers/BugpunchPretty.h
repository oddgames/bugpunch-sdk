// Shared visual helpers for Bugpunch native dialogs (auth screen, etc.).
// Gives every native surface the same gradient cards, glow buttons, and
// accent rings without each file having to redeclare them.

#import <UIKit/UIKit.h>

#ifdef __cplusplus
extern "C" {
#endif

// UIView whose CAGradientLayer auto-resizes on layout.
@interface BPGradientView : UIView
@property (nonatomic, strong) CAGradientLayer* grad;
+ (instancetype)withColors:(NSArray<UIColor*>*)colors
                startPoint:(CGPoint)start
                  endPoint:(CGPoint)end;
@end

// UIButton with an auto-resizing CAGradientLayer behind its title.
@interface BPAccentButton : UIButton
@property (nonatomic, strong) CAGradientLayer* gradLayer;
- (void)applyGradientWithColors:(NSArray<UIColor*>*)colors cornerRadius:(CGFloat)r;
@end

UIColor* BPLighten(UIColor* c, CGFloat amt);
UIColor* BPDarken(UIColor* c, CGFloat amt);
UIColor* BPWithAlpha(UIColor* c, CGFloat a);

/// Brand orange used for the "punch" half of the wordmark + primary CTAs.
/// Hardcoded so a project's customised theme can't mistint brand identity.
UIColor* BPBrandAccent(void);

/// Resolve the currently-key UIWindow via UIScene (iOS 13+). Replaces
/// `[UIApplication sharedApplication].keyWindow` which is deprecated for
/// multi-scene apps. Walks foreground-active window scenes and returns the
/// first window whose `isKeyWindow` is YES. Returns nil if no key window
/// is currently mounted (e.g. backgrounded or pre-scene-attached).
UIWindow* _Nullable BPKeyWindow(void);

#ifdef __cplusplus
}
#endif
