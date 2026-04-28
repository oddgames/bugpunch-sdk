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

#import "BugpunchTheme.h"
#import "BugpunchStrings.h"

// UnitySendMessage is provided by the Unity runtime (libiPhone-lib.a).
// Declared here so the shortcut buttons on the recording overlay can
// bounce taps back to C# without needing a C function pointer callback.
extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

// Interface must precede the C-linkage setup code below that addresses the
// class via [BPOverlayActions class]. Implementation stays near the bottom
// next to the helpers it uses.
@interface BPOverlayActions : NSObject
+ (void)onWelcomeConfirm;
+ (void)onWelcomeCancel;
+ (void)onRecordingTapped;
+ (void)onRecordingDragged:(UIPanGestureRecognizer *)pan;
+ (void)onRequestHelpChoice:(UIButton *)sender;
+ (void)onRequestHelpCancel;
+ (void)onRequestHelpBackdrop:(UITapGestureRecognizer *)tap;
+ (void)onRecordingBarChatTapped;
+ (void)onRecordingBarFeedbackTapped;
@end

typedef void (*ReportOverlayCallback)(void);
typedef void (*ReportOverlayChoiceCallback)(int choice);

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

// ─── Recording-bar Shortcut Buttons ─────────────────────────────
//
// Small circular buttons stacked above the red record circle. Drawn with
// CG so we don't need to ship more PNGs — chat is a speech bubble on blue,
// feedback is a lightbulb on green, matching the picker palette. Handles
// its own tap dispatch via UIControl events.
typedef NS_ENUM(NSInteger, BPShortcutKind) {
    BPShortcutChat = 0,
    BPShortcutFeedback = 1,
};

@interface BPShortcutButton : UIButton
@property(nonatomic, assign) BPShortcutKind kind;
@end
@implementation BPShortcutButton
- (void)drawRect:(CGRect)rect {
    CGContextRef ctx = UIGraphicsGetCurrentContext();
    if (!ctx) return;

    CGFloat w = rect.size.width, h = rect.size.height;
    CGFloat cx = w / 2.0f, cy = h / 2.0f;
    CGFloat r = MIN(w, h) / 2.0f;

    // Accent-coloured circle fill
    UIColor *fill = (self.kind == BPShortcutChat)
        ? [BPTheme color:@"accentChat"
                fallback:[UIColor colorWithRed:0.20 green:0.38 blue:0.60 alpha:1.0]]
        : [BPTheme color:@"accentFeedback"
                fallback:[UIColor colorWithRed:0.25 green:0.49 blue:0.30 alpha:1.0]];
    CGContextSetFillColorWithColor(ctx, fill.CGColor);
    CGContextFillEllipseInRect(ctx, CGRectMake(cx - r * 0.92f, cy - r * 0.92f,
                                                r * 1.84f, r * 1.84f));

    // White glyph (stroke)
    CGContextSetStrokeColorWithColor(ctx, [UIColor whiteColor].CGColor);
    CGContextSetLineWidth(ctx, r * 0.12f);
    CGContextSetLineCap(ctx, kCGLineCapRound);
    CGContextSetLineJoin(ctx, kCGLineJoinRound);

    CGFloat s = r * 0.5f;
    if (self.kind == BPShortcutChat) {
        // Speech bubble with 3 dots + tail
        CGFloat bw = s * 1.7f, bh = s * 1.2f;
        CGRect body = CGRectMake(cx - bw / 2.0f, cy - bh / 2.0f - s * 0.1f, bw, bh);
        UIBezierPath *bodyPath = [UIBezierPath bezierPathWithRoundedRect:body cornerRadius:s * 0.25f];
        CGContextAddPath(ctx, bodyPath.CGPath);
        CGContextStrokePath(ctx);

        // Tail
        CGContextMoveToPoint(ctx, CGRectGetMinX(body) + s * 0.3f, CGRectGetMaxY(body));
        CGContextAddLineToPoint(ctx, CGRectGetMinX(body) + s * 0.1f, CGRectGetMaxY(body) + s * 0.4f);
        CGContextAddLineToPoint(ctx, CGRectGetMinX(body) + s * 0.7f, CGRectGetMaxY(body) - s * 0.05f);
        CGContextStrokePath(ctx);

        // Three dots
        CGContextSetFillColorWithColor(ctx, [UIColor whiteColor].CGColor);
        CGFloat dotR = s * 0.1f;
        CGFloat dotY = cy - s * 0.1f;
        CGContextFillEllipseInRect(ctx, CGRectMake(cx - s * 0.45f - dotR, dotY - dotR, dotR * 2, dotR * 2));
        CGContextFillEllipseInRect(ctx, CGRectMake(cx - dotR, dotY - dotR, dotR * 2, dotR * 2));
        CGContextFillEllipseInRect(ctx, CGRectMake(cx + s * 0.45f - dotR, dotY - dotR, dotR * 2, dotR * 2));
    } else {
        // Lightbulb
        CGContextStrokeEllipseInRect(ctx, CGRectMake(cx - s * 0.7f, cy - s * 0.2f - s * 0.7f,
                                                      s * 1.4f, s * 1.4f));
        // Base — two short horizontal strokes
        CGContextMoveToPoint(ctx, cx - s * 0.35f, cy + s * 0.55f);
        CGContextAddLineToPoint(ctx, cx + s * 0.35f, cy + s * 0.55f);
        CGContextMoveToPoint(ctx, cx - s * 0.25f, cy + s * 0.8f);
        CGContextAddLineToPoint(ctx, cx + s * 0.25f, cy + s * 0.8f);
        CGContextStrokePath(ctx);

        // Filament dots
        CGContextSetFillColorWithColor(ctx, [UIColor whiteColor].CGColor);
        CGFloat fR = s * 0.08f;
        CGContextFillEllipseInRect(ctx, CGRectMake(cx - s * 0.18f - fR, cy - s * 0.15f - fR, fR * 2, fR * 2));
        CGContextFillEllipseInRect(ctx, CGRectMake(cx + s * 0.18f - fR, cy - s * 0.15f - fR, fR * 2, fR * 2));
    }
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

    // Accent-record circle
    UIColor* recordColor = [BPTheme color:@"accentRecord"
                                 fallback:[UIColor colorWithRed:0.83 green:0.18 blue:0.18 alpha:1]];
    CGContextSetFillColorWithColor(ctx, recordColor.CGColor);
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

// ── Request-Help responsive card ──
//
// The picker card re-layouts its option stack when the bounds cross the
// 540pt breakpoint so a rotation from portrait → landscape on an iPad
// (or similar split-screen resize) switches to the horizontal row layout
// without having to rebuild the card.
@interface BPRequestHelpCard : UIView
@property(nonatomic, strong) UIStackView* optionStack;
@property(nonatomic, assign) BOOL wasHorizontal;
@end
@implementation BPRequestHelpCard
- (void)layoutSubviews {
    [super layoutSubviews];
    if (!self.optionStack) return;
    BOOL horiz = self.bounds.size.width >= 540;
    if (horiz != self.wasHorizontal) {
        self.optionStack.axis = horiz ? UILayoutConstraintAxisHorizontal : UILayoutConstraintAxisVertical;
        self.optionStack.distribution = horiz ? UIStackViewDistributionFillEqually : UIStackViewDistributionFill;
        self.optionStack.spacing = horiz ? 12 : 8;
        self.wasHorizontal = horiz;
    }
}
@end

// ─── Static State ───────────────────────────────────────────────

static UIView *sWelcomeBackdrop = nil;
static UIView *sRecordingContainer = nil;
static UIView *sRequestHelpBackdrop = nil;
static UILabel *sTimerLabel = nil;
static NSTimer *sTimer = nil;
static NSDate *sRecordStartTime = nil;
static ReportOverlayCallback sWelcomeConfirmCb = NULL;
static ReportOverlayCallback sWelcomeCancelCb = NULL;
static ReportOverlayCallback sReportTappedCb = NULL;
static ReportOverlayChoiceCallback sRequestHelpChoiceCb = NULL;
static ReportOverlayCallback sRequestHelpCancelCb = NULL;

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

// ─── Request-Help Picker Helpers ────────────────────────────────

// ── Vertical-stack option (legacy / narrow phones) ──
// Icon-leading row with a chevron at the trailing edge. Kept for the
// compact layout when card width < 540pt.
static UIButton* BuildRequestHelpOptionRow(int choice, NSString* title, NSString* caption, UIColor* accent, NSString* iconName) {
    UIButton *btn = [UIButton buttonWithType:UIButtonTypeCustom];
    btn.tag = choice;
    btn.backgroundColor = [BPTheme color:@"cardBackground" fallback:[UIColor colorWithWhite:0.17 alpha:1]];
    btn.layer.cornerRadius = [BPTheme radius:@"cardRadius" fallback:8];
    btn.layer.borderWidth = 1;
    btn.layer.borderColor = [BPTheme color:@"cardBorder" fallback:[UIColor colorWithWhite:0.28 alpha:1]].CGColor;
    btn.translatesAutoresizingMaskIntoConstraints = NO;
    [btn addTarget:[BPOverlayActions class] action:@selector(onRequestHelpChoice:)
          forControlEvents:UIControlEventTouchUpInside];

    UIView *leading = nil;
    UIImage *iconImg = iconName ? [UIImage imageNamed:iconName] : nil;
    if (!iconImg && iconName) {
        NSString *path = [[NSBundle mainBundle] pathForResource:iconName ofType:@"png"];
        if (path) iconImg = [UIImage imageWithContentsOfFile:path];
    }
    if (iconImg) {
        UIImageView *iv = [[UIImageView alloc] initWithImage:iconImg];
        iv.contentMode = UIViewContentModeScaleAspectFit;
        iv.userInteractionEnabled = NO;
        iv.translatesAutoresizingMaskIntoConstraints = NO;
        [btn addSubview:iv];
        leading = iv;
    } else {
        UIView *dot = [[UIView alloc] init];
        dot.backgroundColor = accent;
        dot.layer.cornerRadius = 5;
        dot.userInteractionEnabled = NO;
        dot.translatesAutoresizingMaskIntoConstraints = NO;
        [btn addSubview:dot];
        leading = dot;
    }

    UILabel *titleLabel = [[UILabel alloc] init];
    titleLabel.text = title;
    titleLabel.font = [UIFont boldSystemFontOfSize:15];
    titleLabel.textColor = [BPTheme color:@"textPrimary" fallback:[UIColor whiteColor]];
    titleLabel.userInteractionEnabled = NO;
    titleLabel.translatesAutoresizingMaskIntoConstraints = NO;
    [btn addSubview:titleLabel];

    UILabel *captionLabel = [[UILabel alloc] init];
    captionLabel.text = caption;
    captionLabel.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];
    captionLabel.textColor = [BPTheme color:@"textSecondary" fallback:[UIColor colorWithWhite:0.7 alpha:1]];
    captionLabel.numberOfLines = 0;
    captionLabel.userInteractionEnabled = NO;
    captionLabel.translatesAutoresizingMaskIntoConstraints = NO;
    [btn addSubview:captionLabel];

    UILabel *chev = [[UILabel alloc] init];
    chev.text = @"›";
    chev.font = [UIFont boldSystemFontOfSize:20];
    chev.textColor = [BPTheme color:@"textMuted" fallback:[UIColor colorWithWhite:0.55 alpha:1]];
    chev.userInteractionEnabled = NO;
    chev.translatesAutoresizingMaskIntoConstraints = NO;
    [btn addSubview:chev];

    BOOL hasIcon = [leading isKindOfClass:[UIImageView class]];
    CGFloat leadingSize = hasIcon ? 48 : 10;

    [NSLayoutConstraint activateConstraints:@[
        [btn.heightAnchor constraintGreaterThanOrEqualToConstant:hasIcon ? 72 : 58],

        [leading.leadingAnchor constraintEqualToAnchor:btn.leadingAnchor constant:14],
        [leading.centerYAnchor constraintEqualToAnchor:btn.centerYAnchor],
        [leading.widthAnchor constraintEqualToConstant:leadingSize],
        [leading.heightAnchor constraintEqualToConstant:leadingSize],

        [titleLabel.leadingAnchor constraintEqualToAnchor:leading.trailingAnchor constant:12],
        [titleLabel.topAnchor constraintEqualToAnchor:btn.topAnchor constant:12],
        [titleLabel.trailingAnchor constraintLessThanOrEqualToAnchor:chev.leadingAnchor constant:-8],

        [captionLabel.leadingAnchor constraintEqualToAnchor:titleLabel.leadingAnchor],
        [captionLabel.topAnchor constraintEqualToAnchor:titleLabel.bottomAnchor constant:2],
        [captionLabel.trailingAnchor constraintLessThanOrEqualToAnchor:chev.leadingAnchor constant:-8],
        [captionLabel.bottomAnchor constraintEqualToAnchor:btn.bottomAnchor constant:-12],

        [chev.trailingAnchor constraintEqualToAnchor:btn.trailingAnchor constant:-14],
        [chev.centerYAnchor constraintEqualToAnchor:btn.centerYAnchor],
    ]];

    return btn;
}

// ── Stacked option panel (horizontal picker) ──
//
//   ┌────────────┐
//   │   [icon]   │   64pt square centered
//   │   Title    │   18pt bold
//   │   caption  │   13pt, 2 lines
//   │ ────────── │   2pt accent underline
//   └────────────┘
static UIButton* BuildRequestHelpOptionStacked(int choice, NSString* title, NSString* caption,
                                               UIColor* accent, NSString* iconName) {
    UIButton *btn = [UIButton buttonWithType:UIButtonTypeCustom];
    btn.tag = choice;
    btn.backgroundColor = [BPTheme color:@"cardBackground" fallback:[UIColor colorWithWhite:0.17 alpha:1]];
    btn.layer.cornerRadius = [BPTheme radius:@"cardRadius" fallback:10];
    btn.layer.borderWidth = 1;
    btn.layer.borderColor = [BPTheme color:@"cardBorder" fallback:[UIColor colorWithWhite:0.28 alpha:1]].CGColor;
    btn.layer.masksToBounds = YES;
    btn.translatesAutoresizingMaskIntoConstraints = NO;
    [btn addTarget:[BPOverlayActions class] action:@selector(onRequestHelpChoice:)
          forControlEvents:UIControlEventTouchUpInside];

    // Icon centered at the top.
    UIView* iconView = nil;
    UIImage *iconImg = iconName ? [UIImage imageNamed:iconName] : nil;
    if (!iconImg && iconName) {
        NSString *path = [[NSBundle mainBundle] pathForResource:iconName ofType:@"png"];
        if (path) iconImg = [UIImage imageWithContentsOfFile:path];
    }
    if (iconImg) {
        UIImageView *iv = [[UIImageView alloc] initWithImage:iconImg];
        iv.contentMode = UIViewContentModeScaleAspectFit;
        iv.userInteractionEnabled = NO;
        iv.translatesAutoresizingMaskIntoConstraints = NO;
        [btn addSubview:iv];
        iconView = iv;
    } else {
        UIView *dot = [[UIView alloc] init];
        dot.backgroundColor = accent;
        dot.layer.cornerRadius = 8;
        dot.userInteractionEnabled = NO;
        dot.translatesAutoresizingMaskIntoConstraints = NO;
        [btn addSubview:dot];
        iconView = dot;
    }

    UILabel *titleLabel = [[UILabel alloc] init];
    titleLabel.text = title;
    titleLabel.font = [UIFont boldSystemFontOfSize:18];
    titleLabel.textColor = [BPTheme color:@"textPrimary" fallback:[UIColor whiteColor]];
    titleLabel.textAlignment = NSTextAlignmentCenter;
    titleLabel.userInteractionEnabled = NO;
    titleLabel.translatesAutoresizingMaskIntoConstraints = NO;
    [btn addSubview:titleLabel];

    UILabel *captionLabel = [[UILabel alloc] init];
    captionLabel.text = caption;
    captionLabel.font = [UIFont systemFontOfSize:13];
    captionLabel.textColor = [BPTheme color:@"textSecondary" fallback:[UIColor colorWithWhite:0.7 alpha:1]];
    captionLabel.textAlignment = NSTextAlignmentCenter;
    captionLabel.numberOfLines = 2;
    captionLabel.lineBreakMode = NSLineBreakByTruncatingTail;
    captionLabel.userInteractionEnabled = NO;
    captionLabel.translatesAutoresizingMaskIntoConstraints = NO;
    [btn addSubview:captionLabel];

    // Accent bottom underline — flush with the card's rounded corners via
    // the masksToBounds clip above.
    UIView* underline = [[UIView alloc] init];
    underline.backgroundColor = accent;
    underline.userInteractionEnabled = NO;
    underline.translatesAutoresizingMaskIntoConstraints = NO;
    [btn addSubview:underline];

    BOOL hasIcon = [iconView isKindOfClass:[UIImageView class]];
    CGFloat iconSide = hasIcon ? 64 : 16;

    [NSLayoutConstraint activateConstraints:@[
        [btn.heightAnchor constraintGreaterThanOrEqualToConstant:hasIcon ? 176 : 140],

        [iconView.centerXAnchor constraintEqualToAnchor:btn.centerXAnchor],
        [iconView.topAnchor constraintEqualToAnchor:btn.topAnchor constant:16],
        [iconView.widthAnchor constraintEqualToConstant:iconSide],
        [iconView.heightAnchor constraintEqualToConstant:iconSide],

        [titleLabel.topAnchor constraintEqualToAnchor:iconView.bottomAnchor constant:12],
        [titleLabel.leadingAnchor constraintEqualToAnchor:btn.leadingAnchor constant:8],
        [titleLabel.trailingAnchor constraintEqualToAnchor:btn.trailingAnchor constant:-8],

        [captionLabel.topAnchor constraintEqualToAnchor:titleLabel.bottomAnchor constant:6],
        [captionLabel.leadingAnchor constraintEqualToAnchor:btn.leadingAnchor constant:10],
        [captionLabel.trailingAnchor constraintEqualToAnchor:btn.trailingAnchor constant:-10],
        [captionLabel.bottomAnchor constraintLessThanOrEqualToAnchor:underline.topAnchor constant:-12],

        [underline.leadingAnchor constraintEqualToAnchor:btn.leadingAnchor],
        [underline.trailingAnchor constraintEqualToAnchor:btn.trailingAnchor],
        [underline.bottomAnchor constraintEqualToAnchor:btn.bottomAnchor],
        [underline.heightAnchor constraintEqualToConstant:2],
    ]];

    return btn;
}

// Picker-wide entry point — caller indicates whether it wants the horizontal
// (stacked) or vertical (row-with-chevron) layout.
static UIButton* BuildRequestHelpOption(int choice, NSString* title, NSString* caption, UIColor* accent,
                                         NSString* iconName, BOOL stacked) {
    return stacked
        ? BuildRequestHelpOptionStacked(choice, title, caption, accent, iconName)
        : BuildRequestHelpOptionRow(choice, title, caption, accent, iconName);
}

// ─── C Bridge Functions ─────────────────────────────────────────

extern "C" {

void Bugpunch_ShowRequestHelp(ReportOverlayChoiceCallback onChoice, ReportOverlayCallback onCancel) {
    sRequestHelpChoiceCb = onChoice;
    sRequestHelpCancelCb = onCancel;

    dispatch_async(dispatch_get_main_queue(), ^{
        if (sRequestHelpBackdrop) return;

        UIView *root = GetRootView();
        if (!root) return;

        CGFloat pad = 24;
        // Decide layout once at build time. The responsive BPRequestHelpCard
        // subclass will still re-toggle the axis on rotation, but this picks
        // a sensible starting width so the card is proportioned correctly.
        BOOL horizontal = root.bounds.size.width >= (540 + 48);
        CGFloat cardW = horizontal ? 560 : 340;

        // Backdrop — themed, tappable to cancel.
        sRequestHelpBackdrop = [[UIView alloc] initWithFrame:root.bounds];
        sRequestHelpBackdrop.backgroundColor = [BPTheme color:@"backdrop"
            fallback:[[UIColor blackColor] colorWithAlphaComponent:0.6]];
        sRequestHelpBackdrop.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
        [root addSubview:sRequestHelpBackdrop];

        UITapGestureRecognizer *bgTap = [[UITapGestureRecognizer alloc]
            initWithTarget:[BPOverlayActions class] action:@selector(onRequestHelpBackdrop:)];
        [sRequestHelpBackdrop addGestureRecognizer:bgTap];

        // Card — BPRequestHelpCard re-axes its optionStack on bounds change.
        BPRequestHelpCard *card = [[BPRequestHelpCard alloc] init];
        card.backgroundColor = [BPTheme color:@"cardBackground"
            fallback:[UIColor colorWithWhite:0.13 alpha:0.97]];
        card.layer.cornerRadius = [BPTheme radius:@"cardRadius" fallback:12];
        card.layer.borderWidth = 1;
        card.layer.borderColor = [BPTheme color:@"cardBorder"
            fallback:[UIColor colorWithWhite:0.28 alpha:1]].CGColor;
        card.translatesAutoresizingMaskIntoConstraints = NO;

        UITapGestureRecognizer *cardTap = [[UITapGestureRecognizer alloc] initWithTarget:nil action:NULL];
        cardTap.cancelsTouchesInView = YES;
        [card addGestureRecognizer:cardTap];
        [sRequestHelpBackdrop addSubview:card];

        UILabel *title = [[UILabel alloc] init];
        title.text = [BPStrings text:@"pickerTitle" fallback:@"What would you like to do?"];
        title.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeTitle" fallback:20]];
        title.textColor = [BPTheme color:@"textPrimary" fallback:[UIColor whiteColor]];
        title.textAlignment = horizontal ? NSTextAlignmentCenter : NSTextAlignmentNatural;
        title.numberOfLines = 0;
        title.translatesAutoresizingMaskIntoConstraints = NO;
        [card addSubview:title];

        UILabel *subtitle = [[UILabel alloc] init];
        subtitle.text = [BPStrings text:@"pickerSubtitle"
            fallback:@"Pick what fits — we'll only bother the dev team with what you send."];
        subtitle.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:13]];
        subtitle.textColor = [BPTheme color:@"textSecondary"
            fallback:[UIColor colorWithWhite:0.72 alpha:1]];
        subtitle.textAlignment = horizontal ? NSTextAlignmentCenter : NSTextAlignmentNatural;
        subtitle.numberOfLines = 0;
        subtitle.translatesAutoresizingMaskIntoConstraints = NO;
        [card addSubview:subtitle];

        // Three options — stacked panels for horizontal, row-with-chevron for
        // narrow. Bundled into a UIStackView so the BPRequestHelpCard can
        // toggle the axis on rotation without rebuilding.
        UIButton *opt0 = BuildRequestHelpOption(0,
            [BPStrings text:@"pickerAskTitle" fallback:@"Ask for help"],
            [BPStrings text:@"pickerAskCaption" fallback:@"Short question to the dev team"],
            [BPTheme color:@"accentChat" fallback:[UIColor colorWithRed:0.20 green:0.38 blue:0.60 alpha:1]],
            @"bugpunch-help-ask", horizontal);
        UIButton *opt1 = BuildRequestHelpOption(1,
            [BPStrings text:@"pickerBugTitle" fallback:@"Record a bug"],
            [BPStrings text:@"pickerBugCaption" fallback:@"Capture a video + report a problem"],
            [BPTheme color:@"accentBug" fallback:[UIColor colorWithRed:0.58 green:0.22 blue:0.22 alpha:1]],
            @"bugpunch-help-bug", horizontal);
        UIButton *opt2 = BuildRequestHelpOption(2,
            [BPStrings text:@"pickerFeatureTitle" fallback:@"Request a feature"],
            [BPStrings text:@"pickerFeatureCaption" fallback:@"Suggest / vote on improvements"],
            [BPTheme color:@"accentFeedback" fallback:[UIColor colorWithRed:0.25 green:0.49 blue:0.30 alpha:1]],
            @"bugpunch-help-feedback", horizontal);

        UIStackView *stack = [[UIStackView alloc] initWithArrangedSubviews:@[opt0, opt1, opt2]];
        stack.axis = horizontal ? UILayoutConstraintAxisHorizontal : UILayoutConstraintAxisVertical;
        stack.distribution = horizontal ? UIStackViewDistributionFillEqually : UIStackViewDistributionFill;
        stack.alignment = UIStackViewAlignmentFill;
        stack.spacing = horizontal ? 12 : 8;
        stack.translatesAutoresizingMaskIntoConstraints = NO;
        [card addSubview:stack];
        card.optionStack = stack;
        card.wasHorizontal = horizontal;

        // Cancel button
        UIButton *cancel = [UIButton buttonWithType:UIButtonTypeSystem];
        [cancel setTitle:[BPStrings text:@"pickerCancel" fallback:@"Cancel"]
                forState:UIControlStateNormal];
        [cancel setTitleColor:[BPTheme color:@"textMuted" fallback:[UIColor colorWithWhite:0.6 alpha:1]]
                     forState:UIControlStateNormal];
        cancel.titleLabel.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
        cancel.translatesAutoresizingMaskIntoConstraints = NO;
        [cancel addTarget:[BPOverlayActions class] action:@selector(onRequestHelpCancel)
                 forControlEvents:UIControlEventTouchUpInside];
        [card addSubview:cancel];

        // Card width: prefer the horizontal 560 if we fit, otherwise fall
        // back to the compact 340. We don't pin to a constant so rotation
        // naturally lets the card resize (and the BPRequestHelpCard's
        // layoutSubviews handler re-toggles axis when the bounds cross 540).
        NSLayoutConstraint* maxWidth = [card.widthAnchor constraintLessThanOrEqualToConstant:cardW];
        NSLayoutConstraint* preferredWidth = [card.widthAnchor constraintEqualToConstant:cardW];
        preferredWidth.priority = UILayoutPriorityDefaultHigh;
        NSLayoutConstraint* sideMargin = [card.leadingAnchor
            constraintGreaterThanOrEqualToAnchor:sRequestHelpBackdrop.leadingAnchor constant:24];
        NSLayoutConstraint* sideMargin2 = [card.trailingAnchor
            constraintLessThanOrEqualToAnchor:sRequestHelpBackdrop.trailingAnchor constant:-24];

        [NSLayoutConstraint activateConstraints:@[
            [card.centerXAnchor constraintEqualToAnchor:sRequestHelpBackdrop.centerXAnchor],
            [card.centerYAnchor constraintEqualToAnchor:sRequestHelpBackdrop.centerYAnchor],
            maxWidth, preferredWidth, sideMargin, sideMargin2,

            [title.topAnchor constraintEqualToAnchor:card.topAnchor constant:pad],
            [title.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:pad],
            [title.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-pad],

            [subtitle.topAnchor constraintEqualToAnchor:title.bottomAnchor constant:6],
            [subtitle.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:pad],
            [subtitle.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-pad],

            [stack.topAnchor constraintEqualToAnchor:subtitle.bottomAnchor constant:16],
            [stack.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:pad],
            [stack.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-pad],

            [cancel.topAnchor constraintEqualToAnchor:stack.bottomAnchor constant:12],
            [cancel.centerXAnchor constraintEqualToAnchor:card.centerXAnchor],
            [cancel.bottomAnchor constraintEqualToAnchor:card.bottomAnchor constant:-(pad - 4)],
        ]];
    });
}

void Bugpunch_HideRequestHelp(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        [sRequestHelpBackdrop removeFromSuperview];
        sRequestHelpBackdrop = nil;
    });
}

void Bugpunch_ShowReportWelcome(ReportOverlayCallback onConfirm, ReportOverlayCallback onCancel) {
    sWelcomeConfirmCb = onConfirm;
    sWelcomeCancelCb = onCancel;

    dispatch_async(dispatch_get_main_queue(), ^{
        if (sWelcomeBackdrop) return;

        UIView *root = GetRootView();
        if (!root) return;

        CGFloat pad = 24, padSmall = 12;
        CGFloat cardW = 300;

        // Backdrop — themed dim.
        sWelcomeBackdrop = [[UIView alloc] initWithFrame:root.bounds];
        sWelcomeBackdrop.backgroundColor = [BPTheme color:@"backdrop"
            fallback:[[UIColor blackColor] colorWithAlphaComponent:0.6]];
        sWelcomeBackdrop.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
        [root addSubview:sWelcomeBackdrop];

        // Card
        UIView *card = [[UIView alloc] init];
        card.backgroundColor = [BPTheme color:@"cardBackground"
            fallback:[UIColor colorWithWhite:0.13 alpha:0.94]];
        card.layer.cornerRadius = [BPTheme radius:@"cardRadius" fallback:16];
        card.layer.borderWidth = 1;
        card.layer.borderColor = [BPTheme color:@"cardBorder"
            fallback:[UIColor colorWithWhite:0.28 alpha:1]].CGColor;
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
        title.text = [BPStrings text:@"welcomeTitle" fallback:@"Report a Bug"];
        title.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeTitle" fallback:20]];
        title.textColor = [BPTheme color:@"textPrimary" fallback:[UIColor whiteColor]];
        title.textAlignment = NSTextAlignmentCenter;
        title.translatesAutoresizingMaskIntoConstraints = NO;
        [card addSubview:title];

        // Body
        UILabel *body = [[UILabel alloc] init];
        body.text = [BPStrings text:@"welcomeBody"
            fallback:@"We'll record your screen while you reproduce the issue.\n\nWhen you're ready, tap the report button to send us the details."];
        body.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
        body.textColor = [BPTheme color:@"textSecondary" fallback:[UIColor colorWithWhite:0.73 alpha:1]];
        body.textAlignment = NSTextAlignmentCenter;
        body.numberOfLines = 0;
        body.translatesAutoresizingMaskIntoConstraints = NO;
        [card addSubview:body];

        // "Got it" button — primary accent
        UIButton *gotItBtn = [UIButton buttonWithType:UIButtonTypeSystem];
        [gotItBtn setTitle:[BPStrings text:@"welcomeConfirm" fallback:@"Got it"]
                  forState:UIControlStateNormal];
        [gotItBtn setTitleColor:[BPTheme color:@"textPrimary" fallback:[UIColor whiteColor]]
                       forState:UIControlStateNormal];
        gotItBtn.titleLabel.font = [UIFont boldSystemFontOfSize:16];
        gotItBtn.backgroundColor = [BPTheme color:@"accentPrimary"
            fallback:[UIColor colorWithRed:0.18 green:0.49 blue:0.20 alpha:1]];
        gotItBtn.layer.cornerRadius = 8;
        gotItBtn.translatesAutoresizingMaskIntoConstraints = NO;
        [gotItBtn addTarget:[BPOverlayActions class] action:@selector(onWelcomeConfirm) forControlEvents:UIControlEventTouchUpInside];
        [card addSubview:gotItBtn];

        // Cancel link
        UIButton *cancelBtn = [UIButton buttonWithType:UIButtonTypeSystem];
        [cancelBtn setTitle:[BPStrings text:@"welcomeCancel" fallback:@"Cancel"]
                   forState:UIControlStateNormal];
        [cancelBtn setTitleColor:[BPTheme color:@"textMuted" fallback:[UIColor colorWithWhite:0.53 alpha:1]]
                        forState:UIControlStateNormal];
        cancelBtn.titleLabel.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
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
        CGFloat shortcutSize = 40;
        CGFloat shortcutGap = 8;
        CGFloat margin = 16;

        // Container tall enough for [chat][gap][feedback][gap][record][timer]
        CGFloat contentW = btnSize + 20;
        CGFloat shortcutX = (contentW - shortcutSize) / 2.0f;
        CGFloat shortcutsTotal = (shortcutSize + shortcutGap) * 2;
        CGFloat contentH = shortcutsTotal + btnSize + 24;

        sRecordingContainer = [[UIView alloc] initWithFrame:CGRectMake(0, 0, contentW, contentH)];
        sRecordingContainer.center = CGPointMake(
            root.bounds.size.width - contentW / 2 - margin,
            root.bounds.size.height - contentH / 2 - margin - 40);
        [root addSubview:sRecordingContainer];

        // Chat shortcut (topmost)
        BPShortcutButton *chatBtn = [[BPShortcutButton alloc]
            initWithFrame:CGRectMake(shortcutX, 0, shortcutSize, shortcutSize)];
        chatBtn.kind = BPShortcutChat;
        chatBtn.backgroundColor = [UIColor clearColor];
        chatBtn.layer.shadowColor = [UIColor blackColor].CGColor;
        chatBtn.layer.shadowOffset = CGSizeMake(0, 2);
        chatBtn.layer.shadowOpacity = 0.4;
        chatBtn.layer.shadowRadius = 3;
        [chatBtn addTarget:[BPOverlayActions class]
                    action:@selector(onRecordingBarChatTapped)
          forControlEvents:UIControlEventTouchUpInside];
        [sRecordingContainer addSubview:chatBtn];

        // Feedback shortcut (below chat)
        BPShortcutButton *feedbackBtn = [[BPShortcutButton alloc]
            initWithFrame:CGRectMake(shortcutX, shortcutSize + shortcutGap, shortcutSize, shortcutSize)];
        feedbackBtn.kind = BPShortcutFeedback;
        feedbackBtn.backgroundColor = [UIColor clearColor];
        feedbackBtn.layer.shadowColor = [UIColor blackColor].CGColor;
        feedbackBtn.layer.shadowOffset = CGSizeMake(0, 2);
        feedbackBtn.layer.shadowOpacity = 0.4;
        feedbackBtn.layer.shadowRadius = 3;
        [feedbackBtn addTarget:[BPOverlayActions class]
                        action:@selector(onRecordingBarFeedbackTapped)
              forControlEvents:UIControlEventTouchUpInside];
        [sRecordingContainer addSubview:feedbackBtn];

        // Record button — sits below the shortcuts. Pan + tap below drive
        // it via gestures attached to a wrapper view.
        CGFloat recordY = shortcutsTotal;
        UIView *recordWrapper = [[UIView alloc] initWithFrame:
            CGRectMake(10, recordY, btnSize, btnSize)];
        recordWrapper.backgroundColor = [UIColor clearColor];
        [sRecordingContainer addSubview:recordWrapper];

        BPRecordButton *btn = [[BPRecordButton alloc] initWithFrame:
            CGRectMake(0, 0, btnSize, btnSize)];
        btn.backgroundColor = [UIColor clearColor];
        btn.userInteractionEnabled = NO;
        [recordWrapper addSubview:btn];

        // Tap + pan gestures are on the record wrapper specifically, NOT
        // the full container — we don't want a tap on the shortcut buttons
        // to also fire the record-stop callback.
        UITapGestureRecognizer *tap = [[UITapGestureRecognizer alloc]
            initWithTarget:[BPOverlayActions class] action:@selector(onRecordingTapped)];
        [recordWrapper addGestureRecognizer:tap];

        UIPanGestureRecognizer *pan = [[UIPanGestureRecognizer alloc]
            initWithTarget:[BPOverlayActions class] action:@selector(onRecordingDragged:)];
        [recordWrapper addGestureRecognizer:pan];

        // Timer label
        sTimerLabel = [[UILabel alloc] initWithFrame:CGRectMake(0, recordY + btnSize + 4, contentW, 16)];
        sTimerLabel.text = @"0:00";
        sTimerLabel.font = [UIFont boldSystemFontOfSize:11];
        sTimerLabel.textColor = [BPTheme color:@"textPrimary" fallback:[UIColor whiteColor]];
        sTimerLabel.textAlignment = NSTextAlignmentCenter;
        sTimerLabel.layer.shadowColor = [UIColor blackColor].CGColor;
        sTimerLabel.layer.shadowOffset = CGSizeMake(1, 1);
        sTimerLabel.layer.shadowOpacity = 0.8;
        sTimerLabel.layer.shadowRadius = 1;
        [sRecordingContainer addSubview:sTimerLabel];

        // Drop shadow on container (record button's outer glow)
        sRecordingContainer.layer.shadowColor = [UIColor blackColor].CGColor;
        sRecordingContainer.layer.shadowOffset = CGSizeMake(0, 3);
        sRecordingContainer.layer.shadowOpacity = 0.4;
        sRecordingContainer.layer.shadowRadius = 4;

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

// Native chat board entry point — Phase B of #29. The previous Unity
// round-trip (UnitySendMessage("BugpunchReportCallback",
// "OnShowChatBoardRequested")) bounced into the C# UI Toolkit
// BugpunchChatBoard, which didn't render like a chat app and lived inside
// the Unity surface instead of as a system overlay.
//
// Now points at the new BugpunchChatViewController (BugpunchChatViewController.mm),
// a self-contained UIViewController that owns the bubble list, composer,
// HTTP, and 5s polling natively. C# is no longer on this path on device.
// The C# board still serves as the Editor / Standalone fallback.
extern "C" void Bugpunch_PresentNativeChatViewController(void);

void Bugpunch_ShowChatBoard(void) {
    Bugpunch_PresentNativeChatViewController();
}

// Native feedback board entry point — Phase B part 2 of #29. Mirrors the
// chat-board wiring above: previously the iOS `INativeDialog.ShowFeedbackBoard`
// fell back to the C# UI Toolkit `BugpunchFeedbackBoard`, which rendered
// inside the Unity surface and looked broken on device. Now points at
// the new BugpunchFeedbackViewController (BugpunchFeedbackViewController.mm),
// a self-contained UIViewController that owns the list / detail / submit
// views, voting, similarity-check flow, comments and multipart upload
// natively. The C# board still serves as the Editor / Standalone fallback.
extern "C" void Bugpunch_PresentNativeFeedbackViewController(void);

void Bugpunch_ShowFeedbackBoard(void) {
    Bugpunch_PresentNativeFeedbackViewController();
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

+ (void)onRequestHelpChoice:(UIButton *)sender {
    int choice = (int)sender.tag;
    ReportOverlayChoiceCallback cb = sRequestHelpChoiceCb;
    sRequestHelpChoiceCb = NULL;
    sRequestHelpCancelCb = NULL;
    Bugpunch_HideRequestHelp();
    if (cb) cb(choice);
}

+ (void)onRequestHelpCancel {
    ReportOverlayCallback cb = sRequestHelpCancelCb;
    sRequestHelpChoiceCb = NULL;
    sRequestHelpCancelCb = NULL;
    Bugpunch_HideRequestHelp();
    if (cb) cb();
}

+ (void)onRequestHelpBackdrop:(UITapGestureRecognizer *)tap {
    // Only dismiss when the tap actually lands on the backdrop itself
    // (not on the card, which has its own swallow recognizer).
    if (tap.view != sRequestHelpBackdrop) return;
    CGPoint loc = [tap locationInView:sRequestHelpBackdrop];
    for (UIView *sub in sRequestHelpBackdrop.subviews) {
        if (CGRectContainsPoint(sub.frame, loc)) return; // tap hit the card
    }
    [BPOverlayActions onRequestHelpCancel];
}

+ (void)onRecordingBarChatTapped {
    // Bounce to C#; the chat board lives there (see BugpunchChatBoard.cs).
    // Target matches the Android side via BugpunchReportCallback so both
    // platforms land on the same ReportOverlayCallback MonoBehaviour.
    UnitySendMessage("BugpunchReportCallback", "OnRecordingBarChatTapped", "");
}

+ (void)onRecordingBarFeedbackTapped {
    UnitySendMessage("BugpunchReportCallback", "OnRecordingBarFeedbackTapped", "");
}

@end
