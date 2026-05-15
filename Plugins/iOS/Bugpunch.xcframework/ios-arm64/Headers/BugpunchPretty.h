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

#ifdef __cplusplus
}
#endif
