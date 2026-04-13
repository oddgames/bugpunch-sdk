#import <UIKit/UIKit.h>
#import <AVFoundation/AVFoundation.h>

typedef void (*PermissionCallback)(int result);
typedef void (*BugReportSubmitCallback)(const char* title, const char* description, const char* severity);
typedef void (*BugReportCancelCallback)(void);
typedef void (*CrashReportSubmitCallback)(const char* title, const char* description, const char* severity, int includeVideo, int includeLogs);
typedef void (*CrashReportDismissCallback)(void);

// ─── Crash Report View Controller ────────────────────────────────

@interface BugpunchCrashReportVC : UIViewController
@property (nonatomic, copy) NSString* exceptionMessage;
@property (nonatomic, copy) NSString* stackTrace;
@property (nonatomic, copy) NSString* videoPath;
@property (nonatomic, assign) CrashReportSubmitCallback onSubmit;
@property (nonatomic, assign) CrashReportDismissCallback onDismiss;
@end

@implementation BugpunchCrashReportVC {
    AVPlayer* _player;
    AVPlayerLayer* _playerLayer;
    UIView* _videoContainer;
    UITextField* _titleField;
    UITextView* _descField;
    UISegmentedControl* _sevControl;
    UISwitch* _videoSwitch;
    UISwitch* _logsSwitch;
    UITextView* _stackView;
}

- (void)viewDidLoad {
    [super viewDidLoad];
    self.view.backgroundColor = [[UIColor blackColor] colorWithAlphaComponent:0.92];

    UIScrollView* scroll = [[UIScrollView alloc] initWithFrame:self.view.bounds];
    scroll.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
    scroll.alwaysBounceVertical = YES;
    [self.view addSubview:scroll];

    UIView* content = [[UIView alloc] init];
    content.translatesAutoresizingMaskIntoConstraints = NO;
    [scroll addSubview:content];

    [NSLayoutConstraint activateConstraints:@[
        [content.topAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.topAnchor],
        [content.leadingAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.leadingAnchor],
        [content.trailingAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.trailingAnchor],
        [content.bottomAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.bottomAnchor],
        [content.widthAnchor constraintEqualToAnchor:scroll.frameLayoutGuide.widthAnchor],
    ]];

    CGFloat pad = 16;
    CGFloat y = 0;

    // ── Header ──
    UILabel* header = [[UILabel alloc] init];
    header.text = @"Crash Report";
    header.font = [UIFont boldSystemFontOfSize:22];
    header.textColor = [UIColor colorWithRed:1 green:0.33 blue:0.33 alpha:1];
    header.translatesAutoresizingMaskIntoConstraints = NO;
    [content addSubview:header];

    // ── Video player ──
    BOOL hasVideo = self.videoPath.length > 0 && [[NSFileManager defaultManager] fileExistsAtPath:self.videoPath];
    _videoContainer = [[UIView alloc] init];
    _videoContainer.backgroundColor = [UIColor blackColor];
    _videoContainer.translatesAutoresizingMaskIntoConstraints = NO;
    _videoContainer.hidden = !hasVideo;
    [content addSubview:_videoContainer];

    if (hasVideo) {
        NSURL* url = [NSURL fileURLWithPath:self.videoPath];
        _player = [AVPlayer playerWithURL:url];
        _playerLayer = [AVPlayerLayer playerLayerWithPlayer:_player];
        _playerLayer.videoGravity = AVLayerVideoGravityResizeAspect;
        [_videoContainer.layer addSublayer:_playerLayer];

        // Loop playback
        [[NSNotificationCenter defaultCenter] addObserver:self
                                                 selector:@selector(playerDidFinish:)
                                                     name:AVPlayerItemDidPlayToEndTimeNotification
                                                   object:_player.currentItem];
        [_player play];
    }

    // ── Exception label ──
    UILabel* exLabel = [[UILabel alloc] init];
    exLabel.text = self.exceptionMessage;
    exLabel.font = [UIFont boldSystemFontOfSize:14];
    exLabel.textColor = [UIColor colorWithRed:1 green:0.53 blue:0.53 alpha:1];
    exLabel.numberOfLines = 3;
    exLabel.translatesAutoresizingMaskIntoConstraints = NO;
    [content addSubview:exLabel];

    // ── Stack trace ──
    UILabel* stackLabel = [[UILabel alloc] init];
    stackLabel.text = @"Stack Trace:";
    stackLabel.font = [UIFont systemFontOfSize:12];
    stackLabel.textColor = [UIColor colorWithWhite:0.67 alpha:1];
    stackLabel.translatesAutoresizingMaskIntoConstraints = NO;
    [content addSubview:stackLabel];

    _stackView = [[UITextView alloc] init];
    _stackView.text = self.stackTrace;
    _stackView.font = [UIFont fontWithName:@"Menlo" size:11];
    _stackView.textColor = [UIColor colorWithWhite:0.8 alpha:1];
    _stackView.backgroundColor = [UIColor colorWithWhite:0.1 alpha:1];
    _stackView.editable = NO;
    _stackView.layer.cornerRadius = 6;
    _stackView.translatesAutoresizingMaskIntoConstraints = NO;
    [content addSubview:_stackView];

    // ── Title field ──
    UILabel* titleLabel = [self makeLabel:@"Title"];
    [content addSubview:titleLabel];

    _titleField = [[UITextField alloc] init];
    _titleField.placeholder = @"Brief description of the issue";
    _titleField.textColor = [UIColor whiteColor];
    _titleField.backgroundColor = [UIColor colorWithWhite:0.17 alpha:1];
    _titleField.layer.cornerRadius = 6;
    _titleField.leftView = [[UIView alloc] initWithFrame:CGRectMake(0, 0, 8, 0)];
    _titleField.leftViewMode = UITextFieldViewModeAlways;
    _titleField.translatesAutoresizingMaskIntoConstraints = NO;
    _titleField.attributedPlaceholder = [[NSAttributedString alloc] initWithString:_titleField.placeholder
        attributes:@{NSForegroundColorAttributeName: [UIColor colorWithWhite:0.4 alpha:1]}];
    // Pre-fill from exception
    if (self.exceptionMessage.length > 0) {
        _titleField.text = self.exceptionMessage.length > 120
            ? [self.exceptionMessage substringToIndex:120]
            : self.exceptionMessage;
    }
    [content addSubview:_titleField];

    // ── Description field ──
    UILabel* descLabel = [self makeLabel:@"Description (what were you doing?)"];
    [content addSubview:descLabel];

    _descField = [[UITextView alloc] init];
    _descField.font = [UIFont systemFontOfSize:15];
    _descField.textColor = [UIColor whiteColor];
    _descField.backgroundColor = [UIColor colorWithWhite:0.17 alpha:1];
    _descField.layer.cornerRadius = 6;
    _descField.translatesAutoresizingMaskIntoConstraints = NO;
    [content addSubview:_descField];

    // ── Severity control ──
    UILabel* sevLabel = [self makeLabel:@"Severity"];
    [content addSubview:sevLabel];

    _sevControl = [[UISegmentedControl alloc] initWithItems:@[@"Low", @"Medium", @"High", @"Critical"]];
    _sevControl.selectedSegmentIndex = 2; // High by default for crashes
    _sevControl.translatesAutoresizingMaskIntoConstraints = NO;
    [content addSubview:_sevControl];

    // ── Toggles ──
    UIView* toggleRow = [[UIView alloc] init];
    toggleRow.translatesAutoresizingMaskIntoConstraints = NO;
    [content addSubview:toggleRow];

    UILabel* vidLabel = [self makeLabel:@"Include video"];
    vidLabel.translatesAutoresizingMaskIntoConstraints = NO;
    [toggleRow addSubview:vidLabel];

    _videoSwitch = [[UISwitch alloc] init];
    _videoSwitch.on = hasVideo;
    _videoSwitch.enabled = hasVideo;
    _videoSwitch.translatesAutoresizingMaskIntoConstraints = NO;
    [toggleRow addSubview:_videoSwitch];

    UILabel* logLabel = [self makeLabel:@"Include logs"];
    logLabel.translatesAutoresizingMaskIntoConstraints = NO;
    [toggleRow addSubview:logLabel];

    _logsSwitch = [[UISwitch alloc] init];
    _logsSwitch.on = YES;
    _logsSwitch.translatesAutoresizingMaskIntoConstraints = NO;
    [toggleRow addSubview:_logsSwitch];

    // ── Buttons ──
    UIButton* dismissBtn = [UIButton buttonWithType:UIButtonTypeSystem];
    [dismissBtn setTitle:@"Dismiss" forState:UIControlStateNormal];
    [dismissBtn setTitleColor:[UIColor whiteColor] forState:UIControlStateNormal];
    dismissBtn.backgroundColor = [UIColor colorWithWhite:0.27 alpha:1];
    dismissBtn.layer.cornerRadius = 8;
    dismissBtn.translatesAutoresizingMaskIntoConstraints = NO;
    [dismissBtn addTarget:self action:@selector(onDismissTapped) forControlEvents:UIControlEventTouchUpInside];
    [content addSubview:dismissBtn];

    UIButton* submitBtn = [UIButton buttonWithType:UIButtonTypeSystem];
    [submitBtn setTitle:@"Submit Report" forState:UIControlStateNormal];
    [submitBtn setTitleColor:[UIColor whiteColor] forState:UIControlStateNormal];
    submitBtn.backgroundColor = [UIColor colorWithRed:0.18 green:0.49 blue:0.20 alpha:1];
    submitBtn.layer.cornerRadius = 8;
    submitBtn.titleLabel.font = [UIFont boldSystemFontOfSize:16];
    submitBtn.translatesAutoresizingMaskIntoConstraints = NO;
    [submitBtn addTarget:self action:@selector(onSubmitTapped) forControlEvents:UIControlEventTouchUpInside];
    [content addSubview:submitBtn];

    // ── Auto Layout ──
    CGFloat videoH = hasVideo ? 180 : 0;
    [NSLayoutConstraint activateConstraints:@[
        // Header
        [header.topAnchor constraintEqualToAnchor:content.topAnchor constant:pad * 1.5],
        [header.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:pad],
        [header.trailingAnchor constraintEqualToAnchor:content.trailingAnchor constant:-pad],

        // Video
        [_videoContainer.topAnchor constraintEqualToAnchor:header.bottomAnchor constant:pad],
        [_videoContainer.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:pad],
        [_videoContainer.trailingAnchor constraintEqualToAnchor:content.trailingAnchor constant:-pad],
        [_videoContainer.heightAnchor constraintEqualToConstant:videoH],

        // Exception
        [exLabel.topAnchor constraintEqualToAnchor:_videoContainer.bottomAnchor constant:pad],
        [exLabel.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:pad],
        [exLabel.trailingAnchor constraintEqualToAnchor:content.trailingAnchor constant:-pad],

        // Stack label
        [stackLabel.topAnchor constraintEqualToAnchor:exLabel.bottomAnchor constant:8],
        [stackLabel.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:pad],

        // Stack trace
        [_stackView.topAnchor constraintEqualToAnchor:stackLabel.bottomAnchor constant:4],
        [_stackView.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:pad],
        [_stackView.trailingAnchor constraintEqualToAnchor:content.trailingAnchor constant:-pad],
        [_stackView.heightAnchor constraintEqualToConstant:120],

        // Title label
        [titleLabel.topAnchor constraintEqualToAnchor:_stackView.bottomAnchor constant:pad],
        [titleLabel.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:pad],

        // Title field
        [_titleField.topAnchor constraintEqualToAnchor:titleLabel.bottomAnchor constant:4],
        [_titleField.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:pad],
        [_titleField.trailingAnchor constraintEqualToAnchor:content.trailingAnchor constant:-pad],
        [_titleField.heightAnchor constraintEqualToConstant:40],

        // Desc label
        [descLabel.topAnchor constraintEqualToAnchor:_titleField.bottomAnchor constant:pad],
        [descLabel.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:pad],

        // Desc field
        [_descField.topAnchor constraintEqualToAnchor:descLabel.bottomAnchor constant:4],
        [_descField.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:pad],
        [_descField.trailingAnchor constraintEqualToAnchor:content.trailingAnchor constant:-pad],
        [_descField.heightAnchor constraintEqualToConstant:80],

        // Severity label
        [sevLabel.topAnchor constraintEqualToAnchor:_descField.bottomAnchor constant:pad],
        [sevLabel.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:pad],

        // Severity control
        [_sevControl.topAnchor constraintEqualToAnchor:sevLabel.bottomAnchor constant:4],
        [_sevControl.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:pad],
        [_sevControl.trailingAnchor constraintEqualToAnchor:content.trailingAnchor constant:-pad],

        // Toggle row
        [toggleRow.topAnchor constraintEqualToAnchor:_sevControl.bottomAnchor constant:pad],
        [toggleRow.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:pad],
        [toggleRow.trailingAnchor constraintEqualToAnchor:content.trailingAnchor constant:-pad],
        [toggleRow.heightAnchor constraintEqualToConstant:36],

        // Toggle row children
        [vidLabel.leadingAnchor constraintEqualToAnchor:toggleRow.leadingAnchor],
        [vidLabel.centerYAnchor constraintEqualToAnchor:toggleRow.centerYAnchor],
        [_videoSwitch.leadingAnchor constraintEqualToAnchor:vidLabel.trailingAnchor constant:8],
        [_videoSwitch.centerYAnchor constraintEqualToAnchor:toggleRow.centerYAnchor],
        [logLabel.leadingAnchor constraintEqualToAnchor:_videoSwitch.trailingAnchor constant:24],
        [logLabel.centerYAnchor constraintEqualToAnchor:toggleRow.centerYAnchor],
        [_logsSwitch.leadingAnchor constraintEqualToAnchor:logLabel.trailingAnchor constant:8],
        [_logsSwitch.centerYAnchor constraintEqualToAnchor:toggleRow.centerYAnchor],

        // Buttons
        [dismissBtn.topAnchor constraintEqualToAnchor:toggleRow.bottomAnchor constant:pad * 1.5],
        [dismissBtn.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:pad],
        [dismissBtn.widthAnchor constraintEqualToConstant:100],
        [dismissBtn.heightAnchor constraintEqualToConstant:44],

        [submitBtn.topAnchor constraintEqualToAnchor:toggleRow.bottomAnchor constant:pad * 1.5],
        [submitBtn.trailingAnchor constraintEqualToAnchor:content.trailingAnchor constant:-pad],
        [submitBtn.widthAnchor constraintEqualToConstant:160],
        [submitBtn.heightAnchor constraintEqualToConstant:44],

        // Bottom anchor for scroll content sizing
        [submitBtn.bottomAnchor constraintEqualToAnchor:content.bottomAnchor constant:-pad],
    ]];
}

- (void)viewDidLayoutSubviews {
    [super viewDidLayoutSubviews];
    _playerLayer.frame = _videoContainer.bounds;
}

- (void)playerDidFinish:(NSNotification*)note {
    [_player seekToTime:kCMTimeZero];
    [_player play];
}

- (void)dealloc {
    [[NSNotificationCenter defaultCenter] removeObserver:self];
    [_player pause];
    _player = nil;
}

- (UILabel*)makeLabel:(NSString*)text {
    UILabel* label = [[UILabel alloc] init];
    label.text = text;
    label.font = [UIFont systemFontOfSize:13];
    label.textColor = [UIColor whiteColor];
    label.translatesAutoresizingMaskIntoConstraints = NO;
    return label;
}

- (void)onSubmitTapped {
    NSString* title = _titleField.text ?: @"";
    NSString* desc = _descField.text ?: @"";
    NSArray* sevValues = @[@"low", @"medium", @"high", @"critical"];
    NSString* sev = sevValues[_sevControl.selectedSegmentIndex];
    BOOL inclVideo = _videoSwitch.isOn;
    BOOL inclLogs = _logsSwitch.isOn;

    CrashReportSubmitCallback cb = self.onSubmit;
    [self dismissViewControllerAnimated:YES completion:^{
        if (cb) cb([title UTF8String], [desc UTF8String], [sev UTF8String], inclVideo ? 1 : 0, inclLogs ? 1 : 0);
    }];
}

- (void)onDismissTapped {
    CrashReportDismissCallback cb = self.onDismiss;
    [self dismissViewControllerAnimated:YES completion:^{
        if (cb) cb();
    }];
}

@end

// ─── C Bridge Functions ──────────────────────────────────────────

extern "C" {

void Bugpunch_ShowPermissionDialog(const char* title, const char* message, PermissionCallback callback) {
    NSString* nsTitle = [NSString stringWithUTF8String:title];
    NSString* nsMessage = [NSString stringWithUTF8String:message];

    dispatch_async(dispatch_get_main_queue(), ^{
        UIAlertController* alert = [UIAlertController alertControllerWithTitle:nsTitle
                                                                      message:nsMessage
                                                               preferredStyle:UIAlertControllerStyleAlert];

        [alert addAction:[UIAlertAction actionWithTitle:@"Allow Once" style:UIAlertActionStyleDefault handler:^(UIAlertAction* action) {
            callback(0); // AllowOnce
        }]];

        [alert addAction:[UIAlertAction actionWithTitle:@"Always Allow" style:UIAlertActionStyleDefault handler:^(UIAlertAction* action) {
            callback(1); // AllowAlways
        }]];

        [alert addAction:[UIAlertAction actionWithTitle:@"Deny" style:UIAlertActionStyleCancel handler:^(UIAlertAction* action) {
            callback(2); // Deny
        }]];

        UIViewController* rootVC = [UIApplication sharedApplication].keyWindow.rootViewController;
        while (rootVC.presentedViewController) rootVC = rootVC.presentedViewController;
        [rootVC presentViewController:alert animated:YES completion:nil];
    });
}

void Bugpunch_ShowBugReportDialog(BugReportSubmitCallback onSubmit, BugReportCancelCallback onCancel) {
    dispatch_async(dispatch_get_main_queue(), ^{
        UIAlertController* alert = [UIAlertController alertControllerWithTitle:@"Bug Report"
                                                                      message:@"Describe the issue:"
                                                               preferredStyle:UIAlertControllerStyleAlert];

        [alert addTextFieldWithConfigurationHandler:^(UITextField* field) {
            field.placeholder = @"Title";
        }];

        [alert addTextFieldWithConfigurationHandler:^(UITextField* field) {
            field.placeholder = @"Description (optional)";
        }];

        [alert addTextFieldWithConfigurationHandler:^(UITextField* field) {
            field.placeholder = @"Severity: low / medium / high / critical";
            field.text = @"medium";
        }];

        [alert addAction:[UIAlertAction actionWithTitle:@"Submit" style:UIAlertActionStyleDefault handler:^(UIAlertAction* action) {
            NSString* title = alert.textFields[0].text;
            NSString* desc = alert.textFields[1].text;
            NSString* sev = alert.textFields[2].text;
            onSubmit([title UTF8String], [desc UTF8String], [sev UTF8String]);
        }]];

        [alert addAction:[UIAlertAction actionWithTitle:@"Cancel" style:UIAlertActionStyleCancel handler:^(UIAlertAction* action) {
            onCancel();
        }]];

        UIViewController* rootVC = [UIApplication sharedApplication].keyWindow.rootViewController;
        while (rootVC.presentedViewController) rootVC = rootVC.presentedViewController;
        [rootVC presentViewController:alert animated:YES completion:nil];
    });
}

void Bugpunch_ShowCrashReportOverlay(const char* exceptionMessage, const char* stackTrace, const char* videoPath,
                                      CrashReportSubmitCallback onSubmit, CrashReportDismissCallback onDismiss) {
    NSString* nsException = [NSString stringWithUTF8String:exceptionMessage];
    NSString* nsStack = [NSString stringWithUTF8String:stackTrace];
    NSString* nsVideo = [NSString stringWithUTF8String:videoPath];

    dispatch_async(dispatch_get_main_queue(), ^{
        BugpunchCrashReportVC* vc = [[BugpunchCrashReportVC alloc] init];
        vc.exceptionMessage = nsException;
        vc.stackTrace = nsStack;
        vc.videoPath = nsVideo;
        vc.onSubmit = onSubmit;
        vc.onDismiss = onDismiss;
        vc.modalPresentationStyle = UIModalPresentationOverFullScreen;
        vc.modalTransitionStyle = UIModalTransitionStyleCrossDissolve;

        UIViewController* rootVC = [UIApplication sharedApplication].keyWindow.rootViewController;
        while (rootVC.presentedViewController) rootVC = rootVC.presentedViewController;
        [rootVC presentViewController:vc animated:YES completion:nil];
    });
}

}
