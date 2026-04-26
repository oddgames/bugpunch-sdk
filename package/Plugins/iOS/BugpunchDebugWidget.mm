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
// Unread-chat badge (issue #32) — accent circle at top-right of the widget.
// Shown when the dev team has chat messages the player hasn't seen.
@property (nonatomic, strong) UILabel* unreadBadge;
@property (nonatomic, assign) CGPoint dragStart;
@property (nonatomic, assign) CGPoint originStart;
- (void)applyUnreadCount:(NSInteger)count;
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

    // Unread-chat badge (issue #32) — pinned to the widget's top-right.
    // Hidden until the poller sees a non-zero count; shows N or "99+".
    UIColor* colAccent = [BPTheme color:@"accentChat"
        fallback:[UIColor colorWithRed:0.20 green:0.38 blue:0.60 alpha:1]];
    _unreadBadge = [[UILabel alloc] initWithFrame:CGRectMake(self.bounds.size.width - 18, -8, 22, 22)];
    _unreadBadge.backgroundColor = colAccent;
    _unreadBadge.textColor = [UIColor whiteColor];
    _unreadBadge.textAlignment = NSTextAlignmentCenter;
    _unreadBadge.font = [UIFont boldSystemFontOfSize:11];
    _unreadBadge.layer.cornerRadius = 11;
    _unreadBadge.layer.masksToBounds = YES;
    _unreadBadge.layer.borderColor = [UIColor whiteColor].CGColor;
    _unreadBadge.layer.borderWidth = 1.5;
    _unreadBadge.hidden = YES;
    _unreadBadge.userInteractionEnabled = NO;
    [self addSubview:_unreadBadge];

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

- (void)applyUnreadCount:(NSInteger)count {
    if (count <= 0) {
        if (!self.unreadBadge.hidden) self.unreadBadge.hidden = YES;
        return;
    }
    BOOL wasHidden = self.unreadBadge.hidden;
    NSString* text = count >= 100 ? @"99+" : [NSString stringWithFormat:@"%ld", (long)count];
    self.unreadBadge.text = text;
    // Auto-widen for two/three-digit counts so "99+" doesn't clip.
    CGFloat w = count >= 100 ? 30 : (count >= 10 ? 26 : 22);
    self.unreadBadge.frame = CGRectMake(self.bounds.size.width - w + 4, -8, w, 22);
    self.unreadBadge.hidden = NO;
    if (wasHidden) {
        NSLog(@"[Bugpunch.DebugWidget] unread badge shown: %ld", (long)count);
    }
}

- (void)removeFromSuperview {
    [_recDot.layer removeAllAnimations];
    [super removeFromSuperview];
}

@end

// ── Singleton access ──

static BPDebugWidget* sWidget = nil;
// Last unread count from the poller. Cached so a count that lands before
// the widget is shown is re-applied at attach time. -1 = never set.
static NSInteger sLastUnreadCount = -1;

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
        // Re-apply any unread count that arrived before the widget existed
        // (the poller may have ticked already on a slow startup).
        if (sLastUnreadCount > 0) [sWidget applyUnreadCount:sLastUnreadCount];
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

// Set the unread-chat badge count on the floating debug widget (issue #32).
// Called from the iOS poller every other tick, and from the chat VC's
// markRead path with 0 to clear the badge immediately.
void Bugpunch_SetUnreadCount(int count) {
    NSInteger safe = count < 0 ? 0 : (NSInteger)count;
    sLastUnreadCount = safe;
    dispatch_async(dispatch_get_main_queue(), ^{
        if (sWidget) [sWidget applyUnreadCount:safe];
    });
}

// Read the last unread count (or 0 if the poller has not run). Used by the
// chat VC to log whether it was opened with the badge visible.
int Bugpunch_LastUnreadCount(void) {
    return sLastUnreadCount > 0 ? (int)sLastUnreadCount : 0;
}

}
