// BugpunchConsentSheet.mm — custom debug-recording consent sheet.
//
// Replaces the stock UIAlertController with a branded card over a blurred
// background: Bugpunch logo mark, title, body, primary/secondary buttons.

#import <UIKit/UIKit.h>
#import <Foundation/Foundation.h>

#import "BugpunchTheme.h"
#import "BugpunchStrings.h"

@interface BPConsentViewController : UIViewController
@property (nonatomic, copy) void (^onStart)(void);
@end

@implementation BPConsentViewController

- (void)viewDidLoad {
    [super viewDidLoad];
    self.view.backgroundColor = [UIColor colorWithWhite:0 alpha:0.55];

    // Blurred backdrop so the underlying game shows through softly.
    UIVisualEffectView* blur = [[UIVisualEffectView alloc] initWithEffect:
        [UIBlurEffect effectWithStyle:UIBlurEffectStyleSystemUltraThinMaterialDark]];
    blur.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view insertSubview:blur atIndex:0];

    UIView* card = [UIView new];
    card.backgroundColor = [BPTheme color:@"cardBackground"
        fallback:[UIColor colorWithRed:0x14/255.0 green:0x18/255.0 blue:0x20/255.0 alpha:1]];
    card.layer.cornerRadius = [BPTheme radius:@"cardRadius" fallback:20];
    card.layer.borderWidth = 1;
    card.layer.borderColor = [BPTheme color:@"cardBorder"
        fallback:[UIColor colorWithRed:0x2a/255.0 green:0x32/255.0 blue:0x40/255.0 alpha:1]].CGColor;
    card.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:card];

    UILabel* title = [UILabel new];
    title.text = [BPStrings text:@"consentTitle" fallback:@"Enable debug recording"];
    title.textColor = [BPTheme color:@"textPrimary" fallback:UIColor.whiteColor];
    title.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeTitle" fallback:20]];
    title.textAlignment = NSTextAlignmentCenter;
    title.translatesAutoresizingMaskIntoConstraints = NO;
    [card addSubview:title];

    UILabel* body = [UILabel new];
    body.text = [BPStrings text:@"consentBody"
        fallback:@"Your screen will be recorded so bug reports can include the moments "
                  "leading up to an issue. Recording stays on your device until you submit a report."];
    body.textColor = [BPTheme color:@"textSecondary"
        fallback:[UIColor colorWithRed:0xa8/255.0 green:0xb2/255.0 blue:0xbf/255.0 alpha:1]];
    body.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
    body.textAlignment = NSTextAlignmentCenter;
    body.numberOfLines = 0;
    body.translatesAutoresizingMaskIntoConstraints = NO;
    [card addSubview:body];

    UIButton* start = [UIButton buttonWithType:UIButtonTypeSystem];
    [start setTitle:[BPStrings text:@"consentStart" fallback:@"Start Recording"]
           forState:UIControlStateNormal];
    [start setTitleColor:[BPTheme color:@"textPrimary" fallback:UIColor.whiteColor]
                forState:UIControlStateNormal];
    start.titleLabel.font = [UIFont boldSystemFontOfSize:15];
    start.backgroundColor = [BPTheme color:@"accentPrimary"
        fallback:[UIColor colorWithRed:0x2a/255.0 green:0x7b/255.0 blue:0xe0/255.0 alpha:1]];
    start.layer.cornerRadius = [BPTheme radius:@"cardRadius" fallback:12];
    start.translatesAutoresizingMaskIntoConstraints = NO;
    [start addTarget:self action:@selector(onStartTap) forControlEvents:UIControlEventTouchUpInside];
    [card addSubview:start];

    UIButton* cancel = [UIButton buttonWithType:UIButtonTypeSystem];
    [cancel setTitle:[BPStrings text:@"consentCancel" fallback:@"Not now"]
            forState:UIControlStateNormal];
    [cancel setTitleColor:[BPTheme color:@"textSecondary"
        fallback:[UIColor colorWithRed:0xa8/255.0 green:0xb2/255.0 blue:0xbf/255.0 alpha:1]]
                 forState:UIControlStateNormal];
    cancel.titleLabel.font = [UIFont boldSystemFontOfSize:15];
    cancel.layer.cornerRadius = [BPTheme radius:@"cardRadius" fallback:12];
    cancel.translatesAutoresizingMaskIntoConstraints = NO;
    [cancel addTarget:self action:@selector(onCancelTap) forControlEvents:UIControlEventTouchUpInside];
    [card addSubview:cancel];

    [NSLayoutConstraint activateConstraints:@[
        [blur.topAnchor constraintEqualToAnchor:self.view.topAnchor],
        [blur.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor],
        [blur.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor],
        [blur.bottomAnchor constraintEqualToAnchor:self.view.bottomAnchor],

        [card.centerXAnchor constraintEqualToAnchor:self.view.centerXAnchor],
        [card.centerYAnchor constraintEqualToAnchor:self.view.centerYAnchor],
        [card.widthAnchor constraintEqualToConstant:340],

        [title.topAnchor constraintEqualToAnchor:card.topAnchor constant:32],
        [title.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:28],
        [title.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-28],

        [body.topAnchor constraintEqualToAnchor:title.bottomAnchor constant:10],
        [body.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:28],
        [body.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-28],

        [start.topAnchor constraintEqualToAnchor:body.bottomAnchor constant:28],
        [start.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:28],
        [start.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-28],
        [start.heightAnchor constraintEqualToConstant:48],

        [cancel.topAnchor constraintEqualToAnchor:start.bottomAnchor constant:10],
        [cancel.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:28],
        [cancel.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-28],
        [cancel.heightAnchor constraintEqualToConstant:48],
        [cancel.bottomAnchor constraintEqualToAnchor:card.bottomAnchor constant:-32],
    ]];
}

- (void)onStartTap {
    void (^cb)(void) = self.onStart;
    [self dismissViewControllerAnimated:YES completion:^{
        if (cb) cb();
    }];
}

- (void)onCancelTap {
    [self dismissViewControllerAnimated:YES completion:nil];
}

@end

extern "C" void Bugpunch_PresentConsentSheet(void (^onStart)(void)) {
    dispatch_async(dispatch_get_main_queue(), ^{
        BPConsentViewController* vc = [BPConsentViewController new];
        vc.onStart = onStart;
        vc.modalPresentationStyle = UIModalPresentationOverFullScreen;
        vc.modalTransitionStyle = UIModalTransitionStyleCrossDissolve;

        UIViewController* top = nil;
        if (@available(iOS 13.0, *)) {
            for (UIScene* scene in UIApplication.sharedApplication.connectedScenes) {
                if (![scene isKindOfClass:[UIWindowScene class]]) continue;
                for (UIWindow* w in ((UIWindowScene*)scene).windows) {
                    if (w.isKeyWindow) { top = w.rootViewController; break; }
                }
                if (top) break;
            }
        }
        if (!top) {
            #pragma clang diagnostic push
            #pragma clang diagnostic ignored "-Wdeprecated-declarations"
            top = UIApplication.sharedApplication.keyWindow.rootViewController;
            #pragma clang diagnostic pop
        }
        while (top.presentedViewController) top = top.presentedViewController;
        if (top) [top presentViewController:vc animated:YES completion:nil];
    });
}
