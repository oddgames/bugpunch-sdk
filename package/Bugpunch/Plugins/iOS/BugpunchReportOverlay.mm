// BugpunchReportOverlay.mm
// Native overlay views for the Bugpunch report flow (iOS).
//
// Two overlays:
//   1. Welcome card — explains debug/recording mode, user taps "Got it"
//   2. Recording button — floating draggable red button with elapsed timer,
//      stays visible until app restart.
//
// Copyright (c) ODDGames. All rights reserved.

#import <UIKit/UIKit.h>

// Interface must precede the C-linkage setup code below that addresses the
// class via [BPOverlayActions class]. Implementation stays near the bottom
// next to the helpers it uses.
@interface BPOverlayActions : NSObject
+ (void)onWelcomeConfirm;
+ (void)onWelcomeCancel;
+ (void)onRecordingTapped;
+ (void)onRecordingDragged:(UIPanGestureRecognizer *)pan;
@end

typedef void (*ReportOverlayCallback)(void);

// ─── Icon Drawing Helpers ───────────────────────────────────────

static void DrawCameraIcon(CGContext *ctx, CGRect rect) {
    CGFloat cx = CGRectGetMidX(rect), cy = CGRectGetMidY(rect);
    CGFloat size = MIN(rect.size.width, rect.size.height) * 0.4f;

    CGContextSetStrokeColorWithColor(ctx, [UIColor colorWithRed:0.39 green:0.71 blue:0.96 alpha:1].CGColor);
    CGContextSetLineWidth(ctx, size * 0.12f);

    // Body rectangle
    CGFloat bw = size * 1.4f, bh = size;
    CGRect body = CGRectMake(cx - bw/2, cy - bh/2 + size*0.15f, bw, bh);
    UIBezierPath *bodyPath = [UIBezierPath bezierPathWithRoundedRect:body cornerRadius:size*0.15f];
    CGContextAddPath(ctx, bodyPath.CGPath);
    CGContextStrokePath(ctx);

    // Lens circle
    CGContextStrokeEllipseInRect(ctx, CGRectMake(cx - size*0.3f, cy - size*0.15f, size*0.6f, size*0.6f));

    // Viewfinder bump
    CGFloat bumpW = size * 0.5f;
    CGContextMoveToPoint(ctx, cx - bumpW/2, body.origin.y);
    CGContextAddLineToPoint(ctx, cx - bumpW*0.35f, body.origin.y - size*0.25f);
    CGContextAddLineToPoint(ctx, cx + bumpW*0.35f, body.origin.y - size*0.25f);
    CGContextAddLineToPoint(ctx, cx + bumpW/2, body.origin.y);
    CGContextStrokePath(ctx);
}

static void DrawVideoCameraIcon(CGContext *ctx, CGRect rect) {
    CGFloat cx = CGRectGetMidX(rect), cy = CGRectGetMidY(rect);
    CGFloat size = MIN(rect.size.width, rect.size.height) * 0.4f;

    CGContextSetStrokeColorWithColor(ctx, [UIColor colorWithRed:0.94 green:0.33 blue:0.31 alpha:1].CGColor);
    CGContextSetLineWidth(ctx, size * 0.12f);

    // Body rectangle (shifted left)
    CGFloat bw = size * 1.2f, bh = size * 0.9f;
    CGFloat bodyLeft = cx - size * 0.5f;
    CGRect body = CGRectMake(bodyLeft - bw/2, cy - bh/2, bw, bh);
    UIBezierPath *bodyPath = [UIBezierPath bezierPathWithRoundedRect:body cornerRadius:size*0.12f];
    CGContextAddPath(ctx, bodyPath.CGPath);
    CGContextStrokePath(ctx);

    // Viewfinder triangle
    CGFloat triLeft = CGRectGetMaxX(body) + size * 0.08f;
    CGContextMoveToPoint(ctx, triLeft, cy - bh*0.3f);
    CGContextAddLineToPoint(ctx, triLeft + size*0.55f, cy);
    CGContextAddLineToPoint(ctx, triLeft, cy + bh*0.3f);
    CGContextClosePath(ctx);
    CGContextStrokePath(ctx);

    // Record dot
    CGContextSetFillColorWithColor(ctx, [UIColor colorWithRed:0.94 green:0.33 blue:0.31 alpha:1].CGColor);
    CGFloat dotR = size * 0.1f;
    CGContextFillEllipseInRect(ctx, CGRectMake(body.origin.x + size*0.25f - dotR,
                                                body.origin.y + size*0.2f - dotR,
                                                dotR*2, dotR*2));
}

// ─── Icon Views ─────────────────────────────────────────────────

@interface BPCameraIconView : UIView
@end
@implementation BPCameraIconView
- (void)drawRect:(CGRect)rect {
    CGContextRef ctx = UIGraphicsGetCurrentContext();
    if (ctx) DrawCameraIcon(ctx, rect);
}
@end

@interface BPVideoIconView : UIView
@end
@implementation BPVideoIconView
- (void)drawRect:(CGRect)rect {
    CGContextRef ctx = UIGraphicsGetCurrentContext();
    if (ctx) DrawVideoCameraIcon(ctx, rect);
}
@end

// ─── Record Button View ─────────────────────────────────────────

@interface BPRecordButton : UIView
@end
@implementation BPRecordButton
- (void)drawRect:(CGRect)rect {
    CGContextRef ctx = UIGraphicsGetCurrentContext();
    if (!ctx) return;

    CGFloat r = MIN(rect.size.width, rect.size.height) / 2.0f;
    CGFloat cx = CGRectGetMidX(rect), cy = CGRectGetMidY(rect);

    // Red circle
    CGContextSetFillColorWithColor(ctx, [UIColor colorWithRed:0.83 green:0.18 blue:0.18 alpha:1].CGColor);
    CGContextFillEllipseInRect(ctx, CGRectMake(cx - r*0.9f, cy - r*0.9f, r*1.8f, r*1.8f));

    // White rounded square (stop icon)
    CGFloat sq = r * 0.45f;
    CGContextSetFillColorWithColor(ctx, [UIColor whiteColor].CGColor);
    UIBezierPath *stop = [UIBezierPath bezierPathWithRoundedRect:CGRectMake(cx-sq, cy-sq, sq*2, sq*2)
                                                    cornerRadius:sq*0.2f];
    CGContextAddPath(ctx, stop.CGPath);
    CGContextFillPath(ctx);
}
@end

// ─── Static State ───────────────────────────────────────────────

static UIView *sWelcomeBackdrop = nil;
static UIView *sRecordingContainer = nil;
static UILabel *sTimerLabel = nil;
static NSTimer *sTimer = nil;
static NSDate *sRecordStartTime = nil;
static ReportOverlayCallback sWelcomeConfirmCb = NULL;
static ReportOverlayCallback sWelcomeCancelCb = NULL;
static ReportOverlayCallback sReportTappedCb = NULL;

// ─── Drag State ─────────────────────────────────────────────────

static CGPoint sDragStart;
static CGPoint sViewStartCenter;
static BOOL sIsDragging;

// ─── Helpers ────────────────────────────────────────────────────

static UIView* GetRootView(void) {
    UIWindow *window = nil;
    if (@available(iOS 13.0, *)) {
        for (UIWindowScene *scene in [UIApplication sharedApplication].connectedScenes) {
            if (scene.activationState == UISceneActivationStateForegroundActive) {
                for (UIWindow *w in scene.windows) {
                    if (w.isKeyWindow) { window = w; break; }
                }
                if (window) break;
            }
        }
    }
    if (!window) {
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
        window = [UIApplication sharedApplication].keyWindow;
#pragma clang diagnostic pop
    }
    return window;
}

static void UpdateTimer(void) {
    if (!sTimerLabel || !sRecordStartTime) return;
    NSTimeInterval elapsed = [[NSDate date] timeIntervalSinceDate:sRecordStartTime];
    int m = (int)elapsed / 60;
    int s = (int)elapsed % 60;
    sTimerLabel.text = [NSString stringWithFormat:@"%d:%02d", m, s];
}

// ─── C Bridge Functions ─────────────────────────────────────────

extern "C" {

void Bugpunch_ShowReportWelcome(ReportOverlayCallback onConfirm, ReportOverlayCallback onCancel) {
    sWelcomeConfirmCb = onConfirm;
    sWelcomeCancelCb = onCancel;

    dispatch_async(dispatch_get_main_queue(), ^{
        if (sWelcomeBackdrop) return;

        UIView *root = GetRootView();
        if (!root) return;

        CGFloat pad = 24, padSmall = 12;
        CGFloat cardW = 300;

        // Backdrop
        sWelcomeBackdrop = [[UIView alloc] initWithFrame:root.bounds];
        sWelcomeBackdrop.backgroundColor = [[UIColor blackColor] colorWithAlphaComponent:0.6];
        sWelcomeBackdrop.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
        [root addSubview:sWelcomeBackdrop];

        // Card
        UIView *card = [[UIView alloc] init];
        card.backgroundColor = [UIColor colorWithWhite:0.13 alpha:0.94];
        card.layer.cornerRadius = 16;
        card.translatesAutoresizingMaskIntoConstraints = NO;
        [sWelcomeBackdrop addSubview:card];

        // Bugpunch logo
        UIImageView *logoView = [[UIImageView alloc] init];
        NSString *logoPath = [[NSBundle mainBundle] pathForResource:@"bugpunch-logo" ofType:@"png"];
        if (!logoPath) logoPath = [[NSBundle mainBundle] pathForResource:@"bugpunch-logo@2x" ofType:@"png"];
        if (logoPath) logoView.image = [UIImage imageWithContentsOfFile:logoPath];
        logoView.contentMode = UIViewContentModeScaleAspectFit;
        logoView.translatesAutoresizingMaskIntoConstraints = NO;
        [card addSubview:logoView];

        // Icons row
        UIView *iconsRow = [[UIView alloc] init];
        iconsRow.translatesAutoresizingMaskIntoConstraints = NO;
        [card addSubview:iconsRow];

        BPCameraIconView *camIcon = [[BPCameraIconView alloc] init];
        camIcon.backgroundColor = [UIColor clearColor];
        camIcon.translatesAutoresizingMaskIntoConstraints = NO;
        [iconsRow addSubview:camIcon];

        BPVideoIconView *vidIcon = [[BPVideoIconView alloc] init];
        vidIcon.backgroundColor = [UIColor clearColor];
        vidIcon.translatesAutoresizingMaskIntoConstraints = NO;
        [iconsRow addSubview:vidIcon];

        // Title
        UILabel *title = [[UILabel alloc] init];
        title.text = @"Report a Bug";
        title.font = [UIFont boldSystemFontOfSize:20];
        title.textColor = [UIColor whiteColor];
        title.textAlignment = NSTextAlignmentCenter;
        title.translatesAutoresizingMaskIntoConstraints = NO;
        [card addSubview:title];

        // Body
        UILabel *body = [[UILabel alloc] init];
        body.text = @"We'll record your screen while you reproduce the issue.\n\nWhen you're ready, tap the report button to send us the details.";
        body.font = [UIFont systemFontOfSize:14];
        body.textColor = [UIColor colorWithWhite:0.73 alpha:1];
        body.textAlignment = NSTextAlignmentCenter;
        body.numberOfLines = 0;
        body.translatesAutoresizingMaskIntoConstraints = NO;
        [card addSubview:body];

        // "Got it" button
        UIButton *gotItBtn = [UIButton buttonWithType:UIButtonTypeSystem];
        [gotItBtn setTitle:@"Got it" forState:UIControlStateNormal];
        [gotItBtn setTitleColor:[UIColor whiteColor] forState:UIControlStateNormal];
        gotItBtn.titleLabel.font = [UIFont boldSystemFontOfSize:16];
        gotItBtn.backgroundColor = [UIColor colorWithRed:0.18 green:0.49 blue:0.20 alpha:1];
        gotItBtn.layer.cornerRadius = 8;
        gotItBtn.translatesAutoresizingMaskIntoConstraints = NO;
        [gotItBtn addTarget:[BPOverlayActions class] action:@selector(onWelcomeConfirm) forControlEvents:UIControlEventTouchUpInside];
        [card addSubview:gotItBtn];

        // Cancel link
        UIButton *cancelBtn = [UIButton buttonWithType:UIButtonTypeSystem];
        [cancelBtn setTitle:@"Cancel" forState:UIControlStateNormal];
        [cancelBtn setTitleColor:[UIColor colorWithWhite:0.53 alpha:1] forState:UIControlStateNormal];
        cancelBtn.titleLabel.font = [UIFont systemFontOfSize:14];
        cancelBtn.translatesAutoresizingMaskIntoConstraints = NO;
        [cancelBtn addTarget:[BPOverlayActions class] action:@selector(onWelcomeCancel) forControlEvents:UIControlEventTouchUpInside];
        [card addSubview:cancelBtn];

        // Layout
        [NSLayoutConstraint activateConstraints:@[
            [card.centerXAnchor constraintEqualToAnchor:sWelcomeBackdrop.centerXAnchor],
            [card.centerYAnchor constraintEqualToAnchor:sWelcomeBackdrop.centerYAnchor],
            [card.widthAnchor constraintEqualToConstant:cardW],

            [logoView.topAnchor constraintEqualToAnchor:card.topAnchor constant:pad],
            [logoView.centerXAnchor constraintEqualToAnchor:card.centerXAnchor],
            [logoView.widthAnchor constraintEqualToConstant:44],
            [logoView.heightAnchor constraintEqualToConstant:44],

            [iconsRow.topAnchor constraintEqualToAnchor:logoView.bottomAnchor constant:padSmall],
            [iconsRow.centerXAnchor constraintEqualToAnchor:card.centerXAnchor],
            [iconsRow.heightAnchor constraintEqualToConstant:56],
            [iconsRow.widthAnchor constraintEqualToConstant:128],

            [camIcon.leadingAnchor constraintEqualToAnchor:iconsRow.leadingAnchor],
            [camIcon.topAnchor constraintEqualToAnchor:iconsRow.topAnchor],
            [camIcon.widthAnchor constraintEqualToConstant:56],
            [camIcon.heightAnchor constraintEqualToConstant:56],

            [vidIcon.trailingAnchor constraintEqualToAnchor:iconsRow.trailingAnchor],
            [vidIcon.topAnchor constraintEqualToAnchor:iconsRow.topAnchor],
            [vidIcon.widthAnchor constraintEqualToConstant:56],
            [vidIcon.heightAnchor constraintEqualToConstant:56],

            [title.topAnchor constraintEqualToAnchor:iconsRow.bottomAnchor constant:padSmall],
            [title.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:pad],
            [title.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-pad],

            [body.topAnchor constraintEqualToAnchor:title.bottomAnchor constant:8],
            [body.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:pad],
            [body.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-pad],

            [gotItBtn.topAnchor constraintEqualToAnchor:body.bottomAnchor constant:pad],
            [gotItBtn.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:pad],
            [gotItBtn.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-pad],
            [gotItBtn.heightAnchor constraintEqualToConstant:44],

            [cancelBtn.topAnchor constraintEqualToAnchor:gotItBtn.bottomAnchor constant:padSmall],
            [cancelBtn.centerXAnchor constraintEqualToAnchor:card.centerXAnchor],
            [cancelBtn.bottomAnchor constraintEqualToAnchor:card.bottomAnchor constant:-pad],
        ]];
    });
}

void Bugpunch_HideReportWelcome(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        [sWelcomeBackdrop removeFromSuperview];
        sWelcomeBackdrop = nil;
    });
}

void Bugpunch_ShowRecordingOverlay(ReportOverlayCallback onReportTapped) {
    sReportTappedCb = onReportTapped;

    dispatch_async(dispatch_get_main_queue(), ^{
        if (sRecordingContainer) return;

        UIView *root = GetRootView();
        if (!root) return;

        CGFloat btnSize = 56;
        CGFloat margin = 16;

        // Container
        sRecordingContainer = [[UIView alloc] initWithFrame:CGRectMake(0, 0, btnSize + 20, btnSize + 24)];
        sRecordingContainer.center = CGPointMake(
            root.bounds.size.width - btnSize/2 - margin,
            root.bounds.size.height - btnSize/2 - margin - 40);
        [root addSubview:sRecordingContainer];

        // Record button
        BPRecordButton *btn = [[BPRecordButton alloc] initWithFrame:CGRectMake(10, 0, btnSize, btnSize)];
        btn.backgroundColor = [UIColor clearColor];
        btn.userInteractionEnabled = NO;
        [sRecordingContainer addSubview:btn];

        // Timer label
        sTimerLabel = [[UILabel alloc] initWithFrame:CGRectMake(0, btnSize + 4, btnSize + 20, 16)];
        sTimerLabel.text = @"0:00";
        sTimerLabel.font = [UIFont boldSystemFontOfSize:11];
        sTimerLabel.textColor = [UIColor whiteColor];
        sTimerLabel.textAlignment = NSTextAlignmentCenter;
        sTimerLabel.layer.shadowColor = [UIColor blackColor].CGColor;
        sTimerLabel.layer.shadowOffset = CGSizeMake(1, 1);
        sTimerLabel.layer.shadowOpacity = 0.8;
        sTimerLabel.layer.shadowRadius = 1;
        [sRecordingContainer addSubview:sTimerLabel];

        // Drop shadow on container
        sRecordingContainer.layer.shadowColor = [UIColor blackColor].CGColor;
        sRecordingContainer.layer.shadowOffset = CGSizeMake(0, 3);
        sRecordingContainer.layer.shadowOpacity = 0.4;
        sRecordingContainer.layer.shadowRadius = 4;

        // Tap gesture
        UITapGestureRecognizer *tap = [[UITapGestureRecognizer alloc]
            initWithTarget:[BPOverlayActions class] action:@selector(onRecordingTapped)];
        [sRecordingContainer addGestureRecognizer:tap];

        // Pan (drag) gesture
        UIPanGestureRecognizer *pan = [[UIPanGestureRecognizer alloc]
            initWithTarget:[BPOverlayActions class] action:@selector(onRecordingDragged:)];
        [sRecordingContainer addGestureRecognizer:pan];

        // Start timer
        sRecordStartTime = [NSDate date];
        sTimer = [NSTimer scheduledTimerWithTimeInterval:1.0 repeats:YES block:^(NSTimer *t) {
            UpdateTimer();
        }];
    });
}

void Bugpunch_HideRecordingOverlay(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        [sTimer invalidate];
        sTimer = nil;
        [sRecordingContainer removeFromSuperview];
        sRecordingContainer = nil;
        sTimerLabel = nil;
    });
}

void Bugpunch_ResetRecordingTimer(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        sRecordStartTime = [NSDate date];
        if (sTimerLabel) sTimerLabel.text = @"0:00";
    });
}

} // extern "C"

// ─── Action Target ──────────────────────────────────────────────

@implementation BPOverlayActions

+ (void)onWelcomeConfirm {
    Bugpunch_HideReportWelcome();
    if (sWelcomeConfirmCb) sWelcomeConfirmCb();
}

+ (void)onWelcomeCancel {
    Bugpunch_HideReportWelcome();
    if (sWelcomeCancelCb) sWelcomeCancelCb();
}

+ (void)onRecordingTapped {
    if (sReportTappedCb) sReportTappedCb();
}

+ (void)onRecordingDragged:(UIPanGestureRecognizer *)pan {
    UIView *view = sRecordingContainer;
    if (!view) return;

    UIView *superview = view.superview;
    if (!superview) return;

    CGPoint translation = [pan translationInView:superview];

    if (pan.state == UIGestureRecognizerStateBegan) {
        sViewStartCenter = view.center;
    } else if (pan.state == UIGestureRecognizerStateChanged) {
        view.center = CGPointMake(sViewStartCenter.x + translation.x,
                                   sViewStartCenter.y + translation.y);
    }
}

@end
