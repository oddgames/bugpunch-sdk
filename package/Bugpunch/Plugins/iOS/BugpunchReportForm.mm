// BugpunchReportForm.mm — native iOS bug-report form + annotation canvas.
//
// Shown after the user fires a "bug" report. Displays a screenshot preview
// (tap to annotate), email / description / severity inputs, Cancel/Send.
// On Send, calls back into the coordinator which assembles the upload.

#import <UIKit/UIKit.h>
#import <Foundation/Foundation.h>

// Coordinator entry point (defined in BugpunchDebugMode.mm).
extern "C" void Bugpunch_SubmitReport(const char* title, const char* description,
    const char* reporterEmail, const char* severity,
    const char* screenshotPath, const char* annotationsPath);
extern "C" void Bugpunch_ClearReportInProgress(void);

@class BPAnnotateViewController;
@class BPAnnotateCanvas;

// ── Annotate canvas (transparent-PNG stroke overlay) ──

@interface BPAnnotateCanvas : UIView
@property (nonatomic, strong) UIImage* shot;
@property (nonatomic, strong) NSMutableArray<UIBezierPath*>* strokes;
@property (nonatomic, strong) UIBezierPath* current;
@property (nonatomic, assign) CGFloat shotScale;
@property (nonatomic, assign) CGRect shotRect;
@end

@implementation BPAnnotateCanvas
- (instancetype)initWithFrame:(CGRect)f shot:(UIImage*)shot {
    if (self = [super initWithFrame:f]) {
        _shot = shot;
        _strokes = [NSMutableArray new];
        self.backgroundColor = UIColor.blackColor;
        self.multipleTouchEnabled = NO;
    }
    return self;
}
- (void)layoutSubviews {
    [super layoutSubviews];
    CGSize vs = self.bounds.size;
    CGSize is = _shot.size;
    CGFloat s = MIN(vs.width / is.width, vs.height / is.height);
    _shotScale = s;
    _shotRect = CGRectMake((vs.width - is.width * s) / 2,
                           (vs.height - is.height * s) / 2,
                           is.width * s, is.height * s);
    [self setNeedsDisplay];
}
- (void)drawRect:(CGRect)r {
    [_shot drawInRect:_shotRect];
    [[UIColor colorWithRed:1.0 green:0.17 blue:0.36 alpha:1.0] setStroke];
    for (UIBezierPath* p in _strokes) { p.lineWidth = 10; p.lineCapStyle = kCGLineCapRound; [p stroke]; }
    if (_current) { _current.lineWidth = 10; _current.lineCapStyle = kCGLineCapRound; [_current stroke]; }
}
- (void)touchesBegan:(NSSet<UITouch*>*)touches withEvent:(UIEvent*)e {
    CGPoint p = [touches.anyObject locationInView:self];
    _current = [UIBezierPath bezierPath];
    [_current moveToPoint:p];
    [self setNeedsDisplay];
}
- (void)touchesMoved:(NSSet<UITouch*>*)touches withEvent:(UIEvent*)e {
    if (!_current) return;
    CGPoint p = [touches.anyObject locationInView:self];
    [_current addLineToPoint:p];
    [self setNeedsDisplay];
}
- (void)touchesEnded:(NSSet<UITouch*>*)touches withEvent:(UIEvent*)e {
    if (_current) [_strokes addObject:_current];
    _current = nil;
    [self setNeedsDisplay];
}
- (void)touchesCancelled:(NSSet<UITouch*>*)touches withEvent:(UIEvent*)e {
    _current = nil; [self setNeedsDisplay];
}
- (void)undo { if (_strokes.count) { [_strokes removeLastObject]; [self setNeedsDisplay]; } }
- (void)clearAll { [_strokes removeAllObjects]; [self setNeedsDisplay]; }

/// Renders strokes only (no screenshot), at screenshot dimensions, with alpha.
- (UIImage*)exportOverlay {
    CGSize size = _shot.size;
    UIGraphicsImageRendererFormat* fmt = [UIGraphicsImageRendererFormat defaultFormat];
    fmt.opaque = NO;
    UIGraphicsImageRenderer* r =
        [[UIGraphicsImageRenderer alloc] initWithSize:size format:fmt];
    return [r imageWithActions:^(UIGraphicsImageRendererContext* ctx) {
        CGContextSaveGState(ctx.CGContext);
        CGContextTranslateCTM(ctx.CGContext, -_shotRect.origin.x, -_shotRect.origin.y);
        CGContextScaleCTM(ctx.CGContext, 1.0 / _shotScale, 1.0 / _shotScale);
        [[UIColor colorWithRed:1.0 green:0.17 blue:0.36 alpha:1.0] setStroke];
        CGFloat w = 10.0 / _shotScale;
        for (UIBezierPath* p in _strokes) { p.lineWidth = w; p.lineCapStyle = kCGLineCapRound; [p stroke]; }
        CGContextRestoreGState(ctx.CGContext);
    }];
}
@end

// ── Annotate VC ──

@interface BPAnnotateViewController : UIViewController
@property (nonatomic, strong) BPAnnotateCanvas* canvas;
@property (nonatomic, copy) void (^onDone)(NSString* pngPath);
@end

@implementation BPAnnotateViewController
- (instancetype)initWithShot:(UIImage*)shot done:(void(^)(NSString*))done {
    if (self = [super init]) {
        _canvas = [[BPAnnotateCanvas alloc] initWithFrame:CGRectZero shot:shot];
        _onDone = [done copy];
    }
    return self;
}
- (void)viewDidLoad {
    [super viewDidLoad];
    self.view.backgroundColor = UIColor.blackColor;
    self.modalPresentationStyle = UIModalPresentationFullScreen;
    _canvas.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:_canvas];

    UIStackView* bar = [UIStackView new];
    bar.axis = UILayoutConstraintAxisHorizontal;
    bar.distribution = UIStackViewDistributionFillEqually;
    bar.spacing = 8;
    bar.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:bar];
    [bar addArrangedSubview:[self buttonTitle:@"Undo"   bg:0x394048ff sel:@selector(onUndo)]];
    [bar addArrangedSubview:[self buttonTitle:@"Clear"  bg:0x394048ff sel:@selector(onClear)]];
    [bar addArrangedSubview:[self buttonTitle:@"Cancel" bg:0x394048ff sel:@selector(onCancel)]];
    [bar addArrangedSubview:[self buttonTitle:@"Done"   bg:0x2a7be0ff sel:@selector(onDoneTap)]];

    [NSLayoutConstraint activateConstraints:@[
        [_canvas.topAnchor constraintEqualToAnchor:self.view.topAnchor],
        [_canvas.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor],
        [_canvas.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor],
        [_canvas.bottomAnchor constraintEqualToAnchor:bar.topAnchor],
        [bar.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor constant:12],
        [bar.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor constant:-12],
        [bar.bottomAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.bottomAnchor constant:-12],
        [bar.heightAnchor constraintEqualToConstant:44],
    ]];
}
- (UIButton*)buttonTitle:(NSString*)t bg:(uint32_t)bg sel:(SEL)sel {
    UIButton* b = [UIButton buttonWithType:UIButtonTypeSystem];
    [b setTitle:t forState:UIControlStateNormal];
    [b setTitleColor:UIColor.whiteColor forState:UIControlStateNormal];
    b.backgroundColor = [UIColor colorWithRed:((bg >> 24) & 0xff) / 255.0
                                        green:((bg >> 16) & 0xff) / 255.0
                                         blue:((bg >>  8) & 0xff) / 255.0
                                        alpha:(bg        & 0xff) / 255.0];
    b.layer.cornerRadius = 6;
    [b addTarget:self action:sel forControlEvents:UIControlEventTouchUpInside];
    return b;
}
- (void)onUndo   { [_canvas undo]; }
- (void)onClear  { [_canvas clearAll]; }
- (void)onCancel { if (_onDone) _onDone(nil);
                   [self dismissViewControllerAnimated:YES completion:nil]; }
- (void)onDoneTap {
    UIImage* overlay = [_canvas exportOverlay];
    NSString* caches = NSSearchPathForDirectoriesInDomains(
        NSCachesDirectory, NSUserDomainMask, YES).firstObject;
    NSString* path = [caches stringByAppendingPathComponent:
        [NSString stringWithFormat:@"bp_annotations_%f.png", [NSDate timeIntervalSinceReferenceDate]]];
    NSData* data = UIImagePNGRepresentation(overlay);
    BOOL ok = [data writeToFile:path atomically:YES];
    if (_onDone) _onDone(ok ? path : nil);
    [self dismissViewControllerAnimated:YES completion:nil];
}
@end

// ── Report form VC ──

@interface BPReportFormViewController : UIViewController<UITextFieldDelegate, UITextViewDelegate>
@property (nonatomic, copy) NSString* screenshotPath;
@property (nonatomic, copy) NSString* title_;
@property (nonatomic, copy) NSString* description_;
@property (nonatomic, strong) UIImageView* preview;
@property (nonatomic, strong) UITextField* emailField;
@property (nonatomic, strong) UITextView* descField;
@property (nonatomic, strong) UISegmentedControl* severity;
@property (nonatomic, copy) NSString* annotationsPath;
@end

@implementation BPReportFormViewController

- (void)viewDidLoad {
    [super viewDidLoad];
    self.view.backgroundColor = [UIColor colorWithRed:0x10/255.0 green:0x14/255.0 blue:0x18/255.0 alpha:1];

    UIScrollView* scroll = [UIScrollView new];
    scroll.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:scroll];

    UIStackView* stack = [UIStackView new];
    stack.axis = UILayoutConstraintAxisVertical;
    stack.spacing = 12;
    stack.translatesAutoresizingMaskIntoConstraints = NO;
    [scroll addSubview:stack];

    UILabel* header = [UILabel new];
    header.text = @"Report a bug";
    header.textColor = UIColor.whiteColor;
    header.font = [UIFont boldSystemFontOfSize:22];
    [stack addArrangedSubview:header];

    _preview = [UIImageView new];
    _preview.contentMode = UIViewContentModeScaleAspectFit;
    _preview.backgroundColor = [UIColor colorWithRed:0x1a/255.0 green:0x1f/255.0 blue:0x25/255.0 alpha:1];
    _preview.userInteractionEnabled = YES;
    [_preview addGestureRecognizer:[[UITapGestureRecognizer alloc]
        initWithTarget:self action:@selector(onShotTap)]];
    [_preview.heightAnchor constraintEqualToConstant:240].active = YES;
    [stack addArrangedSubview:_preview];

    UILabel* hint = [UILabel new];
    hint.text = @"Tap screenshot to annotate";
    hint.textColor = [UIColor colorWithRed:0x7a/255.0 green:0x88/255.0 blue:0x99/255.0 alpha:1];
    hint.font = [UIFont systemFontOfSize:12];
    [stack addArrangedSubview:hint];

    [stack addArrangedSubview:[self fieldLabel:@"Your email"]];
    _emailField = [UITextField new];
    [self styleInput:_emailField];
    _emailField.keyboardType = UIKeyboardTypeEmailAddress;
    _emailField.autocapitalizationType = UITextAutocapitalizationTypeNone;
    _emailField.autocorrectionType = UITextAutocorrectionTypeNo;
    [_emailField.heightAnchor constraintEqualToConstant:40].active = YES;
    [stack addArrangedSubview:_emailField];

    [stack addArrangedSubview:[self fieldLabel:@"Description"]];
    _descField = [UITextView new];
    _descField.backgroundColor = [UIColor colorWithRed:0x1a/255.0 green:0x1f/255.0 blue:0x25/255.0 alpha:1];
    _descField.textColor = UIColor.whiteColor;
    _descField.font = [UIFont systemFontOfSize:16];
    _descField.text = _description_ ?: @"";
    [_descField.heightAnchor constraintEqualToConstant:120].active = YES;
    [stack addArrangedSubview:_descField];

    [stack addArrangedSubview:[self fieldLabel:@"Severity"]];
    _severity = [[UISegmentedControl alloc] initWithItems:@[@"Low", @"Medium", @"High", @"Critical"]];
    _severity.selectedSegmentIndex = 1;
    [stack addArrangedSubview:_severity];

    UIStackView* buttons = [UIStackView new];
    buttons.axis = UILayoutConstraintAxisHorizontal;
    buttons.spacing = 12;
    buttons.distribution = UIStackViewDistributionFillEqually;
    UIButton* cancel = [self actionTitle:@"Cancel" bg:0x394048ff sel:@selector(onCancel)];
    UIButton* send   = [self actionTitle:@"Send"   bg:0x2a7be0ff sel:@selector(onSend)];
    [cancel.heightAnchor constraintEqualToConstant:44].active = YES;
    [send.heightAnchor constraintEqualToConstant:44].active = YES;
    [buttons addArrangedSubview:cancel];
    [buttons addArrangedSubview:send];
    [stack addArrangedSubview:buttons];

    [NSLayoutConstraint activateConstraints:@[
        [scroll.topAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.topAnchor],
        [scroll.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor constant:20],
        [scroll.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor constant:-20],
        [scroll.bottomAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.bottomAnchor],
        [stack.topAnchor constraintEqualToAnchor:scroll.topAnchor constant:20],
        [stack.leadingAnchor constraintEqualToAnchor:scroll.leadingAnchor],
        [stack.trailingAnchor constraintEqualToAnchor:scroll.trailingAnchor],
        [stack.bottomAnchor constraintEqualToAnchor:scroll.bottomAnchor constant:-20],
        [stack.widthAnchor constraintEqualToAnchor:scroll.widthAnchor],
    ]];

    [self refreshPreview];
}

- (UILabel*)fieldLabel:(NSString*)t {
    UILabel* l = [UILabel new];
    l.text = t;
    l.textColor = [UIColor colorWithRed:0xb0/255.0 green:0xba/255.0 blue:0xc5/255.0 alpha:1];
    l.font = [UIFont systemFontOfSize:13];
    return l;
}

- (void)styleInput:(UITextField*)f {
    f.backgroundColor = [UIColor colorWithRed:0x1a/255.0 green:0x1f/255.0 blue:0x25/255.0 alpha:1];
    f.textColor = UIColor.whiteColor;
    f.font = [UIFont systemFontOfSize:16];
    f.borderStyle = UITextBorderStyleNone;
    f.leftView = [[UIView alloc] initWithFrame:CGRectMake(0,0,10,0)];
    f.leftViewMode = UITextFieldViewModeAlways;
}

- (UIButton*)actionTitle:(NSString*)t bg:(uint32_t)bg sel:(SEL)sel {
    UIButton* b = [UIButton buttonWithType:UIButtonTypeSystem];
    [b setTitle:t forState:UIControlStateNormal];
    [b setTitleColor:UIColor.whiteColor forState:UIControlStateNormal];
    b.backgroundColor = [UIColor colorWithRed:((bg >> 24) & 0xff) / 255.0
                                        green:((bg >> 16) & 0xff) / 255.0
                                         blue:((bg >>  8) & 0xff) / 255.0
                                        alpha:(bg & 0xff) / 255.0];
    b.layer.cornerRadius = 6;
    b.titleLabel.font = [UIFont boldSystemFontOfSize:16];
    [b addTarget:self action:sel forControlEvents:UIControlEventTouchUpInside];
    return b;
}

- (void)refreshPreview {
    UIImage* shot = [UIImage imageWithContentsOfFile:_screenshotPath];
    if (!shot) return;
    if (_annotationsPath) {
        UIImage* overlay = [UIImage imageWithContentsOfFile:_annotationsPath];
        if (overlay) {
            UIGraphicsBeginImageContextWithOptions(shot.size, YES, 0);
            [shot drawInRect:CGRectMake(0, 0, shot.size.width, shot.size.height)];
            [overlay drawInRect:CGRectMake(0, 0, shot.size.width, shot.size.height)];
            UIImage* combined = UIGraphicsGetImageFromCurrentImageContext();
            UIGraphicsEndImageContext();
            _preview.image = combined;
            return;
        }
    }
    _preview.image = shot;
}

- (void)onShotTap {
    UIImage* shot = [UIImage imageWithContentsOfFile:_screenshotPath];
    if (!shot) return;
    __weak __typeof(self) weakSelf = self;
    BPAnnotateViewController* a = [[BPAnnotateViewController alloc]
        initWithShot:shot done:^(NSString* path) {
            if (path) { weakSelf.annotationsPath = path; [weakSelf refreshPreview]; }
        }];
    [self presentViewController:a animated:YES completion:nil];
}

- (void)onCancel {
    [self dismissViewControllerAnimated:YES completion:^{
        Bugpunch_ClearReportInProgress();
    }];
}

- (void)onSend {
    NSString* desc = _descField.text ?: @"";
    if (desc.length == 0) {
        UIAlertController* a = [UIAlertController
            alertControllerWithTitle:nil message:@"Please add a description"
            preferredStyle:UIAlertControllerStyleAlert];
        [a addAction:[UIAlertAction actionWithTitle:@"OK" style:UIAlertActionStyleDefault handler:nil]];
        [self presentViewController:a animated:YES completion:nil];
        return;
    }
    NSArray* sev = @[@"Low", @"Medium", @"High", @"Critical"];
    NSString* severity = sev[_severity.selectedSegmentIndex];
    Bugpunch_SubmitReport(
        [(_title_ ?: @"Bug report") UTF8String],
        [desc UTF8String],
        [(_emailField.text ?: @"") UTF8String],
        [severity UTF8String],
        [_screenshotPath UTF8String],
        _annotationsPath ? [_annotationsPath UTF8String] : "");
    [self dismissViewControllerAnimated:YES completion:^{
        Bugpunch_ClearReportInProgress();
    }];
}
@end

// ── Presenter API (called from BugpunchDebugMode.mm) ──

extern "C" void Bugpunch_PresentReportForm(const char* screenshotPath,
                                           const char* title,
                                           const char* description) {
    NSString* shot = screenshotPath ? [NSString stringWithUTF8String:screenshotPath] : nil;
    NSString* t = title ? [NSString stringWithUTF8String:title] : nil;
    NSString* d = description ? [NSString stringWithUTF8String:description] : nil;
    dispatch_async(dispatch_get_main_queue(), ^{
        BPReportFormViewController* vc = [BPReportFormViewController new];
        vc.screenshotPath = shot;
        vc.title_ = t;
        vc.description_ = d;
        vc.modalPresentationStyle = UIModalPresentationFullScreen;

        UIViewController* top = nil;
        if (@available(iOS 13.0, *)) {
            for (UIScene* s in UIApplication.sharedApplication.connectedScenes) {
                if (![s isKindOfClass:[UIWindowScene class]]) continue;
                for (UIWindow* w in ((UIWindowScene*)s).windows) {
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
        [top presentViewController:vc animated:YES completion:nil];
    });
}
