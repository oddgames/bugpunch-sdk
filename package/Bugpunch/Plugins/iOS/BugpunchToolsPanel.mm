// BugpunchToolsPanel.mm — Native full-screen debug tools panel for iOS.
//
// Shows categories as horizontal chips, a search bar, and tool items with
// button / toggle / slider controls. Uses SF Symbols for icons (free, built
// into iOS 13+). Tool definitions are passed as JSON from C#; callbacks fire
// via UnitySendMessage.

#import <UIKit/UIKit.h>

// ── Colours ──
#define COL_BG        [UIColor colorWithRed:0.08 green:0.09 blue:0.11 alpha:0.97]
#define COL_PANEL     [UIColor colorWithRed:0.11 green:0.12 blue:0.15 alpha:1]
#define COL_ACCENT    [UIColor colorWithRed:0.25 green:0.56 blue:0.96 alpha:1]
#define COL_DANGER    [UIColor colorWithRed:0.85 green:0.22 blue:0.22 alpha:1]
#define COL_WARN      [UIColor colorWithRed:0.85 green:0.60 blue:0.18 alpha:1]
#define COL_TEXT      [UIColor colorWithRed:0.90 green:0.91 blue:0.93 alpha:1]
#define COL_DIM       [UIColor colorWithRed:0.55 green:0.57 blue:0.62 alpha:1]
#define COL_CAT       [UIColor colorWithRed:0.14 green:0.15 blue:0.19 alpha:1]
#define COL_CAT_SEL   [UIColor colorWithRed:0.20 green:0.22 blue:0.28 alpha:1]
#define COL_ITEM      [UIColor colorWithRed:0.12 green:0.13 blue:0.17 alpha:1]
#define COL_SEARCH    [UIColor colorWithRed:0.16 green:0.17 blue:0.21 alpha:1]
#define COL_TOG_ON    [UIColor colorWithRed:0.20 green:0.72 blue:0.40 alpha:1]
#define COL_TOG_OFF   [UIColor colorWithRed:0.30 green:0.31 blue:0.35 alpha:1]

// ── SF Symbol name mapping from Feather icon names ──
static NSString* BPSFSymbol(NSString* featherName) {
    static NSDictionary* map;
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        map = @{
            @"play":            @"play.fill",
            @"pause":           @"pause.fill",
            @"settings":        @"gearshape",
            @"gear":            @"gearshape",
            @"search":          @"magnifyingglass",
            @"x":               @"xmark",
            @"close":           @"xmark",
            @"alert-triangle":  @"exclamationmark.triangle",
            @"warning":         @"exclamationmark.triangle",
            @"zap":             @"bolt.fill",
            @"bolt":            @"bolt.fill",
            @"eye":             @"eye",
            @"eye-off":         @"eye.slash",
            @"activity":        @"waveform.path.ecg",
            @"chart":           @"chart.bar",
            @"sliders":         @"slider.horizontal.3",
            @"tune":            @"slider.horizontal.3",
            @"send":            @"paperplane",
            @"flag":            @"flag",
            @"clock":           @"clock",
            @"cpu":             @"cpu",
            @"target":          @"target",
            @"crosshair":       @"scope",
            @"terminal":        @"terminal",
            @"toggle-left":     @"switch.2",
            @"toggle-right":    @"switch.2",
            @"info":            @"info.circle",
            @"check":           @"checkmark",
            @"refresh":         @"arrow.clockwise",
            @"refresh-cw":      @"arrow.clockwise",
            @"trash":           @"trash",
            @"trash-2":         @"trash",
            @"shield":          @"shield",
            @"layers":          @"square.3.layers.3d",
            @"monitor":         @"display",
            @"smartphone":      @"iphone",
            @"camera":          @"camera",
            @"image":           @"photo",
            @"film":            @"film",
            @"sun":             @"sun.max",
            @"moon":            @"moon",
            @"code":            @"chevron.left.forwardslash.chevron.right",
            @"grid":            @"square.grid.2x2",
            @"list":            @"list.bullet",
            @"menu":            @"line.3.horizontal",
            @"wifi":            @"wifi",
            @"battery":         @"battery.100",
            @"bar-chart":       @"chart.bar",
            @"bar-chart-2":     @"chart.bar.xaxis",
            @"box":             @"cube",
        };
    });
    return map[featherName] ?: @"circle";
}

static UIColor* BPParseColor(NSString* c) {
    if ([c isEqualToString:@"danger"]) return COL_DANGER;
    if ([c isEqualToString:@"warn"])   return COL_WARN;
    if ([c isEqualToString:@"accent"]) return COL_ACCENT;
    return COL_ACCENT;
}

// ── Tool item model ──

@interface BPToolItem : NSObject
@property (nonatomic, copy) NSString* toolId;
@property (nonatomic, copy) NSString* name;
@property (nonatomic, copy) NSString* category;
@property (nonatomic, copy) NSString* desc;
@property (nonatomic, copy) NSString* icon;
@property (nonatomic, copy) NSString* controlType;
@property (nonatomic, strong) UIColor* color;
@property (nonatomic, assign) BOOL toggleValue;
@property (nonatomic, assign) float sliderValue, sliderMin, sliderMax;
@end
@implementation BPToolItem
@end

// ── Tools ViewController ──

@interface BPToolsViewController : UIViewController <UISearchBarDelegate>
@property (nonatomic, strong) NSArray<BPToolItem*>* allTools;
@property (nonatomic, strong) NSMutableArray<BPToolItem*>* filteredTools;
@property (nonatomic, copy) NSString* activeCategory;
@property (nonatomic, copy) NSString* searchFilter;
@property (nonatomic, strong) UIScrollView* catScroll;
@property (nonatomic, strong) UIStackView* catStack;
@property (nonatomic, strong) UIScrollView* toolScroll;
@property (nonatomic, strong) UIStackView* toolStack;
@end

@implementation BPToolsViewController

- (instancetype)initWithToolsJSON:(NSString*)json {
    self = [super init];
    if (self) {
        _activeCategory = @"All";
        _searchFilter = @"";
        _filteredTools = [NSMutableArray array];
        [self parseTools:json];
    }
    return self;
}

- (void)parseTools:(NSString*)json {
    if (!json.length) { _allTools = @[]; return; }
    NSData* data = [json dataUsingEncoding:NSUTF8StringEncoding];
    NSArray* arr = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
    if (![arr isKindOfClass:[NSArray class]]) { _allTools = @[]; return; }
    NSMutableArray* tools = [NSMutableArray array];
    for (NSDictionary* d in arr) {
        BPToolItem* t = [BPToolItem new];
        t.toolId = d[@"id"] ?: [NSString stringWithFormat:@"tool_%lu", (unsigned long)tools.count];
        t.name = d[@"name"] ?: @"Tool";
        t.category = d[@"category"] ?: @"General";
        t.desc = d[@"description"] ?: @"";
        t.icon = d[@"icon"] ?: @"settings";
        t.controlType = d[@"controlType"] ?: @"button";
        t.color = BPParseColor(d[@"color"] ?: @"accent");
        t.toggleValue = [d[@"toggleValue"] boolValue];
        t.sliderValue = [d[@"sliderValue"] floatValue];
        t.sliderMin = [d[@"sliderMin"] floatValue];
        t.sliderMax = d[@"sliderMax"] ? [d[@"sliderMax"] floatValue] : 1.0f;
        [tools addObject:t];
    }
    _allTools = tools;
}

- (void)viewDidLoad {
    [super viewDidLoad];
    // Transparent scrim — tap outside the panel to dismiss.
    self.view.backgroundColor = [UIColor colorWithWhite:0 alpha:0.5];
    UITapGestureRecognizer* dismissTap = [[UITapGestureRecognizer alloc]
        initWithTarget:self action:@selector(closeTapped)];
    dismissTap.cancelsTouchesInView = NO;
    [self.view addGestureRecognizer:dismissTap];

    BOOL landscape = self.view.bounds.size.width > self.view.bounds.size.height;
    CGFloat pad = landscape ? 10 : 16;
    CGFloat hMargin = landscape ? 40 : 24;
    CGFloat vMargin = landscape ? 8 : 24;

    // Panel card — semi-transparent, rounded, smaller than screen.
    UIView* panel = [[UIView alloc] init];
    panel.backgroundColor = [UIColor colorWithRed:0.08 green:0.09 blue:0.11 alpha:0.92];
    panel.layer.cornerRadius = 16;
    panel.clipsToBounds = YES;
    panel.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:panel];
    [NSLayoutConstraint activateConstraints:@[
        [panel.topAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.topAnchor constant:vMargin],
        [panel.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor constant:hMargin],
        [panel.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor constant:-hMargin],
        [panel.bottomAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.bottomAnchor constant:-vMargin],
    ]];

    // ── Header ──
    UIView* header = [[UIView alloc] init];
    header.translatesAutoresizingMaskIntoConstraints = NO;
    [panel addSubview:header];
    [NSLayoutConstraint activateConstraints:@[
        [header.topAnchor constraintEqualToAnchor:panel.topAnchor],
        [header.leadingAnchor constraintEqualToAnchor:panel.leadingAnchor constant:pad],
        [header.trailingAnchor constraintEqualToAnchor:panel.trailingAnchor constant:-pad],
        [header.heightAnchor constraintEqualToConstant:landscape ? 40 : 50],
    ]];

    UILabel* title = [[UILabel alloc] init];
    title.text = @"Debug Tools";
    title.textColor = COL_TEXT;
    title.font = [UIFont boldSystemFontOfSize:landscape ? 18 : 22];
    title.translatesAutoresizingMaskIntoConstraints = NO;
    [header addSubview:title];
    [NSLayoutConstraint activateConstraints:@[
        [title.leadingAnchor constraintEqualToAnchor:header.leadingAnchor],
        [title.centerYAnchor constraintEqualToAnchor:header.centerYAnchor],
    ]];

    UIButton* closeBtn = [UIButton systemButtonWithImage:
        [UIImage systemImageNamed:@"xmark" withConfiguration:
            [UIImageSymbolConfiguration configurationWithPointSize:18 weight:UIImageSymbolWeightMedium]]
        target:self action:@selector(closeTapped)];
    closeBtn.tintColor = COL_DIM;
    closeBtn.translatesAutoresizingMaskIntoConstraints = NO;
    [header addSubview:closeBtn];
    [NSLayoutConstraint activateConstraints:@[
        [closeBtn.trailingAnchor constraintEqualToAnchor:header.trailingAnchor],
        [closeBtn.centerYAnchor constraintEqualToAnchor:header.centerYAnchor],
        [closeBtn.widthAnchor constraintEqualToConstant:36],
        [closeBtn.heightAnchor constraintEqualToConstant:36],
    ]];

    // Search bar
    UISearchBar* search = [[UISearchBar alloc] init];
    search.delegate = self;
    search.placeholder = @"Search tools...";
    search.searchBarStyle = UISearchBarStyleMinimal;
    search.barTintColor = COL_SEARCH;
    search.tintColor = COL_ACCENT;
    [search.searchTextField setTextColor:COL_TEXT];
    search.translatesAutoresizingMaskIntoConstraints = NO;
    [header addSubview:search];
    [NSLayoutConstraint activateConstraints:@[
        [search.leadingAnchor constraintEqualToAnchor:title.trailingAnchor constant:12],
        [search.trailingAnchor constraintEqualToAnchor:closeBtn.leadingAnchor constant:-8],
        [search.centerYAnchor constraintEqualToAnchor:header.centerYAnchor],
        [search.heightAnchor constraintEqualToConstant:36],
    ]];

    if (landscape) {
        // ── Landscape: categories as vertical sidebar on the left, tool list on the right ──
        UIView* body = [[UIView alloc] init];
        body.translatesAutoresizingMaskIntoConstraints = NO;
        [panel addSubview:body];
        [NSLayoutConstraint activateConstraints:@[
            [body.topAnchor constraintEqualToAnchor:header.bottomAnchor constant:4],
            [body.leadingAnchor constraintEqualToAnchor:panel.leadingAnchor constant:pad],
            [body.trailingAnchor constraintEqualToAnchor:panel.trailingAnchor constant:-pad],
            [body.bottomAnchor constraintEqualToAnchor:panel.bottomAnchor],
        ]];

        // Category sidebar (vertical scroll)
        _catScroll = [[UIScrollView alloc] init];
        _catScroll.showsVerticalScrollIndicator = NO;
        _catScroll.translatesAutoresizingMaskIntoConstraints = NO;
        [body addSubview:_catScroll];
        [NSLayoutConstraint activateConstraints:@[
            [_catScroll.topAnchor constraintEqualToAnchor:body.topAnchor],
            [_catScroll.leadingAnchor constraintEqualToAnchor:body.leadingAnchor],
            [_catScroll.bottomAnchor constraintEqualToAnchor:body.bottomAnchor],
            [_catScroll.widthAnchor constraintEqualToConstant:120],
        ]];
        _catStack = [[UIStackView alloc] init];
        _catStack.axis = UILayoutConstraintAxisVertical;
        _catStack.spacing = 4;
        _catStack.translatesAutoresizingMaskIntoConstraints = NO;
        [_catScroll addSubview:_catStack];
        [NSLayoutConstraint activateConstraints:@[
            [_catStack.topAnchor constraintEqualToAnchor:_catScroll.topAnchor],
            [_catStack.leadingAnchor constraintEqualToAnchor:_catScroll.leadingAnchor],
            [_catStack.trailingAnchor constraintEqualToAnchor:_catScroll.trailingAnchor],
            [_catStack.widthAnchor constraintEqualToAnchor:_catScroll.widthAnchor],
        ]];

        // Tool list (fills remaining width)
        _toolScroll = [[UIScrollView alloc] init];
        _toolScroll.translatesAutoresizingMaskIntoConstraints = NO;
        _toolScroll.alwaysBounceVertical = YES;
        [body addSubview:_toolScroll];
        [NSLayoutConstraint activateConstraints:@[
            [_toolScroll.topAnchor constraintEqualToAnchor:body.topAnchor],
            [_toolScroll.leadingAnchor constraintEqualToAnchor:_catScroll.trailingAnchor constant:10],
            [_toolScroll.trailingAnchor constraintEqualToAnchor:body.trailingAnchor],
            [_toolScroll.bottomAnchor constraintEqualToAnchor:body.bottomAnchor],
        ]];
        _toolStack = [[UIStackView alloc] init];
        _toolStack.axis = UILayoutConstraintAxisVertical;
        _toolStack.spacing = 4;
        _toolStack.translatesAutoresizingMaskIntoConstraints = NO;
        [_toolScroll addSubview:_toolStack];
        [NSLayoutConstraint activateConstraints:@[
            [_toolStack.topAnchor constraintEqualToAnchor:_toolScroll.topAnchor],
            [_toolStack.leadingAnchor constraintEqualToAnchor:_toolScroll.leadingAnchor],
            [_toolStack.trailingAnchor constraintEqualToAnchor:_toolScroll.trailingAnchor],
            [_toolStack.bottomAnchor constraintEqualToAnchor:_toolScroll.bottomAnchor],
            [_toolStack.widthAnchor constraintEqualToAnchor:_toolScroll.widthAnchor],
        ]];
    } else {
        // ── Portrait: categories as horizontal chips above the tool list ──
        _catScroll = [[UIScrollView alloc] init];
        _catScroll.showsHorizontalScrollIndicator = NO;
        _catScroll.translatesAutoresizingMaskIntoConstraints = NO;
        [panel addSubview:_catScroll];
        [NSLayoutConstraint activateConstraints:@[
            [_catScroll.topAnchor constraintEqualToAnchor:header.bottomAnchor constant:8],
            [_catScroll.leadingAnchor constraintEqualToAnchor:panel.leadingAnchor constant:pad],
            [_catScroll.trailingAnchor constraintEqualToAnchor:panel.trailingAnchor constant:-pad],
            [_catScroll.heightAnchor constraintEqualToConstant:40],
        ]];
        _catStack = [[UIStackView alloc] init];
        _catStack.axis = UILayoutConstraintAxisHorizontal;
        _catStack.spacing = 6;
        _catStack.translatesAutoresizingMaskIntoConstraints = NO;
        [_catScroll addSubview:_catStack];
        [NSLayoutConstraint activateConstraints:@[
            [_catStack.topAnchor constraintEqualToAnchor:_catScroll.topAnchor],
            [_catStack.leadingAnchor constraintEqualToAnchor:_catScroll.leadingAnchor],
            [_catStack.trailingAnchor constraintEqualToAnchor:_catScroll.trailingAnchor],
            [_catStack.heightAnchor constraintEqualToAnchor:_catScroll.heightAnchor],
        ]];

        // Tool list
        _toolScroll = [[UIScrollView alloc] init];
        _toolScroll.translatesAutoresizingMaskIntoConstraints = NO;
        _toolScroll.alwaysBounceVertical = YES;
        [panel addSubview:_toolScroll];
        [NSLayoutConstraint activateConstraints:@[
            [_toolScroll.topAnchor constraintEqualToAnchor:_catScroll.bottomAnchor constant:8],
            [_toolScroll.leadingAnchor constraintEqualToAnchor:panel.leadingAnchor constant:pad],
            [_toolScroll.trailingAnchor constraintEqualToAnchor:panel.trailingAnchor constant:-pad],
            [_toolScroll.bottomAnchor constraintEqualToAnchor:panel.bottomAnchor],
        ]];
        _toolStack = [[UIStackView alloc] init];
        _toolStack.axis = UILayoutConstraintAxisVertical;
        _toolStack.spacing = 4;
        _toolStack.translatesAutoresizingMaskIntoConstraints = NO;
        [_toolScroll addSubview:_toolStack];
        [NSLayoutConstraint activateConstraints:@[
            [_toolStack.topAnchor constraintEqualToAnchor:_toolScroll.topAnchor],
            [_toolStack.leadingAnchor constraintEqualToAnchor:_toolScroll.leadingAnchor],
            [_toolStack.trailingAnchor constraintEqualToAnchor:_toolScroll.trailingAnchor],
            [_toolStack.bottomAnchor constraintEqualToAnchor:_toolScroll.bottomAnchor],
            [_toolStack.widthAnchor constraintEqualToAnchor:_toolScroll.widthAnchor],
        ]];
    }

    [self rebuildCategories];
    [self rebuildToolList];
}

- (void)closeTapped {
    [self dismissViewControllerAnimated:YES completion:nil];
}

// ── Search ──

- (void)searchBar:(UISearchBar*)bar textDidChange:(NSString*)text {
    _searchFilter = text ?: @"";
    [self rebuildToolList];
}

// ── Categories ──

- (void)rebuildCategories {
    for (UIView* v in _catStack.arrangedSubviews) [v removeFromSuperview];

    NSMutableOrderedSet<NSString*>* cats = [NSMutableOrderedSet orderedSetWithObject:@"All"];
    for (BPToolItem* t in _allTools) [cats addObject:t.category];

    for (NSString* cat in cats) {
        UIButton* chip = [UIButton new];
        [chip setTitle:cat forState:UIControlStateNormal];
        [chip setTitleColor:[cat isEqualToString:_activeCategory] ? COL_TEXT : COL_DIM
                   forState:UIControlStateNormal];
        chip.titleLabel.font = [UIFont systemFontOfSize:14];
        chip.backgroundColor = [cat isEqualToString:_activeCategory] ? COL_CAT_SEL : COL_CAT;
        chip.layer.cornerRadius = 16;
        chip.contentEdgeInsets = UIEdgeInsetsMake(6, 14, 6, 14);
        [chip addTarget:self action:@selector(catTapped:) forControlEvents:UIControlEventTouchUpInside];
        chip.tag = [cats indexOfObject:cat];
        chip.accessibilityLabel = cat;  // stash the name
        [_catStack addArrangedSubview:chip];
    }
}

- (void)catTapped:(UIButton*)sender {
    _activeCategory = sender.currentTitle;
    [self rebuildCategories];
    [self rebuildToolList];
}

// ── Tool list ──

- (void)rebuildToolList {
    for (UIView* v in _toolStack.arrangedSubviews) [v removeFromSuperview];

    NSString* lastCat = @"";
    for (BPToolItem* t in _allTools) {
        if (![_activeCategory isEqualToString:@"All"] && ![t.category isEqualToString:_activeCategory]) continue;
        if (_searchFilter.length > 0 &&
            [t.name rangeOfString:_searchFilter options:NSCaseInsensitiveSearch].location == NSNotFound &&
            [t.desc rangeOfString:_searchFilter options:NSCaseInsensitiveSearch].location == NSNotFound) continue;

        // Category header in "All" mode
        if ([_activeCategory isEqualToString:@"All"] && ![t.category isEqualToString:lastCat]) {
            lastCat = t.category;
            UILabel* header = [[UILabel alloc] init];
            header.text = [t.category uppercaseString];
            header.textColor = COL_DIM;
            header.font = [UIFont boldSystemFontOfSize:11];
            UIView* wrap = [[UIView alloc] init];
            wrap.translatesAutoresizingMaskIntoConstraints = NO;
            header.translatesAutoresizingMaskIntoConstraints = NO;
            [wrap addSubview:header];
            [NSLayoutConstraint activateConstraints:@[
                [header.leadingAnchor constraintEqualToAnchor:wrap.leadingAnchor constant:4],
                [header.bottomAnchor constraintEqualToAnchor:wrap.bottomAnchor],
                [wrap.heightAnchor constraintEqualToConstant:32],
            ]];
            [_toolStack addArrangedSubview:wrap];
        }

        [_toolStack addArrangedSubview:[self makeToolRow:t]];
    }
}

- (UIView*)makeToolRow:(BPToolItem*)tool {
    UIView* row = [[UIView alloc] init];
    row.backgroundColor = COL_ITEM;
    row.layer.cornerRadius = 10;
    row.translatesAutoresizingMaskIntoConstraints = NO;
    [row.heightAnchor constraintGreaterThanOrEqualToConstant:
        [tool.controlType isEqualToString:@"slider"] ? 72 : 56].active = YES;

    // Icon (SF Symbol)
    UIImageView* icon = [[UIImageView alloc] init];
    UIImageSymbolConfiguration* cfg = [UIImageSymbolConfiguration
        configurationWithPointSize:18 weight:UIImageSymbolWeightMedium];
    icon.image = [UIImage systemImageNamed:BPSFSymbol(tool.icon) withConfiguration:cfg];
    icon.tintColor = tool.color;
    icon.contentMode = UIViewContentModeCenter;
    icon.translatesAutoresizingMaskIntoConstraints = NO;
    [row addSubview:icon];
    [NSLayoutConstraint activateConstraints:@[
        [icon.leadingAnchor constraintEqualToAnchor:row.leadingAnchor constant:12],
        [icon.centerYAnchor constraintEqualToAnchor:row.centerYAnchor],
        [icon.widthAnchor constraintEqualToConstant:28],
    ]];

    // Name
    UILabel* name = [[UILabel alloc] init];
    name.text = tool.name;
    name.textColor = COL_TEXT;
    name.font = [UIFont systemFontOfSize:15];
    name.translatesAutoresizingMaskIntoConstraints = NO;
    [row addSubview:name];

    // Description
    UILabel* desc = [[UILabel alloc] init];
    desc.text = tool.desc;
    desc.textColor = COL_DIM;
    desc.font = [UIFont systemFontOfSize:12];
    desc.numberOfLines = 2;
    desc.lineBreakMode = NSLineBreakByWordWrapping;
    desc.translatesAutoresizingMaskIntoConstraints = NO;
    [row addSubview:desc];

    [NSLayoutConstraint activateConstraints:@[
        [name.leadingAnchor constraintEqualToAnchor:icon.trailingAnchor constant:10],
        [name.topAnchor constraintEqualToAnchor:row.topAnchor constant:10],
        [name.trailingAnchor constraintLessThanOrEqualToAnchor:row.trailingAnchor constant:-120],
        [desc.leadingAnchor constraintEqualToAnchor:name.leadingAnchor],
        [desc.topAnchor constraintEqualToAnchor:name.bottomAnchor constant:2],
        [desc.trailingAnchor constraintEqualToAnchor:name.trailingAnchor],
    ]];

    // Control
    if ([tool.controlType isEqualToString:@"button"]) {
        UIButton* btn = [UIButton new];
        [btn setTitle:@"Run" forState:UIControlStateNormal];
        [btn setTitleColor:UIColor.whiteColor forState:UIControlStateNormal];
        btn.titleLabel.font = [UIFont boldSystemFontOfSize:13];
        btn.backgroundColor = tool.color;
        btn.layer.cornerRadius = 8;
        btn.contentEdgeInsets = UIEdgeInsetsMake(6, 16, 6, 16);
        btn.translatesAutoresizingMaskIntoConstraints = NO;
        // Scale + darken on press for immediate tap feedback.
        [btn addTarget:self action:@selector(buttonTouchDown:) forControlEvents:UIControlEventTouchDown];
        [btn addTarget:self action:@selector(buttonTouchUp:) forControlEvents:UIControlEventTouchUpInside | UIControlEventTouchUpOutside | UIControlEventTouchCancel];
        [btn addTarget:self action:@selector(buttonTapped:) forControlEvents:UIControlEventTouchUpInside];
        btn.accessibilityIdentifier = tool.toolId;
        [row addSubview:btn];
        [NSLayoutConstraint activateConstraints:@[
            [btn.trailingAnchor constraintEqualToAnchor:row.trailingAnchor constant:-12],
            [btn.centerYAnchor constraintEqualToAnchor:row.centerYAnchor],
        ]];
    } else if ([tool.controlType isEqualToString:@"toggle"]) {
        UISwitch* sw = [[UISwitch alloc] init];
        sw.on = tool.toggleValue;
        sw.onTintColor = COL_TOG_ON;
        sw.translatesAutoresizingMaskIntoConstraints = NO;
        sw.accessibilityIdentifier = tool.toolId;
        [sw addTarget:self action:@selector(toggleChanged:) forControlEvents:UIControlEventValueChanged];
        [row addSubview:sw];
        [NSLayoutConstraint activateConstraints:@[
            [sw.trailingAnchor constraintEqualToAnchor:row.trailingAnchor constant:-12],
            [sw.centerYAnchor constraintEqualToAnchor:row.centerYAnchor],
        ]];
    } else if ([tool.controlType isEqualToString:@"slider"]) {
        UILabel* valLabel = [[UILabel alloc] init];
        valLabel.text = [NSString stringWithFormat:@"%.1f", tool.sliderValue];
        valLabel.textColor = COL_DIM;
        valLabel.font = [UIFont monospacedDigitSystemFontOfSize:12 weight:UIFontWeightRegular];
        valLabel.textAlignment = NSTextAlignmentRight;
        valLabel.translatesAutoresizingMaskIntoConstraints = NO;
        valLabel.tag = 999;
        [row addSubview:valLabel];

        UISlider* slider = [[UISlider alloc] init];
        slider.minimumValue = tool.sliderMin;
        slider.maximumValue = tool.sliderMax;
        slider.value = tool.sliderValue;
        slider.tintColor = COL_ACCENT;
        slider.translatesAutoresizingMaskIntoConstraints = NO;
        slider.accessibilityIdentifier = tool.toolId;
        [slider addTarget:self action:@selector(sliderChanged:) forControlEvents:UIControlEventValueChanged];
        [row addSubview:slider];

        [NSLayoutConstraint activateConstraints:@[
            [valLabel.trailingAnchor constraintEqualToAnchor:row.trailingAnchor constant:-12],
            [valLabel.topAnchor constraintEqualToAnchor:row.topAnchor constant:8],
            [valLabel.widthAnchor constraintEqualToConstant:50],
            [slider.trailingAnchor constraintEqualToAnchor:row.trailingAnchor constant:-12],
            [slider.bottomAnchor constraintEqualToAnchor:row.bottomAnchor constant:-8],
            [slider.widthAnchor constraintEqualToConstant:140],
        ]];
    }

    return row;
}

// ── Callbacks ──

- (void)buttonTouchDown:(UIButton*)sender {
    // Instant visual dim + shrink so the tap feels responsive before the
    // script actually runs on the managed side.
    [UIView animateWithDuration:0.08 animations:^{
        sender.alpha = 0.7;
        sender.transform = CGAffineTransformMakeScale(0.94, 0.94);
    }];
}

- (void)buttonTouchUp:(UIButton*)sender {
    [UIView animateWithDuration:0.18 animations:^{
        sender.alpha = 1.0;
        sender.transform = CGAffineTransformIdentity;
    }];
}

- (void)buttonTapped:(UIButton*)sender {
    // Fire the callback immediately so the game runs the action with zero delay.
    [self sendCallback:sender.accessibilityIdentifier action:@"click" value:@""];
    // Flash "Running…" in the button title so the user sees the press registered.
    NSString* original = [sender titleForState:UIControlStateNormal];
    [sender setTitle:@"Running\u2026" forState:UIControlStateNormal];
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.6 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
        [sender setTitle:original forState:UIControlStateNormal];
    });
}

- (void)toggleChanged:(UISwitch*)sender {
    [self sendCallback:sender.accessibilityIdentifier action:@"toggle" value:sender.on ? @"true" : @"false"];
}

- (void)sliderChanged:(UISlider*)sender {
    // Update value label
    UIView* row = sender.superview;
    UILabel* valLabel = [row viewWithTag:999];
    valLabel.text = [NSString stringWithFormat:@"%.1f", sender.value];
    [self sendCallback:sender.accessibilityIdentifier action:@"slider"
                 value:[NSString stringWithFormat:@"%f", sender.value]];
}

- (void)sendCallback:(NSString*)toolId action:(NSString*)action value:(NSString*)value {
    extern void BugpunchUnity_SendMessage(const char*, const char*, const char*);
    NSString* msg = [NSString stringWithFormat:@"%@|%@|%@", toolId, action, value];
    BugpunchUnity_SendMessage("BugpunchToolsBridge", "OnToolAction", [msg UTF8String]);
}

- (UIStatusBarStyle)preferredStatusBarStyle { return UIStatusBarStyleLightContent; }

@end

// ── C API ──

extern "C" {

void Bugpunch_ShowToolsPanel(const char* toolsJson) {
    NSString* json = toolsJson ? [NSString stringWithUTF8String:toolsJson] : @"[]";
    dispatch_async(dispatch_get_main_queue(), ^{
        UIViewController* root = nil;
        if (@available(iOS 13.0, *)) {
            for (UIScene* scene in UIApplication.sharedApplication.connectedScenes) {
                if (![scene isKindOfClass:[UIWindowScene class]]) continue;
                UIWindowScene* ws = (UIWindowScene*)scene;
                for (UIWindow* w in ws.windows) {
                    if (w.rootViewController) { root = w.rootViewController; break; }
                }
                if (root) break;
            }
        }
        if (!root) {
            #pragma clang diagnostic push
            #pragma clang diagnostic ignored "-Wdeprecated-declarations"
            root = UIApplication.sharedApplication.keyWindow.rootViewController;
            #pragma clang diagnostic pop
        }
        if (!root) return;

        // Walk to the topmost presented VC.
        while (root.presentedViewController) root = root.presentedViewController;

        BPToolsViewController* vc = [[BPToolsViewController alloc] initWithToolsJSON:json];
        vc.modalPresentationStyle = UIModalPresentationOverFullScreen;
        vc.modalTransitionStyle = UIModalTransitionStyleCrossDissolve;
        [root presentViewController:vc animated:YES completion:nil];
    });
}

}
