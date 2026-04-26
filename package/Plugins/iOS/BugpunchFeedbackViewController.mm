// BugpunchFeedbackViewController.mm
//
// Native iOS feedback board — the "Request a feature" surface (with voting).
// Phase B part 2 of #29 (the iOS half; Android's BugpunchFeedbackActivity
// shipped earlier in the same release under
// android-src/.../BugpunchFeedbackActivity.java). Replaces the v1 path that
// fell back to the C# UI Toolkit BugpunchFeedbackBoard, which rendered
// inside the Unity surface and looked broken on device.
//
// Three views switched in a single VC by swapping the body container's
// children — same shape as the chat VC and the Android Activity:
//
//   • List   — search field + scroll of feedback rows (vote pill on each
//              row, 48pt+ tap target, accent fill when voted, swallows the
//              row tap so an upvote doesn't open the detail) + bottom
//              "+ New feedback" CTA.
//   • Detail — back chevron header, title, vote button (wide), full body
//              (NSDataDetector URL underlining, no markdown for v1), image
//              attachment thumbs, comments list with author + body, dense
//              comment composer (UITextView + accent send circle).
//   • Submit — title field, multi-line description, attach screenshot, then
//              POST /api/feedback/similarity first; if a match scores
//              > 0.85 the inline similarity prompt offers Vote-for-existing
//              or Post-mine-anyway before creating the duplicate.
//
// Endpoints (all relative to BPDebugMode.shared.config[@"serverUrl"]):
//   GET  /api/feedback?sort=votes
//   POST /api/feedback                          { title, description, attachments, bypassSimilarity? }
//   POST /api/feedback/similarity               { title, description }
//   POST /api/feedback/<id>/vote                {}
//   GET  /api/feedback/<id>/comments
//   POST /api/feedback/<id>/comments            { body, attachments }
//   POST /api/feedback/attachments              (multipart, returns { url, mime, width, height })
//
// Auth: Authorization: Bearer <apiKey> + X-Device-Id: <stable> — server
// resolves projectId from the API key.
//
// Server returns mixed PascalCase / camelCase — every field lookup falls
// back to both keys (id / Id, title / Title, …) like the C# board.
//
// Copyright (c) ODDGames. All rights reserved.

#import <UIKit/UIKit.h>
#import <Foundation/Foundation.h>
#import <objc/runtime.h>

#import "BugpunchTheme.h"
#import "BugpunchStrings.h"

// ── Cross-file deps (mirrors BugpunchChatViewController.mm) ─────

// Stable device id (Keychain UUID). Lives in BugpunchIdentity.mm.
extern "C" const char* Bugpunch_GetStableDeviceId(void);

// Native screenshot — writes a JPEG to disk and fires the callback on the
// main thread. Lives in BugpunchScreenshot.mm.
typedef void (*BugpunchScreenshotCallback)(const char* requestId, int success, const char* payload);
extern "C" void Bugpunch_CaptureScreenshot(const char* requestId, const char* outputPath,
                                           int quality, BugpunchScreenshotCallback cb);

// Shared coordinator config dict (serverUrl / apiKey). Lives in
// BugpunchDebugMode.mm. Forward-declared the same way every other plugin
// file in this folder reads config (BugpunchPoller.mm,
// BugpunchChatViewController.mm, BugpunchDirectives.mm).
@interface BPDebugMode : NSObject
@property (nonatomic, strong) NSDictionary* config;
+ (instancetype)shared;
@end

// ── Helpers ─────────────────────────────────────────────────────

static NSString* BPFbServerUrl(void) {
    NSString* s = [BPDebugMode shared].config[@"serverUrl"];
    if (![s isKindOfClass:[NSString class]]) return @"";
    while ([s hasSuffix:@"/"]) s = [s substringToIndex:s.length - 1];
    return s;
}

static NSString* BPFbApiKey(void) {
    NSString* s = [BPDebugMode shared].config[@"apiKey"];
    return [s isKindOfClass:[NSString class]] ? s : @"";
}

static NSString* BPFbDeviceId(void) {
    const char* c = Bugpunch_GetStableDeviceId();
    return c ? [NSString stringWithUTF8String:c] : @"";
}

/** PascalCase-or-camelCase fallback string lookup matching Android. */
static NSString* BPFbStr(NSDictionary* m, NSString* a, NSString* b, NSString* fallback) {
    if (![m isKindOfClass:[NSDictionary class]]) return fallback;
    id v = m[a];
    if (![v isKindOfClass:[NSString class]] || ((NSString*)v).length == 0) v = m[b];
    if (![v isKindOfClass:[NSString class]]) return fallback;
    return (NSString*)v;
}

/** PascalCase-or-camelCase fallback array lookup. */
static NSArray* BPFbArr(NSDictionary* m, NSString* a, NSString* b) {
    if (![m isKindOfClass:[NSDictionary class]]) return nil;
    id v = m[a];
    if (![v isKindOfClass:[NSArray class]]) v = m[b];
    return [v isKindOfClass:[NSArray class]] ? (NSArray*)v : nil;
}

/** PascalCase-or-camelCase fallback int lookup. */
static NSInteger BPFbInt(NSDictionary* m, NSString* a, NSString* b, NSInteger fallback) {
    if (![m isKindOfClass:[NSDictionary class]]) return fallback;
    id v = m[a];
    if (![v isKindOfClass:[NSNumber class]]) v = m[b];
    return [v isKindOfClass:[NSNumber class]] ? [(NSNumber*)v integerValue] : fallback;
}

/** PascalCase-or-camelCase fallback bool lookup. */
static BOOL BPFbBool(NSDictionary* m, NSString* a, NSString* b, BOOL fallback) {
    if (![m isKindOfClass:[NSDictionary class]]) return fallback;
    id v = m[a];
    if (![v isKindOfClass:[NSNumber class]]) v = m[b];
    return [v isKindOfClass:[NSNumber class]] ? [(NSNumber*)v boolValue] : fallback;
}

/** Mirrors the C# BugpunchMarkdownRenderer.StripForPreview / the Android
 *  Activity's stripMarkdownForPreview, so list previews look the same as
 *  on the dashboard / Editor fallback — markers go away but content stays. */
static NSString* BPFbStripMarkdownForPreview(NSString* s) {
    if (s.length == 0) return @"";
    NSError* err = nil;
    NSMutableString* out = [s mutableCopy];
    NSArray<NSArray*>* patterns = @[
        @[ @"`([^`]+)`",                       @"$1" ],
        @[ @"\\*\\*([^*]+)\\*\\*",             @"$1" ],
        @[ @"\\*([^*]+)\\*",                   @"$1" ],
        @[ @"\\[([^\\]]+)\\]\\((https?://[^)]+)\\)", @"$1" ],
        @[ @"(?m)^\\s*[-*]\\s+",               @"• " ],
        @[ @"(?m)^\\s*\\d+\\.\\s+",            @""   ],
    ];
    for (NSArray* pair in patterns) {
        NSRegularExpression* re = [NSRegularExpression
            regularExpressionWithPattern:pair[0] options:0 error:&err];
        if (!re) continue;
        [re replaceMatchesInString:out options:0
                             range:NSMakeRange(0, out.length)
                      withTemplate:pair[1]];
    }
    return out;
}

// ── Pending attachment ──────────────────────────────────────────

/** One in-flight attachment in either composer (submit form / comment).
 *  Captured locally then uploaded multipart to /api/feedback/attachments,
 *  which returns a server-side URL we embed in the create / comment POST. */
@interface BPFbPendingAttachment : NSObject
@property (nonatomic, copy)   NSString* localPath;
@property (nonatomic, copy)   NSString* url;     // server URL after upload
@property (nonatomic, copy)   NSString* mime;
@property (nonatomic, assign) NSInteger width;
@property (nonatomic, assign) NSInteger height;
@end
@implementation BPFbPendingAttachment
@end

// ── View Controller ─────────────────────────────────────────────

@interface BugpunchFeedbackViewController : UIViewController <UITextFieldDelegate, UITextViewDelegate>
@end

@implementation BugpunchFeedbackViewController {
    // Theme palette — resolved once in viewDidLoad.
    UIColor* _cBg;
    UIColor* _cHeader;
    UIColor* _cCard;
    UIColor* _cBorder;
    UIColor* _cText;
    UIColor* _cTextDim;
    UIColor* _cTextMuted;
    UIColor* _cAccent;
    UIColor* _cAccentFeedback;

    // Chrome
    UIView*       _headerBar;
    UIView*       _bodyContainer;

    // List state
    NSMutableArray<NSDictionary*>* _items;
    NSString*                      _listSearchFilter;

    // Detail state
    NSMutableDictionary*                       _detailItem;       // the currently-open item (mutable so we can flip vote optimistically)
    NSMutableArray<NSDictionary*>*             _detailComments;
    NSMutableArray<BPFbPendingAttachment*>*    _commentDraftAttachments;

    // Submit state — kept across the similarity prompt so "Post mine
    // anyway" can resubmit with the same payload.
    NSString*                                  _pendingTitle;
    NSString*                                  _pendingDescription;
    NSMutableArray<BPFbPendingAttachment*>*    _submitDraftAttachments;

    // Shared session — single keep-alive across all feedback HTTP for the
    // lifetime of the VC. Mirrors BugpunchChatViewController.
    NSURLSession* _session;
}

#pragma mark – Lifecycle

- (void)viewDidLoad {
    [super viewDidLoad];

    _items                   = [NSMutableArray array];
    _detailComments          = [NSMutableArray array];
    _commentDraftAttachments = [NSMutableArray array];
    _submitDraftAttachments  = [NSMutableArray array];
    _listSearchFilter        = @"";

    [self applyTheme];
    self.view.backgroundColor = _cBg;

    NSURLSessionConfiguration* cfg = [NSURLSessionConfiguration defaultSessionConfiguration];
    cfg.timeoutIntervalForRequest = 15.0;
    cfg.timeoutIntervalForResource = 60.0;
    _session = [NSURLSession sessionWithConfiguration:cfg];

    [self buildChrome];
    [self showListView];
}

- (void)dealloc {
    [_session invalidateAndCancel];
}

- (void)applyTheme {
    _cBg             = [BPTheme color:@"backdrop"        fallback:[UIColor colorWithRed:0.063 green:0.071 blue:0.086 alpha:1]];
    _cHeader         = [BPTheme color:@"cardBackground"  fallback:[UIColor colorWithRed:0.106 green:0.122 blue:0.145 alpha:1]];
    _cCard           = [BPTheme color:@"cardBackground"  fallback:[UIColor colorWithRed:0.106 green:0.122 blue:0.145 alpha:1]];
    _cBorder         = [BPTheme color:@"cardBorder"      fallback:[UIColor colorWithRed:0.165 green:0.192 blue:0.224 alpha:1]];
    _cText           = [BPTheme color:@"textPrimary"     fallback:[UIColor colorWithRed:0.945 green:0.957 blue:0.969 alpha:1]];
    _cTextDim        = [BPTheme color:@"textSecondary"   fallback:[UIColor colorWithRed:0.722 green:0.761 blue:0.812 alpha:1]];
    _cTextMuted      = [BPTheme color:@"textMuted"       fallback:[UIColor colorWithRed:0.545 green:0.584 blue:0.635 alpha:1]];
    _cAccent         = [BPTheme color:@"accentPrimary"   fallback:[UIColor colorWithRed:0.20  green:0.38  blue:0.60  alpha:1]];
    _cAccentFeedback = [BPTheme color:@"accentFeedback"  fallback:[UIColor colorWithRed:0.25  green:0.49  blue:0.30  alpha:1]];
}

#pragma mark – Chrome (header + body container)

- (void)buildChrome {
    _headerBar = [[UIView alloc] init];
    _headerBar.translatesAutoresizingMaskIntoConstraints = NO;
    _headerBar.backgroundColor = _cHeader;
    [self.view addSubview:_headerBar];

    UIView* hairline = [[UIView alloc] init];
    hairline.translatesAutoresizingMaskIntoConstraints = NO;
    hairline.backgroundColor = _cBorder;
    hairline.tag = 991;          // -applyHeader: keeps anything with this tag.
    [_headerBar addSubview:hairline];

    _bodyContainer = [[UIView alloc] init];
    _bodyContainer.translatesAutoresizingMaskIntoConstraints = NO;
    _bodyContainer.backgroundColor = _cBg;
    [self.view addSubview:_bodyContainer];

    UILayoutGuide* safe = self.view.safeAreaLayoutGuide;
    [NSLayoutConstraint activateConstraints:@[
        [_headerBar.topAnchor constraintEqualToAnchor:safe.topAnchor],
        [_headerBar.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor],
        [_headerBar.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor],
        [_headerBar.heightAnchor constraintEqualToConstant:60],

        [hairline.leadingAnchor constraintEqualToAnchor:_headerBar.leadingAnchor],
        [hairline.trailingAnchor constraintEqualToAnchor:_headerBar.trailingAnchor],
        [hairline.bottomAnchor constraintEqualToAnchor:_headerBar.bottomAnchor],
        [hairline.heightAnchor constraintEqualToConstant:0.5],

        [_bodyContainer.topAnchor constraintEqualToAnchor:_headerBar.bottomAnchor],
        [_bodyContainer.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor],
        [_bodyContainer.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor],
        [_bodyContainer.bottomAnchor constraintEqualToAnchor:safe.bottomAnchor],
    ]];
}

/** Replace the header's leading control + title text — every view-switch
 *  rebuilds its chrome row inside _headerBar so back / title text matches
 *  the active screen. Mirrors the Android replaceHeader(). */
- (void)applyHeader:(NSString*)title leadingText:(NSString*)leading onLeading:(SEL)leadingSel {
    // Drop everything except the tagged hairline, then re-add the leading
    // control + title for the active view. Mirrors Android replaceHeader().
    for (UIView* v in [_headerBar.subviews copy]) {
        if (v.tag == 991) continue;
        [v removeFromSuperview];
    }

    UIButton* leadingBtn = [UIButton buttonWithType:UIButtonTypeSystem];
    leadingBtn.translatesAutoresizingMaskIntoConstraints = NO;
    [leadingBtn setTitle:leading forState:UIControlStateNormal];
    [leadingBtn setTitleColor:_cTextDim forState:UIControlStateNormal];
    leadingBtn.titleLabel.font = [UIFont systemFontOfSize:[leading isEqualToString:@"✕"] ? 22 : 28];
    [leadingBtn addTarget:self action:leadingSel forControlEvents:UIControlEventTouchUpInside];
    [_headerBar addSubview:leadingBtn];

    UILabel* titleLbl = [[UILabel alloc] init];
    titleLbl.translatesAutoresizingMaskIntoConstraints = NO;
    titleLbl.text = title;
    titleLbl.textColor = _cText;
    titleLbl.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeTitle" fallback:17]];
    [_headerBar addSubview:titleLbl];

    [NSLayoutConstraint activateConstraints:@[
        [leadingBtn.leadingAnchor constraintEqualToAnchor:_headerBar.leadingAnchor constant:8],
        [leadingBtn.centerYAnchor constraintEqualToAnchor:_headerBar.centerYAnchor],
        [leadingBtn.widthAnchor constraintEqualToConstant:40],
        [leadingBtn.heightAnchor constraintEqualToConstant:40],

        [titleLbl.leadingAnchor constraintEqualToAnchor:leadingBtn.trailingAnchor constant:8],
        [titleLbl.centerYAnchor constraintEqualToAnchor:_headerBar.centerYAnchor],
        [titleLbl.trailingAnchor constraintLessThanOrEqualToAnchor:_headerBar.trailingAnchor constant:-12],
    ]];
}

#pragma mark – View switching

- (void)clearBody {
    for (UIView* v in [_bodyContainer.subviews copy]) [v removeFromSuperview];
}

- (void)showListView {
    [self applyHeader:[BPStrings text:@"feedbackTitle" fallback:@"Feedback"]
          leadingText:@"✕"
            onLeading:@selector(onCloseTapped)];
    [self clearBody];
    UIView* body = [self buildListBody];
    body.translatesAutoresizingMaskIntoConstraints = NO;
    [_bodyContainer addSubview:body];
    [NSLayoutConstraint activateConstraints:@[
        [body.topAnchor constraintEqualToAnchor:_bodyContainer.topAnchor],
        [body.leadingAnchor constraintEqualToAnchor:_bodyContainer.leadingAnchor],
        [body.trailingAnchor constraintEqualToAnchor:_bodyContainer.trailingAnchor],
        [body.bottomAnchor constraintEqualToAnchor:_bodyContainer.bottomAnchor],
    ]];
    [self fetchList];
}

- (void)showDetailView:(NSDictionary*)item {
    _detailItem = [item mutableCopy];
    [_detailComments removeAllObjects];
    [_commentDraftAttachments removeAllObjects];

    [self applyHeader:[BPStrings text:@"feedbackDetailTitle" fallback:@"Feedback"]
          leadingText:@"‹"
            onLeading:@selector(showListView)];
    [self renderDetailBody];
    NSString* itemId = BPFbStr(_detailItem, @"id", @"Id", @"");
    [self fetchComments:itemId];
}

- (void)showSubmitView {
    _pendingTitle = @"";
    _pendingDescription = @"";
    [_submitDraftAttachments removeAllObjects];

    [self applyHeader:[BPStrings text:@"feedbackNewTitle" fallback:@"New feedback"]
          leadingText:@"‹"
            onLeading:@selector(showListView)];
    [self renderSubmitBody];
}

- (void)renderDetailBody {
    [self clearBody];
    UIView* body = [self buildDetailBody];
    body.translatesAutoresizingMaskIntoConstraints = NO;
    [_bodyContainer addSubview:body];
    [NSLayoutConstraint activateConstraints:@[
        [body.topAnchor constraintEqualToAnchor:_bodyContainer.topAnchor],
        [body.leadingAnchor constraintEqualToAnchor:_bodyContainer.leadingAnchor],
        [body.trailingAnchor constraintEqualToAnchor:_bodyContainer.trailingAnchor],
        [body.bottomAnchor constraintEqualToAnchor:_bodyContainer.bottomAnchor],
    ]];
}

- (void)renderSubmitBody {
    [self clearBody];
    UIView* body = [self buildSubmitBody];
    body.translatesAutoresizingMaskIntoConstraints = NO;
    [_bodyContainer addSubview:body];
    [NSLayoutConstraint activateConstraints:@[
        [body.topAnchor constraintEqualToAnchor:_bodyContainer.topAnchor],
        [body.leadingAnchor constraintEqualToAnchor:_bodyContainer.leadingAnchor],
        [body.trailingAnchor constraintEqualToAnchor:_bodyContainer.trailingAnchor],
        [body.bottomAnchor constraintEqualToAnchor:_bodyContainer.bottomAnchor],
    ]];
}

#pragma mark – Actions

- (void)onCloseTapped {
    [self dismissViewControllerAnimated:YES completion:nil];
}

#pragma mark – List body

- (UIView*)buildListBody {
    UIView* col = [[UIView alloc] init];
    col.backgroundColor = _cBg;

    UILabel* subtitle = [[UILabel alloc] init];
    subtitle.translatesAutoresizingMaskIntoConstraints = NO;
    subtitle.text = [BPStrings text:@"feedbackSubtitle"
                           fallback:@"Suggest what to build next, or vote on what others have asked for."];
    subtitle.textColor = _cTextDim;
    subtitle.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12] + 1];
    subtitle.numberOfLines = 0;
    [col addSubview:subtitle];

    UITextField* search = [[UITextField alloc] init];
    search.translatesAutoresizingMaskIntoConstraints = NO;
    search.placeholder = [BPStrings text:@"feedbackSearchHint" fallback:@"Search feedback…"];
    search.textColor = _cText;
    search.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
    search.backgroundColor = _cHeader;
    search.layer.cornerRadius = 8;
    search.layer.borderWidth = 1;
    search.layer.borderColor = _cBorder.CGColor;
    search.delegate = self;
    search.returnKeyType = UIReturnKeySearch;
    search.keyboardAppearance = UIKeyboardAppearanceDark;
    search.attributedPlaceholder = [[NSAttributedString alloc]
        initWithString:search.placeholder
            attributes:@{ NSForegroundColorAttributeName: _cTextMuted }];
    UIView* leftPad = [[UIView alloc] initWithFrame:CGRectMake(0, 0, 12, 1)];
    search.leftView = leftPad;
    search.leftViewMode = UITextFieldViewModeAlways;
    UIView* rightPad = [[UIView alloc] initWithFrame:CGRectMake(0, 0, 12, 1)];
    search.rightView = rightPad;
    search.rightViewMode = UITextFieldViewModeAlways;
    [search addTarget:self action:@selector(onSearchChanged:)
     forControlEvents:UIControlEventEditingChanged];
    [col addSubview:search];

    UIScrollView* scroll = [[UIScrollView alloc] init];
    scroll.translatesAutoresizingMaskIntoConstraints = NO;
    scroll.alwaysBounceVertical = YES;
    scroll.keyboardDismissMode = UIScrollViewKeyboardDismissModeOnDrag;
    [col addSubview:scroll];

    UIView* listWrap = [[UIView alloc] init];
    listWrap.translatesAutoresizingMaskIntoConstraints = NO;
    listWrap.tag = 4001; // findViewWithTag-like lookup in -rebuildListInto:.
    [scroll addSubview:listWrap];

    // "+ New feedback" pinned at the bottom.
    UIButton* newBtn = [UIButton buttonWithType:UIButtonTypeCustom];
    newBtn.translatesAutoresizingMaskIntoConstraints = NO;
    [newBtn setTitle:[BPStrings text:@"feedbackNewButton" fallback:@"+ New feedback"]
            forState:UIControlStateNormal];
    [newBtn setTitleColor:[UIColor whiteColor] forState:UIControlStateNormal];
    newBtn.titleLabel.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
    newBtn.backgroundColor = _cAccentFeedback;
    newBtn.layer.cornerRadius = 8;
    newBtn.layer.masksToBounds = YES;
    newBtn.contentEdgeInsets = UIEdgeInsetsMake(12, 16, 12, 16);
    [newBtn addTarget:self action:@selector(showSubmitView) forControlEvents:UIControlEventTouchUpInside];
    [col addSubview:newBtn];

    [NSLayoutConstraint activateConstraints:@[
        [subtitle.topAnchor constraintEqualToAnchor:col.topAnchor constant:12],
        [subtitle.leadingAnchor constraintEqualToAnchor:col.leadingAnchor constant:12],
        [subtitle.trailingAnchor constraintEqualToAnchor:col.trailingAnchor constant:-12],

        [search.topAnchor constraintEqualToAnchor:subtitle.bottomAnchor constant:10],
        [search.leadingAnchor constraintEqualToAnchor:col.leadingAnchor constant:12],
        [search.trailingAnchor constraintEqualToAnchor:col.trailingAnchor constant:-12],
        [search.heightAnchor constraintEqualToConstant:40],

        [scroll.topAnchor constraintEqualToAnchor:search.bottomAnchor constant:8],
        [scroll.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
        [scroll.trailingAnchor constraintEqualToAnchor:col.trailingAnchor],
        [scroll.bottomAnchor constraintEqualToAnchor:newBtn.topAnchor constant:-8],

        [listWrap.topAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.topAnchor constant:0],
        [listWrap.leadingAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.leadingAnchor constant:12],
        [listWrap.trailingAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.trailingAnchor constant:-12],
        [listWrap.bottomAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.bottomAnchor],
        [listWrap.widthAnchor constraintEqualToAnchor:scroll.frameLayoutGuide.widthAnchor constant:-24],

        [newBtn.leadingAnchor constraintEqualToAnchor:col.leadingAnchor constant:12],
        [newBtn.trailingAnchor constraintEqualToAnchor:col.trailingAnchor constant:-12],
        [newBtn.bottomAnchor constraintEqualToAnchor:col.bottomAnchor constant:-12],
    ]];

    [self rebuildListInto:listWrap];
    return col;
}

- (void)onSearchChanged:(UITextField*)tf {
    _listSearchFilter = tf.text ?: @"";
    UIView* listWrap = [self.view viewWithTag:4001];
    if (listWrap) [self rebuildListInto:listWrap];
}

- (void)rebuildListInto:(UIView*)listWrap {
    for (UIView* v in [listWrap.subviews copy]) [v removeFromSuperview];

    NSString* filter = [_listSearchFilter
        stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceAndNewlineCharacterSet]];
    filter = filter.lowercaseString ?: @"";
    NSMutableArray<NSDictionary*>* visible = [NSMutableArray array];
    for (NSDictionary* it in _items) {
        if (filter.length > 0) {
            NSString* title = BPFbStr(it, @"title", @"Title", @"").lowercaseString;
            if ([title rangeOfString:filter].location == NSNotFound) continue;
        }
        [visible addObject:it];
    }

    if (visible.count == 0) {
        UILabel* empty = [[UILabel alloc] init];
        empty.translatesAutoresizingMaskIntoConstraints = NO;
        empty.text = (_items.count == 0)
            ? [BPStrings text:@"feedbackEmpty"
                     fallback:@"No feedback yet. Be the first to suggest something!"]
            : [BPStrings text:@"feedbackNoMatches" fallback:@"No feedback matches your search."];
        empty.textColor = _cTextMuted;
        empty.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12] + 1];
        empty.textAlignment = NSTextAlignmentCenter;
        empty.numberOfLines = 0;
        [listWrap addSubview:empty];
        [NSLayoutConstraint activateConstraints:@[
            [empty.topAnchor constraintEqualToAnchor:listWrap.topAnchor constant:40],
            [empty.leadingAnchor constraintEqualToAnchor:listWrap.leadingAnchor constant:24],
            [empty.trailingAnchor constraintEqualToAnchor:listWrap.trailingAnchor constant:-24],
            [empty.bottomAnchor constraintEqualToAnchor:listWrap.bottomAnchor constant:-40],
        ]];
        return;
    }

    UIView* prev = nil;
    for (NSDictionary* it in visible) {
        UIView* row = [self buildListRow:it];
        row.translatesAutoresizingMaskIntoConstraints = NO;
        [listWrap addSubview:row];
        [NSLayoutConstraint activateConstraints:@[
            [row.leadingAnchor constraintEqualToAnchor:listWrap.leadingAnchor],
            [row.trailingAnchor constraintEqualToAnchor:listWrap.trailingAnchor],
            [row.topAnchor constraintEqualToAnchor:(prev ?: listWrap).bottomAnchor
                                          constant:prev ? 6 : 0],
        ]];
        prev = row;
    }
    if (prev) [prev.bottomAnchor constraintEqualToAnchor:listWrap.bottomAnchor].active = YES;
}

- (UIView*)buildListRow:(NSDictionary*)item {
    UIView* row = [[UIView alloc] init];
    row.backgroundColor = _cCard;
    row.layer.cornerRadius = 8;
    row.layer.borderWidth = 1;
    row.layer.borderColor = _cBorder.CGColor;

    // Tap on row body opens detail. The vote pill installs its own tap
    // recognizer that swallows the touch so an upvote doesn't navigate.
    UITapGestureRecognizer* tap = [[UITapGestureRecognizer alloc]
        initWithTarget:self action:@selector(onRowTapped:)];
    [row addGestureRecognizer:tap];
    objc_setAssociatedObject(row, "bp_item", item, OBJC_ASSOCIATION_RETAIN_NONATOMIC);

    // Text column
    UILabel* titleLabel = [[UILabel alloc] init];
    titleLabel.translatesAutoresizingMaskIntoConstraints = NO;
    titleLabel.text = BPFbStr(item, @"title", @"Title", @"(untitled)");
    titleLabel.textColor = _cText;
    titleLabel.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
    titleLabel.numberOfLines = 1;
    [row addSubview:titleLabel];

    NSString* body = BPFbStr(item, @"body", @"Body", @"");
    UILabel* bodyLabel = nil;
    if (body.length > 0) {
        NSString* preview = BPFbStripMarkdownForPreview(body);
        if (preview.length > 160) preview = [[preview substringToIndex:157] stringByAppendingString:@"…"];
        bodyLabel = [[UILabel alloc] init];
        bodyLabel.translatesAutoresizingMaskIntoConstraints = NO;
        bodyLabel.text = preview;
        bodyLabel.textColor = _cTextDim;
        bodyLabel.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];
        bodyLabel.numberOfLines = 3;
        [row addSubview:bodyLabel];
    }

    NSArray* atts = BPFbArr(item, @"attachments", @"Attachments");
    UILabel* badge = nil;
    if (atts.count > 0) {
        badge = [[UILabel alloc] init];
        badge.translatesAutoresizingMaskIntoConstraints = NO;
        NSInteger n = (NSInteger)atts.count;
        badge.text = [NSString stringWithFormat:@"📎 %ld %@",
                      (long)n, n == 1 ? @"image" : @"images"];
        badge.textColor = _cTextDim;
        badge.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];
        [row addSubview:badge];
    }

    // Vote pill (vertical) — accent fill when voted.
    BOOL hasMyVote = BPFbBool(item, @"hasMyVote", @"HasMyVote", NO);
    NSInteger voteCount = BPFbInt(item, @"voteCount", @"VoteCount", 0);
    UIView* pill = [self buildVotePill:hasMyVote count:voteCount wide:NO];
    pill.translatesAutoresizingMaskIntoConstraints = NO;
    objc_setAssociatedObject(pill, "bp_item", item, OBJC_ASSOCIATION_RETAIN_NONATOMIC);
    UITapGestureRecognizer* voteTap = [[UITapGestureRecognizer alloc]
        initWithTarget:self action:@selector(onVotePillTapped:)];
    voteTap.cancelsTouchesInView = YES;
    [pill addGestureRecognizer:voteTap];
    [row addSubview:pill];

    [NSLayoutConstraint activateConstraints:@[
        [titleLabel.topAnchor constraintEqualToAnchor:row.topAnchor constant:10],
        [titleLabel.leadingAnchor constraintEqualToAnchor:row.leadingAnchor constant:12],
        [titleLabel.trailingAnchor constraintEqualToAnchor:pill.leadingAnchor constant:-12],

        [pill.trailingAnchor constraintEqualToAnchor:row.trailingAnchor constant:-8],
        [pill.centerYAnchor constraintEqualToAnchor:row.centerYAnchor],
        [pill.widthAnchor constraintGreaterThanOrEqualToConstant:56],
        [pill.heightAnchor constraintGreaterThanOrEqualToConstant:48],
    ]];

    UIView* lastText = titleLabel;
    if (bodyLabel) {
        [NSLayoutConstraint activateConstraints:@[
            [bodyLabel.topAnchor constraintEqualToAnchor:lastText.bottomAnchor constant:2],
            [bodyLabel.leadingAnchor constraintEqualToAnchor:row.leadingAnchor constant:12],
            [bodyLabel.trailingAnchor constraintEqualToAnchor:pill.leadingAnchor constant:-12],
        ]];
        lastText = bodyLabel;
    }
    if (badge) {
        [NSLayoutConstraint activateConstraints:@[
            [badge.topAnchor constraintEqualToAnchor:lastText.bottomAnchor constant:2],
            [badge.leadingAnchor constraintEqualToAnchor:row.leadingAnchor constant:12],
            [badge.trailingAnchor constraintEqualToAnchor:pill.leadingAnchor constant:-12],
        ]];
        lastText = badge;
    }
    [lastText.bottomAnchor constraintEqualToAnchor:row.bottomAnchor constant:-10].active = YES;

    return row;
}

- (void)onRowTapped:(UITapGestureRecognizer*)g {
    NSDictionary* it = objc_getAssociatedObject(g.view, "bp_item");
    if (it) [self showDetailView:it];
}

- (void)onVotePillTapped:(UITapGestureRecognizer*)g {
    NSDictionary* it = objc_getAssociatedObject(g.view, "bp_item");
    if (it) [self toggleVote:it];
}

/** Vote pill — vertical (▲ on top, count below) for list rows; horizontal
 *  (▲ then "N votes") for the detail view. 48pt+ tap target. */
- (UIView*)buildVotePill:(BOOL)voted count:(NSInteger)count wide:(BOOL)wide {
    UIView* pill = [[UIView alloc] init];
    pill.layer.cornerRadius = 8;
    pill.userInteractionEnabled = YES;
    [self applyVoteFill:pill voted:voted];

    UILabel* arrow = [[UILabel alloc] init];
    arrow.translatesAutoresizingMaskIntoConstraints = NO;
    arrow.text = @"▲";
    arrow.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];
    arrow.textColor = voted ? _cText : _cTextDim;
    arrow.textAlignment = NSTextAlignmentCenter;
    [pill addSubview:arrow];

    UILabel* countView = [[UILabel alloc] init];
    countView.translatesAutoresizingMaskIntoConstraints = NO;
    if (wide) {
        countView.text = [NSString stringWithFormat:@"%ld %@",
                          (long)count, count == 1 ? @"vote" : @"votes"];
        countView.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12] + 1];
    } else {
        countView.text = [NSString stringWithFormat:@"%ld", (long)count];
        countView.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];
    }
    countView.textColor = voted ? _cText : _cTextDim;
    countView.textAlignment = NSTextAlignmentCenter;
    [pill addSubview:countView];

    if (wide) {
        [NSLayoutConstraint activateConstraints:@[
            [arrow.leadingAnchor constraintEqualToAnchor:pill.leadingAnchor constant:12],
            [arrow.centerYAnchor constraintEqualToAnchor:pill.centerYAnchor],

            [countView.leadingAnchor constraintEqualToAnchor:arrow.trailingAnchor constant:6],
            [countView.trailingAnchor constraintEqualToAnchor:pill.trailingAnchor constant:-12],
            [countView.topAnchor constraintEqualToAnchor:pill.topAnchor constant:8],
            [countView.bottomAnchor constraintEqualToAnchor:pill.bottomAnchor constant:-8],
        ]];
    } else {
        [NSLayoutConstraint activateConstraints:@[
            [arrow.topAnchor constraintEqualToAnchor:pill.topAnchor constant:8],
            [arrow.centerXAnchor constraintEqualToAnchor:pill.centerXAnchor],
            [arrow.leadingAnchor constraintEqualToAnchor:pill.leadingAnchor constant:10],
            [arrow.trailingAnchor constraintEqualToAnchor:pill.trailingAnchor constant:-10],

            [countView.topAnchor constraintEqualToAnchor:arrow.bottomAnchor constant:2],
            [countView.centerXAnchor constraintEqualToAnchor:pill.centerXAnchor],
            [countView.bottomAnchor constraintEqualToAnchor:pill.bottomAnchor constant:-8],
        ]];
    }
    return pill;
}

- (void)applyVoteFill:(UIView*)pill voted:(BOOL)voted {
    pill.backgroundColor = voted ? _cAccentFeedback : _cBorder;
    pill.layer.borderWidth = 1;
    pill.layer.borderColor = (voted ? _cAccentFeedback : _cBorder).CGColor;
}

#pragma mark – Detail body

- (UIView*)buildDetailBody {
    UIScrollView* scroll = [[UIScrollView alloc] init];
    scroll.backgroundColor = _cBg;
    scroll.alwaysBounceVertical = YES;
    scroll.keyboardDismissMode = UIScrollViewKeyboardDismissModeOnDrag;

    UIView* col = [[UIView alloc] init];
    col.translatesAutoresizingMaskIntoConstraints = NO;
    [scroll addSubview:col];

    [NSLayoutConstraint activateConstraints:@[
        [col.topAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.topAnchor constant:12],
        [col.leadingAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.leadingAnchor constant:12],
        [col.trailingAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.trailingAnchor constant:-12],
        [col.bottomAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.bottomAnchor constant:-12],
        [col.widthAnchor constraintEqualToAnchor:scroll.frameLayoutGuide.widthAnchor constant:-24],
    ]];

    if (!_detailItem) {
        UILabel* err = [[UILabel alloc] init];
        err.translatesAutoresizingMaskIntoConstraints = NO;
        err.text = [BPStrings text:@"feedbackDetailMissing"
                          fallback:@"Could not load feedback item."];
        err.textColor = _cTextMuted;
        [col addSubview:err];
        [NSLayoutConstraint activateConstraints:@[
            [err.topAnchor constraintEqualToAnchor:col.topAnchor],
            [err.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
            [err.trailingAnchor constraintEqualToAnchor:col.trailingAnchor],
            [err.bottomAnchor constraintEqualToAnchor:col.bottomAnchor],
        ]];
        return scroll;
    }

    UIView* prev = nil;

    UILabel* titleLabel = [[UILabel alloc] init];
    titleLabel.translatesAutoresizingMaskIntoConstraints = NO;
    titleLabel.text = BPFbStr(_detailItem, @"title", @"Title", @"(untitled)");
    titleLabel.textColor = _cText;
    titleLabel.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeTitle" fallback:17] - 1];
    titleLabel.numberOfLines = 0;
    [col addSubview:titleLabel];
    [NSLayoutConstraint activateConstraints:@[
        [titleLabel.topAnchor constraintEqualToAnchor:col.topAnchor],
        [titleLabel.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
        [titleLabel.trailingAnchor constraintEqualToAnchor:col.trailingAnchor],
    ]];
    prev = titleLabel;

    // Wide vote pill directly under the title.
    BOOL hasMyVote = BPFbBool(_detailItem, @"hasMyVote", @"HasMyVote", NO);
    NSInteger voteCount = BPFbInt(_detailItem, @"voteCount", @"VoteCount", 0);
    UIView* pill = [self buildVotePill:hasMyVote count:voteCount wide:YES];
    pill.translatesAutoresizingMaskIntoConstraints = NO;
    objc_setAssociatedObject(pill, "bp_item", _detailItem, OBJC_ASSOCIATION_RETAIN_NONATOMIC);
    UITapGestureRecognizer* voteTap = [[UITapGestureRecognizer alloc]
        initWithTarget:self action:@selector(onVotePillTapped:)];
    [pill addGestureRecognizer:voteTap];
    [col addSubview:pill];
    [NSLayoutConstraint activateConstraints:@[
        [pill.topAnchor constraintEqualToAnchor:prev.bottomAnchor constant:8],
        [pill.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
        [pill.heightAnchor constraintGreaterThanOrEqualToConstant:48],
    ]];
    prev = pill;

    NSString* body = BPFbStr(_detailItem, @"body", @"Body", @"");
    if (body.length > 0) {
        UILabel* bodyView = [[UILabel alloc] init];
        bodyView.translatesAutoresizingMaskIntoConstraints = NO;
        bodyView.numberOfLines = 0;
        bodyView.textColor = _cTextDim;
        bodyView.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12] + 1];
        NSAttributedString* linked = [self linkifyText:body
                                                 color:bodyView.textColor
                                                  font:bodyView.font];
        if (linked) bodyView.attributedText = linked; else bodyView.text = body;
        [col addSubview:bodyView];
        [NSLayoutConstraint activateConstraints:@[
            [bodyView.topAnchor constraintEqualToAnchor:prev.bottomAnchor constant:10],
            [bodyView.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
            [bodyView.trailingAnchor constraintEqualToAnchor:col.trailingAnchor],
        ]];
        prev = bodyView;
    }

    NSArray* atts = BPFbArr(_detailItem, @"attachments", @"Attachments");
    for (id raw in (atts ?: @[])) {
        if (![raw isKindOfClass:[NSDictionary class]]) continue;
        UIView* thumb = [self buildAttachmentThumb:(NSDictionary*)raw];
        if (!thumb) continue;
        thumb.translatesAutoresizingMaskIntoConstraints = NO;
        [col addSubview:thumb];
        [NSLayoutConstraint activateConstraints:@[
            [thumb.topAnchor constraintEqualToAnchor:prev.bottomAnchor constant:6],
            [thumb.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
        ]];
        prev = thumb;
    }

    UILabel* commentsHeader = [[UILabel alloc] init];
    commentsHeader.translatesAutoresizingMaskIntoConstraints = NO;
    commentsHeader.text = [BPStrings text:@"feedbackComments" fallback:@"Comments"];
    commentsHeader.textColor = _cText;
    commentsHeader.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
    [col addSubview:commentsHeader];
    [NSLayoutConstraint activateConstraints:@[
        [commentsHeader.topAnchor constraintEqualToAnchor:prev.bottomAnchor constant:14],
        [commentsHeader.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
    ]];
    prev = commentsHeader;

    UIView* commentsList = [[UIView alloc] init];
    commentsList.translatesAutoresizingMaskIntoConstraints = NO;
    commentsList.tag = 4002;
    [col addSubview:commentsList];
    [NSLayoutConstraint activateConstraints:@[
        [commentsList.topAnchor constraintEqualToAnchor:prev.bottomAnchor constant:6],
        [commentsList.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
        [commentsList.trailingAnchor constraintEqualToAnchor:col.trailingAnchor],
    ]];
    prev = commentsList;

    UIView* composer = [self buildCommentComposer];
    composer.translatesAutoresizingMaskIntoConstraints = NO;
    [col addSubview:composer];
    [NSLayoutConstraint activateConstraints:@[
        [composer.topAnchor constraintEqualToAnchor:prev.bottomAnchor constant:12],
        [composer.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
        [composer.trailingAnchor constraintEqualToAnchor:col.trailingAnchor],
        [composer.bottomAnchor constraintEqualToAnchor:col.bottomAnchor],
    ]];

    [self renderComments:_detailComments];

    return scroll;
}

- (void)renderComments:(NSArray<NSDictionary*>*)comments {
    UIView* list = [self.view viewWithTag:4002];
    if (!list) return;
    for (UIView* v in [list.subviews copy]) [v removeFromSuperview];

    if (comments.count == 0) {
        UILabel* empty = [[UILabel alloc] init];
        empty.translatesAutoresizingMaskIntoConstraints = NO;
        empty.text = [BPStrings text:@"feedbackCommentsEmpty"
                            fallback:@"No comments yet. Be the first to reply."];
        empty.textColor = _cTextMuted;
        empty.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];
        [list addSubview:empty];
        [NSLayoutConstraint activateConstraints:@[
            [empty.topAnchor constraintEqualToAnchor:list.topAnchor],
            [empty.leadingAnchor constraintEqualToAnchor:list.leadingAnchor],
            [empty.trailingAnchor constraintEqualToAnchor:list.trailingAnchor],
            [empty.bottomAnchor constraintEqualToAnchor:list.bottomAnchor],
        ]];
        return;
    }

    UIView* prev = nil;
    for (NSDictionary* c in comments) {
        UIView* row = [self buildCommentRow:c];
        row.translatesAutoresizingMaskIntoConstraints = NO;
        [list addSubview:row];
        [NSLayoutConstraint activateConstraints:@[
            [row.leadingAnchor constraintEqualToAnchor:list.leadingAnchor],
            [row.trailingAnchor constraintEqualToAnchor:list.trailingAnchor],
            [row.topAnchor constraintEqualToAnchor:(prev ?: list).bottomAnchor constant:prev ? 6 : 0],
        ]];
        prev = row;
    }
    if (prev) [prev.bottomAnchor constraintEqualToAnchor:list.bottomAnchor].active = YES;
}

- (UIView*)buildCommentRow:(NSDictionary*)c {
    UIView* card = [[UIView alloc] init];
    card.backgroundColor = _cCard;
    card.layer.cornerRadius = 8;
    card.layer.borderWidth = 1;
    card.layer.borderColor = _cBorder.CGColor;

    BOOL staff = BPFbBool(c, @"authorIsStaff", @"AuthorIsStaff", NO);
    NSString* name = BPFbStr(c, @"authorName", @"AuthorName", @"");
    if (name.length == 0) name = @"Anonymous";

    UILabel* nameView = [[UILabel alloc] init];
    nameView.translatesAutoresizingMaskIntoConstraints = NO;
    nameView.text = name;
    nameView.textColor = _cText;
    nameView.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];
    [card addSubview:nameView];

    UIView* lastTop = nameView;

    UILabel* badge = nil;
    if (staff) {
        badge = [[UILabel alloc] init];
        badge.translatesAutoresizingMaskIntoConstraints = NO;
        badge.text = @" staff ";
        badge.textColor = [UIColor whiteColor];
        badge.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12] - 2];
        badge.backgroundColor = _cAccent;
        badge.layer.cornerRadius = 3;
        badge.layer.masksToBounds = YES;
        [card addSubview:badge];
        [NSLayoutConstraint activateConstraints:@[
            [badge.leadingAnchor constraintEqualToAnchor:nameView.trailingAnchor constant:6],
            [badge.centerYAnchor constraintEqualToAnchor:nameView.centerYAnchor],
        ]];
    }

    BOOL deleted = BPFbStr(c, @"deletedAt", @"DeletedAt", @"").length > 0;
    if (deleted) {
        UILabel* del = [[UILabel alloc] init];
        del.translatesAutoresizingMaskIntoConstraints = NO;
        del.text = @"[deleted]";
        del.textColor = _cTextMuted;
        del.font = [UIFont italicSystemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];
        [card addSubview:del];
        [NSLayoutConstraint activateConstraints:@[
            [nameView.topAnchor constraintEqualToAnchor:card.topAnchor constant:10],
            [nameView.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:10],
            [del.topAnchor constraintEqualToAnchor:nameView.bottomAnchor constant:2],
            [del.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:10],
            [del.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-10],
            [del.bottomAnchor constraintEqualToAnchor:card.bottomAnchor constant:-10],
        ]];
        return card;
    }

    NSString* body = BPFbStr(c, @"body", @"Body", @"");
    UILabel* bodyView = nil;
    if (body.length > 0) {
        bodyView = [[UILabel alloc] init];
        bodyView.translatesAutoresizingMaskIntoConstraints = NO;
        bodyView.numberOfLines = 0;
        bodyView.textColor = _cTextDim;
        bodyView.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];
        NSAttributedString* linked = [self linkifyText:body
                                                 color:bodyView.textColor
                                                  font:bodyView.font];
        if (linked) bodyView.attributedText = linked; else bodyView.text = body;
        [card addSubview:bodyView];
    }

    [NSLayoutConstraint activateConstraints:@[
        [nameView.topAnchor constraintEqualToAnchor:card.topAnchor constant:10],
        [nameView.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:10],
    ]];

    if (bodyView) {
        [NSLayoutConstraint activateConstraints:@[
            [bodyView.topAnchor constraintEqualToAnchor:lastTop.bottomAnchor constant:2],
            [bodyView.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:10],
            [bodyView.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-10],
        ]];
        lastTop = bodyView;
    }

    UIView* prevAtt = lastTop;
    NSArray* atts = BPFbArr(c, @"attachments", @"Attachments");
    for (id raw in (atts ?: @[])) {
        if (![raw isKindOfClass:[NSDictionary class]]) continue;
        UIView* thumb = [self buildAttachmentThumb:(NSDictionary*)raw];
        if (!thumb) continue;
        thumb.translatesAutoresizingMaskIntoConstraints = NO;
        [card addSubview:thumb];
        [NSLayoutConstraint activateConstraints:@[
            [thumb.topAnchor constraintEqualToAnchor:prevAtt.bottomAnchor constant:6],
            [thumb.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:10],
        ]];
        prevAtt = thumb;
    }
    [prevAtt.bottomAnchor constraintEqualToAnchor:card.bottomAnchor constant:-10].active = YES;

    return card;
}

#pragma mark – Comment composer

- (UIView*)buildCommentComposer {
    UIView* column = [[UIView alloc] init];
    column.backgroundColor = _cHeader;
    column.layer.cornerRadius = 8;

    // Pending-attachment chip strip above the input row.
    UIScrollView* strip = [[UIScrollView alloc] init];
    strip.translatesAutoresizingMaskIntoConstraints = NO;
    strip.tag = 4011;
    strip.showsHorizontalScrollIndicator = NO;
    strip.alwaysBounceHorizontal = NO;
    strip.hidden = YES;
    [column addSubview:strip];

    UIView* stripContent = [[UIView alloc] init];
    stripContent.translatesAutoresizingMaskIntoConstraints = NO;
    stripContent.tag = 4012;
    [strip addSubview:stripContent];

    NSLayoutConstraint* stripHeight = [strip.heightAnchor constraintEqualToConstant:0];
    stripHeight.identifier = @"stripHeight";
    objc_setAssociatedObject(strip, "bp_height", stripHeight, OBJC_ASSOCIATION_RETAIN_NONATOMIC);
    stripHeight.active = YES;

    // Input row.
    UIView* bar = [[UIView alloc] init];
    bar.translatesAutoresizingMaskIntoConstraints = NO;
    [column addSubview:bar];

    // Attach (📷 screenshot only) circle button — keeps parity with the
    // chat composer styling, but no multi-option pill since v1 supports
    // image attachments only on feedback.
    UIButton* attach = [UIButton buttonWithType:UIButtonTypeSystem];
    attach.translatesAutoresizingMaskIntoConstraints = NO;
    [attach setTitle:@"📷" forState:UIControlStateNormal];
    [attach setTitleColor:_cText forState:UIControlStateNormal];
    attach.titleLabel.font = [UIFont systemFontOfSize:16];
    attach.backgroundColor = _cBorder;
    attach.layer.cornerRadius = 20;
    attach.layer.masksToBounds = YES;
    [attach addTarget:self action:@selector(onCommentAttachTapped) forControlEvents:UIControlEventTouchUpInside];
    [bar addSubview:attach];

    UITextView* composer = [[UITextView alloc] init];
    composer.translatesAutoresizingMaskIntoConstraints = NO;
    composer.tag = 4013;
    composer.delegate = self;
    composer.backgroundColor = _cBg;
    composer.textColor = _cText;
    composer.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
    composer.layer.borderWidth = 1.0;
    composer.layer.borderColor = _cBorder.CGColor;
    composer.layer.cornerRadius = 20;
    composer.textContainerInset = UIEdgeInsetsMake(10, 10, 10, 10);
    composer.scrollEnabled = NO;
    composer.returnKeyType = UIReturnKeyDefault;
    composer.keyboardAppearance = UIKeyboardAppearanceDark;
    [bar addSubview:composer];

    UILabel* placeholder = [[UILabel alloc] init];
    placeholder.translatesAutoresizingMaskIntoConstraints = NO;
    placeholder.tag = 4014;
    placeholder.text = [BPStrings text:@"feedbackCommentHint" fallback:@"Reply to this thread…"];
    placeholder.textColor = _cTextMuted;
    placeholder.font = composer.font;
    placeholder.userInteractionEnabled = NO;
    [composer addSubview:placeholder];

    UIButton* sendBtn = [UIButton buttonWithType:UIButtonTypeCustom];
    sendBtn.translatesAutoresizingMaskIntoConstraints = NO;
    sendBtn.tag = 4015;
    [sendBtn setTitle:@"➤" forState:UIControlStateNormal];
    [sendBtn setTitleColor:[UIColor whiteColor] forState:UIControlStateNormal];
    sendBtn.titleLabel.font = [UIFont boldSystemFontOfSize:18];
    sendBtn.backgroundColor = _cAccentFeedback;
    sendBtn.layer.cornerRadius = 20;
    sendBtn.layer.masksToBounds = YES;
    sendBtn.alpha = 0.4;
    sendBtn.enabled = NO;
    [sendBtn addTarget:self action:@selector(onCommentSendTapped) forControlEvents:UIControlEventTouchUpInside];
    [bar addSubview:sendBtn];

    [NSLayoutConstraint activateConstraints:@[
        [strip.topAnchor constraintEqualToAnchor:column.topAnchor],
        [strip.leadingAnchor constraintEqualToAnchor:column.leadingAnchor],
        [strip.trailingAnchor constraintEqualToAnchor:column.trailingAnchor],

        [stripContent.topAnchor constraintEqualToAnchor:strip.contentLayoutGuide.topAnchor constant:6],
        [stripContent.leadingAnchor constraintEqualToAnchor:strip.contentLayoutGuide.leadingAnchor constant:8],
        [stripContent.trailingAnchor constraintEqualToAnchor:strip.contentLayoutGuide.trailingAnchor constant:-8],
        [stripContent.bottomAnchor constraintEqualToAnchor:strip.contentLayoutGuide.bottomAnchor constant:-6],

        [bar.topAnchor constraintEqualToAnchor:strip.bottomAnchor],
        [bar.leadingAnchor constraintEqualToAnchor:column.leadingAnchor constant:8],
        [bar.trailingAnchor constraintEqualToAnchor:column.trailingAnchor constant:-8],
        [bar.bottomAnchor constraintEqualToAnchor:column.bottomAnchor constant:-8],

        [attach.leadingAnchor constraintEqualToAnchor:bar.leadingAnchor],
        [attach.bottomAnchor constraintEqualToAnchor:bar.bottomAnchor constant:-2],
        [attach.widthAnchor constraintEqualToConstant:40],
        [attach.heightAnchor constraintEqualToConstant:40],

        [composer.leadingAnchor constraintEqualToAnchor:attach.trailingAnchor constant:8],
        [composer.topAnchor constraintEqualToAnchor:bar.topAnchor constant:4],
        [composer.bottomAnchor constraintEqualToAnchor:bar.bottomAnchor constant:-2],
        [composer.trailingAnchor constraintEqualToAnchor:sendBtn.leadingAnchor constant:-8],
        [composer.heightAnchor constraintGreaterThanOrEqualToConstant:40],
        [composer.heightAnchor constraintLessThanOrEqualToConstant:120],

        [placeholder.leadingAnchor constraintEqualToAnchor:composer.leadingAnchor constant:14],
        [placeholder.topAnchor constraintEqualToAnchor:composer.topAnchor constant:10],

        [sendBtn.trailingAnchor constraintEqualToAnchor:bar.trailingAnchor],
        [sendBtn.bottomAnchor constraintEqualToAnchor:bar.bottomAnchor constant:-2],
        [sendBtn.widthAnchor constraintEqualToConstant:40],
        [sendBtn.heightAnchor constraintEqualToConstant:40],
    ]];

    return column;
}

- (void)textViewDidChange:(UITextView *)textView {
    if (textView.tag == 4013) {
        UILabel* placeholder = (UILabel*)[textView viewWithTag:4014];
        placeholder.hidden = textView.text.length > 0;
        [self updateCommentSendEnabled];
    }
}

- (void)updateCommentSendEnabled {
    UITextView* composer = (UITextView*)[self.view viewWithTag:4013];
    UIButton* sendBtn = (UIButton*)[self.view viewWithTag:4015];
    if (!composer || !sendBtn) return;
    NSString* trimmed = [composer.text stringByTrimmingCharactersInSet:
        [NSCharacterSet whitespaceAndNewlineCharacterSet]];
    BOOL ok = trimmed.length > 0 || _commentDraftAttachments.count > 0;
    sendBtn.enabled = ok;
    sendBtn.alpha = ok ? 1.0 : 0.4;
}

- (void)onCommentAttachTapped {
    [self captureScreenshotForSubmit:NO];
}

- (void)onCommentSendTapped {
    if (!_detailItem) return;
    UITextView* composer = (UITextView*)[self.view viewWithTag:4013];
    NSString* draft = [composer.text stringByTrimmingCharactersInSet:
        [NSCharacterSet whitespaceAndNewlineCharacterSet]] ?: @"";
    if (draft.length == 0 && _commentDraftAttachments.count == 0) return;
    NSString* itemId = BPFbStr(_detailItem, @"id", @"Id", @"");
    if (itemId.length == 0) return;

    [self postComment:itemId body:draft completion:^(BOOL ok) {
        if (!ok) return;
        composer.text = @"";
        UILabel* placeholder = (UILabel*)[composer viewWithTag:4014];
        placeholder.hidden = NO;
        [self->_commentDraftAttachments removeAllObjects];
        [self rebuildCommentStrip];
        [self updateCommentSendEnabled];
        [self fetchComments:itemId];
    }];
}

- (void)rebuildCommentStrip {
    UIScrollView* strip = (UIScrollView*)[self.view viewWithTag:4011];
    UIView* stripContent = [self.view viewWithTag:4012];
    if (!strip || !stripContent) return;
    NSLayoutConstraint* h = objc_getAssociatedObject(strip, "bp_height");

    for (UIView* v in [stripContent.subviews copy]) [v removeFromSuperview];
    if (_commentDraftAttachments.count == 0) {
        strip.hidden = YES;
        h.constant = 0;
        return;
    }
    strip.hidden = NO;
    h.constant = 60;

    UIView* prev = nil;
    for (BPFbPendingAttachment* a in _commentDraftAttachments) {
        UIView* chip = [self buildAttachmentChip:a list:_commentDraftAttachments stripTag:4011];
        chip.translatesAutoresizingMaskIntoConstraints = NO;
        [stripContent addSubview:chip];
        [NSLayoutConstraint activateConstraints:@[
            [chip.topAnchor constraintEqualToAnchor:stripContent.topAnchor],
            [chip.bottomAnchor constraintEqualToAnchor:stripContent.bottomAnchor],
        ]];
        if (prev) {
            [chip.leadingAnchor constraintEqualToAnchor:prev.trailingAnchor constant:6].active = YES;
        } else {
            [chip.leadingAnchor constraintEqualToAnchor:stripContent.leadingAnchor].active = YES;
        }
        prev = chip;
    }
    if (prev) {
        [prev.trailingAnchor constraintLessThanOrEqualToAnchor:stripContent.trailingAnchor].active = YES;
    }
}

#pragma mark – Submit body

- (UIView*)buildSubmitBody {
    UIScrollView* scroll = [[UIScrollView alloc] init];
    scroll.backgroundColor = _cBg;
    scroll.alwaysBounceVertical = YES;
    scroll.keyboardDismissMode = UIScrollViewKeyboardDismissModeOnDrag;

    UIView* col = [[UIView alloc] init];
    col.translatesAutoresizingMaskIntoConstraints = NO;
    [scroll addSubview:col];
    [NSLayoutConstraint activateConstraints:@[
        [col.topAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.topAnchor constant:12],
        [col.leadingAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.leadingAnchor constant:12],
        [col.trailingAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.trailingAnchor constant:-12],
        [col.bottomAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.bottomAnchor constant:-12],
        [col.widthAnchor constraintEqualToAnchor:scroll.frameLayoutGuide.widthAnchor constant:-24],
    ]];

    UILabel* intro = [[UILabel alloc] init];
    intro.translatesAutoresizingMaskIntoConstraints = NO;
    intro.text = [BPStrings text:@"feedbackNewIntro"
                        fallback:@"Write a short title and (optionally) describe what you'd like."];
    intro.textColor = _cTextDim;
    intro.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12] + 1];
    intro.numberOfLines = 0;
    [col addSubview:intro];

    UILabel* titleLabel = [self buildFieldLabel:[BPStrings text:@"feedbackTitleLabel" fallback:@"Title"]];
    [col addSubview:titleLabel];

    UITextField* titleField = [[UITextField alloc] init];
    titleField.translatesAutoresizingMaskIntoConstraints = NO;
    titleField.tag = 4021;
    titleField.placeholder = [BPStrings text:@"feedbackTitleHint" fallback:@"One-line summary"];
    titleField.textColor = _cText;
    titleField.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
    titleField.backgroundColor = _cHeader;
    titleField.layer.cornerRadius = 8;
    titleField.layer.borderWidth = 1;
    titleField.layer.borderColor = _cBorder.CGColor;
    titleField.attributedPlaceholder = [[NSAttributedString alloc]
        initWithString:titleField.placeholder
            attributes:@{ NSForegroundColorAttributeName: _cTextMuted }];
    titleField.returnKeyType = UIReturnKeyNext;
    titleField.keyboardAppearance = UIKeyboardAppearanceDark;
    titleField.delegate = self;
    UIView* leftPad = [[UIView alloc] initWithFrame:CGRectMake(0, 0, 12, 1)];
    titleField.leftView = leftPad;
    titleField.leftViewMode = UITextFieldViewModeAlways;
    [titleField addTarget:self action:@selector(onSubmitTitleChanged:)
         forControlEvents:UIControlEventEditingChanged];
    [col addSubview:titleField];

    UILabel* descLabel = [self buildFieldLabel:
        [BPStrings text:@"feedbackDescLabel" fallback:@"Description (optional)"]];
    [col addSubview:descLabel];

    UITextView* descField = [[UITextView alloc] init];
    descField.translatesAutoresizingMaskIntoConstraints = NO;
    descField.tag = 4022;
    descField.delegate = self;
    descField.backgroundColor = _cHeader;
    descField.textColor = _cText;
    descField.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
    descField.layer.cornerRadius = 8;
    descField.layer.borderWidth = 1;
    descField.layer.borderColor = _cBorder.CGColor;
    descField.textContainerInset = UIEdgeInsetsMake(10, 10, 10, 10);
    descField.keyboardAppearance = UIKeyboardAppearanceDark;
    descField.scrollEnabled = NO;
    [col addSubview:descField];

    UILabel* descPlaceholder = [[UILabel alloc] init];
    descPlaceholder.translatesAutoresizingMaskIntoConstraints = NO;
    descPlaceholder.tag = 4023;
    descPlaceholder.text = [BPStrings text:@"feedbackDescHint"
                                  fallback:@"More detail — how it would work, why it matters…"];
    descPlaceholder.textColor = _cTextMuted;
    descPlaceholder.font = descField.font;
    descPlaceholder.userInteractionEnabled = NO;
    [descField addSubview:descPlaceholder];

    // Pending-attachment chip strip.
    UIScrollView* strip = [[UIScrollView alloc] init];
    strip.translatesAutoresizingMaskIntoConstraints = NO;
    strip.tag = 4031;
    strip.showsHorizontalScrollIndicator = NO;
    strip.alwaysBounceHorizontal = NO;
    strip.hidden = YES;
    [col addSubview:strip];

    UIView* stripContent = [[UIView alloc] init];
    stripContent.translatesAutoresizingMaskIntoConstraints = NO;
    stripContent.tag = 4032;
    [strip addSubview:stripContent];

    NSLayoutConstraint* stripHeight = [strip.heightAnchor constraintEqualToConstant:0];
    objc_setAssociatedObject(strip, "bp_height", stripHeight, OBJC_ASSOCIATION_RETAIN_NONATOMIC);
    stripHeight.active = YES;

    // Action row.
    UIView* actions = [[UIView alloc] init];
    actions.translatesAutoresizingMaskIntoConstraints = NO;
    [col addSubview:actions];

    UIButton* attachBtn = [self buildOutlineButton:
        [@"📷  " stringByAppendingString:
            [BPStrings text:@"feedbackAttach" fallback:@"Attach screenshot"]]];
    [attachBtn addTarget:self action:@selector(onSubmitAttachTapped) forControlEvents:UIControlEventTouchUpInside];
    [actions addSubview:attachBtn];

    UIButton* cancelBtn = [self buildOutlineButton:
        [BPStrings text:@"feedbackCancel" fallback:@"Cancel"]];
    [cancelBtn addTarget:self action:@selector(showListView) forControlEvents:UIControlEventTouchUpInside];
    [actions addSubview:cancelBtn];

    UIButton* submitBtn = [self buildFilledButton:
        [BPStrings text:@"feedbackSubmit" fallback:@"Submit"]];
    submitBtn.tag = 4033;
    submitBtn.enabled = NO;
    submitBtn.alpha = 0.4;
    [submitBtn addTarget:self action:@selector(onSubmitTapped:) forControlEvents:UIControlEventTouchUpInside];
    [actions addSubview:submitBtn];

    [NSLayoutConstraint activateConstraints:@[
        [intro.topAnchor constraintEqualToAnchor:col.topAnchor],
        [intro.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
        [intro.trailingAnchor constraintEqualToAnchor:col.trailingAnchor],

        [titleLabel.topAnchor constraintEqualToAnchor:intro.bottomAnchor constant:12],
        [titleLabel.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],

        [titleField.topAnchor constraintEqualToAnchor:titleLabel.bottomAnchor constant:4],
        [titleField.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
        [titleField.trailingAnchor constraintEqualToAnchor:col.trailingAnchor],
        [titleField.heightAnchor constraintEqualToConstant:40],

        [descLabel.topAnchor constraintEqualToAnchor:titleField.bottomAnchor constant:10],
        [descLabel.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],

        [descField.topAnchor constraintEqualToAnchor:descLabel.bottomAnchor constant:4],
        [descField.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
        [descField.trailingAnchor constraintEqualToAnchor:col.trailingAnchor],
        [descField.heightAnchor constraintGreaterThanOrEqualToConstant:120],

        [descPlaceholder.leadingAnchor constraintEqualToAnchor:descField.leadingAnchor constant:14],
        [descPlaceholder.topAnchor constraintEqualToAnchor:descField.topAnchor constant:10],

        [strip.topAnchor constraintEqualToAnchor:descField.bottomAnchor constant:8],
        [strip.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
        [strip.trailingAnchor constraintEqualToAnchor:col.trailingAnchor],

        [stripContent.topAnchor constraintEqualToAnchor:strip.contentLayoutGuide.topAnchor],
        [stripContent.leadingAnchor constraintEqualToAnchor:strip.contentLayoutGuide.leadingAnchor],
        [stripContent.trailingAnchor constraintEqualToAnchor:strip.contentLayoutGuide.trailingAnchor],
        [stripContent.bottomAnchor constraintEqualToAnchor:strip.contentLayoutGuide.bottomAnchor],

        [actions.topAnchor constraintEqualToAnchor:strip.bottomAnchor constant:12],
        [actions.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
        [actions.trailingAnchor constraintEqualToAnchor:col.trailingAnchor],
        [actions.bottomAnchor constraintEqualToAnchor:col.bottomAnchor],
        [actions.heightAnchor constraintGreaterThanOrEqualToConstant:44],

        [attachBtn.leadingAnchor constraintEqualToAnchor:actions.leadingAnchor],
        [attachBtn.centerYAnchor constraintEqualToAnchor:actions.centerYAnchor],

        [submitBtn.trailingAnchor constraintEqualToAnchor:actions.trailingAnchor],
        [submitBtn.centerYAnchor constraintEqualToAnchor:actions.centerYAnchor],

        [cancelBtn.trailingAnchor constraintEqualToAnchor:submitBtn.leadingAnchor constant:-8],
        [cancelBtn.centerYAnchor constraintEqualToAnchor:actions.centerYAnchor],
    ]];

    return scroll;
}

- (UILabel*)buildFieldLabel:(NSString*)text {
    UILabel* l = [[UILabel alloc] init];
    l.translatesAutoresizingMaskIntoConstraints = NO;
    l.text = text;
    l.textColor = _cTextDim;
    l.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12] + 1];
    return l;
}

- (UIButton*)buildOutlineButton:(NSString*)title {
    UIButton* b = [UIButton buttonWithType:UIButtonTypeCustom];
    b.translatesAutoresizingMaskIntoConstraints = NO;
    [b setTitle:title forState:UIControlStateNormal];
    [b setTitleColor:_cTextDim forState:UIControlStateNormal];
    b.titleLabel.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
    b.backgroundColor = _cBorder;
    b.layer.cornerRadius = 8;
    b.layer.masksToBounds = YES;
    b.contentEdgeInsets = UIEdgeInsetsMake(10, 14, 10, 14);
    return b;
}

- (UIButton*)buildFilledButton:(NSString*)title {
    UIButton* b = [UIButton buttonWithType:UIButtonTypeCustom];
    b.translatesAutoresizingMaskIntoConstraints = NO;
    [b setTitle:title forState:UIControlStateNormal];
    [b setTitleColor:[UIColor whiteColor] forState:UIControlStateNormal];
    b.titleLabel.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
    b.backgroundColor = _cAccentFeedback;
    b.layer.cornerRadius = 8;
    b.layer.masksToBounds = YES;
    b.contentEdgeInsets = UIEdgeInsetsMake(10, 16, 10, 16);
    return b;
}

- (void)onSubmitTitleChanged:(UITextField*)tf {
    UIButton* submit = (UIButton*)[self.view viewWithTag:4033];
    BOOL ok = [tf.text stringByTrimmingCharactersInSet:
        [NSCharacterSet whitespaceAndNewlineCharacterSet]].length > 0;
    submit.enabled = ok;
    submit.alpha = ok ? 1.0 : 0.4;
}

- (void)onSubmitAttachTapped {
    [self captureScreenshotForSubmit:YES];
}

- (void)onSubmitTapped:(UIButton*)submit {
    UITextField* titleField = (UITextField*)[self.view viewWithTag:4021];
    UITextView* descField = (UITextView*)[self.view viewWithTag:4022];
    NSString* title = [titleField.text stringByTrimmingCharactersInSet:
        [NSCharacterSet whitespaceAndNewlineCharacterSet]] ?: @"";
    NSString* desc = [descField.text stringByTrimmingCharactersInSet:
        [NSCharacterSet whitespaceAndNewlineCharacterSet]] ?: @"";
    if (title.length == 0) return;
    _pendingTitle = title;
    _pendingDescription = desc;
    submit.enabled = NO;
    submit.alpha = 0.4;
    [submit setTitle:[BPStrings text:@"feedbackChecking" fallback:@"Checking…"]
            forState:UIControlStateNormal];

    [self checkSimilarityThenSubmit:title description:desc completion:^{
        UIButton* btn = (UIButton*)[self.view viewWithTag:4033];
        if (!btn) return;
        btn.enabled = YES;
        btn.alpha = 1.0;
        [btn setTitle:[BPStrings text:@"feedbackSubmit" fallback:@"Submit"]
             forState:UIControlStateNormal];
    }];
}

- (void)rebuildSubmitStrip {
    UIScrollView* strip = (UIScrollView*)[self.view viewWithTag:4031];
    UIView* stripContent = [self.view viewWithTag:4032];
    if (!strip || !stripContent) return;
    NSLayoutConstraint* h = objc_getAssociatedObject(strip, "bp_height");

    for (UIView* v in [stripContent.subviews copy]) [v removeFromSuperview];
    if (_submitDraftAttachments.count == 0) {
        strip.hidden = YES;
        h.constant = 0;
        return;
    }
    strip.hidden = NO;
    h.constant = 60;

    UIView* prev = nil;
    for (BPFbPendingAttachment* a in _submitDraftAttachments) {
        UIView* chip = [self buildAttachmentChip:a list:_submitDraftAttachments stripTag:4031];
        chip.translatesAutoresizingMaskIntoConstraints = NO;
        [stripContent addSubview:chip];
        [NSLayoutConstraint activateConstraints:@[
            [chip.topAnchor constraintEqualToAnchor:stripContent.topAnchor],
            [chip.bottomAnchor constraintEqualToAnchor:stripContent.bottomAnchor],
        ]];
        if (prev) {
            [chip.leadingAnchor constraintEqualToAnchor:prev.trailingAnchor constant:6].active = YES;
        } else {
            [chip.leadingAnchor constraintEqualToAnchor:stripContent.leadingAnchor].active = YES;
        }
        prev = chip;
    }
    if (prev) {
        [prev.trailingAnchor constraintLessThanOrEqualToAnchor:stripContent.trailingAnchor].active = YES;
    }
}

#pragma mark – Similarity prompt (replaces the submit body)

- (void)showSimilarityPrompt:(NSDictionary*)match resetSubmit:(void(^)(void))resetSubmit {
    [self applyHeader:[BPStrings text:@"feedbackNewTitle" fallback:@"New feedback"]
          leadingText:@"‹"
            onLeading:@selector(showListView)];
    [self clearBody];

    UIScrollView* scroll = [[UIScrollView alloc] init];
    scroll.translatesAutoresizingMaskIntoConstraints = NO;
    scroll.backgroundColor = _cBg;
    scroll.alwaysBounceVertical = YES;
    [_bodyContainer addSubview:scroll];

    UIView* col = [[UIView alloc] init];
    col.translatesAutoresizingMaskIntoConstraints = NO;
    [scroll addSubview:col];

    [NSLayoutConstraint activateConstraints:@[
        [scroll.topAnchor constraintEqualToAnchor:_bodyContainer.topAnchor],
        [scroll.leadingAnchor constraintEqualToAnchor:_bodyContainer.leadingAnchor],
        [scroll.trailingAnchor constraintEqualToAnchor:_bodyContainer.trailingAnchor],
        [scroll.bottomAnchor constraintEqualToAnchor:_bodyContainer.bottomAnchor],

        [col.topAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.topAnchor constant:16],
        [col.leadingAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.leadingAnchor constant:16],
        [col.trailingAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.trailingAnchor constant:-16],
        [col.bottomAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.bottomAnchor constant:-16],
        [col.widthAnchor constraintEqualToAnchor:scroll.frameLayoutGuide.widthAnchor constant:-32],
    ]];

    UILabel* heading = [[UILabel alloc] init];
    heading.translatesAutoresizingMaskIntoConstraints = NO;
    heading.text = [BPStrings text:@"feedbackSimilarTitle"
                          fallback:@"Similar feedback already exists"];
    heading.textColor = _cText;
    heading.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeTitle" fallback:17] - 1];
    heading.numberOfLines = 0;
    [col addSubview:heading];

    UILabel* intro = [[UILabel alloc] init];
    intro.translatesAutoresizingMaskIntoConstraints = NO;
    intro.text = [BPStrings text:@"feedbackSimilarIntro" fallback:@"Sounds similar to:"];
    intro.textColor = _cTextDim;
    intro.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12] + 1];
    [col addSubview:intro];

    UIView* card = [[UIView alloc] init];
    card.translatesAutoresizingMaskIntoConstraints = NO;
    card.backgroundColor = _cCard;
    card.layer.borderWidth = 1;
    card.layer.borderColor = _cBorder.CGColor;
    card.layer.cornerRadius = 8;
    [col addSubview:card];

    UILabel* matchTitle = [[UILabel alloc] init];
    matchTitle.translatesAutoresizingMaskIntoConstraints = NO;
    matchTitle.text = BPFbStr(match, @"title", @"Title", @"(untitled)");
    matchTitle.textColor = _cText;
    matchTitle.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14] + 1];
    matchTitle.numberOfLines = 0;
    [card addSubview:matchTitle];

    NSInteger votes = BPFbInt(match, @"voteCount", @"VoteCount", 0);
    UILabel* votesLabel = [[UILabel alloc] init];
    votesLabel.translatesAutoresizingMaskIntoConstraints = NO;
    votesLabel.text = [NSString stringWithFormat:@"%ld %@",
                       (long)votes, votes == 1 ? @"vote" : @"votes"];
    votesLabel.textColor = _cTextMuted;
    votesLabel.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];
    [card addSubview:votesLabel];

    NSString* mbody = BPFbStr(match, @"body", @"Body", @"");
    UILabel* mbView = nil;
    if (mbody.length > 0) {
        if (mbody.length > 240) mbody = [[mbody substringToIndex:237] stringByAppendingString:@"…"];
        mbView = [[UILabel alloc] init];
        mbView.translatesAutoresizingMaskIntoConstraints = NO;
        mbView.text = mbody;
        mbView.textColor = _cTextDim;
        mbView.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];
        mbView.numberOfLines = 0;
        [card addSubview:mbView];
    }

    UILabel* question = [[UILabel alloc] init];
    question.translatesAutoresizingMaskIntoConstraints = NO;
    question.text = [BPStrings text:@"feedbackSimilarQuestion"
                           fallback:@"Want to vote for that instead?"];
    question.textColor = _cTextDim;
    question.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12] + 1];
    question.numberOfLines = 0;
    [col addSubview:question];

    UIButton* postMine = [self buildOutlineButton:
        [BPStrings text:@"feedbackPostAnyway" fallback:@"Post mine anyway"]];
    objc_setAssociatedObject(postMine, "bp_reset", resetSubmit, OBJC_ASSOCIATION_COPY_NONATOMIC);
    [postMine addTarget:self action:@selector(onPostMineAnyway:)
       forControlEvents:UIControlEventTouchUpInside];
    [col addSubview:postMine];

    UIButton* voteBtn = [self buildFilledButton:
        [BPStrings text:@"feedbackVoteForThat" fallback:@"Vote for that"]];
    NSString* matchId = BPFbStr(match, @"id", @"Id", @"");
    objc_setAssociatedObject(voteBtn, "bp_id", matchId, OBJC_ASSOCIATION_COPY_NONATOMIC);
    [voteBtn addTarget:self action:@selector(onVoteForMatch:)
      forControlEvents:UIControlEventTouchUpInside];
    [col addSubview:voteBtn];

    [NSLayoutConstraint activateConstraints:@[
        [heading.topAnchor constraintEqualToAnchor:col.topAnchor],
        [heading.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
        [heading.trailingAnchor constraintEqualToAnchor:col.trailingAnchor],

        [intro.topAnchor constraintEqualToAnchor:heading.bottomAnchor constant:8],
        [intro.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
        [intro.trailingAnchor constraintEqualToAnchor:col.trailingAnchor],

        [card.topAnchor constraintEqualToAnchor:intro.bottomAnchor constant:6],
        [card.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
        [card.trailingAnchor constraintEqualToAnchor:col.trailingAnchor],

        [matchTitle.topAnchor constraintEqualToAnchor:card.topAnchor constant:12],
        [matchTitle.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:12],
        [matchTitle.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-12],

        [votesLabel.topAnchor constraintEqualToAnchor:matchTitle.bottomAnchor constant:2],
        [votesLabel.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:12],
        [votesLabel.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-12],
    ]];

    UIView* lastInCard = votesLabel;
    if (mbView) {
        [NSLayoutConstraint activateConstraints:@[
            [mbView.topAnchor constraintEqualToAnchor:votesLabel.bottomAnchor constant:6],
            [mbView.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:12],
            [mbView.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-12],
        ]];
        lastInCard = mbView;
    }
    [lastInCard.bottomAnchor constraintEqualToAnchor:card.bottomAnchor constant:-12].active = YES;

    [NSLayoutConstraint activateConstraints:@[
        [question.topAnchor constraintEqualToAnchor:card.bottomAnchor constant:10],
        [question.leadingAnchor constraintEqualToAnchor:col.leadingAnchor],
        [question.trailingAnchor constraintEqualToAnchor:col.trailingAnchor],

        [voteBtn.topAnchor constraintEqualToAnchor:question.bottomAnchor constant:12],
        [voteBtn.trailingAnchor constraintEqualToAnchor:col.trailingAnchor],
        [voteBtn.bottomAnchor constraintEqualToAnchor:col.bottomAnchor],

        [postMine.trailingAnchor constraintEqualToAnchor:voteBtn.leadingAnchor constant:-8],
        [postMine.centerYAnchor constraintEqualToAnchor:voteBtn.centerYAnchor],
    ]];
}

- (void)onPostMineAnyway:(UIButton*)b {
    void (^reset)(void) = objc_getAssociatedObject(b, "bp_reset");
    [self createFeedback:_pendingTitle description:_pendingDescription bypass:YES completion:^(BOOL ok) {
        if (reset) reset();
        [self showListView];
    }];
}

- (void)onVoteForMatch:(UIButton*)b {
    NSString* mid = objc_getAssociatedObject(b, "bp_id");
    [self voteFor:mid completion:^{ [self showListView]; }];
}

#pragma mark – Attachment chip / thumb

- (UIView*)buildAttachmentChip:(BPFbPendingAttachment*)a
                          list:(NSMutableArray*)list
                      stripTag:(NSInteger)stripTag {
    UIView* chip = [[UIView alloc] init];
    chip.backgroundColor = _cBg;
    chip.layer.cornerRadius = 8;
    chip.layer.borderWidth = 1;
    chip.layer.borderColor = _cBorder.CGColor;
    chip.layer.masksToBounds = YES;

    UIImageView* iv = [[UIImageView alloc] init];
    iv.translatesAutoresizingMaskIntoConstraints = NO;
    iv.contentMode = UIViewContentModeScaleAspectFill;
    iv.clipsToBounds = YES;
    UIImage* img = a.localPath ? [UIImage imageWithContentsOfFile:a.localPath] : nil;
    if (img) iv.image = img;
    [chip addSubview:iv];

    UIButton* x = [UIButton buttonWithType:UIButtonTypeSystem];
    x.translatesAutoresizingMaskIntoConstraints = NO;
    [x setTitle:@"×" forState:UIControlStateNormal];
    [x setTitleColor:_cText forState:UIControlStateNormal];
    x.titleLabel.font = [UIFont boldSystemFontOfSize:14];
    x.backgroundColor = _cHeader;
    x.layer.cornerRadius = 10;
    x.layer.masksToBounds = YES;
    objc_setAssociatedObject(x, "bp_att", a, OBJC_ASSOCIATION_RETAIN_NONATOMIC);
    objc_setAssociatedObject(x, "bp_list", list, OBJC_ASSOCIATION_RETAIN_NONATOMIC);
    objc_setAssociatedObject(x, "bp_strip_tag", @(stripTag), OBJC_ASSOCIATION_RETAIN_NONATOMIC);
    [x addTarget:self action:@selector(onChipRemoveTapped:) forControlEvents:UIControlEventTouchUpInside];
    [chip addSubview:x];

    [NSLayoutConstraint activateConstraints:@[
        [chip.widthAnchor constraintEqualToConstant:48],
        [chip.heightAnchor constraintEqualToConstant:48],

        [iv.topAnchor constraintEqualToAnchor:chip.topAnchor],
        [iv.bottomAnchor constraintEqualToAnchor:chip.bottomAnchor],
        [iv.leadingAnchor constraintEqualToAnchor:chip.leadingAnchor],
        [iv.trailingAnchor constraintEqualToAnchor:chip.trailingAnchor],

        [x.topAnchor constraintEqualToAnchor:chip.topAnchor constant:2],
        [x.trailingAnchor constraintEqualToAnchor:chip.trailingAnchor constant:-2],
        [x.widthAnchor constraintEqualToConstant:20],
        [x.heightAnchor constraintEqualToConstant:20],
    ]];
    return chip;
}

- (void)onChipRemoveTapped:(UIButton*)x {
    BPFbPendingAttachment* a = objc_getAssociatedObject(x, "bp_att");
    NSMutableArray* list = objc_getAssociatedObject(x, "bp_list");
    NSNumber* tag = objc_getAssociatedObject(x, "bp_strip_tag");
    if (a && list) [list removeObjectIdenticalTo:a];
    if (tag.integerValue == 4011) {
        [self rebuildCommentStrip];
        [self updateCommentSendEnabled];
    } else if (tag.integerValue == 4031) {
        [self rebuildSubmitStrip];
    }
}

/** Image attachment thumb under a feedback body / comment — same shape as
 *  the chat's buildAttachmentThumb. Server URLs render a 📷 placeholder
 *  for v1; local-path captures (e.g. just-snapped screenshot we haven't
 *  navigated away from) render the bitmap directly. Tapping a remote-URL
 *  thumb opens the URL in Safari, mirroring the Android side. */
- (UIView*)buildAttachmentThumb:(NSDictionary*)ao {
    NSString* localPath = BPFbStr(ao, @"localPath", @"LocalPath", @"");
    NSString* url       = BPFbStr(ao, @"url",       @"Url",       @"");

    UIView* container = [[UIView alloc] init];
    container.layer.cornerRadius = 8;
    container.layer.masksToBounds = YES;
    container.backgroundColor = _cCard;
    [container.widthAnchor constraintEqualToConstant:160].active = YES;
    [container.heightAnchor constraintEqualToConstant:160].active = YES;

    if (localPath.length > 0) {
        UIImage* img = [UIImage imageWithContentsOfFile:localPath];
        if (img) {
            UIImageView* iv = [[UIImageView alloc] initWithImage:img];
            iv.translatesAutoresizingMaskIntoConstraints = NO;
            iv.contentMode = UIViewContentModeScaleAspectFill;
            iv.clipsToBounds = YES;
            [container addSubview:iv];
            [NSLayoutConstraint activateConstraints:@[
                [iv.topAnchor constraintEqualToAnchor:container.topAnchor],
                [iv.bottomAnchor constraintEqualToAnchor:container.bottomAnchor],
                [iv.leadingAnchor constraintEqualToAnchor:container.leadingAnchor],
                [iv.trailingAnchor constraintEqualToAnchor:container.trailingAnchor],
            ]];
            return container;
        }
    }
    UILabel* glyph = [[UILabel alloc] init];
    glyph.translatesAutoresizingMaskIntoConstraints = NO;
    glyph.text = @"📷";
    glyph.textAlignment = NSTextAlignmentCenter;
    glyph.font = [UIFont systemFontOfSize:36];
    [container addSubview:glyph];
    [NSLayoutConstraint activateConstraints:@[
        [glyph.centerXAnchor constraintEqualToAnchor:container.centerXAnchor],
        [glyph.centerYAnchor constraintEqualToAnchor:container.centerYAnchor],
    ]];
    if (url.length > 0) {
        container.userInteractionEnabled = YES;
        UITapGestureRecognizer* tap = [[UITapGestureRecognizer alloc]
            initWithTarget:self action:@selector(onAttachmentThumbTapped:)];
        objc_setAssociatedObject(container, "bp_url", url, OBJC_ASSOCIATION_COPY_NONATOMIC);
        [container addGestureRecognizer:tap];
    }
    return container;
}

- (void)onAttachmentThumbTapped:(UITapGestureRecognizer*)g {
    NSString* url = objc_getAssociatedObject(g.view, "bp_url");
    if (url.length == 0) return;
    NSURL* nsurl = [NSURL URLWithString:url];
    if (nsurl) [[UIApplication sharedApplication] openURL:nsurl options:@{} completionHandler:nil];
}

#pragma mark – Linkify

/** Use NSDataDetector to underline / colour URLs in body text the same way
 *  Android's Linkify.WEB_URLS does. Identical to the chat VC's helper but
 *  duplicated here per "no shared helpers across the two VCs" — both files
 *  are intentionally self-contained. */
- (NSAttributedString*)linkifyText:(NSString*)text color:(UIColor*)color font:(UIFont*)font {
    if (text.length == 0) return nil;
    NSMutableAttributedString* s = [[NSMutableAttributedString alloc] initWithString:text
                                                                          attributes:@{
        NSForegroundColorAttributeName: color,
        NSFontAttributeName: font,
    }];
    NSError* err = nil;
    NSDataDetector* dd = [NSDataDetector dataDetectorWithTypes:NSTextCheckingTypeLink error:&err];
    if (!dd) return s;
    [dd enumerateMatchesInString:text options:0 range:NSMakeRange(0, text.length)
                      usingBlock:^(NSTextCheckingResult * _Nullable r, NSMatchingFlags flags, BOOL * _Nonnull stop) {
        if (!r) return;
        [s addAttribute:NSUnderlineStyleAttributeName
                  value:@(NSUnderlineStyleSingle)
                  range:r.range];
    }];
    return s;
}

#pragma mark – Capture screenshot

/** Capture a screenshot for either composer. Mirrors the chat VC's
 *  -onTakeScreenshot dance: dismiss self briefly so the captured frame is
 *  the live game view, then re-present once the file is on disk; queue
 *  the upload in the background. */
- (void)captureScreenshotForSubmit:(BOOL)isSubmit {
    NSString* dir = NSTemporaryDirectory();
    NSString* outPath = [dir stringByAppendingPathComponent:
        [NSString stringWithFormat:@"bp_feedback_shot_%lld.jpg",
            (long long)([NSDate date].timeIntervalSince1970 * 1000)]];

    UIViewController* presenter = self.presentingViewController;
    [self dismissViewControllerAnimated:YES completion:^{
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.25 * NSEC_PER_SEC)),
                       dispatch_get_main_queue(), ^{
            Bugpunch_CaptureScreenshot([@"feedback" UTF8String],
                [outPath UTF8String], 90,
                ^(const char* requestId, int success, const char* payload) {
                    NSString* path = (success && payload) ? [NSString stringWithUTF8String:payload] : nil;
                    dispatch_async(dispatch_get_main_queue(), ^{
                        if (path) {
                            BPFbPendingAttachment* a = [BPFbPendingAttachment new];
                            a.localPath = path;
                            if (isSubmit) {
                                [self->_submitDraftAttachments addObject:a];
                            } else {
                                [self->_commentDraftAttachments addObject:a];
                            }
                            // Upload in background; URL is filled in when it lands.
                            dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
                                [self uploadAttachmentSync:a];
                            });
                        }
                        UIViewController* top = presenter ?: [BugpunchFeedbackViewController topPresentingVC];
                        if (top) {
                            [top presentViewController:self animated:YES completion:^{
                                if (isSubmit) {
                                    [self rebuildSubmitStrip];
                                } else {
                                    [self rebuildCommentStrip];
                                    [self updateCommentSendEnabled];
                                }
                            }];
                        }
                    });
                });
        });
    }];
}

#pragma mark – HTTP helpers

- (void)http:(NSString*)method path:(NSString*)path
    jsonBody:(NSDictionary*)body
   rawString:(NSString*)rawJson
  completion:(void(^)(NSData* data, NSInteger status))completion {
    NSString* base = BPFbServerUrl();
    NSString* key  = BPFbApiKey();
    if (base.length == 0 || key.length == 0) {
        if (completion) completion(nil, 0);
        return;
    }
    NSURL* url = [NSURL URLWithString:[base stringByAppendingString:path]];
    NSMutableURLRequest* req = [NSMutableURLRequest requestWithURL:url];
    req.HTTPMethod = method;
    req.timeoutInterval = 15.0;
    [req setValue:[@"Bearer " stringByAppendingString:key] forHTTPHeaderField:@"Authorization"];
    [req setValue:BPFbDeviceId() forHTTPHeaderField:@"X-Device-Id"];
    [req setValue:@"application/json" forHTTPHeaderField:@"Accept"];

    NSData* data = nil;
    if (body) {
        NSError* enc = nil;
        data = [NSJSONSerialization dataWithJSONObject:body options:0 error:&enc];
        if (enc) { if (completion) completion(nil, 0); return; }
    } else if (rawJson) {
        data = [rawJson dataUsingEncoding:NSUTF8StringEncoding];
    }
    if (data) {
        req.HTTPBody = data;
        [req setValue:@"application/json" forHTTPHeaderField:@"Content-Type"];
    }

    NSURLSessionDataTask* t = [_session dataTaskWithRequest:req
        completionHandler:^(NSData * _Nullable d, NSURLResponse * _Nullable r, NSError * _Nullable err) {
        NSHTTPURLResponse* http = (NSHTTPURLResponse*)r;
        if (completion) completion(err ? nil : d, http.statusCode);
    }];
    [t resume];
}

/** Multipart-upload an attachment file — matches the C# board's
 *  upload-on-attach so submit-time is fast. Bypasses the native upload
 *  queue (BugpunchUploader); attachments fail-fast rather than retry
 *  across launches. Mirrors BugpunchChatViewController -uploadAttachmentSync. */
- (void)uploadAttachmentSync:(BPFbPendingAttachment*)a {
    NSString* base = BPFbServerUrl();
    NSString* key  = BPFbApiKey();
    if (base.length == 0 || key.length == 0 || a.localPath.length == 0) return;
    NSData* data = [NSData dataWithContentsOfFile:a.localPath];
    if (data.length == 0) return;
    if (data.length > 5 * 1024 * 1024) {
        NSLog(@"[Bugpunch.Feedback] attachment over 5MB, skipping");
        return;
    }

    NSString* boundary = [NSString stringWithFormat:@"----bp%lld",
        (long long)([NSDate date].timeIntervalSince1970 * 1000)];
    NSURL* url = [NSURL URLWithString:[base stringByAppendingString:@"/api/feedback/attachments"]];
    NSMutableURLRequest* req = [NSMutableURLRequest requestWithURL:url];
    req.HTTPMethod = @"POST";
    req.timeoutInterval = 60.0;
    [req setValue:[@"Bearer " stringByAppendingString:key] forHTTPHeaderField:@"Authorization"];
    [req setValue:BPFbDeviceId() forHTTPHeaderField:@"X-Device-Id"];
    [req setValue:[NSString stringWithFormat:@"multipart/form-data; boundary=%@", boundary]
        forHTTPHeaderField:@"Content-Type"];

    NSString* filename = a.localPath.lastPathComponent;
    NSMutableData* body = [NSMutableData data];
    NSString* pre = [NSString stringWithFormat:
        @"--%@\r\nContent-Disposition: form-data; name=\"file\"; filename=\"%@\"\r\nContent-Type: image/jpeg\r\n\r\n",
        boundary, filename];
    [body appendData:[pre dataUsingEncoding:NSUTF8StringEncoding]];
    [body appendData:data];
    NSString* post = [NSString stringWithFormat:@"\r\n--%@--\r\n", boundary];
    [body appendData:[post dataUsingEncoding:NSUTF8StringEncoding]];

    dispatch_semaphore_t sem = dispatch_semaphore_create(0);
    NSURLSessionUploadTask* t = [_session uploadTaskWithRequest:req fromData:body
        completionHandler:^(NSData * _Nullable d, NSURLResponse * _Nullable r, NSError * _Nullable err) {
        NSHTTPURLResponse* http = (NSHTTPURLResponse*)r;
        if (!err && http.statusCode >= 200 && http.statusCode < 300 && d.length > 0) {
            id obj = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
            if ([obj isKindOfClass:[NSDictionary class]]) {
                NSDictionary* dict = (NSDictionary*)obj;
                a.url    = BPFbStr(dict, @"url",  @"Url",  @"");
                a.mime   = BPFbStr(dict, @"mime", @"Mime", @"image/jpeg");
                a.width  = BPFbInt(dict, @"width",  @"Width",  0);
                a.height = BPFbInt(dict, @"height", @"Height", 0);
                if (a.url.length == 0) a.url = nil;
            }
        }
        dispatch_semaphore_signal(sem);
    }];
    [t resume];
    dispatch_semaphore_wait(sem, DISPATCH_TIME_FOREVER);
}

/** Build the attachments JSON array the server expects. Skips entries whose
 *  upload didn't complete in time — matches the Android Activity. */
- (NSArray*)attachmentsArray:(NSArray<BPFbPendingAttachment*>*)list {
    NSMutableArray* out = [NSMutableArray array];
    for (BPFbPendingAttachment* a in list) {
        if (a.url.length == 0) continue;
        NSMutableDictionary* d = [NSMutableDictionary dictionary];
        d[@"type"] = @"image";
        d[@"url"]  = a.url;
        d[@"mime"] = a.mime.length > 0 ? a.mime : @"image/jpeg";
        if (a.width > 0)  d[@"width"]  = @(a.width);
        if (a.height > 0) d[@"height"] = @(a.height);
        [out addObject:d];
    }
    return out;
}

#pragma mark – HTTP fetches

- (void)fetchList {
    [self http:@"GET" path:@"/api/feedback?sort=votes" jsonBody:nil rawString:nil
    completion:^(NSData* d, NSInteger status) {
        if (!d) return;
        id parsed = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
        if (![parsed isKindOfClass:[NSArray class]]) return;
        NSArray* arr = (NSArray*)parsed;
        NSMutableArray* incoming = [NSMutableArray arrayWithCapacity:arr.count];
        for (id o in arr) if ([o isKindOfClass:[NSDictionary class]]) [incoming addObject:o];
        dispatch_async(dispatch_get_main_queue(), ^{
            [self->_items setArray:incoming];
            UIView* listWrap = [self.view viewWithTag:4001];
            if (listWrap) [self rebuildListInto:listWrap];
        });
    }];
}

- (void)fetchComments:(NSString*)itemId {
    if (itemId.length == 0) return;
    NSString* enc = [itemId stringByAddingPercentEncodingWithAllowedCharacters:
        [NSCharacterSet URLPathAllowedCharacterSet]];
    NSString* path = [NSString stringWithFormat:@"/api/feedback/%@/comments", enc ?: itemId];
    [self http:@"GET" path:path jsonBody:nil rawString:nil
    completion:^(NSData* d, NSInteger status) {
        if (!d) return;
        id parsed = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
        if (![parsed isKindOfClass:[NSDictionary class]]) return;
        NSArray* arr = BPFbArr((NSDictionary*)parsed, @"comments", @"Comments");
        NSMutableArray* incoming = [NSMutableArray array];
        for (id o in (arr ?: @[])) if ([o isKindOfClass:[NSDictionary class]]) [incoming addObject:o];
        dispatch_async(dispatch_get_main_queue(), ^{
            [self->_detailComments setArray:incoming];
            [self renderComments:self->_detailComments];
        });
    }];
}

#pragma mark – Mutations

/** Optimistic flip first — server reply reconciles with whatever the
 *  canonical state ends up being. Mirrors the Android toggleVote. */
- (void)toggleVote:(NSDictionary*)item {
    NSString* itemId = BPFbStr(item, @"id", @"Id", @"");
    if (itemId.length == 0) return;

    BOOL before = BPFbBool(item, @"hasMyVote", @"HasMyVote", NO);
    NSInteger prev = BPFbInt(item, @"voteCount", @"VoteCount", 0);

    // Patch our cached copy in _items so the row re-renders with the new state.
    for (NSUInteger i = 0; i < _items.count; i++) {
        NSString* eid = BPFbStr(_items[i], @"id", @"Id", @"");
        if ([eid isEqualToString:itemId]) {
            NSMutableDictionary* mut = [_items[i] mutableCopy];
            mut[@"hasMyVote"] = @(!before);
            mut[@"voteCount"] = @(prev + (before ? -1 : 1));
            _items[i] = mut;
            break;
        }
    }
    if (_detailItem) {
        NSString* did = BPFbStr(_detailItem, @"id", @"Id", @"");
        if ([did isEqualToString:itemId]) {
            _detailItem[@"hasMyVote"] = @(!before);
            _detailItem[@"voteCount"] = @(prev + (before ? -1 : 1));
        }
    }
    UIView* listWrap = [self.view viewWithTag:4001];
    if (listWrap) [self rebuildListInto:listWrap];
    if (_detailItem && [BPFbStr(_detailItem, @"id", @"Id", @"") isEqualToString:itemId]) {
        [self renderDetailBody];
    }

    NSString* enc = [itemId stringByAddingPercentEncodingWithAllowedCharacters:
        [NSCharacterSet URLPathAllowedCharacterSet]];
    NSString* path = [NSString stringWithFormat:@"/api/feedback/%@/vote", enc ?: itemId];
    [self http:@"POST" path:path jsonBody:nil rawString:@"{}"
    completion:^(NSData* d, NSInteger status) {
        if (!d) return;
        id parsed = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
        if (![parsed isKindOfClass:[NSDictionary class]]) return;
        NSDictionary* o = (NSDictionary*)parsed;
        NSInteger voteCount = BPFbInt(o, @"voteCount", @"VoteCount", prev);
        BOOL hasMyVote = BPFbBool(o, @"hasMyVote", @"HasMyVote", !before);
        dispatch_async(dispatch_get_main_queue(), ^{
            for (NSUInteger i = 0; i < self->_items.count; i++) {
                NSString* eid = BPFbStr(self->_items[i], @"id", @"Id", @"");
                if ([eid isEqualToString:itemId]) {
                    NSMutableDictionary* mut = [self->_items[i] mutableCopy];
                    mut[@"voteCount"] = @(voteCount);
                    mut[@"hasMyVote"] = @(hasMyVote);
                    self->_items[i] = mut;
                    break;
                }
            }
            if (self->_detailItem
                && [BPFbStr(self->_detailItem, @"id", @"Id", @"") isEqualToString:itemId]) {
                self->_detailItem[@"voteCount"] = @(voteCount);
                self->_detailItem[@"hasMyVote"] = @(hasMyVote);
                [self renderDetailBody];
            }
            UIView* lw = [self.view viewWithTag:4001];
            if (lw) [self rebuildListInto:lw];
        });
    }];
}

- (void)voteFor:(NSString*)itemId completion:(void(^)(void))onDone {
    if (itemId.length == 0) {
        if (onDone) onDone();
        return;
    }
    NSString* enc = [itemId stringByAddingPercentEncodingWithAllowedCharacters:
        [NSCharacterSet URLPathAllowedCharacterSet]];
    NSString* path = [NSString stringWithFormat:@"/api/feedback/%@/vote", enc ?: itemId];
    [self http:@"POST" path:path jsonBody:nil rawString:@"{}"
    completion:^(NSData* d, NSInteger status) {
        dispatch_async(dispatch_get_main_queue(), ^{ if (onDone) onDone(); });
    }];
}

- (void)checkSimilarityThenSubmit:(NSString*)title
                      description:(NSString*)description
                       completion:(void(^)(void))onDone {
    NSDictionary* payload = @{ @"title": title ?: @"", @"description": description ?: @"" };
    [self http:@"POST" path:@"/api/feedback/similarity" jsonBody:payload rawString:nil
    completion:^(NSData* d, NSInteger status) {
        NSDictionary* top = nil;
        double topScore = 0;
        if (d) {
            id parsed = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
            if ([parsed isKindOfClass:[NSDictionary class]]) {
                NSArray* matches = BPFbArr((NSDictionary*)parsed, @"matches", @"Matches");
                for (id m in (matches ?: @[])) {
                    if (![m isKindOfClass:[NSDictionary class]]) continue;
                    id score = ((NSDictionary*)m)[@"score"];
                    if (![score isKindOfClass:[NSNumber class]]) score = ((NSDictionary*)m)[@"Score"];
                    double s = [score isKindOfClass:[NSNumber class]] ? [(NSNumber*)score doubleValue] : 0;
                    if (s > 0.85 && s > topScore) {
                        top = (NSDictionary*)m;
                        topScore = s;
                    }
                }
            }
        }
        dispatch_async(dispatch_get_main_queue(), ^{
            if (top) {
                [self showSimilarityPrompt:top resetSubmit:onDone];
            } else {
                [self createFeedback:title description:description bypass:NO completion:^(BOOL ok) {
                    if (onDone) onDone();
                    [self showListView];
                }];
            }
        });
    }];
}

- (void)createFeedback:(NSString*)title
           description:(NSString*)description
                bypass:(BOOL)bypass
            completion:(void(^)(BOOL ok))onDone {
    NSMutableDictionary* payload = [NSMutableDictionary dictionary];
    payload[@"title"]       = title ?: @"";
    payload[@"description"] = description ?: @"";
    if (bypass) payload[@"bypassSimilarity"] = @YES;
    payload[@"attachments"] = [self attachmentsArray:_submitDraftAttachments];
    [self http:@"POST" path:@"/api/feedback" jsonBody:payload rawString:nil
    completion:^(NSData* d, NSInteger status) {
        BOOL ok = status >= 200 && status < 300;
        dispatch_async(dispatch_get_main_queue(), ^{
            if (ok) {
                [self->_submitDraftAttachments removeAllObjects];
            } else {
                [self toast:[BPStrings text:@"feedbackSubmitFailed"
                                   fallback:@"Could not submit feedback. Please try again."]];
            }
            if (onDone) onDone(ok);
        });
    }];
}

/** Wait for in-flight uploads briefly so the comment payload doesn't lose
 *  a freshly attached screenshot. Then post and refresh the comment list. */
- (void)postComment:(NSString*)itemId body:(NSString*)body completion:(void(^)(BOOL ok))onSent {
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        NSArray<BPFbPendingAttachment*>* snapshot = [self->_commentDraftAttachments copy];
        NSDate* deadline = [NSDate dateWithTimeIntervalSinceNow:8.0];
        for (BPFbPendingAttachment* a in snapshot) {
            while (a.url.length == 0 && [[NSDate date] compare:deadline] == NSOrderedAscending) {
                [NSThread sleepForTimeInterval:0.15];
            }
        }
        NSDictionary* payload = @{
            @"body":        body ?: @"",
            @"playerName":  @"",
            @"attachments": [self attachmentsArray:snapshot],
        };
        NSString* enc = [itemId stringByAddingPercentEncodingWithAllowedCharacters:
            [NSCharacterSet URLPathAllowedCharacterSet]];
        NSString* path = [NSString stringWithFormat:@"/api/feedback/%@/comments", enc ?: itemId];
        [self http:@"POST" path:path jsonBody:payload rawString:nil
        completion:^(NSData* d, NSInteger status) {
            BOOL ok = status >= 200 && status < 300;
            dispatch_async(dispatch_get_main_queue(), ^{
                if (!ok) {
                    [self toast:[BPStrings text:@"feedbackCommentFailed"
                                       fallback:@"Could not post comment. Please try again."]];
                }
                if (onSent) onSent(ok);
            });
        }];
    });
}

/** Lightweight inline toast — there's no UIKit equivalent of Toast, so we
 *  fade a small pill at the bottom of the body container. Matches the
 *  Android Activity's `Toast.makeText` calls in spirit; we can't reuse
 *  one from elsewhere because the chat VC doesn't define a generic toast. */
- (void)toast:(NSString*)text {
    UILabel* pill = [[UILabel alloc] init];
    pill.translatesAutoresizingMaskIntoConstraints = NO;
    pill.text = [NSString stringWithFormat:@"  %@  ", text ?: @""];
    pill.textColor = [UIColor whiteColor];
    pill.backgroundColor = [UIColor colorWithWhite:0 alpha:0.85];
    pill.layer.cornerRadius = 8;
    pill.layer.masksToBounds = YES;
    pill.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
    pill.numberOfLines = 0;
    pill.textAlignment = NSTextAlignmentCenter;
    pill.alpha = 0;
    [self.view addSubview:pill];
    [NSLayoutConstraint activateConstraints:@[
        [pill.centerXAnchor constraintEqualToAnchor:self.view.centerXAnchor],
        [pill.bottomAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.bottomAnchor constant:-24],
        [pill.leadingAnchor constraintGreaterThanOrEqualToAnchor:self.view.leadingAnchor constant:24],
        [pill.trailingAnchor constraintLessThanOrEqualToAnchor:self.view.trailingAnchor constant:-24],
        [pill.heightAnchor constraintGreaterThanOrEqualToConstant:40],
    ]];
    [UIView animateWithDuration:0.18 animations:^{ pill.alpha = 1.0; } completion:^(BOOL f) {
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(2.2 * NSEC_PER_SEC)),
                       dispatch_get_main_queue(), ^{
            [UIView animateWithDuration:0.25 animations:^{ pill.alpha = 0; }
                             completion:^(BOOL f2) { [pill removeFromSuperview]; }];
        });
    }];
}

#pragma mark – Class helpers

/** Walk up to the topmost presentable VC — same shape as the chat VC's
 *  helper. Used when re-presenting after a screenshot capture round-trip. */
+ (UIViewController*)topPresentingVC {
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
    return top;
}

@end

// ── Public entry point ──────────────────────────────────────────
//
// Called by Bugpunch_ShowFeedbackBoard in BugpunchReportOverlay.mm in place
// of the old fallback to the C# UI Toolkit BugpunchFeedbackBoard. Self-
// contained: builds + presents a fresh full-screen VC every time.

extern "C" void Bugpunch_PresentNativeFeedbackViewController(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        BugpunchFeedbackViewController* vc = [BugpunchFeedbackViewController new];
        vc.modalPresentationStyle = UIModalPresentationFullScreen;
        UIViewController* top = [BugpunchFeedbackViewController topPresentingVC];
        if (!top) return;
        [top presentViewController:vc animated:YES completion:nil];
    });
}
