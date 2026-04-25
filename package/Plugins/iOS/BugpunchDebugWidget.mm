// BugpunchDebugWidget.mm — Draggable floating debug widget for iOS.
//
// Shows a blinking recording indicator plus Report and Tools buttons. Can be
// dragged anywhere on screen. Added to the key UIWindow.

#import <UIKit/UIKit.h>

#import "BugpunchTheme.h"
#import "BugpunchStrings.h"

extern void BugpunchUnity_SendMessage(const char*, const char*, const char*);

@interface BPDebugWidget : UIView
@property (nonatomic, strong) UIView* recDot;
@property (nonatomic, assign) CGPoint dragStart;
@property (nonatomic, assign) CGPoint originStart;
@end

@implementation BPDebugWidget

- (instancetype)init {
    self = [super initWithFrame:CGRectMake(16, 80, 142, 40)];
    if (!self) return nil;

    UIColor* colCardBg  = [BPTheme color:@"cardBackground"
        fallback:[UIColor colorWithRed:0.08 green:0.09 blue:0.11 alpha:0.88]];
    UIColor* colBorder  = [BPTheme color:@"cardBorder"
        fallback:[UIColor colorWithRed:0.16 green:0.19 blue:0.25 alpha:1]];
    UIColor* colRec     = [BPTheme color:@"accentRecord"
        fallback:[UIColor colorWithRed:0.88 green:0.19 blue:0.19 alpha:1]];
    UIColor* colBug     = [BPTheme color:@"accentBug"
        fallback:[UIColor colorWithRed:0.85 green:0.22 blue:0.22 alpha:1]];
    UIColor* colTools   = [BPTheme color:@"cardBorder"
        fallback:[UIColor colorWithRed:0.20 green:0.22 blue:0.28 alpha:1]];
    UIColor* colMuted   = [BPTheme color:@"textMuted"
        fallback:[UIColor colorWithRed:0.55 green:0.57 blue:0.62 alpha:1]];
    UIColor* colText    = [BPTheme color:@"textPrimary" fallback:UIColor.whiteColor];

    self.backgroundColor = colCardBg;
    self.layer.cornerRadius = 20;
    self.layer.borderColor = colBorder.CGColor;
    self.layer.borderWidth = 1;
    self.layer.shadowColor = UIColor.blackColor.CGColor;
    self.layer.shadowOffset = CGSizeMake(0, 4);
    self.layer.shadowRadius = 8;
    self.layer.shadowOpacity = 0.4;
    self.clipsToBounds = NO;

    // Recording dot
    _recDot = [[UIView alloc] initWithFrame:CGRectMake(12, 15, 10, 10)];
    _recDot.backgroundColor = colRec;
    _recDot.layer.cornerRadius = 5;
    [self addSubview:_recDot];

    CGFloat radius = [BPTheme radius:@"cardRadius" fallback:12];

    // Report button
    UIButton* report = [UIButton new];
    report.frame = CGRectMake(32, 5, 60, 30);
    [report setTitle:[BPStrings text:@"widgetReport" fallback:@"Report"]
            forState:UIControlStateNormal];
    [report setTitleColor:colText forState:UIControlStateNormal];
    report.titleLabel.font = [UIFont boldSystemFontOfSize:12];
    report.backgroundColor = colBug;
    report.layer.cornerRadius = radius;
    [report addTarget:self action:@selector(reportTapped) forControlEvents:UIControlEventTouchUpInside];
    [self addSubview:report];

    // Tools button — toolbox icon
    UIButton* tools = [UIButton new];
    tools.frame = CGRectMake(98, 5, 32, 30);
    if (@available(iOS 14.0, *)) {
        [tools setImage:[UIImage systemImageNamed:@"wrench.and.screwdriver" withConfiguration:
            [UIImageSymbolConfiguration configurationWithPointSize:14]] forState:UIControlStateNormal];
        tools.tintColor = colMuted;
    } else {
        [tools setTitle:[BPStrings text:@"widgetTools" fallback:@"Tools"] forState:UIControlStateNormal];
        [tools setTitleColor:colMuted forState:UIControlStateNormal];
        tools.titleLabel.font = [UIFont systemFontOfSize:13];
    }
    tools.backgroundColor = colTools;
    tools.layer.cornerRadius = radius;
    [tools addTarget:self action:@selector(toolsTapped) forControlEvents:UIControlEventTouchUpInside];
    [self addSubview:tools];

    // Pan gesture for dragging
    UIPanGestureRecognizer* pan = [[UIPanGestureRecognizer alloc]
        initWithTarget:self action:@selector(handlePan:)];
    [self addGestureRecognizer:pan];

    // Blink the recording dot
    [UIView animateWithDuration:0.5
                          delay:0
                        options:(UIViewAnimationOptionRepeat |
                                 UIViewAnimationOptionAutoreverse |
                                 UIViewAnimationOptionAllowUserInteraction)
                     animations:^{ self.recDot.alpha = 0.2; }
                     completion:nil];

    return self;
}

- (void)handlePan:(UIPanGestureRecognizer*)g {
    if (g.state == UIGestureRecognizerStateBegan) {
        _dragStart = [g locationInView:self.superview];
        _originStart = self.center;
    } else if (g.state == UIGestureRecognizerStateChanged) {
        CGPoint now = [g locationInView:self.superview];
        CGFloat dx = now.x - _dragStart.x;
        CGFloat dy = now.y - _dragStart.y;
        CGPoint newCenter = CGPointMake(_originStart.x + dx, _originStart.y + dy);
        // Clamp to screen
        CGRect bounds = self.superview.bounds;
        CGFloat hw = self.bounds.size.width / 2;
        CGFloat hh = self.bounds.size.height / 2;
        newCenter.x = MAX(hw, MIN(bounds.size.width - hw, newCenter.x));
        newCenter.y = MAX(hh, MIN(bounds.size.height - hh, newCenter.y));
        self.center = newCenter;
    }
}

- (void)reportTapped {
    extern void Bugpunch_ReportBug(const char*, const char*, const char*, const char*);
    Bugpunch_ReportBug("bug", "Bug report", "Triggered from debug widget", "");
}

- (void)toolsTapped {
    BugpunchUnity_SendMessage("BugpunchToolsBridge", "OnShowTools", "");
}

- (void)removeFromSuperview {
    [_recDot.layer removeAllAnimations];
    [super removeFromSuperview];
}

@end

// ── Singleton access ──

static BPDebugWidget* sWidget = nil;

extern "C" {

void Bugpunch_ShowDebugWidget(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (sWidget) return;
        UIWindow* window = nil;
        if (@available(iOS 13.0, *)) {
            for (UIScene* scene in UIApplication.sharedApplication.connectedScenes) {
                if (![scene isKindOfClass:[UIWindowScene class]]) continue;
                if (scene.activationState != UISceneActivationStateForegroundActive) continue;
                UIWindowScene* ws = (UIWindowScene*)scene;
                for (UIWindow* w in ws.windows) {
                    if (w.isKeyWindow) { window = w; break; }
                }
                if (window) break;
            }
        }
        if (!window) {
            #pragma clang diagnostic push
            #pragma clang diagnostic ignored "-Wdeprecated-declarations"
            window = UIApplication.sharedApplication.keyWindow;
            #pragma clang diagnostic pop
        }
        if (!window) return;
        sWidget = [[BPDebugWidget alloc] init];
        [window addSubview:sWidget];
    });
}

void Bugpunch_HideDebugWidget(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        [sWidget removeFromSuperview];
        sWidget = nil;
    });
}

bool Bugpunch_IsDebugWidgetShowing(void) {
    return sWidget != nil;
}

}
