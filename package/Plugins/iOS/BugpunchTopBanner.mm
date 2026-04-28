// BugpunchTopBanner.mm — top-of-screen status pills for the SDK.
//
// Two singletons share this file:
//   • Upload banner   — non-interactive, auto-hide, surfaces crash-upload
//                       progress while the queue drains. Called from
//                       BugpunchUploader.mm around BPDrainQueueSync.
//   • Chat banner     — persistent, clickable, with X. Surfaces unread QA
//                       chat messages. Driven from C# BugpunchClient via
//                       Bugpunch_TopBanner_ShowChat / _HideChat.
//
// Same visual language as the Android BugpunchUploadStatusBanner /
// BugpunchChatBanner pair. Banners stack vertically (upload at top,
// chat just below) so both can be visible at once if the player gets a
// QA message while a crash report is uploading.
//
// Thread-safe: every UI mutation hops to main.

#import <UIKit/UIKit.h>

extern "C" {
void UnitySendMessage(const char* obj, const char* method, const char* msg);
}

// ── Shared helpers ────────────────────────────────────────────────────────

static UIWindow* BPTB_KeyWindow(void) {
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
    return UIApplication.sharedApplication.keyWindow;
}

static CGFloat BPTB_TopInset(UIWindow* window) {
    CGFloat top = 8;
    if (@available(iOS 11, *)) top += window.safeAreaInsets.top;
    return top;
}

// ── Upload banner ─────────────────────────────────────────────────────────

@interface BPUploadBanner : NSObject
+ (instancetype)shared;
- (void)showOrUpdate:(NSInteger)pending;
- (void)hide;
@end

@implementation BPUploadBanner {
    UIView* _pill;
    UILabel* _label;
    UIActivityIndicatorView* _spinner;
}

+ (instancetype)shared {
    static BPUploadBanner* s; static dispatch_once_t once;
    dispatch_once(&once, ^{ s = [BPUploadBanner new]; });
    return s;
}

- (void)showOrUpdate:(NSInteger)pending {
    dispatch_async(dispatch_get_main_queue(), ^{ [self showOrUpdateOnMain:pending]; });
}

- (void)hide {
    dispatch_async(dispatch_get_main_queue(), ^{ [self hideOnMain]; });
}

- (void)showOrUpdateOnMain:(NSInteger)pending {
    @try {
        UIWindow* window = BPTB_KeyWindow();
        if (!window) return;

        if (!_pill) {
            _pill = [self buildPill];
            [window addSubview:_pill];
            [self position];
            _pill.alpha = 0;
            _pill.transform = CGAffineTransformMakeTranslation(0, -16);
            [UIView animateWithDuration:0.18 animations:^{
                self->_pill.alpha = 1;
                self->_pill.transform = CGAffineTransformIdentity;
            }];
        } else if (_pill.window != window) {
            // Window swap — re-attach.
            [_pill removeFromSuperview];
            [window addSubview:_pill];
            [self position];
        }
        _label.text = [self buildLabel:pending];
        [self position];
    } @catch (NSException* e) {
        NSLog(@"[Bugpunch.UploadBanner] showOrUpdate exception: %@", e);
    }
}

- (void)hideOnMain {
    if (!_pill) return;
    UIView* p = _pill;
    [UIView animateWithDuration:0.18
        animations:^{
            p.alpha = 0;
            p.transform = CGAffineTransformMakeTranslation(0, -16);
        }
        completion:^(BOOL done) {
            [p removeFromSuperview];
        }];
    _pill = nil;
    _label = nil;
    _spinner = nil;
}

- (UIView*)buildPill {
    UIView* pill = [[UIView alloc] init];
    pill.backgroundColor = [UIColor colorWithRed:0.094 green:0.094 blue:0.106 alpha:0.9];
    pill.layer.cornerRadius = 18;
    pill.layer.borderWidth = 1;
    pill.layer.borderColor = [UIColor colorWithWhite:1 alpha:0.25].CGColor;
    pill.layer.shadowColor = UIColor.blackColor.CGColor;
    pill.layer.shadowOpacity = 0.3;
    pill.layer.shadowRadius = 6;
    pill.layer.shadowOffset = CGSizeMake(0, 2);
    pill.translatesAutoresizingMaskIntoConstraints = NO;

    _spinner = [[UIActivityIndicatorView alloc]
        initWithActivityIndicatorStyle:UIActivityIndicatorViewStyleWhite];
    _spinner.translatesAutoresizingMaskIntoConstraints = NO;
    [_spinner startAnimating];
    [pill addSubview:_spinner];

    _label = [[UILabel alloc] init];
    _label.textColor = UIColor.whiteColor;
    _label.font = [UIFont systemFontOfSize:13];
    _label.text = @"Sending crash report…";
    _label.translatesAutoresizingMaskIntoConstraints = NO;
    [pill addSubview:_label];

    [NSLayoutConstraint activateConstraints:@[
        [_spinner.leadingAnchor constraintEqualToAnchor:pill.leadingAnchor constant:12],
        [_spinner.centerYAnchor constraintEqualToAnchor:pill.centerYAnchor],
        [_label.leadingAnchor constraintEqualToAnchor:_spinner.trailingAnchor constant:10],
        [_label.trailingAnchor constraintEqualToAnchor:pill.trailingAnchor constant:-12],
        [_label.centerYAnchor constraintEqualToAnchor:pill.centerYAnchor],
        [pill.heightAnchor constraintEqualToConstant:36],
    ]];
    return pill;
}

- (NSString*)buildLabel:(NSInteger)pending {
    if (pending <= 0) return @"Crash report sent";
    if (pending == 1) return @"Sending crash report…";
    return [NSString stringWithFormat:@"Sending crash reports (%ld)…", (long)pending];
}

- (void)position {
    UIWindow* window = BPTB_KeyWindow();
    if (!window || !_pill) return;
    CGSize sz = [_pill systemLayoutSizeFittingSize:UILayoutFittingCompressedSize];
    CGFloat top = BPTB_TopInset(window) + 16;
    _pill.frame = CGRectMake((window.bounds.size.width - sz.width) / 2, top,
                             sz.width, sz.height);
}

@end

// ── Chat banner ───────────────────────────────────────────────────────────

@interface BPChatBanner : NSObject
+ (instancetype)shared;
- (void)showOrUpdate:(NSInteger)unread;
- (void)hide;
@end

@implementation BPChatBanner {
    UIView* _pill;
    UILabel* _label;
}

+ (instancetype)shared {
    static BPChatBanner* s; static dispatch_once_t once;
    dispatch_once(&once, ^{ s = [BPChatBanner new]; });
    return s;
}

- (void)showOrUpdate:(NSInteger)unread {
    dispatch_async(dispatch_get_main_queue(), ^{ [self showOrUpdateOnMain:unread]; });
}

- (void)hide {
    dispatch_async(dispatch_get_main_queue(), ^{ [self hideOnMain]; });
}

- (void)showOrUpdateOnMain:(NSInteger)unread {
    @try {
        UIWindow* window = BPTB_KeyWindow();
        if (!window) return;

        if (!_pill) {
            _pill = [self buildPill];
            [window addSubview:_pill];
            [self position];
            _pill.alpha = 0;
            _pill.transform = CGAffineTransformMakeTranslation(0, -16);
            [UIView animateWithDuration:0.18 animations:^{
                self->_pill.alpha = 1;
                self->_pill.transform = CGAffineTransformIdentity;
            }];
        } else if (_pill.window != window) {
            [_pill removeFromSuperview];
            [window addSubview:_pill];
            [self position];
        }
        _label.text = [self buildLabel:unread];
        [self position];
    } @catch (NSException* e) {
        NSLog(@"[Bugpunch.ChatBanner] showOrUpdate exception: %@", e);
    }
}

- (void)hideOnMain {
    if (!_pill) return;
    UIView* p = _pill;
    [UIView animateWithDuration:0.18
        animations:^{
            p.alpha = 0;
            p.transform = CGAffineTransformMakeTranslation(0, -16);
        }
        completion:^(BOOL done) {
            [p removeFromSuperview];
        }];
    _pill = nil;
    _label = nil;
}

- (UIView*)buildPill {
    // Drop-down notification card. Wider + taller than the old slim pill,
    // with a primary heading + sub-line so the player notices. Has *no*
    // dismiss button — the only way to clear it is to tap (which opens
    // chat and marks the messages read). This matches the request that
    // the notification "needs to be interacted with to get rid of".
    UIView* pill = [[UIView alloc] init];
    pill.backgroundColor = [UIColor colorWithRed:0.122 green:0.306 blue:0.475 alpha:0.96];
    pill.layer.cornerRadius = 14;
    pill.layer.borderWidth = 1;
    pill.layer.borderColor = [UIColor colorWithWhite:1 alpha:0.40].CGColor;
    pill.layer.shadowColor = UIColor.blackColor.CGColor;
    pill.layer.shadowOpacity = 0.45;
    pill.layer.shadowRadius = 10;
    pill.layer.shadowOffset = CGSizeMake(0, 4);
    pill.translatesAutoresizingMaskIntoConstraints = NO;
    pill.userInteractionEnabled = YES;

    UILabel* icon = [[UILabel alloc] init];
    icon.text = @"\U0001F4AC"; // 💬
    icon.font = [UIFont systemFontOfSize:24];
    icon.translatesAutoresizingMaskIntoConstraints = NO;
    [pill addSubview:icon];

    _label = [[UILabel alloc] init];
    _label.textColor = UIColor.whiteColor;
    _label.font = [UIFont boldSystemFontOfSize:15];
    _label.text = @"New message from a dev";
    _label.numberOfLines = 1;
    _label.translatesAutoresizingMaskIntoConstraints = NO;
    [pill addSubview:_label];

    UILabel* sub = [[UILabel alloc] init];
    sub.textColor = [UIColor colorWithWhite:1 alpha:0.75];
    sub.font = [UIFont systemFontOfSize:12];
    sub.text = @"Tap to read";
    sub.translatesAutoresizingMaskIntoConstraints = NO;
    [pill addSubview:sub];

    UITapGestureRecognizer* tap = [[UITapGestureRecognizer alloc]
        initWithTarget:self action:@selector(onPillTap)];
    [pill addGestureRecognizer:tap];

    [NSLayoutConstraint activateConstraints:@[
        [icon.leadingAnchor constraintEqualToAnchor:pill.leadingAnchor constant:16],
        [icon.centerYAnchor constraintEqualToAnchor:pill.centerYAnchor],
        [icon.widthAnchor constraintEqualToConstant:28],

        [_label.leadingAnchor constraintEqualToAnchor:icon.trailingAnchor constant:12],
        [_label.trailingAnchor constraintEqualToAnchor:pill.trailingAnchor constant:-16],
        [_label.topAnchor constraintEqualToAnchor:pill.topAnchor constant:10],

        [sub.leadingAnchor constraintEqualToAnchor:_label.leadingAnchor],
        [sub.trailingAnchor constraintEqualToAnchor:_label.trailingAnchor],
        [sub.topAnchor constraintEqualToAnchor:_label.bottomAnchor constant:2],
        [sub.bottomAnchor constraintEqualToAnchor:pill.bottomAnchor constant:-10],
    ]];
    return pill;
}

- (NSString*)buildLabel:(NSInteger)unread {
    if (unread <= 1) return @"New message from a dev";
    return [NSString stringWithFormat:@"%ld new messages from a dev", (long)unread];
}

- (void)position {
    UIWindow* window = BPTB_KeyWindow();
    if (!window || !_pill) return;
    // Drop-down card spans most of the screen width — capped at 420 px on
    // iPad so it doesn't stretch ridiculously, and indented from the
    // edges by 16 pt on phones.
    CGFloat winW = window.bounds.size.width;
    CGFloat width = MIN(420, winW - 32);
    CGSize sz = [_pill systemLayoutSizeFittingSize:CGSizeMake(width, UILayoutFittingCompressedSize.height)
                          withHorizontalFittingPriority:UILayoutPriorityRequired
                                verticalFittingPriority:UILayoutPriorityFittingSizeLevel];
    // Stack below the upload banner (which sits at top+16 with ~36 height + 8 gap).
    CGFloat top = BPTB_TopInset(window) + 16 + 36 + 8;
    _pill.frame = CGRectMake((winW - width) / 2, top, width, sz.height);
}

- (void)onPillTap {
    UnitySendMessage("BugpunchClient", "OnChatBannerOpened", "");
    // Open the chat board (defined in BugpunchReportOverlay.mm).
    extern void Bugpunch_ShowChatBoard(void);
    Bugpunch_ShowChatBoard();
    [self hide];
}

@end

// ── C entry points ────────────────────────────────────────────────────────

extern "C" {

void Bugpunch_TopBanner_ShowUpload(int pending) {
    [[BPUploadBanner shared] showOrUpdate:pending];
}

void Bugpunch_TopBanner_HideUpload(void) {
    [[BPUploadBanner shared] hide];
}

void Bugpunch_TopBanner_ShowChat(int unread) {
    [[BPChatBanner shared] showOrUpdate:unread];
}

void Bugpunch_TopBanner_HideChat(void) {
    [[BPChatBanner shared] hide];
}

}
