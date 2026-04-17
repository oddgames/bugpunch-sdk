// BugpunchReportForm.mm — native iOS bug-report form + annotation canvas.
//
// Shown after the user fires a "bug" report. Displays a screenshot preview
// (tap to annotate), email / description / severity inputs, Cancel/Send.
// On Send, calls back into the coordinator which assembles the upload.

#import <UIKit/UIKit.h>
#import <Foundation/Foundation.h>
#import <QuartzCore/QuartzCore.h>

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

// ── Palette (mirrors Android BugpunchReportActivity). ──
#define BP_COLOR_BG          [UIColor colorWithRed:0x0B/255.0 green:0x0D/255.0 blue:0x10/255.0 alpha:1]
#define BP_COLOR_SURFACE     [UIColor colorWithRed:0x17/255.0 green:0x1B/255.0 blue:0x20/255.0 alpha:1]
#define BP_COLOR_SURFACE_ALT [UIColor colorWithRed:0x1E/255.0 green:0x24/255.0 blue:0x2B/255.0 alpha:1]
#define BP_COLOR_BORDER      [UIColor colorWithRed:0x2A/255.0 green:0x31/255.0 blue:0x39/255.0 alpha:1]
#define BP_COLOR_TEXT        [UIColor colorWithRed:0xF1/255.0 green:0xF4/255.0 blue:0xF7/255.0 alpha:1]
#define BP_COLOR_TEXT_DIM    [UIColor colorWithRed:0x8B/255.0 green:0x95/255.0 blue:0xA2/255.0 alpha:1]
#define BP_COLOR_TEXT_LABEL  [UIColor colorWithRed:0xB8/255.0 green:0xC2/255.0 blue:0xCF/255.0 alpha:1]
#define BP_COLOR_ACCENT      [UIColor colorWithRed:0x3B/255.0 green:0x82/255.0 blue:0xF6/255.0 alpha:1]
#define BP_COLOR_ACCENT_DARK [UIColor colorWithRed:0x25/255.0 green:0x63/255.0 blue:0xEB/255.0 alpha:1]

@interface BPReportFormViewController : UIViewController<UITextFieldDelegate, UITextViewDelegate>
@property (nonatomic, copy) NSString* screenshotPath;
@property (nonatomic, copy) NSString* title_;
@property (nonatomic, copy) NSString* description_;
@property (nonatomic, strong) UIImageView* preview;
@property (nonatomic, strong) UIView* previewCard;
@property (nonatomic, strong) UITextField* emailField;
@property (nonatomic, strong) UITextView* descField;
@property (nonatomic, strong) UISegmentedControl* severity;
@property (nonatomic, copy) NSString* annotationsPath;
@property (nonatomic, strong) UIView* root;   // the swappable layout root
@property (nonatomic, strong) NSMutableArray<NSString*>* extraScreenshots;
@property (nonatomic, strong) UIStackView* thumbStrip;
@end

@implementation BPReportFormViewController

- (void)viewDidLoad {
    [super viewDidLoad];
    self.view.backgroundColor = BP_COLOR_BG;

    // Shared input/segmented/preview instances survive across layout rebuilds
    // so text typed by the user isn't lost on rotation.
    _preview = [UIImageView new];
    _preview.contentMode = UIViewContentModeScaleAspectFit;
    _preview.userInteractionEnabled = YES;
    _preview.clipsToBounds = YES;
    _preview.layer.cornerRadius = 8;
    [_preview addGestureRecognizer:[[UITapGestureRecognizer alloc]
        initWithTarget:self action:@selector(onShotTap)]];

    _emailField = [UITextField new];
    [self styleInput:_emailField];
    _emailField.keyboardType = UIKeyboardTypeEmailAddress;
    _emailField.autocapitalizationType = UITextAutocapitalizationTypeNone;
    _emailField.autocorrectionType = UITextAutocorrectionTypeNo;
    _emailField.attributedPlaceholder = [[NSAttributedString alloc]
        initWithString:@"you@studio.com"
            attributes:@{NSForegroundColorAttributeName: BP_COLOR_TEXT_DIM}];
    [_emailField.heightAnchor constraintEqualToConstant:44].active = YES;

    _descField = [UITextView new];
    _descField.backgroundColor = BP_COLOR_SURFACE;
    _descField.textColor = BP_COLOR_TEXT;
    _descField.font = [UIFont systemFontOfSize:15];
    _descField.textContainerInset = UIEdgeInsetsMake(12, 10, 12, 10);
    _descField.layer.cornerRadius = 10;
    _descField.layer.borderColor = BP_COLOR_BORDER.CGColor;
    _descField.layer.borderWidth = 1;
    _descField.text = _description_ ?: @"";
    [_descField.heightAnchor constraintEqualToConstant:120].active = YES;

    _severity = [[UISegmentedControl alloc]
        initWithItems:@[@"Low", @"Medium", @"High", @"Critical"]];
    _severity.selectedSegmentIndex = 1;
    if (@available(iOS 13.0, *)) {
        _severity.selectedSegmentTintColor = BP_COLOR_ACCENT;
        _severity.backgroundColor = BP_COLOR_SURFACE;
        [_severity setTitleTextAttributes:@{NSForegroundColorAttributeName: BP_COLOR_TEXT_LABEL}
                                 forState:UIControlStateNormal];
        [_severity setTitleTextAttributes:@{NSForegroundColorAttributeName: UIColor.whiteColor,
                                            NSFontAttributeName: [UIFont boldSystemFontOfSize:13]}
                                 forState:UIControlStateSelected];
    }

    [self buildLayoutForSize:self.view.bounds.size];
    [self refreshPreview];
}

- (void)viewWillTransitionToSize:(CGSize)size
       withTransitionCoordinator:(id<UIViewControllerTransitionCoordinator>)coord {
    [super viewWillTransitionToSize:size withTransitionCoordinator:coord];
    [coord animateAlongsideTransition:^(id<UIViewControllerTransitionCoordinatorContext> ctx) {
        [self buildLayoutForSize:size];
    } completion:nil];
}

// Header block: eyebrow + title + accent gradient bar.
- (UIView*)buildHeaderBlock {
    UIStackView* s = [UIStackView new];
    s.axis = UILayoutConstraintAxisVertical;
    s.spacing = 4;
    s.alignment = UIStackViewAlignmentLeading;

    UILabel* eyebrow = [UILabel new];
    eyebrow.text = @"BUG REPORT";
    eyebrow.textColor = BP_COLOR_ACCENT;
    eyebrow.font = [UIFont boldSystemFontOfSize:11];
    [s addArrangedSubview:eyebrow];

    UILabel* header = [UILabel new];
    header.text = @"Tell us what happened";
    header.textColor = BP_COLOR_TEXT;
    header.font = [UIFont boldSystemFontOfSize:22];
    [s addArrangedSubview:header];

    UIView* barContainer = [UIView new];
    CAGradientLayer* bar = [CAGradientLayer layer];
    bar.colors = @[(id)BP_COLOR_ACCENT.CGColor, (id)BP_COLOR_ACCENT_DARK.CGColor];
    bar.startPoint = CGPointMake(0, 0.5);
    bar.endPoint = CGPointMake(1, 0.5);
    bar.cornerRadius = 1.5;
    bar.frame = CGRectMake(0, 0, 32, 3);
    [barContainer.layer addSublayer:bar];
    [barContainer.widthAnchor constraintEqualToConstant:32].active = YES;
    [barContainer.heightAnchor constraintEqualToConstant:3].active = YES;
    [s addArrangedSubview:barContainer];

    UIView* spacer = [UIView new];
    [spacer.heightAnchor constraintEqualToConstant:8].active = YES;
    [s addArrangedSubview:spacer];

    return s;
}

// Screenshot card with a floating "Tap to annotate" pill bottom-right.
- (UIView*)buildPreviewCard {
    UIView* card = [UIView new];
    card.backgroundColor = BP_COLOR_SURFACE_ALT;
    card.layer.cornerRadius = 12;
    card.layer.borderColor = BP_COLOR_BORDER.CGColor;
    card.layer.borderWidth = 1;
    card.layer.shadowColor = UIColor.blackColor.CGColor;
    card.layer.shadowOpacity = 0.4;
    card.layer.shadowOffset = CGSizeMake(0, 6);
    card.layer.shadowRadius = 14;

    [_preview removeFromSuperview];
    _preview.translatesAutoresizingMaskIntoConstraints = NO;
    [card addSubview:_preview];

    UILabel* pill = [UILabel new];
    pill.text = @"  \u270E  Tap to annotate  ";
    pill.textColor = BP_COLOR_TEXT;
    pill.font = [UIFont boldSystemFontOfSize:11];
    pill.backgroundColor = [UIColor colorWithRed:0x0B/255.0 green:0x0D/255.0 blue:0x10/255.0 alpha:0.8];
    pill.layer.cornerRadius = 11;
    pill.layer.borderColor = [BP_COLOR_BORDER colorWithAlphaComponent:0.2].CGColor;
    pill.layer.borderWidth = 1;
    pill.clipsToBounds = YES;
    pill.translatesAutoresizingMaskIntoConstraints = NO;
    [card addSubview:pill];

    [NSLayoutConstraint activateConstraints:@[
        [_preview.topAnchor      constraintEqualToAnchor:card.topAnchor      constant:4],
        [_preview.leadingAnchor  constraintEqualToAnchor:card.leadingAnchor  constant:4],
        [_preview.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-4],
        [_preview.bottomAnchor   constraintEqualToAnchor:card.bottomAnchor   constant:-4],
        [pill.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-10],
        [pill.bottomAnchor constraintEqualToAnchor:card.bottomAnchor constant:-10],
        [pill.heightAnchor constraintEqualToConstant:22],
    ]];

    _previewCard = card;
    return card;
}

// Assembles the form column — header, fields, action buttons. Used on both
// orientations; the caller decides where to place it (stacked under the
// preview in portrait, beside it in landscape).
- (UIStackView*)buildFormColumnIncludingHeader:(BOOL)includeHeader {
    UIStackView* form = [UIStackView new];
    form.axis = UILayoutConstraintAxisVertical;
    form.spacing = 8;

    if (includeHeader) {
        [form addArrangedSubview:[self buildHeaderBlock]];
    }

    [_emailField removeFromSuperview];
    [_descField removeFromSuperview];
    [_severity removeFromSuperview];

    // Extra screenshots strip (only in landscape — portrait adds it to the outer column)
    if (includeHeader) {
        UIView* thumbs = [self buildThumbStrip];
        if (thumbs) [form addArrangedSubview:thumbs];
    }

    [form addArrangedSubview:[self fieldLabel:@"Your email"]];
    [form addArrangedSubview:_emailField];

    [form addArrangedSubview:[self fieldLabel:@"Description"]];
    [form addArrangedSubview:_descField];

    [form addArrangedSubview:[self fieldLabel:@"Severity"]];
    [form addArrangedSubview:_severity];

    UIView* gap = [UIView new];
    [gap.heightAnchor constraintEqualToConstant:12].active = YES;
    [form addArrangedSubview:gap];

    UIStackView* buttons = [UIStackView new];
    buttons.axis = UILayoutConstraintAxisHorizontal;
    buttons.spacing = 12;
    buttons.distribution = UIStackViewDistributionFillEqually;
    UIButton* cancel = [self secondaryButtonTitle:@"Cancel" sel:@selector(onCancel)];
    UIButton* send   = [self primaryButtonTitle:@"Send report" sel:@selector(onSend)];
    [cancel.heightAnchor constraintEqualToConstant:46].active = YES;
    [send.heightAnchor constraintEqualToConstant:46].active = YES;
    [buttons addArrangedSubview:cancel];
    [buttons addArrangedSubview:send];
    [form addArrangedSubview:buttons];

    return form;
}

// Rebuild the layout tree for the given viewport size. Swaps between a
// single scrolling column (portrait) and two columns (landscape).
// ── Extra screenshots thumbnail strip ──

- (UIView*)buildThumbStrip {
    if (!_extraScreenshots || _extraScreenshots.count == 0) return nil;
    if (!_thumbStrip) {
        _thumbStrip = [UIStackView new];
        _thumbStrip.axis = UILayoutConstraintAxisHorizontal;
        _thumbStrip.spacing = 6;
        _thumbStrip.alignment = UIStackViewAlignmentCenter;
    }
    // Rebuild contents
    for (UIView* v in _thumbStrip.arrangedSubviews) [v removeFromSuperview];

    UIView* wrapper = [UIView new];
    wrapper.translatesAutoresizingMaskIntoConstraints = NO;

    // Header label
    UILabel* lbl = [UILabel new];
    lbl.text = [NSString stringWithFormat:@"SCREENSHOTS (%lu)", (unsigned long)_extraScreenshots.count];
    lbl.textColor = BP_COLOR_TEXT_LABEL;
    lbl.font = [UIFont boldSystemFontOfSize:11];
    lbl.translatesAutoresizingMaskIntoConstraints = NO;
    [wrapper addSubview:lbl];

    UIScrollView* scroll = [UIScrollView new];
    scroll.showsHorizontalScrollIndicator = NO;
    scroll.translatesAutoresizingMaskIntoConstraints = NO;
    [wrapper addSubview:scroll];

    _thumbStrip.translatesAutoresizingMaskIntoConstraints = NO;
    [scroll addSubview:_thumbStrip];

    [NSLayoutConstraint activateConstraints:@[
        [lbl.topAnchor constraintEqualToAnchor:wrapper.topAnchor],
        [lbl.leadingAnchor constraintEqualToAnchor:wrapper.leadingAnchor],
        [scroll.topAnchor constraintEqualToAnchor:lbl.bottomAnchor constant:6],
        [scroll.leadingAnchor constraintEqualToAnchor:wrapper.leadingAnchor],
        [scroll.trailingAnchor constraintEqualToAnchor:wrapper.trailingAnchor],
        [scroll.bottomAnchor constraintEqualToAnchor:wrapper.bottomAnchor],
        [scroll.heightAnchor constraintEqualToConstant:70],
        [_thumbStrip.topAnchor constraintEqualToAnchor:scroll.topAnchor],
        [_thumbStrip.leadingAnchor constraintEqualToAnchor:scroll.leadingAnchor],
        [_thumbStrip.trailingAnchor constraintEqualToAnchor:scroll.trailingAnchor],
        [_thumbStrip.bottomAnchor constraintEqualToAnchor:scroll.bottomAnchor],
        [_thumbStrip.heightAnchor constraintEqualToAnchor:scroll.heightAnchor],
    ]];

    for (NSUInteger i = 0; i < _extraScreenshots.count; i++) {
        NSString* path = _extraScreenshots[i];
        UIView* frame = [UIView new];
        frame.translatesAutoresizingMaskIntoConstraints = NO;
        [frame.widthAnchor constraintEqualToConstant:90].active = YES;
        [frame.heightAnchor constraintEqualToConstant:64].active = YES;

        UIImageView* img = [UIImageView new];
        img.contentMode = UIViewContentModeScaleAspectCover;
        img.clipsToBounds = YES;
        img.layer.cornerRadius = 6;
        img.image = [UIImage imageWithContentsOfFile:path];
        img.translatesAutoresizingMaskIntoConstraints = NO;
        [frame addSubview:img];
        [NSLayoutConstraint activateConstraints:@[
            [img.topAnchor constraintEqualToAnchor:frame.topAnchor],
            [img.leadingAnchor constraintEqualToAnchor:frame.leadingAnchor],
            [img.trailingAnchor constraintEqualToAnchor:frame.trailingAnchor],
            [img.bottomAnchor constraintEqualToAnchor:frame.bottomAnchor],
        ]];

        // Dismiss X
        UIButton* x = [UIButton new];
        [x setTitle:@"✕" forState:UIControlStateNormal];
        [x setTitleColor:UIColor.whiteColor forState:UIControlStateNormal];
        x.titleLabel.font = [UIFont systemFontOfSize:11];
        x.backgroundColor = [UIColor colorWithWhite:0 alpha:0.7];
        x.layer.cornerRadius = 10;
        x.translatesAutoresizingMaskIntoConstraints = NO;
        x.tag = (NSInteger)i;
        [x addTarget:self action:@selector(dismissThumb:) forControlEvents:UIControlEventTouchUpInside];
        [frame addSubview:x];
        [NSLayoutConstraint activateConstraints:@[
            [x.topAnchor constraintEqualToAnchor:frame.topAnchor constant:2],
            [x.trailingAnchor constraintEqualToAnchor:frame.trailingAnchor constant:-2],
            [x.widthAnchor constraintEqualToConstant:20],
            [x.heightAnchor constraintEqualToConstant:20],
        ]];

        [_thumbStrip addArrangedSubview:frame];
    }

    return wrapper;
}

- (void)dismissThumb:(UIButton*)sender {
    NSInteger idx = sender.tag;
    if (idx >= 0 && idx < (NSInteger)_extraScreenshots.count) {
        [_extraScreenshots removeObjectAtIndex:idx];
        [self buildLayoutForSize:self.view.bounds.size];
    }
}

- (void)buildLayoutForSize:(CGSize)size {
    [_root removeFromSuperview];

    BOOL landscape = size.width > size.height;
    UIEdgeInsets edge = UIEdgeInsetsMake(20, 20, 20, 20);

    // The preview card and form are re-composed fresh for each orientation.
    // The inner controls (_emailField, _descField, _severity, _preview) are
    // re-parented, so their text and state survive the rebuild.
    UIView* preview = [self buildPreviewCard];
    UIStackView* form = [self buildFormColumnIncludingHeader:landscape];

    UIView* rootView;
    if (landscape) {
        UIView* container = [UIView new];
        container.backgroundColor = BP_COLOR_BG;
        container.translatesAutoresizingMaskIntoConstraints = NO;

        preview.translatesAutoresizingMaskIntoConstraints = NO;
        [container addSubview:preview];

        UIScrollView* scroll = [UIScrollView new];
        scroll.translatesAutoresizingMaskIntoConstraints = NO;
        scroll.showsVerticalScrollIndicator = NO;
        [container addSubview:scroll];

        form.translatesAutoresizingMaskIntoConstraints = NO;
        [scroll addSubview:form];

        [NSLayoutConstraint activateConstraints:@[
            [preview.topAnchor      constraintEqualToAnchor:container.safeAreaLayoutGuide.topAnchor constant:edge.top],
            [preview.leadingAnchor  constraintEqualToAnchor:container.safeAreaLayoutGuide.leadingAnchor constant:edge.left],
            [preview.bottomAnchor   constraintEqualToAnchor:container.safeAreaLayoutGuide.bottomAnchor constant:-edge.bottom],
            [preview.widthAnchor    constraintEqualToAnchor:container.widthAnchor multiplier:0.5 constant:-edge.left - 10],

            [scroll.topAnchor      constraintEqualToAnchor:container.safeAreaLayoutGuide.topAnchor constant:edge.top],
            [scroll.leadingAnchor  constraintEqualToAnchor:preview.trailingAnchor constant:20],
            [scroll.trailingAnchor constraintEqualToAnchor:container.safeAreaLayoutGuide.trailingAnchor constant:-edge.right],
            [scroll.bottomAnchor   constraintEqualToAnchor:container.safeAreaLayoutGuide.bottomAnchor constant:-edge.bottom],

            [form.topAnchor      constraintEqualToAnchor:scroll.topAnchor],
            [form.leadingAnchor  constraintEqualToAnchor:scroll.leadingAnchor],
            [form.trailingAnchor constraintEqualToAnchor:scroll.trailingAnchor],
            [form.bottomAnchor   constraintEqualToAnchor:scroll.bottomAnchor],
            [form.widthAnchor    constraintEqualToAnchor:scroll.widthAnchor],
        ]];
        rootView = container;
    } else {
        UIScrollView* scroll = [UIScrollView new];
        scroll.backgroundColor = BP_COLOR_BG;
        scroll.translatesAutoresizingMaskIntoConstraints = NO;
        scroll.showsVerticalScrollIndicator = NO;

        UIStackView* column = [UIStackView new];
        column.axis = UILayoutConstraintAxisVertical;
        column.spacing = 14;
        column.translatesAutoresizingMaskIntoConstraints = NO;

        // Portrait: header → preview → form. Header renders above everything
        // else so the page has a clear hierarchy on small viewports.
        [column addArrangedSubview:[self buildHeaderBlock]];
        preview.translatesAutoresizingMaskIntoConstraints = NO;
        [preview.heightAnchor constraintEqualToConstant:240].active = YES;
        [column addArrangedSubview:preview];
        UIView* thumbs = [self buildThumbStrip];
        if (thumbs) [column addArrangedSubview:thumbs];
        [column addArrangedSubview:form];

        [scroll addSubview:column];

        [NSLayoutConstraint activateConstraints:@[
            [column.topAnchor      constraintEqualToAnchor:scroll.topAnchor      constant:edge.top],
            [column.leadingAnchor  constraintEqualToAnchor:scroll.leadingAnchor  constant:edge.left],
            [column.trailingAnchor constraintEqualToAnchor:scroll.trailingAnchor constant:-edge.right],
            [column.bottomAnchor   constraintEqualToAnchor:scroll.bottomAnchor   constant:-edge.bottom],
            [column.widthAnchor    constraintEqualToAnchor:scroll.widthAnchor    constant:-edge.left - edge.right],
        ]];
        rootView = scroll;
    }

    rootView.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:rootView];
    [NSLayoutConstraint activateConstraints:@[
        [rootView.topAnchor      constraintEqualToAnchor:self.view.topAnchor],
        [rootView.leadingAnchor  constraintEqualToAnchor:self.view.leadingAnchor],
        [rootView.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor],
        [rootView.bottomAnchor   constraintEqualToAnchor:self.view.bottomAnchor],
    ]];
    _root = rootView;
}

- (UILabel*)fieldLabel:(NSString*)t {
    UILabel* l = [UILabel new];
    l.text = [t uppercaseString];
    l.textColor = BP_COLOR_TEXT_LABEL;
    l.font = [UIFont boldSystemFontOfSize:11];
    return l;
}

- (void)styleInput:(UITextField*)f {
    f.backgroundColor = BP_COLOR_SURFACE;
    f.textColor = BP_COLOR_TEXT;
    f.font = [UIFont systemFontOfSize:15];
    f.borderStyle = UITextBorderStyleNone;
    f.layer.cornerRadius = 10;
    f.layer.borderColor = BP_COLOR_BORDER.CGColor;
    f.layer.borderWidth = 1;
    f.leftView = [[UIView alloc] initWithFrame:CGRectMake(0,0,12,0)];
    f.leftViewMode = UITextFieldViewModeAlways;
}

// Primary action — filled accent, drop shadow, bold label.
- (UIButton*)primaryButtonTitle:(NSString*)t sel:(SEL)sel {
    UIButton* b = [UIButton buttonWithType:UIButtonTypeSystem];
    [b setTitle:t forState:UIControlStateNormal];
    [b setTitleColor:UIColor.whiteColor forState:UIControlStateNormal];
    b.backgroundColor = BP_COLOR_ACCENT;
    b.layer.cornerRadius = 10;
    b.layer.shadowColor = BP_COLOR_ACCENT_DARK.CGColor;
    b.layer.shadowOpacity = 0.45;
    b.layer.shadowOffset = CGSizeMake(0, 4);
    b.layer.shadowRadius = 8;
    b.titleLabel.font = [UIFont boldSystemFontOfSize:15];
    [b addTarget:self action:sel forControlEvents:UIControlEventTouchUpInside];
    return b;
}

// Secondary action — surface fill, border, light shadow.
- (UIButton*)secondaryButtonTitle:(NSString*)t sel:(SEL)sel {
    UIButton* b = [UIButton buttonWithType:UIButtonTypeSystem];
    [b setTitle:t forState:UIControlStateNormal];
    [b setTitleColor:BP_COLOR_TEXT forState:UIControlStateNormal];
    b.backgroundColor = BP_COLOR_SURFACE;
    b.layer.cornerRadius = 10;
    b.layer.borderColor = BP_COLOR_BORDER.CGColor;
    b.layer.borderWidth = 1;
    b.layer.shadowColor = UIColor.blackColor.CGColor;
    b.layer.shadowOpacity = 0.3;
    b.layer.shadowOffset = CGSizeMake(0, 2);
    b.layer.shadowRadius = 6;
    b.titleLabel.font = [UIFont boldSystemFontOfSize:15];
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
    Bugpunch_PresentReportFormWithExtras(screenshotPath, title, description, NULL);
}

extern "C" void Bugpunch_PresentReportFormWithExtras(const char* screenshotPath,
                                                      const char* title,
                                                      const char* description,
                                                      const char* extraPathsCsv) {
    NSString* shot = screenshotPath ? [NSString stringWithUTF8String:screenshotPath] : nil;
    NSString* t = title ? [NSString stringWithUTF8String:title] : nil;
    NSString* d = description ? [NSString stringWithUTF8String:description] : nil;
    NSMutableArray<NSString*>* extras = [NSMutableArray array];
    if (extraPathsCsv && *extraPathsCsv) {
        for (NSString* p in [[NSString stringWithUTF8String:extraPathsCsv] componentsSeparatedByString:@","]) {
            if (p.length > 0 && [[NSFileManager defaultManager] fileExistsAtPath:p])
                [extras addObject:p];
        }
    }
    dispatch_async(dispatch_get_main_queue(), ^{
        BPReportFormViewController* vc = [BPReportFormViewController new];
        vc.screenshotPath = shot;
        vc.title_ = t;
        vc.description_ = d;
        vc.extraScreenshots = extras;
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
