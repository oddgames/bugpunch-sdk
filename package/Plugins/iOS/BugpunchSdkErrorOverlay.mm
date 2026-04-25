#import <UIKit/UIKit.h>

// On-screen banner that surfaces internal Bugpunch SDK errors so the dev/QA
// isn't in the dark when the SDK itself fails (P-Invoke errors, JSON parse
// failures, swallowed exceptions, etc.). Mirrors the Android
// BugpunchSdkErrorOverlay class.
//
// - Pill at the top of the key window with the latest error.
// - Auto-hides after 6s of no new errors.
// - Tap to expand into a card listing recent errors with stacks.
// - Always-on collection: the ring fills regardless of overlay-visible.
//
// Safe to call from any thread — UI mutations hop to main.

static const NSUInteger kBPRingCapacity = 50;
static const NSTimeInterval kBPAutoHideSec = 6.0;

@interface BPSdkErrorEntry : NSObject
@property (nonatomic, copy) NSString* source;
@property (nonatomic, copy) NSString* message;
@property (nonatomic, copy) NSString* stack;
@property (nonatomic, assign) NSTimeInterval timestamp;
@property (nonatomic, assign) NSInteger count;
@end
@implementation BPSdkErrorEntry @end

@interface BPSdkErrorOverlay : NSObject
+ (instancetype)shared;

- (void)setOverlayEnabled:(BOOL)enabled;
- (void)reportSource:(NSString*)source message:(NSString*)message stack:(NSString*)stack;
- (NSArray<BPSdkErrorEntry*>*)snapshot;
- (void)clear;
@end

@implementation BPSdkErrorOverlay {
    NSMutableArray<BPSdkErrorEntry*>* _ring;          // newest first
    NSLock* _ringLock;
    BOOL _overlayEnabled;
    BOOL _expanded;
    UIView* _bannerView;
    UILabel* _bannerLabel;
    UILabel* _bannerCount;
    UIView* _expandedView;
    NSTimer* _hideTimer;
}

+ (instancetype)shared {
    static BPSdkErrorOverlay* s; static dispatch_once_t once;
    dispatch_once(&once, ^{ s = [BPSdkErrorOverlay new]; });
    return s;
}

- (instancetype)init {
    if ((self = [super init])) {
        _ring = [NSMutableArray arrayWithCapacity:kBPRingCapacity];
        _ringLock = [NSLock new];
        _overlayEnabled = YES;
    }
    return self;
}

- (void)setOverlayEnabled:(BOOL)enabled {
    _overlayEnabled = enabled;
    if (!enabled) {
        dispatch_async(dispatch_get_main_queue(), ^{ [self hideAll]; });
    }
}

- (BOOL)overlayEnabled { return _overlayEnabled; }

- (NSArray<BPSdkErrorEntry*>*)snapshot {
    [_ringLock lock];
    NSArray* copy = [_ring copy];
    [_ringLock unlock];
    return copy;
}

- (void)clear {
    [_ringLock lock];
    [_ring removeAllObjects];
    [_ringLock unlock];
    dispatch_async(dispatch_get_main_queue(), ^{ [self hideAll]; });
}

- (void)reportSource:(NSString*)source message:(NSString*)message stack:(NSString*)stack {
    NSString* src = source.length ? source : @"Bugpunch";
    NSString* msg = message ?: @"";
    NSString* st  = stack ?: @"";

    BPSdkErrorEntry* pinned;
    [_ringLock lock];
    BPSdkErrorEntry* head = _ring.firstObject;
    if (head && [head.source isEqualToString:src] && [head.message isEqualToString:msg]) {
        head.count += 1;
        pinned = head;
    } else {
        BPSdkErrorEntry* e = [BPSdkErrorEntry new];
        e.source = src; e.message = msg; e.stack = st;
        e.timestamp = [NSDate date].timeIntervalSince1970;
        e.count = 1;
        [_ring insertObject:e atIndex:0];
        while (_ring.count > kBPRingCapacity) [_ring removeLastObject];
        pinned = e;
    }
    [_ringLock unlock];

    if (!_overlayEnabled) return;
    BPSdkErrorEntry* latest = pinned;
    dispatch_async(dispatch_get_main_queue(), ^{ [self showOrUpdate:latest]; });
}

// ─── UI ─────────────────────────────────────────────────────────────

- (UIWindow*)keyWindow {
    if (@available(iOS 13, *)) {
        for (UIScene* scene in UIApplication.sharedApplication.connectedScenes) {
            if ([scene isKindOfClass:UIWindowScene.class]
                && scene.activationState == UISceneActivationStateForegroundActive) {
                for (UIWindow* w in ((UIWindowScene*)scene).windows) {
                    if (w.isKeyWindow) return w;
                }
            }
        }
    }
    return UIApplication.sharedApplication.keyWindow; // fallback for iOS 12
}

- (void)showOrUpdate:(BPSdkErrorEntry*)latest {
    if (!_overlayEnabled) return;
    if (_expanded) { [self rebuildExpanded]; return; }

    UIWindow* window = [self keyWindow];
    if (!window) return;

    @try {
        if (!_bannerView) {
            _bannerView = [self buildBanner];
            CGFloat topInset = 32;
            if (@available(iOS 11, *)) topInset += window.safeAreaInsets.top;
            CGSize sz = [_bannerView systemLayoutSizeFittingSize:UILayoutFittingCompressedSize];
            _bannerView.frame = CGRectMake(
                (window.bounds.size.width - sz.width) / 2,
                topInset, sz.width, sz.height);
            [window addSubview:_bannerView];
        }
        [self updateBannerText:latest];
        // Re-fit after text change.
        CGSize sz = [_bannerView systemLayoutSizeFittingSize:UILayoutFittingCompressedSize];
        CGRect f = _bannerView.frame;
        f.size = sz;
        f.origin.x = (window.bounds.size.width - sz.width) / 2;
        _bannerView.frame = f;

        [self scheduleAutoHide];
    } @catch (NSException* e) {
        NSLog(@"[Bugpunch.SdkError] showOrUpdate exception: %@", e);
    }
}

- (UIView*)buildBanner {
    UIView* pill = [[UIView alloc] init];
    pill.backgroundColor = [UIColor colorWithRed:0.69 green:0 blue:0.13 alpha:0.93];
    pill.layer.cornerRadius = 18;
    pill.layer.borderWidth = 1;
    pill.layer.borderColor = [UIColor colorWithWhite:1 alpha:0.2].CGColor;
    pill.layer.shadowColor = UIColor.blackColor.CGColor;
    pill.layer.shadowOpacity = 0.3;
    pill.layer.shadowRadius = 6;
    pill.layer.shadowOffset = CGSizeMake(0, 2);
    pill.translatesAutoresizingMaskIntoConstraints = NO;

    UILabel* icon = [[UILabel alloc] init];
    icon.text = @"!";
    icon.textColor = UIColor.whiteColor;
    icon.font = [UIFont boldSystemFontOfSize:14];
    icon.translatesAutoresizingMaskIntoConstraints = NO;
    [pill addSubview:icon];

    _bannerLabel = [[UILabel alloc] init];
    _bannerLabel.textColor = UIColor.whiteColor;
    _bannerLabel.font = [UIFont systemFontOfSize:12];
    _bannerLabel.lineBreakMode = NSLineBreakByTruncatingTail;
    _bannerLabel.translatesAutoresizingMaskIntoConstraints = NO;
    [pill addSubview:_bannerLabel];

    _bannerCount = [[UILabel alloc] init];
    _bannerCount.textColor = UIColor.whiteColor;
    _bannerCount.font = [UIFont boldSystemFontOfSize:11];
    _bannerCount.hidden = YES;
    _bannerCount.translatesAutoresizingMaskIntoConstraints = NO;
    [pill addSubview:_bannerCount];

    [NSLayoutConstraint activateConstraints:@[
        [icon.leadingAnchor constraintEqualToAnchor:pill.leadingAnchor constant:14],
        [icon.centerYAnchor constraintEqualToAnchor:pill.centerYAnchor],

        [_bannerLabel.leadingAnchor constraintEqualToAnchor:icon.trailingAnchor constant:8],
        [_bannerLabel.centerYAnchor constraintEqualToAnchor:pill.centerYAnchor],
        [_bannerLabel.widthAnchor constraintLessThanOrEqualToConstant:240],

        [_bannerCount.leadingAnchor constraintEqualToAnchor:_bannerLabel.trailingAnchor constant:8],
        [_bannerCount.trailingAnchor constraintEqualToAnchor:pill.trailingAnchor constant:-14],
        [_bannerCount.centerYAnchor constraintEqualToAnchor:pill.centerYAnchor],

        [pill.heightAnchor constraintEqualToConstant:36],
    ]];

    UITapGestureRecognizer* tap = [[UITapGestureRecognizer alloc]
        initWithTarget:self action:@selector(onPillTap)];
    [pill addGestureRecognizer:tap];

    return pill;
}

- (void)updateBannerText:(BPSdkErrorEntry*)latest {
    if (!_bannerLabel || !latest) return;
    _bannerLabel.text = [NSString stringWithFormat:@"[%@] %@",
        latest.source ?: @"Bugpunch", latest.message ?: @""];

    NSUInteger total;
    [_ringLock lock]; total = _ring.count; [_ringLock unlock];
    if (total > 1) {
        _bannerCount.text = [NSString stringWithFormat:@"+%lu", (unsigned long)(total - 1)];
        _bannerCount.hidden = NO;
    } else if (latest.count > 1) {
        _bannerCount.text = [NSString stringWithFormat:@"×%ld", (long)latest.count];
        _bannerCount.hidden = NO;
    } else {
        _bannerCount.hidden = YES;
    }
}

- (void)scheduleAutoHide {
    [_hideTimer invalidate];
    _hideTimer = [NSTimer scheduledTimerWithTimeInterval:kBPAutoHideSec
                                                  target:self
                                                selector:@selector(autoHide)
                                                userInfo:nil
                                                 repeats:NO];
}

- (void)autoHide {
    if (_expanded) return;
    [self hideBanner];
}

- (void)hideBanner {
    [_hideTimer invalidate]; _hideTimer = nil;
    [_bannerView removeFromSuperview];
    _bannerView = nil;
    _bannerLabel = nil;
    _bannerCount = nil;
}

- (void)hideAll {
    if (_expanded) [self hideExpanded];
    [self hideBanner];
}

- (void)onPillTap { [self showExpanded]; }

// ─── Expanded card ─────────────────────────────────────────────────

- (void)showExpanded {
    if (_expanded) return;
    UIWindow* window = [self keyWindow];
    if (!window) return;
    _expanded = YES;
    [self hideBanner];

    UIView* backdrop = [[UIView alloc] initWithFrame:window.bounds];
    backdrop.backgroundColor = [UIColor colorWithWhite:0 alpha:0.8];
    backdrop.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
    UITapGestureRecognizer* dismiss = [[UITapGestureRecognizer alloc]
        initWithTarget:self action:@selector(hideExpanded)];
    [backdrop addGestureRecognizer:dismiss];

    UIScrollView* scroll = [[UIScrollView alloc] init];
    scroll.translatesAutoresizingMaskIntoConstraints = NO;
    [backdrop addSubview:scroll];

    UIView* card = [[UIView alloc] init];
    card.backgroundColor = [UIColor colorWithWhite:0.09 alpha:0.94];
    card.layer.cornerRadius = 12;
    card.layer.borderWidth = 1;
    card.layer.borderColor = [UIColor colorWithWhite:0.27 alpha:1].CGColor;
    card.translatesAutoresizingMaskIntoConstraints = NO;
    // Stop taps on the card from dismissing.
    UITapGestureRecognizer* swallow = [[UITapGestureRecognizer alloc] initWithTarget:self action:@selector(noop)];
    [card addGestureRecognizer:swallow];
    [scroll addSubview:card];

    UIStackView* stack = [[UIStackView alloc] init];
    stack.axis = UILayoutConstraintAxisVertical;
    stack.spacing = 6;
    stack.translatesAutoresizingMaskIntoConstraints = NO;
    [card addSubview:stack];

    UILabel* title = [[UILabel alloc] init];
    title.text = @"Bugpunch SDK errors";
    title.textColor = UIColor.whiteColor;
    title.font = [UIFont boldSystemFontOfSize:16];
    [stack addArrangedSubview:title];

    UILabel* subtitle = [[UILabel alloc] init];
    subtitle.text = @"Internal SDK problems. Tap dismiss to hide; toggle off via Bugpunch.SetSdkErrorOverlay(false).";
    subtitle.textColor = [UIColor colorWithWhite:0.72 alpha:1];
    subtitle.font = [UIFont systemFontOfSize:11];
    subtitle.numberOfLines = 0;
    [stack addArrangedSubview:subtitle];

    UIStackView* entriesCol = [[UIStackView alloc] init];
    entriesCol.axis = UILayoutConstraintAxisVertical;
    entriesCol.spacing = 8;
    entriesCol.tag = 0xBE001;
    [stack addArrangedSubview:entriesCol];

    [self populateEntries:entriesCol];

    UIStackView* footer = [[UIStackView alloc] init];
    footer.axis = UILayoutConstraintAxisHorizontal;
    footer.distribution = UIStackViewDistributionFillEqually;
    footer.spacing = 12;
    [stack addArrangedSubview:footer];

    UIButton* clear = [UIButton buttonWithType:UIButtonTypeSystem];
    [clear setTitle:@"Clear" forState:UIControlStateNormal];
    [clear setTitleColor:[UIColor colorWithWhite:0.6 alpha:1] forState:UIControlStateNormal];
    [clear addTarget:self action:@selector(onClearTap) forControlEvents:UIControlEventTouchUpInside];
    [footer addArrangedSubview:clear];

    UIButton* dismissBtn = [UIButton buttonWithType:UIButtonTypeSystem];
    [dismissBtn setTitle:@"Dismiss" forState:UIControlStateNormal];
    [dismissBtn setTitleColor:UIColor.whiteColor forState:UIControlStateNormal];
    dismissBtn.titleLabel.font = [UIFont boldSystemFontOfSize:13];
    [dismissBtn addTarget:self action:@selector(hideExpanded) forControlEvents:UIControlEventTouchUpInside];
    [footer addArrangedSubview:dismissBtn];

    [NSLayoutConstraint activateConstraints:@[
        [scroll.topAnchor constraintEqualToAnchor:backdrop.safeAreaLayoutGuide.topAnchor constant:24],
        [scroll.leadingAnchor constraintEqualToAnchor:backdrop.leadingAnchor],
        [scroll.trailingAnchor constraintEqualToAnchor:backdrop.trailingAnchor],
        [scroll.bottomAnchor constraintEqualToAnchor:backdrop.safeAreaLayoutGuide.bottomAnchor constant:-24],

        [card.topAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.topAnchor],
        [card.bottomAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.bottomAnchor],
        [card.centerXAnchor constraintEqualToAnchor:scroll.centerXAnchor],
        [card.widthAnchor constraintLessThanOrEqualToConstant:360],
        [card.widthAnchor constraintEqualToAnchor:scroll.frameLayoutGuide.widthAnchor constant:-32],

        [stack.topAnchor constraintEqualToAnchor:card.topAnchor constant:16],
        [stack.bottomAnchor constraintEqualToAnchor:card.bottomAnchor constant:-16],
        [stack.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:16],
        [stack.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-16],
    ]];

    [window addSubview:backdrop];
    _expandedView = backdrop;
}

- (void)noop {}

- (void)onClearTap {
    [_ringLock lock]; [_ring removeAllObjects]; [_ringLock unlock];
    [self hideExpanded];
}

- (void)rebuildExpanded {
    if (!_expandedView) return;
    UIStackView* col = (UIStackView*)[_expandedView viewWithTag:0xBE001];
    if (!col) return;
    for (UIView* v in [col.arrangedSubviews copy]) {
        [col removeArrangedSubview:v];
        [v removeFromSuperview];
    }
    [self populateEntries:col];
}

- (void)populateEntries:(UIStackView*)col {
    NSArray<BPSdkErrorEntry*>* snap = [self snapshot];
    if (snap.count == 0) {
        UILabel* empty = [[UILabel alloc] init];
        empty.text = @"No SDK errors recorded.";
        empty.textColor = [UIColor colorWithWhite:0.72 alpha:1];
        empty.font = [UIFont systemFontOfSize:12];
        [col addArrangedSubview:empty];
        return;
    }
    for (BPSdkErrorEntry* e in snap) {
        UIStackView* row = [[UIStackView alloc] init];
        row.axis = UILayoutConstraintAxisVertical;
        row.spacing = 2;

        UILabel* head = [[UILabel alloc] init];
        NSString* suffix = e.count > 1 ? [NSString stringWithFormat:@"  ×%ld", (long)e.count] : @"";
        head.text = [NSString stringWithFormat:@"[%@] %@%@", e.source, e.message, suffix];
        head.textColor = [UIColor colorWithRed:1 green:0.42 blue:0.42 alpha:1];
        head.font = [UIFont boldSystemFontOfSize:12];
        head.numberOfLines = 0;
        [row addArrangedSubview:head];

        if (e.stack.length) {
            UILabel* trace = [[UILabel alloc] init];
            trace.text = e.stack;
            trace.textColor = [UIColor colorWithWhite:0.82 alpha:1];
            trace.font = [UIFont fontWithName:@"Menlo" size:10] ?: [UIFont systemFontOfSize:10];
            trace.numberOfLines = 8;
            [row addArrangedSubview:trace];
        }

        UIView* divider = [[UIView alloc] init];
        divider.backgroundColor = [UIColor colorWithWhite:0.2 alpha:1];
        [divider.heightAnchor constraintEqualToConstant:1].active = YES;
        [col addArrangedSubview:row];
        [col addArrangedSubview:divider];
    }
}

- (void)hideExpanded {
    _expanded = NO;
    [_expandedView removeFromSuperview];
    _expandedView = nil;
}

@end

// ─── C entry points (called from BugpunchNative.cs P-Invoke) ────────

extern "C" void Bugpunch_ReportSdkError(const char* source, const char* message, const char* stack) {
    NSString* src = source ? [NSString stringWithUTF8String:source] : @"";
    NSString* msg = message ? [NSString stringWithUTF8String:message] : @"";
    NSString* st  = stack   ? [NSString stringWithUTF8String:stack]   : @"";
    [[BPSdkErrorOverlay shared] reportSource:src message:msg stack:st];
}

extern "C" void Bugpunch_SetSdkErrorOverlay(int enabled) {
    [[BPSdkErrorOverlay shared] setOverlayEnabled:enabled != 0];
}

// Native-side helper for in-process Obj-C catch blocks.
extern "C" void BPSdkError_Report(const char* source, const char* message, const char* stack) {
    Bugpunch_ReportSdkError(source, message, stack);
}
