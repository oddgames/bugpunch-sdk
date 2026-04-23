// BugpunchDebugWidget.mm — Draggable floating debug widget for iOS.
//
// Shows a blinking recording indicator plus Report / Screenshot / Tools
// buttons. Can be dragged anywhere on screen. Added to the key UIWindow.

#import <UIKit/UIKit.h>

extern void BugpunchUnity_SendMessage(const char*, const char*, const char*);

@interface BPDebugWidget : UIView
@property (nonatomic, strong) UIView* recDot;
@property (nonatomic, assign) CGPoint dragStart;
@property (nonatomic, assign) CGPoint originStart;
@end

@implementation BPDebugWidget

- (instancetype)init {
    self = [super initWithFrame:CGRectMake(16, 80, 210, 40)];
    if (!self) return nil;

    self.backgroundColor = [UIColor colorWithRed:0.08 green:0.09 blue:0.11 alpha:0.88];
    self.layer.cornerRadius = 20;
    self.layer.borderColor = [UIColor colorWithRed:0.16 green:0.19 blue:0.25 alpha:1].CGColor;
    self.layer.borderWidth = 1;
    self.layer.shadowColor = UIColor.blackColor.CGColor;
    self.layer.shadowOffset = CGSizeMake(0, 4);
    self.layer.shadowRadius = 8;
    self.layer.shadowOpacity = 0.4;
    self.clipsToBounds = NO;

    // Recording dot
    _recDot = [[UIView alloc] initWithFrame:CGRectMake(12, 15, 10, 10)];
    _recDot.backgroundColor = [UIColor colorWithRed:0.88 green:0.19 blue:0.19 alpha:1];
    _recDot.layer.cornerRadius = 5;
    [self addSubview:_recDot];

    // Report button
    UIButton* report = [UIButton new];
    report.frame = CGRectMake(32, 5, 60, 30);
    [report setTitle:@"Report" forState:UIControlStateNormal];
    [report setTitleColor:UIColor.whiteColor forState:UIControlStateNormal];
    report.titleLabel.font = [UIFont boldSystemFontOfSize:12];
    report.backgroundColor = [UIColor colorWithRed:0.85 green:0.22 blue:0.22 alpha:1];
    report.layer.cornerRadius = 12;
    [report addTarget:self action:@selector(reportTapped) forControlEvents:UIControlEventTouchUpInside];
    [self addSubview:report];

    // Screenshot button
    UIButton* shot = [UIButton new];
    shot.frame = CGRectMake(98, 5, 28, 30);
    if (@available(iOS 13.0, *)) {
        [shot setImage:[UIImage systemImageNamed:@"camera" withConfiguration:
            [UIImageSymbolConfiguration configurationWithPointSize:14]] forState:UIControlStateNormal];
        shot.tintColor = [UIColor colorWithRed:0.55 green:0.57 blue:0.62 alpha:1];
    } else {
        [shot setTitle:@"SS" forState:UIControlStateNormal];
        [shot setTitleColor:[UIColor colorWithRed:0.55 green:0.57 blue:0.62 alpha:1] forState:UIControlStateNormal];
        shot.titleLabel.font = [UIFont systemFontOfSize:11];
    }
    shot.backgroundColor = [UIColor colorWithRed:0.20 green:0.22 blue:0.28 alpha:1];
    shot.layer.cornerRadius = 12;
    [shot addTarget:self action:@selector(screenshotTapped) forControlEvents:UIControlEventTouchUpInside];
    [self addSubview:shot];

    // Tools button
    UIButton* tools = [UIButton new];
    tools.frame = CGRectMake(132, 5, 60, 30);
    [tools setTitle:@"Tools" forState:UIControlStateNormal];
    [tools setTitleColor:[UIColor colorWithRed:0.55 green:0.57 blue:0.62 alpha:1]
               forState:UIControlStateNormal];
    tools.titleLabel.font = [UIFont systemFontOfSize:13];
    tools.backgroundColor = [UIColor colorWithRed:0.20 green:0.22 blue:0.28 alpha:1];
    tools.layer.cornerRadius = 12;
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

- (void)screenshotTapped {
    // Capture a screenshot and notify Unity so it can be added to the report gallery.
    extern bool Bugpunch_BackbufferReady(void);
    extern bool Bugpunch_WriteBackbufferJPEG(const char*, float);
    NSString* caches = NSSearchPathForDirectoriesInDomains(
        NSCachesDirectory, NSUserDomainMask, YES).firstObject;
    NSString* path = [caches stringByAppendingPathComponent:
        [NSString stringWithFormat:@"bp_manual_%f.jpg", [NSDate timeIntervalSinceReferenceDate]]];
    if (Bugpunch_BackbufferReady()) {
        Bugpunch_WriteBackbufferJPEG([path UTF8String], 0.85f);
    }
    long long ts = (long long)([[NSDate date] timeIntervalSince1970] * 1000);
    NSString* msg = [NSString stringWithFormat:@"%@|%lld", path, ts];
    BugpunchUnity_SendMessage("BugpunchToolsBridge", "OnManualScreenshot", [msg UTF8String]);

    // Visual feedback — flash the button
    UIView* flash = [[UIView alloc] initWithFrame:self.bounds];
    flash.backgroundColor = [UIColor colorWithWhite:1 alpha:0.3];
    flash.layer.cornerRadius = self.layer.cornerRadius;
    flash.userInteractionEnabled = NO;
    [self addSubview:flash];
    [UIView animateWithDuration:0.3 animations:^{ flash.alpha = 0; }
        completion:^(BOOL _) { [flash removeFromSuperview]; }];
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
