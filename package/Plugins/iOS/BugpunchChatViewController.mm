// BugpunchChatViewController.mm
//
// Native iOS chat board — the "Ask for help" surface. Phase B of #29 (the
// iOS half; Android's BugpunchChatActivity shipped in Phase A under
// android-src/.../BugpunchChatActivity.java). Replaces the v1 path that
// bounced via UnitySendMessage("BugpunchReportCallback",
// "OnShowChatBoardRequested") into the C# UI Toolkit BugpunchChatBoard —
// that surface didn't look like a chat app and rendered inside the Unity
// view rather than as a system-level overlay.
//
// Mirrors the Android Activity's UX/state model exactly: Messenger-style
// header (avatar circle / title / subtitle / close X), bubble list with
// asymmetric corner tails (own = right + accent, team = left + grey),
// composer with "+" attach toggle, attach pill (📷 Screenshot / 🎥 Record
// video — video stubbed), pending attachment chips, inline approve /
// decline bubbles for scriptRequest / dataRequest. HTTP + 5s polling live
// here directly — no Unity round-trip.
//
// Endpoints (all relative to BPDebugMode.shared.config[@"serverUrl"]):
//   GET  /api/v1/chat/hours
//   GET  /api/v1/chat/thread
//   GET  /api/v1/chat/messages?since=<iso>
//   POST /api/v1/chat/message              { body, attachments?: [...] }
//   POST /api/v1/chat/read                 {}
//   POST /api/v1/chat/upload               (multipart)
//   POST /api/v1/chat/request/answer       { messageId, approved }
//
// Auth: X-Api-Key: <apiKey> + X-Device-Id: <stable> — server resolves
// projectId from the API key.
//
// Server returns mixed PascalCase / camelCase across endpoints; every
// field lookup falls back to both keys (Id / id, Sender / sender, …) the
// way the C# board does.
//
// Copyright (c) ODDGames. All rights reserved.

#import <UIKit/UIKit.h>
#import <Foundation/Foundation.h>
#import <objc/runtime.h>

#import "BugpunchTheme.h"
#import "BugpunchStrings.h"

// ── Cross-file deps ─────────────────────────────────────────────

// Stable device id (Keychain UUID). Lives in BugpunchIdentity.mm.
extern "C" const char* Bugpunch_GetStableDeviceId(void);

// UnitySendMessage lives in libiPhone-lib.a. Used to bounce approved
// scriptRequest / dataRequest payloads up to BugpunchClient where the
// ODDGames.Scripting runtime actually executes them.
extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

// Native screenshot — writes a JPEG to disk and fires the callback on the
// main thread. Lives in BugpunchScreenshot.mm.
typedef void (*BugpunchScreenshotCallback)(const char* requestId, int success, const char* payload);
extern "C" void Bugpunch_CaptureScreenshot(const char* requestId, const char* outputPath,
                                           int quality, BugpunchScreenshotCallback cb);

// ODDRecorder — single-segment ReplayKit-backed MP4 recorder. Used for the
// chat video attachment flow (issue #30). ReplayKit's startCapture itself
// shows the iOS system "Allow recording?" prompt the first time, so we
// don't need a custom consent sheet here. Lives in ODDRecorder.mm.
extern "C" void ODDRecorder_Start(const char* outputPath, int width, int height,
                                  int fps, int bitrate, bool includeAudio);
extern "C" void ODDRecorder_Stop(char* outPath, int outPathLen);
extern "C" bool ODDRecorder_IsRecording(void);

// Shared coordinator config dict (serverUrl / apiKey). Lives in
// BugpunchDebugMode.mm. Forward-declared the same way every other plugin
// file in this folder reads config (BugpunchPoller.mm,
// BugpunchDirectives.mm, BugpunchPerfMonitor.mm).
@interface BPDebugMode : NSObject
@property (nonatomic, strong) NSDictionary* config;
+ (instancetype)shared;
@end

// ── Forward decls ───────────────────────────────────────────────

@class BugpunchChatViewController;

// Pending-attachment record — the composer holds a list of these between
// pick and send.
@interface BPChatPendingAttachment : NSObject
@property (nonatomic, copy) NSString* type;     // @"image" | @"video"
@property (nonatomic, copy) NSString* path;     // absolute file path on disk
@end
@implementation BPChatPendingAttachment
@end

// ── Helpers ─────────────────────────────────────────────────────

static NSString* BPChatServerUrl(void) {
    NSString* s = [BPDebugMode shared].config[@"serverUrl"];
    if (![s isKindOfClass:[NSString class]]) return @"";
    while ([s hasSuffix:@"/"]) s = [s substringToIndex:s.length - 1];
    return s;
}

static NSString* BPChatApiKey(void) {
    NSString* s = [BPDebugMode shared].config[@"apiKey"];
    return [s isKindOfClass:[NSString class]] ? s : @"";
}

static NSString* BPChatDeviceId(void) {
    const char* c = Bugpunch_GetStableDeviceId();
    return c ? [NSString stringWithUTF8String:c] : @"";
}

/** Format an ISO-8601 timestamp as e.g. "Apr 25, 3:45 PM" using the device
 *  locale. Tolerates fractional seconds + Z by trimming them off before
 *  parsing — matches the Android side. */
static NSString* BPChatFormatTime(NSString* iso) {
    if (iso.length == 0) return @"";
    NSString* trimmed = iso;
    NSRange dot = [trimmed rangeOfString:@"."];
    if (dot.location != NSNotFound) trimmed = [trimmed substringToIndex:dot.location];
    if ([trimmed hasSuffix:@"Z"]) trimmed = [trimmed substringToIndex:trimmed.length - 1];
    static NSDateFormatter* in_ = nil;
    if (!in_) {
        in_ = [NSDateFormatter new];
        in_.dateFormat = @"yyyy-MM-dd'T'HH:mm:ss";
        in_.timeZone = [NSTimeZone timeZoneWithAbbreviation:@"UTC"];
        in_.locale = [NSLocale localeWithLocaleIdentifier:@"en_US_POSIX"];
    }
    NSDate* d = [in_ dateFromString:trimmed];
    if (!d) return iso;
    static NSDateFormatter* out_ = nil;
    if (!out_) {
        out_ = [NSDateFormatter new];
        out_.dateFormat = @"MMM d, h:mm a";
    }
    return [out_ stringFromDate:d];
}

/** PascalCase-or-camelCase fallback string lookup matching Android. */
static NSString* BPChatStr(NSDictionary* m, NSString* a, NSString* b, NSString* fallback) {
    id v = m[a];
    if (![v isKindOfClass:[NSString class]] || ((NSString*)v).length == 0) v = m[b];
    if (![v isKindOfClass:[NSString class]]) return fallback;
    return (NSString*)v;
}

/** PascalCase-or-camelCase fallback array lookup. */
static NSArray* BPChatArr(NSDictionary* m, NSString* a, NSString* b) {
    id v = m[a];
    if (![v isKindOfClass:[NSArray class]]) v = m[b];
    return [v isKindOfClass:[NSArray class]] ? (NSArray*)v : nil;
}

// ── Bubble cell ────────────────────────────────────────────────
//
// Asymmetric corner tail via UIBezierPath path mask — done at -drawRect:
// time so resizes always re-clip. The cell is a passive drawer; the VC
// builds + lays it out per-message.
@interface BPChatBubbleView : UIView
@property (nonatomic, assign) BOOL mine;
@property (nonatomic, strong) UIColor* fillColor;
@end
@implementation BPChatBubbleView
- (void)layoutSubviews {
    [super layoutSubviews];
    [self updateMask];
}
- (void)updateMask {
    if (self.bounds.size.width <= 0 || self.bounds.size.height <= 0) return;
    CGFloat r = 16, tail = 4;
    UIRectCorner mineCorners = UIRectCornerTopLeft | UIRectCornerTopRight | UIRectCornerBottomLeft;
    UIRectCorner theirCorners = UIRectCornerTopLeft | UIRectCornerTopRight | UIRectCornerBottomRight;
    UIBezierPath* p = [UIBezierPath bezierPathWithRoundedRect:self.bounds
                                            byRoundingCorners:self.mine ? mineCorners : theirCorners
                                                  cornerRadii:CGSizeMake(r, r)];
    // Smaller corner on the tail side.
    CAShapeLayer* mask = [CAShapeLayer layer];
    mask.path = p.CGPath;
    self.layer.mask = mask;
    self.backgroundColor = self.fillColor;
    // Tail visual is implicit — the asymmetric mask gives the bubble its
    // direction. Matches the Android setCornerRadii() hack with mismatched
    // tail/normal radii.
    (void)tail;
}
@end

// ── Disabled-overlay (chat globally off) ────────────────────────
@interface BPChatDisabledOverlay : UIView
@end
@implementation BPChatDisabledOverlay
@end

// ── View Controller ─────────────────────────────────────────────

@interface BugpunchChatViewController : UIViewController <UITextViewDelegate>
@end

@implementation BugpunchChatViewController {
    // Theme palette — resolved once in viewDidLoad so we can pass the same
    // colours into nested helpers without re-querying every time.
    UIColor* _cBg;
    UIColor* _cHeader;
    UIColor* _cBorder;
    UIColor* _cText;
    UIColor* _cTextDim;
    UIColor* _cTextMuted;
    UIColor* _cAccent;
    UIColor* _cBubbleOther;

    // Chrome
    UIScrollView* _scroll;
    UIView*       _messagesColumn;
    UILabel*      _emptyLabel;
    UILabel*      _hoursBanner;
    BPChatDisabledOverlay* _disabledOverlay;

    // Composer column
    UIView*       _composerBar;
    UIView*       _attachPill;            // floating Screenshot/Video chooser
    NSLayoutConstraint* _attachPillHeight;// 0 when hidden, intrinsic when shown
    UIScrollView* _attachStrip;           // pending attachment chips
    UIView*       _attachStripContent;    // inner stack of chips
    NSLayoutConstraint* _attachStripHeight; // 0 when hidden, 44 when chips present
    UIButton*     _attachBtn;             // "+" toggle
    UITextView*   _composer;
    UILabel*      _composerPlaceholder;   // overlay label — UITextView lacks a placeholder
    UIButton*     _sendBtn;

    // Layout — composer bottom anchor moves with the keyboard.
    NSLayoutConstraint* _composerBottom;

    // State
    NSMutableArray<NSDictionary*>* _messages;            // raw JSON dicts
    NSMutableArray<BPChatPendingAttachment*>* _pending;  // composer drafts
    NSString*  _threadId;
    NSString*  _lastMessageAt;
    BOOL       _disabled;
    BOOL       _offHours;
    NSString*  _hoursMessage;

    // Polling
    NSTimer*   _pollTimer;
    NSURLSession* _session;
}

static const NSTimeInterval kBPChatPollInterval = 5.0;

#pragma mark – Lifecycle

- (void)viewDidLoad {
    [super viewDidLoad];

    _messages = [NSMutableArray array];
    _pending  = [NSMutableArray array];

    [self applyTheme];
    self.view.backgroundColor = _cBg;

    // Telemetry (#32) — note whether the player tapped through with the
    // unread badge visible. Just a log line for v1; full analytics is out
    // of scope.
    extern int Bugpunch_LastUnreadCount(void);
    NSLog(@"[Bugpunch.ChatVC] chat opened (badge was: %d)", Bugpunch_LastUnreadCount());

    // Shared session. Mirrors BugpunchPoller — keeps a single keep-alive
    // connection across all chat HTTP for the lifetime of the VC.
    NSURLSessionConfiguration* cfg = [NSURLSessionConfiguration defaultSessionConfiguration];
    cfg.timeoutIntervalForRequest = 15.0;
    cfg.timeoutIntervalForResource = 60.0;
    _session = [NSURLSession sessionWithConfiguration:cfg];

    [self buildHoursBanner];
    [self buildMessagesArea];
    [self buildComposer];
    [self buildFloatingClose];
    [self buildDisabledOverlay];

    // Keyboard tracking — push composer up so it isn't covered.
    [[NSNotificationCenter defaultCenter] addObserver:self
        selector:@selector(onKeyboardWillChangeFrame:)
            name:UIKeyboardWillChangeFrameNotification
          object:nil];

    // Bootstrap.
    [self fetchHours];
    [self fetchThread];
}

- (void)viewWillAppear:(BOOL)animated {
    [super viewWillAppear:animated];
    [self startPolling];
}

- (void)viewWillDisappear:(BOOL)animated {
    [super viewWillDisappear:animated];
    [self stopPolling];
}

- (void)dealloc {
    [_pollTimer invalidate];
    [[NSNotificationCenter defaultCenter] removeObserver:self];
    [_session invalidateAndCancel];
}

#pragma mark – Rotation

// Host games commonly lock orientation at the AppDelegate level — the chat
// surface should rotate freely so the player can hold the phone naturally
// while typing. Capped by the orientations the host's Info.plist declares;
// nothing we can override at the SDK layer can extend past that.
- (UIInterfaceOrientationMask)supportedInterfaceOrientations {
    return UIInterfaceOrientationMaskAll;
}
- (BOOL)shouldAutorotate { return YES; }

- (void)applyTheme {
    _cBg          = [BPTheme color:@"backdrop"        fallback:[UIColor colorWithRed:0.063 green:0.071 blue:0.086 alpha:1]];
    _cHeader      = [BPTheme color:@"cardBackground"  fallback:[UIColor colorWithRed:0.106 green:0.122 blue:0.145 alpha:1]];
    _cBorder      = [BPTheme color:@"cardBorder"      fallback:[UIColor colorWithRed:0.165 green:0.192 blue:0.224 alpha:1]];
    _cText        = [BPTheme color:@"textPrimary"     fallback:[UIColor colorWithRed:0.945 green:0.957 blue:0.969 alpha:1]];
    _cTextDim     = [BPTheme color:@"textSecondary"   fallback:[UIColor colorWithRed:0.722 green:0.761 blue:0.812 alpha:1]];
    _cTextMuted   = [BPTheme color:@"textMuted"       fallback:[UIColor colorWithRed:0.545 green:0.584 blue:0.635 alpha:1]];
    _cAccent      = [BPTheme color:@"accentChat"      fallback:[UIColor colorWithRed:0.20  green:0.38  blue:0.60  alpha:1]];
    _cBubbleOther = [BPTheme color:@"cardBorder"      fallback:[UIColor colorWithRed:0.165 green:0.192 blue:0.224 alpha:1]];
}

#pragma mark – Floating close

/** Small ✕ in the top-right that dismisses the VC. Replaces the old header
 *  bar so the conversation has the whole screen. 32pt circle, 16pt glyph,
 *  added last so it sits above the message list and composer. */
- (void)buildFloatingClose {
    UIButton* close = [UIButton buttonWithType:UIButtonTypeCustom];
    close.translatesAutoresizingMaskIntoConstraints = NO;
    [close setTitle:@"✕" forState:UIControlStateNormal];
    [close setTitleColor:_cTextDim forState:UIControlStateNormal];
    close.titleLabel.font = [UIFont systemFontOfSize:16];
    close.backgroundColor = [_cHeader colorWithAlphaComponent:0.8];
    close.layer.cornerRadius = 16;
    close.layer.masksToBounds = YES;
    [close addTarget:self action:@selector(onCloseTapped) forControlEvents:UIControlEventTouchUpInside];
    [self.view addSubview:close];

    UILayoutGuide* safe = self.view.safeAreaLayoutGuide;
    [NSLayoutConstraint activateConstraints:@[
        [close.topAnchor constraintEqualToAnchor:safe.topAnchor constant:8],
        [close.trailingAnchor constraintEqualToAnchor:safe.trailingAnchor constant:-8],
        [close.widthAnchor constraintEqualToConstant:32],
        [close.heightAnchor constraintEqualToConstant:32],
    ]];
}

#pragma mark – Hours banner

- (void)buildHoursBanner {
    _hoursBanner = [[UILabel alloc] init];
    _hoursBanner.translatesAutoresizingMaskIntoConstraints = NO;
    _hoursBanner.numberOfLines = 0;
    _hoursBanner.backgroundColor = [UIColor colorWithRed:0.20 green:0.17 blue:0.09 alpha:1.0];
    _hoursBanner.textColor = [UIColor colorWithRed:0.96 green:0.81 blue:0.49 alpha:1.0];
    _hoursBanner.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];
    _hoursBanner.textAlignment = NSTextAlignmentCenter;
    _hoursBanner.hidden = YES;
    [self.view addSubview:_hoursBanner];

    UILayoutGuide* safe = self.view.safeAreaLayoutGuide;
    [NSLayoutConstraint activateConstraints:@[
        [_hoursBanner.topAnchor constraintEqualToAnchor:safe.topAnchor],
        [_hoursBanner.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor],
        [_hoursBanner.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor],
    ]];
}

#pragma mark – Messages area

- (void)buildMessagesArea {
    _scroll = [[UIScrollView alloc] init];
    _scroll.translatesAutoresizingMaskIntoConstraints = NO;
    _scroll.backgroundColor = _cBg;
    _scroll.alwaysBounceVertical = YES;
    _scroll.keyboardDismissMode = UIScrollViewKeyboardDismissModeInteractive;
    [self.view addSubview:_scroll];

    _messagesColumn = [[UIView alloc] init];
    _messagesColumn.translatesAutoresizingMaskIntoConstraints = NO;
    [_scroll addSubview:_messagesColumn];

    _emptyLabel = [[UILabel alloc] init];
    _emptyLabel.translatesAutoresizingMaskIntoConstraints = NO;
    _emptyLabel.text = [BPStrings text:@"chatEmpty" fallback:@"Say hi 👋 — the dev team will reply here"];
    _emptyLabel.textColor = _cTextMuted;
    _emptyLabel.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
    _emptyLabel.textAlignment = NSTextAlignmentCenter;
    _emptyLabel.numberOfLines = 0;
    [self.view addSubview:_emptyLabel];

    [NSLayoutConstraint activateConstraints:@[
        [_emptyLabel.centerXAnchor constraintEqualToAnchor:self.view.centerXAnchor],
        [_emptyLabel.centerYAnchor constraintEqualToAnchor:self.view.centerYAnchor],
        [_emptyLabel.leadingAnchor constraintGreaterThanOrEqualToAnchor:self.view.leadingAnchor constant:32],
        [_emptyLabel.trailingAnchor constraintLessThanOrEqualToAnchor:self.view.trailingAnchor constant:-32],

        [_messagesColumn.topAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.topAnchor constant:12],
        [_messagesColumn.leadingAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.leadingAnchor constant:12],
        [_messagesColumn.trailingAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.trailingAnchor constant:-12],
        [_messagesColumn.bottomAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.bottomAnchor constant:-12],
        [_messagesColumn.widthAnchor constraintEqualToAnchor:_scroll.frameLayoutGuide.widthAnchor constant:-24],
    ]];
}

#pragma mark – Composer

- (void)buildComposer {
    UIView* column = [[UIView alloc] init];
    column.translatesAutoresizingMaskIntoConstraints = NO;
    column.backgroundColor = _cHeader;
    [self.view addSubview:column];
    _composerBar = column;

    // Top hairline above the composer (matches the bottom one on the header).
    UIView* hairline = [[UIView alloc] init];
    hairline.translatesAutoresizingMaskIntoConstraints = NO;
    hairline.backgroundColor = _cBorder;
    [column addSubview:hairline];

    // Attach pill (Screenshot / Record video). Sits above the composer when
    // toggled — flush-left so it visually anchors to the "+" button.
    _attachPill = [self buildAttachPill];
    _attachPill.translatesAutoresizingMaskIntoConstraints = NO;
    _attachPill.hidden = YES;
    [column addSubview:_attachPill];

    // Pending-attachment chip strip — horizontal scroll above the input row.
    _attachStrip = [[UIScrollView alloc] init];
    _attachStrip.translatesAutoresizingMaskIntoConstraints = NO;
    _attachStrip.showsHorizontalScrollIndicator = NO;
    _attachStrip.alwaysBounceHorizontal = NO;
    _attachStrip.hidden = YES;
    [column addSubview:_attachStrip];

    _attachStripContent = [[UIView alloc] init];
    _attachStripContent.translatesAutoresizingMaskIntoConstraints = NO;
    [_attachStrip addSubview:_attachStripContent];

    // Input row.
    UIView* inputRow = [[UIView alloc] init];
    inputRow.translatesAutoresizingMaskIntoConstraints = NO;
    [column addSubview:inputRow];

    _attachBtn = [UIButton buttonWithType:UIButtonTypeSystem];
    _attachBtn.translatesAutoresizingMaskIntoConstraints = NO;
    [_attachBtn setTitle:@"+" forState:UIControlStateNormal];
    [_attachBtn setTitleColor:_cText forState:UIControlStateNormal];
    _attachBtn.titleLabel.font = [UIFont boldSystemFontOfSize:24];
    _attachBtn.backgroundColor = _cBorder;
    _attachBtn.layer.cornerRadius = 20;
    _attachBtn.layer.masksToBounds = YES;
    [_attachBtn addTarget:self action:@selector(onAttachToggle) forControlEvents:UIControlEventTouchUpInside];
    [inputRow addSubview:_attachBtn];

    _composer = [[UITextView alloc] init];
    _composer.translatesAutoresizingMaskIntoConstraints = NO;
    _composer.delegate = self;
    _composer.backgroundColor = _cBg;
    _composer.textColor = _cText;
    _composer.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:15]];
    _composer.layer.borderWidth = 1.0;
    _composer.layer.borderColor = _cBorder.CGColor;
    _composer.layer.cornerRadius = 20;
    _composer.textContainerInset = UIEdgeInsetsMake(10, 10, 10, 10);
    _composer.scrollEnabled = NO;     // grows with content up to maxHeight
    _composer.returnKeyType = UIReturnKeyDefault;
    _composer.keyboardAppearance = UIKeyboardAppearanceDark;
    [inputRow addSubview:_composer];

    _composerPlaceholder = [[UILabel alloc] init];
    _composerPlaceholder.translatesAutoresizingMaskIntoConstraints = NO;
    _composerPlaceholder.text = [BPStrings text:@"chatComposerHint" fallback:@"Type a message…"];
    _composerPlaceholder.textColor = _cTextMuted;
    _composerPlaceholder.font = _composer.font;
    _composerPlaceholder.userInteractionEnabled = NO;
    [_composer addSubview:_composerPlaceholder];

    _sendBtn = [UIButton buttonWithType:UIButtonTypeCustom];
    _sendBtn.translatesAutoresizingMaskIntoConstraints = NO;
    [_sendBtn setTitle:@"➤" forState:UIControlStateNormal];
    [_sendBtn setTitleColor:[UIColor whiteColor] forState:UIControlStateNormal];
    _sendBtn.titleLabel.font = [UIFont boldSystemFontOfSize:18];
    _sendBtn.backgroundColor = _cAccent;
    _sendBtn.layer.cornerRadius = 20;
    _sendBtn.layer.masksToBounds = YES;
    [_sendBtn addTarget:self action:@selector(onSendTapped) forControlEvents:UIControlEventTouchUpInside];
    [inputRow addSubview:_sendBtn];

    UILayoutGuide* safe = self.view.safeAreaLayoutGuide;
    _composerBottom = [column.bottomAnchor constraintEqualToAnchor:safe.bottomAnchor];

    [NSLayoutConstraint activateConstraints:@[
        // Column anchors — pinned to bottom of safe area; keyboard moves the
        // bottom anchor in onKeyboardWillChangeFrame:.
        [column.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor],
        [column.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor],
        _composerBottom,

        [hairline.leadingAnchor constraintEqualToAnchor:column.leadingAnchor],
        [hairline.trailingAnchor constraintEqualToAnchor:column.trailingAnchor],
        [hairline.topAnchor constraintEqualToAnchor:column.topAnchor],
        [hairline.heightAnchor constraintEqualToConstant:0.5],

        // Attach pill (top-most child) — collapses to zero when hidden so
        // the composer hugs the keyboard / safe-area bottom; springs back
        // to its intrinsic ~52pt when toggled visible.
        [_attachPill.topAnchor constraintEqualToAnchor:hairline.bottomAnchor],
        [_attachPill.leadingAnchor constraintEqualToAnchor:column.leadingAnchor constant:8],
        [_attachPill.trailingAnchor constraintLessThanOrEqualToAnchor:column.trailingAnchor constant:-8],
        (_attachPillHeight = [_attachPill.heightAnchor constraintEqualToConstant:0]),

        // Strip below pill
        [_attachStrip.topAnchor constraintEqualToAnchor:_attachPill.bottomAnchor],
        [_attachStrip.leadingAnchor constraintEqualToAnchor:column.leadingAnchor],
        [_attachStrip.trailingAnchor constraintEqualToAnchor:column.trailingAnchor],

        [_attachStripContent.topAnchor constraintEqualToAnchor:_attachStrip.contentLayoutGuide.topAnchor constant:6],
        [_attachStripContent.leadingAnchor constraintEqualToAnchor:_attachStrip.contentLayoutGuide.leadingAnchor constant:8],
        [_attachStripContent.trailingAnchor constraintEqualToAnchor:_attachStrip.contentLayoutGuide.trailingAnchor constant:-8],
        [_attachStripContent.bottomAnchor constraintEqualToAnchor:_attachStrip.contentLayoutGuide.bottomAnchor constant:-6],
        // Strip height collapses to zero when no chips, springs to 44pt
        // when there are. Toggled in -rebuildAttachmentStrip.
        (_attachStripHeight = [_attachStrip.heightAnchor constraintEqualToConstant:0]),

        // Input row
        [inputRow.topAnchor constraintEqualToAnchor:_attachStrip.bottomAnchor],
        [inputRow.leadingAnchor constraintEqualToAnchor:column.leadingAnchor constant:8],
        [inputRow.trailingAnchor constraintEqualToAnchor:column.trailingAnchor constant:-8],
        [inputRow.bottomAnchor constraintEqualToAnchor:column.bottomAnchor constant:-8],

        [_attachBtn.leadingAnchor constraintEqualToAnchor:inputRow.leadingAnchor],
        [_attachBtn.bottomAnchor constraintEqualToAnchor:inputRow.bottomAnchor constant:-2],
        [_attachBtn.widthAnchor constraintEqualToConstant:40],
        [_attachBtn.heightAnchor constraintEqualToConstant:40],

        [_composer.leadingAnchor constraintEqualToAnchor:_attachBtn.trailingAnchor constant:8],
        [_composer.topAnchor constraintEqualToAnchor:inputRow.topAnchor constant:4],
        [_composer.bottomAnchor constraintEqualToAnchor:inputRow.bottomAnchor constant:-2],
        [_composer.trailingAnchor constraintEqualToAnchor:_sendBtn.leadingAnchor constant:-8],
        // 4-line cap roughly = 4 * lineHeight + insets ≈ 120pt.
        [_composer.heightAnchor constraintGreaterThanOrEqualToConstant:40],
        [_composer.heightAnchor constraintLessThanOrEqualToConstant:120],

        [_composerPlaceholder.leadingAnchor constraintEqualToAnchor:_composer.leadingAnchor constant:14],
        [_composerPlaceholder.topAnchor constraintEqualToAnchor:_composer.topAnchor constant:10],

        [_sendBtn.trailingAnchor constraintEqualToAnchor:inputRow.trailingAnchor],
        [_sendBtn.bottomAnchor constraintEqualToAnchor:inputRow.bottomAnchor constant:-2],
        [_sendBtn.widthAnchor constraintEqualToConstant:40],
        [_sendBtn.heightAnchor constraintEqualToConstant:40],

        // Scroll fills above composer.
        [_scroll.topAnchor constraintEqualToAnchor:_hoursBanner.bottomAnchor],
        [_scroll.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor],
        [_scroll.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor],
        [_scroll.bottomAnchor constraintEqualToAnchor:column.topAnchor],
    ]];

    [self updateSendEnabled];
}

- (UIView*)buildAttachPill {
    UIView* outer = [[UIView alloc] init];
    outer.translatesAutoresizingMaskIntoConstraints = NO;

    // Capsule with two options separated by a thin divider.
    UIView* caps = [[UIView alloc] init];
    caps.translatesAutoresizingMaskIntoConstraints = NO;
    caps.backgroundColor = _cBg;
    caps.layer.cornerRadius = 22;
    caps.layer.borderWidth = 1;
    caps.layer.borderColor = _cBorder.CGColor;
    [outer addSubview:caps];

    UIButton* shot = [self pillOption:@"📷  Screenshot" sel:@selector(onTakeScreenshot)];
    UIButton* video = [self pillOption:@"🎥  Record video" sel:@selector(onStartVideo)];
    UIView* sep = [[UIView alloc] init];
    sep.translatesAutoresizingMaskIntoConstraints = NO;
    sep.backgroundColor = _cBorder;

    [caps addSubview:shot];
    [caps addSubview:sep];
    [caps addSubview:video];

    [NSLayoutConstraint activateConstraints:@[
        [caps.topAnchor constraintEqualToAnchor:outer.topAnchor constant:8],
        [caps.bottomAnchor constraintEqualToAnchor:outer.bottomAnchor],
        [caps.leadingAnchor constraintEqualToAnchor:outer.leadingAnchor],
        [caps.trailingAnchor constraintLessThanOrEqualToAnchor:outer.trailingAnchor],

        [shot.leadingAnchor constraintEqualToAnchor:caps.leadingAnchor constant:6],
        [shot.topAnchor constraintEqualToAnchor:caps.topAnchor constant:6],
        [shot.bottomAnchor constraintEqualToAnchor:caps.bottomAnchor constant:-6],

        [sep.leadingAnchor constraintEqualToAnchor:shot.trailingAnchor constant:2],
        [sep.centerYAnchor constraintEqualToAnchor:caps.centerYAnchor],
        [sep.widthAnchor constraintEqualToConstant:1],
        [sep.heightAnchor constraintEqualToConstant:20],

        [video.leadingAnchor constraintEqualToAnchor:sep.trailingAnchor constant:2],
        [video.trailingAnchor constraintEqualToAnchor:caps.trailingAnchor constant:-6],
        [video.topAnchor constraintEqualToAnchor:caps.topAnchor constant:6],
        [video.bottomAnchor constraintEqualToAnchor:caps.bottomAnchor constant:-6],
    ]];
    return outer;
}

- (UIButton*)pillOption:(NSString*)title sel:(SEL)sel {
    UIButton* b = [UIButton buttonWithType:UIButtonTypeSystem];
    b.translatesAutoresizingMaskIntoConstraints = NO;
    [b setTitle:title forState:UIControlStateNormal];
    [b setTitleColor:_cText forState:UIControlStateNormal];
    b.titleLabel.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
    b.contentEdgeInsets = UIEdgeInsetsMake(8, 14, 8, 14);
    [b addTarget:self action:sel forControlEvents:UIControlEventTouchUpInside];
    return b;
}

#pragma mark – Disabled overlay

- (void)buildDisabledOverlay {
    _disabledOverlay = [[BPChatDisabledOverlay alloc] init];
    _disabledOverlay.translatesAutoresizingMaskIntoConstraints = NO;
    _disabledOverlay.backgroundColor = [UIColor colorWithWhite:0 alpha:0.8];
    _disabledOverlay.hidden = YES;
    [self.view addSubview:_disabledOverlay];

    UILabel* msg = [[UILabel alloc] init];
    msg.translatesAutoresizingMaskIntoConstraints = NO;
    msg.text = [BPStrings text:@"chatDisabled" fallback:@"Chat is unavailable right now."];
    msg.textColor = _cText;
    msg.textAlignment = NSTextAlignmentCenter;
    msg.numberOfLines = 0;
    msg.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:15]];
    [_disabledOverlay addSubview:msg];

    UIButton* close = [UIButton buttonWithType:UIButtonTypeSystem];
    close.translatesAutoresizingMaskIntoConstraints = NO;
    [close setTitle:[BPStrings text:@"close" fallback:@"Close"] forState:UIControlStateNormal];
    [close setTitleColor:_cAccent forState:UIControlStateNormal];
    close.titleLabel.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:15]];
    [close addTarget:self action:@selector(onCloseTapped) forControlEvents:UIControlEventTouchUpInside];
    [_disabledOverlay addSubview:close];

    [NSLayoutConstraint activateConstraints:@[
        [_disabledOverlay.topAnchor constraintEqualToAnchor:self.view.topAnchor],
        [_disabledOverlay.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor],
        [_disabledOverlay.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor],
        [_disabledOverlay.bottomAnchor constraintEqualToAnchor:self.view.bottomAnchor],

        [msg.centerXAnchor constraintEqualToAnchor:_disabledOverlay.centerXAnchor],
        [msg.centerYAnchor constraintEqualToAnchor:_disabledOverlay.centerYAnchor constant:-20],
        [msg.leadingAnchor constraintGreaterThanOrEqualToAnchor:_disabledOverlay.leadingAnchor constant:24],
        [msg.trailingAnchor constraintLessThanOrEqualToAnchor:_disabledOverlay.trailingAnchor constant:-24],

        [close.centerXAnchor constraintEqualToAnchor:_disabledOverlay.centerXAnchor],
        [close.topAnchor constraintEqualToAnchor:msg.bottomAnchor constant:18],
    ]];
}

#pragma mark – Actions

- (void)onCloseTapped {
    [self dismissViewControllerAnimated:YES completion:nil];
}

- (void)onAttachToggle {
    if (_disabled) return;
    BOOL nowHidden = !_attachPill.hidden;
    _attachPill.hidden = nowHidden;
    // 52pt accommodates the 8pt outer top inset + ~44pt capsule from
    // -buildAttachPill (8pt outer top + 6pt inner pad + ~24pt button row +
    // 6pt inner pad). Adjust if the pill grows.
    _attachPillHeight.active = nowHidden;
    [UIView animateWithDuration:0.18 animations:^{ [self.view layoutIfNeeded]; }];
}

- (void)hideAttachPill {
    _attachPill.hidden = YES;
    _attachPillHeight.active = YES;
}

- (void)onTakeScreenshot {
    [self hideAttachPill];
    NSString* dir = NSTemporaryDirectory();
    NSString* outPath = [dir stringByAppendingPathComponent:
        [NSString stringWithFormat:@"bp_chat_shot_%lld.jpg", (long long)([NSDate date].timeIntervalSince1970 * 1000)]];

    // Hide the chat VC briefly so the captured frame is the live game view.
    // Re-present once the file is on disk. Matches the Android moveTaskToBack
    // dance.
    UIViewController* presenter = self.presentingViewController;
    [self dismissViewControllerAnimated:YES completion:^{
        // Tiny delay so any dismiss animation has cleared before we capture.
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.25 * NSEC_PER_SEC)),
                       dispatch_get_main_queue(), ^{
            Bugpunch_CaptureScreenshot([@"chat" UTF8String],
                [outPath UTF8String], 90,
                ^(const char* requestId, int success, const char* payload) {
                    NSString* path = (success && payload) ? [NSString stringWithUTF8String:payload] : nil;
                    dispatch_async(dispatch_get_main_queue(), ^{
                        // Re-present and add the chip if capture succeeded.
                        if (path) {
                            BPChatPendingAttachment* a = [BPChatPendingAttachment new];
                            a.type = @"image";
                            a.path = path;
                            [self->_pending addObject:a];
                        }
                        UIViewController* top = presenter ?: [BugpunchChatViewController topPresentingVC];
                        if (top) {
                            [top presentViewController:self animated:YES completion:^{
                                [self rebuildAttachmentStrip];
                                [self updateSendEnabled];
                            }];
                        }
                    });
                });
        });
    }];
}

// ── Chat video attachment (issue #30) ──────────────────────────
//
// Mirrors the Android Activity flow: hide attach pill → dismiss chat to the
// game view → start ODDRecorder writing a single-segment MP4 to the temp
// dir (ReplayKit's startCapture handles its own iOS consent prompt the
// first time it's called) → float a Stop pill at the top of the key window
// → on Stop tap, stop the recorder, re-present the chat VC with the .mp4
// path attached as a pending chip.
//
// We hold the in-progress output path as a static rather than an ivar
// because the chat VC dismisses while recording — we need the Stop pill
// (which lives on the key window, not the VC) to know which file to
// finalise without asking the dismissed VC.

// In-progress MP4 path (without `.mp4` extension — ODDRecorder appends it).
static NSString* sBPChatVideoOutPathStem = nil;
// Floating Stop pill — lives on the key window so it survives chat dismiss.
static UIView* sBPChatStopPill = nil;
// VC instance to re-present once recording finishes — held weakly so a
// dealloc'd VC doesn't keep dangling.
static __weak BugpunchChatViewController* sBPChatVCWaitingForVideo = nil;

- (void)onStartVideo {
    [self hideAttachPill];

    NSString* dir = NSTemporaryDirectory();
    // ODDRecorder_Start appends `.mp4` to whatever stem we hand it.
    NSString* stem = [dir stringByAppendingPathComponent:
        [NSString stringWithFormat:@"bp_chat_video_%lld",
            (long long)([NSDate date].timeIntervalSince1970 * 1000)]];
    sBPChatVideoOutPathStem = stem;
    sBPChatVCWaitingForVideo = self;

    UIViewController* presenter = self.presentingViewController;
    __weak typeof(self) weakSelf = self;
    [self dismissViewControllerAnimated:YES completion:^{
        // Tiny delay so the dismiss animation has cleared before we ask
        // ReplayKit for the screen, mirroring the screenshot path.
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.25 * NSEC_PER_SEC)),
                       dispatch_get_main_queue(), ^{
            // 0,0 → ODDRecorder picks up the screen size automatically.
            // 30 fps, 2 Mbps matches the bug-report defaults and keeps the
            // file comfortably below the 30 MB chat upload cap for short
            // recordings (>~2 minutes if the player goes wild). No audio:
            // chat videos are visual repros, not voice memos, and skipping
            // audio also dodges the mic-permission prompt entirely.
            ODDRecorder_Start([stem UTF8String], 0, 0, 30, 2000000, false);
            // Show the Stop pill once we've handed off to the recorder.
            // ReplayKit's own consent dialog (if it appears) renders on top
            // of the pill, so the user always has a reachable Stop control
            // once they've granted permission.
            [BugpunchChatViewController showChatStopPillAttachedTo:presenter
                                                              forVC:weakSelf];
        });
    }];
}

/** Shows the floating red "Stop ⏺" pill at the top of the key window. The
 *  pill is added to the application's key window rather than any specific
 *  view controller because the chat VC has been dismissed by this point —
 *  same reason the static state above isn't an ivar. */
+ (void)showChatStopPillAttachedTo:(UIViewController*)presenter
                              forVC:(BugpunchChatViewController*)vc {
    if (sBPChatStopPill) return;

    UIWindow* window = nil;
    if (@available(iOS 13.0, *)) {
        for (UIScene* s in UIApplication.sharedApplication.connectedScenes) {
            if (![s isKindOfClass:[UIWindowScene class]]) continue;
            for (UIWindow* w in ((UIWindowScene*)s).windows) {
                if (w.isKeyWindow) { window = w; break; }
            }
            if (window) break;
        }
    }
    if (!window) {
        #pragma clang diagnostic push
        #pragma clang diagnostic ignored "-Wdeprecated-declarations"
        window = UIApplication.sharedApplication.keyWindow;
        #pragma clang diagnostic pop
    }
    if (!window) return;

    UIColor* accent = [BPTheme color:@"accentRecord"
                            fallback:[UIColor colorWithRed:0.82 green:0.18 blue:0.18 alpha:1.0]];

    UIView* pill = [[UIView alloc] init];
    pill.translatesAutoresizingMaskIntoConstraints = NO;
    pill.backgroundColor = accent;
    pill.layer.cornerRadius = 22;
    pill.userInteractionEnabled = YES;

    UIView* dot = [[UIView alloc] init];
    dot.translatesAutoresizingMaskIntoConstraints = NO;
    dot.backgroundColor = UIColor.whiteColor;
    dot.layer.cornerRadius = 5;
    [pill addSubview:dot];

    UILabel* label = [[UILabel alloc] init];
    label.translatesAutoresizingMaskIntoConstraints = NO;
    label.text = [BPStrings text:@"chatStopRecording" fallback:@"Stop ⏺"];
    label.textColor = UIColor.whiteColor;
    label.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
    [pill addSubview:label];

    UITapGestureRecognizer* tap = [[UITapGestureRecognizer alloc]
        initWithTarget:[BugpunchChatViewController class]
                action:@selector(onChatStopPillTapped)];
    [pill addGestureRecognizer:tap];

    [window addSubview:pill];
    sBPChatStopPill = pill;

    UILayoutGuide* safe = window.safeAreaLayoutGuide;
    [NSLayoutConstraint activateConstraints:@[
        [pill.topAnchor constraintEqualToAnchor:safe.topAnchor constant:12],
        [pill.centerXAnchor constraintEqualToAnchor:window.centerXAnchor],
        [pill.heightAnchor constraintEqualToConstant:44],

        [dot.leadingAnchor constraintEqualToAnchor:pill.leadingAnchor constant:14],
        [dot.centerYAnchor constraintEqualToAnchor:pill.centerYAnchor],
        [dot.widthAnchor constraintEqualToConstant:10],
        [dot.heightAnchor constraintEqualToConstant:10],

        [label.leadingAnchor constraintEqualToAnchor:dot.trailingAnchor constant:8],
        [label.trailingAnchor constraintEqualToAnchor:pill.trailingAnchor constant:-14],
        [label.centerYAnchor constraintEqualToAnchor:pill.centerYAnchor],
    ]];

    // Capture the presenter / VC into the static path-completion block so
    // -onChatStopPillTapped can re-present without depending on a strong
    // VC reference (the chat VC is currently dismissed).
    objc_setAssociatedObject(pill, "bpPresenter", presenter, OBJC_ASSOCIATION_RETAIN_NONATOMIC);
    objc_setAssociatedObject(pill, "bpVC", vc, OBJC_ASSOCIATION_ASSIGN);
}

+ (void)hideChatStopPill {
    if (!sBPChatStopPill) return;
    [sBPChatStopPill removeFromSuperview];
    sBPChatStopPill = nil;
}

+ (void)onChatStopPillTapped {
    UIView* pill = sBPChatStopPill;
    UIViewController* presenter = pill ? objc_getAssociatedObject(pill, "bpPresenter") : nil;
    BugpunchChatViewController* vc = pill ? objc_getAssociatedObject(pill, "bpVC") : nil;
    [self hideChatStopPill];

    NSString* stem = sBPChatVideoOutPathStem;
    sBPChatVideoOutPathStem = nil;

    // Drain ODDRecorder synchronously off the main queue so we don't block
    // the touch path; ODDRecorder_Stop takes up to 10s to finalise the file.
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        // ODDRecorder_Stop's outPath buffer is filled with the actual file
        // path on success; we pre-compute the expected path from the stem
        // for the failure case where the buffer is left empty.
        char outPath[1024] = {0};
        ODDRecorder_Stop(outPath, sizeof(outPath));

        NSString* finalPath = nil;
        if (outPath[0] != 0) {
            finalPath = [NSString stringWithUTF8String:outPath];
        } else if (stem) {
            finalPath = [stem stringByAppendingString:@".mp4"];
        }

        // Validate file exists + is non-empty before handing it back. A
        // ReplayKit failure leaves a zero-byte file which would just make
        // a useless chip.
        BOOL ok = NO;
        if (finalPath.length > 0) {
            NSDictionary* attrs = [[NSFileManager defaultManager]
                attributesOfItemAtPath:finalPath error:nil];
            ok = attrs && [attrs[NSFileSize] longLongValue] > 0;
        }

        dispatch_async(dispatch_get_main_queue(), ^{
            // Re-present the chat VC. If the VC was deallocated for any
            // reason (rare — chat is the top presenter under normal flow),
            // build a fresh one. Either way the recorded file becomes a
            // pending attachment chip and the player can hit send.
            BugpunchChatViewController* target = vc ?: [BugpunchChatViewController new];
            target.modalPresentationStyle = UIModalPresentationFullScreen;
            UIViewController* top = presenter ?: [BugpunchChatViewController topPresentingVC];
            if (!top) return;
            [top presentViewController:target animated:YES completion:^{
                if (ok && finalPath) {
                    [target appendPendingVideoAttachment:finalPath];
                } else if (finalPath) {
                    NSLog(@"[Bugpunch.ChatVC] chat video segment invalid — discarding %@", finalPath);
                    [[NSFileManager defaultManager] removeItemAtPath:finalPath error:nil];
                }
            }];
        });
    });
}

/** Hook used by the static stop-pill handler to add the captured MP4 to
 *  this VC's pending-attachment list once the chat is back on screen. */
- (void)appendPendingVideoAttachment:(NSString*)path {
    BPChatPendingAttachment* a = [BPChatPendingAttachment new];
    a.type = @"video";
    a.path = path;
    [self->_pending addObject:a];
    [self rebuildAttachmentStrip];
    [self updateSendEnabled];
}

- (void)onSendTapped {
    [self sendCurrent];
}

- (void)updateSendEnabled {
    BOOL hasText = _composer.text.length > 0
        && [[_composer.text stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceAndNewlineCharacterSet]] length] > 0;
    BOOL ok = (hasText || _pending.count > 0) && !_disabled;
    _sendBtn.alpha = ok ? 1.0 : 0.4;
    _sendBtn.enabled = ok;
}

#pragma mark – UITextViewDelegate

- (void)textViewDidChange:(UITextView *)textView {
    _composerPlaceholder.hidden = textView.text.length > 0;
    [self updateSendEnabled];
}

#pragma mark – Pending attachment chips

- (void)rebuildAttachmentStrip {
    for (UIView* v in [_attachStripContent.subviews copy]) [v removeFromSuperview];
    if (_pending.count == 0) {
        _attachStrip.hidden = YES;
        _attachStripHeight.constant = 0;
        return;
    }
    _attachStrip.hidden = NO;
    _attachStripHeight.constant = 44;

    UIView* prev = nil;
    for (BPChatPendingAttachment* a in _pending) {
        UIView* chip = [[UIView alloc] init];
        chip.translatesAutoresizingMaskIntoConstraints = NO;
        chip.backgroundColor = _cBg;
        chip.layer.cornerRadius = 8;
        chip.layer.borderWidth = 1;
        chip.layer.borderColor = _cBorder.CGColor;

        UILabel* label = [[UILabel alloc] init];
        label.translatesAutoresizingMaskIntoConstraints = NO;
        NSString* glyph = [a.type isEqualToString:@"image"] ? @"📷 " : @"🎥 ";
        label.text = [glyph stringByAppendingString:a.path.lastPathComponent];
        label.textColor = _cTextDim;
        label.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];

        UIButton* x = [UIButton buttonWithType:UIButtonTypeSystem];
        x.translatesAutoresizingMaskIntoConstraints = NO;
        [x setTitle:@"×" forState:UIControlStateNormal];
        [x setTitleColor:_cTextMuted forState:UIControlStateNormal];
        x.titleLabel.font = [UIFont boldSystemFontOfSize:18];
        // Capture the attachment so the action knows which one to pop.
        BPChatPendingAttachment* cap = a;
        [x addTarget:self action:@selector(onChipRemove:) forControlEvents:UIControlEventTouchUpInside];
        x.tag = (NSInteger)[_pending indexOfObjectIdenticalTo:cap];

        [chip addSubview:label];
        [chip addSubview:x];
        [_attachStripContent addSubview:chip];

        [NSLayoutConstraint activateConstraints:@[
            [label.leadingAnchor constraintEqualToAnchor:chip.leadingAnchor constant:8],
            [label.centerYAnchor constraintEqualToAnchor:chip.centerYAnchor],

            [x.leadingAnchor constraintEqualToAnchor:label.trailingAnchor constant:6],
            [x.trailingAnchor constraintEqualToAnchor:chip.trailingAnchor constant:-4],
            [x.centerYAnchor constraintEqualToAnchor:chip.centerYAnchor],
            [x.widthAnchor constraintEqualToConstant:24],
            [x.heightAnchor constraintEqualToConstant:24],

            [chip.topAnchor constraintEqualToAnchor:_attachStripContent.topAnchor],
            [chip.bottomAnchor constraintEqualToAnchor:_attachStripContent.bottomAnchor],
        ]];

        if (prev) {
            [chip.leadingAnchor constraintEqualToAnchor:prev.trailingAnchor constant:6].active = YES;
        } else {
            [chip.leadingAnchor constraintEqualToAnchor:_attachStripContent.leadingAnchor].active = YES;
        }
        prev = chip;
    }
    if (prev) {
        [prev.trailingAnchor constraintLessThanOrEqualToAnchor:_attachStripContent.trailingAnchor].active = YES;
    }
}

- (void)onChipRemove:(UIButton*)sender {
    NSInteger i = sender.tag;
    if (i < 0 || i >= (NSInteger)_pending.count) return;
    [_pending removeObjectAtIndex:i];
    [self rebuildAttachmentStrip];
    [self updateSendEnabled];
}

#pragma mark – Render messages

- (void)renderMessages {
    for (UIView* v in [_messagesColumn.subviews copy]) [v removeFromSuperview];
    if (_messages.count == 0) {
        _emptyLabel.hidden = NO;
        return;
    }
    _emptyLabel.hidden = YES;

    UIView* prev = nil;
    for (NSDictionary* m in _messages) {
        UIView* row = [self buildBubbleRow:m];
        row.translatesAutoresizingMaskIntoConstraints = NO;
        [_messagesColumn addSubview:row];
        [NSLayoutConstraint activateConstraints:@[
            [row.leadingAnchor constraintEqualToAnchor:_messagesColumn.leadingAnchor],
            [row.trailingAnchor constraintEqualToAnchor:_messagesColumn.trailingAnchor],
            [row.topAnchor constraintEqualToAnchor:(prev ?: _messagesColumn).bottomAnchor constant:prev ? 6 : 0],
        ]];
        prev = row;
    }
    if (prev) {
        [prev.bottomAnchor constraintEqualToAnchor:_messagesColumn.bottomAnchor].active = YES;
    }

    // Scroll to bottom after layout.
    dispatch_async(dispatch_get_main_queue(), ^{
        [self scrollToBottom];
    });
}

- (void)scrollToBottom {
    [self.view layoutIfNeeded];
    CGFloat y = MAX(0, _scroll.contentSize.height - _scroll.bounds.size.height
                    + _scroll.contentInset.bottom);
    [_scroll setContentOffset:CGPointMake(0, y) animated:NO];
}

/** Build a single bubble row (attachments + body + optional approve/decline
 *  + timestamp), aligned to the correct side. Mirrors the Android
 *  buildBubble layout. */
- (UIView*)buildBubbleRow:(NSDictionary*)m {
    NSString* sender = BPChatStr(m, @"Sender", @"sender", @"qa");
    BOOL mine = [sender caseInsensitiveCompare:@"sdk"] == NSOrderedSame;
    NSString* body = BPChatStr(m, @"Body", @"body", @"");
    NSString* createdAt = BPChatStr(m, @"CreatedAt", @"createdAt", @"");
    NSString* type = BPChatStr(m, @"Type", @"type", @"text");
    NSString* requestState = BPChatStr(m, @"RequestState", @"requestState", @"pending");
    NSArray* atts = BPChatArr(m, @"Attachments", @"attachments");

    BOOL isRequest = ([type caseInsensitiveCompare:@"scriptRequest"] == NSOrderedSame
                  ||  [type caseInsensitiveCompare:@"dataRequest"]   == NSOrderedSame) && !mine;
    BOOL pendingRequest = isRequest && [requestState caseInsensitiveCompare:@"pending"] == NSOrderedSame;

    UIView* row = [[UIView alloc] init];

    UIView* prev = nil;
    // 1. Attachments (image thumbs, video placeholder).
    for (id a in (atts ?: @[])) {
        if (![a isKindOfClass:[NSDictionary class]]) continue;
        UIView* thumb = [self buildAttachmentThumb:(NSDictionary*)a mine:mine];
        if (!thumb) continue;
        thumb.translatesAutoresizingMaskIntoConstraints = NO;
        [row addSubview:thumb];
        [NSLayoutConstraint activateConstraints:@[
            [thumb.topAnchor constraintEqualToAnchor:(prev ?: row).bottomAnchor constant:prev ? 4 : 0],
            mine ? [thumb.trailingAnchor constraintEqualToAnchor:row.trailingAnchor]
                 : [thumb.leadingAnchor constraintEqualToAnchor:row.leadingAnchor],
        ]];
        prev = thumb;
    }

    // 2. Bubble body — special inline approve/decline if pending request,
    //    plain text bubble otherwise (or skipped if attachment-only).
    if (pendingRequest) {
        UIView* req = [self buildRequestBubble:m type:type body:body];
        req.translatesAutoresizingMaskIntoConstraints = NO;
        [row addSubview:req];
        [NSLayoutConstraint activateConstraints:@[
            [req.topAnchor constraintEqualToAnchor:(prev ?: row).bottomAnchor constant:prev ? 4 : 0],
            [req.leadingAnchor constraintEqualToAnchor:row.leadingAnchor],
            [req.trailingAnchor constraintLessThanOrEqualToAnchor:row.trailingAnchor],
            [req.widthAnchor constraintLessThanOrEqualToAnchor:row.widthAnchor multiplier:0.85],
        ]];
        prev = req;
    } else {
        BOOL hasAttachments = atts.count > 0;
        if (body.length > 0 || !hasAttachments) {
            BPChatBubbleView* bubble = [[BPChatBubbleView alloc] init];
            bubble.translatesAutoresizingMaskIntoConstraints = NO;
            bubble.mine = mine;
            bubble.fillColor = mine ? _cAccent : _cBubbleOther;

            UILabel* label = [[UILabel alloc] init];
            label.translatesAutoresizingMaskIntoConstraints = NO;
            NSString* shown = body;
            if (shown.length == 0 && hasAttachments) {
                NSString* attType = BPChatStr(atts.firstObject, @"type", @"Type", @"image");
                shown = [attType caseInsensitiveCompare:@"image"] == NSOrderedSame ? @"📷" : @"🎥";
            }
            label.text = shown;
            label.numberOfLines = 0;
            label.textColor = mine ? [UIColor whiteColor] : _cText;
            label.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
            // URL autolink — fall back to attributed string with link
            // attribute on substring matches.
            NSAttributedString* linked = [self linkifyText:shown
                                                     color:label.textColor
                                                      font:label.font];
            if (linked) label.attributedText = linked; else label.text = shown;
            [bubble addSubview:label];

            [row addSubview:bubble];
            [NSLayoutConstraint activateConstraints:@[
                [bubble.topAnchor constraintEqualToAnchor:(prev ?: row).bottomAnchor constant:prev ? 4 : 0],
                [bubble.widthAnchor constraintLessThanOrEqualToAnchor:row.widthAnchor multiplier:0.8],
                mine ? [bubble.trailingAnchor constraintEqualToAnchor:row.trailingAnchor]
                     : [bubble.leadingAnchor constraintEqualToAnchor:row.leadingAnchor],

                [label.topAnchor constraintEqualToAnchor:bubble.topAnchor constant:8],
                [label.bottomAnchor constraintEqualToAnchor:bubble.bottomAnchor constant:-8],
                [label.leadingAnchor constraintEqualToAnchor:bubble.leadingAnchor constant:12],
                [label.trailingAnchor constraintEqualToAnchor:bubble.trailingAnchor constant:-12],
            ]];
            prev = bubble;
        }

        // 3. Final-state badge for resolved requests.
        if (([type caseInsensitiveCompare:@"scriptRequest"] == NSOrderedSame
          || [type caseInsensitiveCompare:@"dataRequest"]   == NSOrderedSame)
          && [requestState caseInsensitiveCompare:@"pending"] != NSOrderedSame) {
            UILabel* badge = [[UILabel alloc] init];
            badge.translatesAutoresizingMaskIntoConstraints = NO;
            BOOL approved = [requestState caseInsensitiveCompare:@"approved"] == NSOrderedSame;
            badge.text = approved ? @"✓ Approved" : @"✗ Declined";
            badge.textColor = approved ? _cAccent : _cTextMuted;
            badge.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];
            [row addSubview:badge];
            [NSLayoutConstraint activateConstraints:@[
                [badge.topAnchor constraintEqualToAnchor:(prev ?: row).bottomAnchor constant:prev ? 2 : 0],
                mine ? [badge.trailingAnchor constraintEqualToAnchor:row.trailingAnchor constant:-6]
                     : [badge.leadingAnchor constraintEqualToAnchor:row.leadingAnchor constant:6],
            ]];
            prev = badge;
        }
    }

    // 4. Time caption underneath, aligned to the bubble side.
    if (createdAt.length > 0) {
        UILabel* time = [[UILabel alloc] init];
        time.translatesAutoresizingMaskIntoConstraints = NO;
        time.text = BPChatFormatTime(createdAt);
        time.textColor = _cTextMuted;
        time.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:11] - 1];
        [row addSubview:time];
        [NSLayoutConstraint activateConstraints:@[
            [time.topAnchor constraintEqualToAnchor:(prev ?: row).bottomAnchor constant:prev ? 2 : 0],
            mine ? [time.trailingAnchor constraintEqualToAnchor:row.trailingAnchor constant:-6]
                 : [time.leadingAnchor constraintEqualToAnchor:row.leadingAnchor constant:6],
            [time.bottomAnchor constraintEqualToAnchor:row.bottomAnchor],
        ]];
    } else if (prev) {
        [prev.bottomAnchor constraintEqualToAnchor:row.bottomAnchor].active = YES;
    }
    return row;
}

/** Use NSDataDetector to underline / colour URLs in the body text the same
 *  way Linkify.WEB_URLS does on Android. */
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

/** Image / video attachment thumb above the bubble body. Image: load from
 *  local path; video: glyph placeholder. Server-fetched remote thumbs are
 *  a follow-up — same scope cut as the Android version. */
- (UIView*)buildAttachmentThumb:(NSDictionary*)ao mine:(BOOL)mine {
    NSString* type      = BPChatStr(ao, @"type",      @"Type",      @"image");
    NSString* localPath = BPChatStr(ao, @"localPath", @"LocalPath", @"");
    UIView* container = [[UIView alloc] init];
    container.layer.cornerRadius = 12;
    container.layer.masksToBounds = YES;
    container.backgroundColor = _cBubbleOther;
    [container.widthAnchor constraintEqualToConstant:160].active = YES;
    [container.heightAnchor constraintEqualToConstant:160].active = YES;

    if ([type caseInsensitiveCompare:@"image"] == NSOrderedSame && localPath.length > 0) {
        UIImage* img = [UIImage imageWithContentsOfFile:localPath];
        if (img) {
            UIImageView* iv = [[UIImageView alloc] initWithImage:img];
            iv.translatesAutoresizingMaskIntoConstraints = NO;
            iv.contentMode = UIViewContentModeScaleAspectFill;
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
    // Placeholder glyph (server URL fetch + video first-frame extraction
    // are out of scope for this MVP).
    UILabel* glyph = [[UILabel alloc] init];
    glyph.translatesAutoresizingMaskIntoConstraints = NO;
    glyph.text = [type caseInsensitiveCompare:@"video"] == NSOrderedSame ? @"🎥" : @"📷";
    glyph.textAlignment = NSTextAlignmentCenter;
    glyph.font = [UIFont systemFontOfSize:36];
    [container addSubview:glyph];
    [NSLayoutConstraint activateConstraints:@[
        [glyph.centerXAnchor constraintEqualToAnchor:container.centerXAnchor],
        [glyph.centerYAnchor constraintEqualToAnchor:container.centerYAnchor],
    ]];
    return container;
}

/** Inline approve/decline panel for a pending dataRequest / scriptRequest.
 *  Tapping POSTs to /chat/request/answer + (for scriptRequest) follows up
 *  with a stub reply — full ScriptRunner JNI/IL2CPP bridge is the
 *  follow-up issue, same as Android. */
- (UIView*)buildRequestBubble:(NSDictionary*)m type:(NSString*)type body:(NSString*)body {
    BOOL isScript = [type caseInsensitiveCompare:@"scriptRequest"] == NSOrderedSame;
    NSString* description = BPChatStr(m, @"RequestDescription", @"requestDescription", @"");
    BOOL hasDescription = description.length > 0;
    // Spoiler model: when we have a plain-English description, the source
    // preview hides behind a "Show script" toggle. Keeps players from
    // having to read C# to make an informed approve/decline call. When
    // there's no description (legacy QA composer or non-script request)
    // we fall back to showing the body inline like before.
    BOOL collapseSource = isScript && hasDescription;

    UIView* container = [[UIView alloc] init];
    container.backgroundColor = _cBubbleOther;
    container.layer.cornerRadius = 12;
    container.layer.borderWidth = 1;
    container.layer.borderColor = _cAccent.CGColor;
    container.layer.masksToBounds = YES;

    // Use a vertical stack so hidden children (the collapsed source
    // preview) cleanly drop out of layout without manual constraint
    // gymnastics.
    UIStackView* stack = [[UIStackView alloc] init];
    stack.translatesAutoresizingMaskIntoConstraints = NO;
    stack.axis = UILayoutConstraintAxisVertical;
    stack.alignment = UIStackViewAlignmentFill;
    stack.spacing = 6;
    [container addSubview:stack];

    UILabel* header = [[UILabel alloc] init];
    header.text = isScript
        ? [BPStrings text:@"chatScriptRequest" fallback:@"The dev team wants to run a script:"]
        : [BPStrings text:@"chatDataRequest"   fallback:@"The dev team is asking for data:"];
    header.textColor = _cTextDim;
    header.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];
    header.numberOfLines = 0;
    [stack addArrangedSubview:header];

    // Description (only when present) — sits prominently between the
    // header and the (possibly hidden) source.
    if (hasDescription) {
        UILabel* descLabel = [[UILabel alloc] init];
        descLabel.text = description;
        descLabel.textColor = _cText;
        descLabel.numberOfLines = 0;
        descLabel.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
        [stack addArrangedSubview:descLabel];
    }

    UILabel* preview = [[UILabel alloc] init];
    NSString* shown = body ?: @"";
    if (shown.length > 600) shown = [[shown substringToIndex:600] stringByAppendingString:@"\n…"];
    preview.text = shown;
    preview.textColor = _cText;
    preview.numberOfLines = 0;
    preview.font = isScript
        ? [UIFont fontWithName:@"Menlo" size:[BPTheme font:@"fontSizeCaption" fallback:12]]
        : [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];
    if (!preview.font) preview.font = [UIFont systemFontOfSize:12];
    preview.hidden = collapseSource;

    // Spoiler toggle — only present on script requests that ship a
    // description. Tapping flips preview.hidden + the title.
    UIButton* spoiler = nil;
    if (collapseSource) {
        spoiler = [UIButton buttonWithType:UIButtonTypeSystem];
        [spoiler setTitle:[BPStrings text:@"chatShowScript" fallback:@"▸ Show script"]
                 forState:UIControlStateNormal];
        [spoiler setTitleColor:_cAccent forState:UIControlStateNormal];
        spoiler.titleLabel.font = [UIFont systemFontOfSize:[BPTheme font:@"fontSizeCaption" fallback:12]];
        spoiler.contentHorizontalAlignment = UIControlContentHorizontalAlignmentLeading;
        objc_setAssociatedObject(spoiler, "bp_preview", preview, OBJC_ASSOCIATION_RETAIN_NONATOMIC);
        [spoiler addTarget:self
                    action:@selector(onSpoilerTapped:)
          forControlEvents:UIControlEventTouchUpInside];
        [stack addArrangedSubview:spoiler];
    }

    [stack addArrangedSubview:preview];

    UIButton* decline = [self requestActionButton:[BPStrings text:@"chatDecline" fallback:@"Decline"]
                                              bg:_cBorder
                                              fg:_cTextDim];
    UIButton* approve = [self requestActionButton:[BPStrings text:@"chatApprove" fallback:@"Approve"]
                                              bg:_cAccent
                                              fg:[UIColor whiteColor]];
    NSDictionary* mc = [m copy];
    [decline addTarget:self action:@selector(onDeclineTapped:) forControlEvents:UIControlEventTouchUpInside];
    [approve addTarget:self action:@selector(onApproveTapped:) forControlEvents:UIControlEventTouchUpInside];
    objc_setAssociatedObject(decline, "bp_msg", mc, OBJC_ASSOCIATION_RETAIN_NONATOMIC);
    objc_setAssociatedObject(approve, "bp_msg", mc, OBJC_ASSOCIATION_RETAIN_NONATOMIC);

    // Action row — own horizontal stack so the buttons sit neatly to the
    // right inside the vertical stack.
    UIView* spacer = [[UIView alloc] init];
    [spacer setContentHuggingPriority:UILayoutPriorityDefaultLow forAxis:UILayoutConstraintAxisHorizontal];
    UIStackView* actionRow = [[UIStackView alloc] initWithArrangedSubviews:@[ spacer, decline, approve ]];
    actionRow.axis = UILayoutConstraintAxisHorizontal;
    actionRow.spacing = 8;
    actionRow.alignment = UIStackViewAlignmentCenter;
    [stack setCustomSpacing:10 afterView:preview];
    [stack addArrangedSubview:actionRow];

    [NSLayoutConstraint activateConstraints:@[
        [stack.topAnchor      constraintEqualToAnchor:container.topAnchor      constant:12],
        [stack.leadingAnchor  constraintEqualToAnchor:container.leadingAnchor  constant:12],
        [stack.trailingAnchor constraintEqualToAnchor:container.trailingAnchor constant:-12],
        [stack.bottomAnchor   constraintEqualToAnchor:container.bottomAnchor   constant:-12],
    ]];
    return container;
}

- (void)onSpoilerTapped:(UIButton*)sender {
    UILabel* preview = objc_getAssociatedObject(sender, "bp_preview");
    if (!preview) return;
    BOOL nowHidden = !preview.hidden;
    preview.hidden = nowHidden;
    [sender setTitle:(nowHidden
                        ? [BPStrings text:@"chatShowScript" fallback:@"▸ Show script"]
                        : [BPStrings text:@"chatHideScript" fallback:@"▾ Hide script"])
            forState:UIControlStateNormal];
}

- (UIButton*)requestActionButton:(NSString*)title bg:(UIColor*)bg fg:(UIColor*)fg {
    UIButton* b = [UIButton buttonWithType:UIButtonTypeCustom];
    b.translatesAutoresizingMaskIntoConstraints = NO;
    [b setTitle:title forState:UIControlStateNormal];
    [b setTitleColor:fg forState:UIControlStateNormal];
    b.titleLabel.font = [UIFont boldSystemFontOfSize:[BPTheme font:@"fontSizeBody" fallback:14]];
    b.backgroundColor = bg;
    b.layer.cornerRadius = 8;
    b.contentEdgeInsets = UIEdgeInsetsMake(10, 16, 10, 16);
    return b;
}

- (void)onApproveTapped:(UIButton*)sender {
    NSDictionary* m = objc_getAssociatedObject(sender, "bp_msg");
    [self answerRequest:m approved:YES];
}

- (void)onDeclineTapped:(UIButton*)sender {
    NSDictionary* m = objc_getAssociatedObject(sender, "bp_msg");
    [self answerRequest:m approved:NO];
}

- (void)answerRequest:(NSDictionary*)m approved:(BOOL)approved {
    NSString* mid = BPChatStr(m, @"Id", @"id", @"");
    if (mid.length == 0) return;
    NSString* type = BPChatStr(m, @"Type", @"type", @"");
    NSString* kind = BPChatStr(m, @"RequestKind", @"requestKind", @"");

    // Optimistic flip — find this message in our state by id and stamp it
    // approved/declined so the buttons disappear immediately.
    for (NSUInteger i = 0; i < _messages.count; i++) {
        NSString* idHere = BPChatStr(_messages[i], @"Id", @"id", @"");
        if ([idHere isEqualToString:mid]) {
            NSMutableDictionary* mut = [_messages[i] mutableCopy];
            mut[@"RequestState"] = approved ? @"approved" : @"declined";
            _messages[i] = mut;
            break;
        }
    }
    [self renderMessages];

    NSDictionary* payload = @{ @"messageId": mid, @"approved": @(approved) };
    [self http:@"POST" path:@"/api/v1/chat/request/answer"
        jsonBody:payload
      completion:^(NSDictionary* resp, NSInteger status) {
        if (approved) {
            BOOL handledNatively = NO;

            // Native fulfilment for kinds that don't need Mono / IL2CPP —
            // capture, upload, and result POST all happen on the native
            // side. C# is never on the path for these.
            if ([type caseInsensitiveCompare:@"dataRequest"] == NSOrderedSame
                && [kind caseInsensitiveCompare:@"screenshot"] == NSOrderedSame) {
                [self performNativeScreenshotForRequest:mid];
                handledNatively = YES;
            } else if ([type caseInsensitiveCompare:@"dataRequest"] == NSOrderedSame
                && [kind caseInsensitiveCompare:@"video"] == NSOrderedSame) {
                // On-demand chat-video capture is tracked under
                // bugpunch-sdk-unity#39. Post a typed error so QA sees an
                // explicit "not implemented" rather than silence.
                [self postRequestResult:mid
                                 status:@"error"
                                    log:nil
                                  error:@"Video capture for chat not yet implemented (bugpunch-sdk-unity#39)"
                            attachments:nil];
                handledNatively = YES;
            }

            if (!handledNatively) {
                // Bounce to C# for kinds that need Mono — script source
                // execution today, plus the legacy untyped dataRequest
                // fallback. New payload format includes the kind so the
                // C# layer can branch without re-fetching the message.
                NSString* body = BPChatStr(m, @"Body", @"body", @"");
                NSData* utf8 = [body dataUsingEncoding:NSUTF8StringEncoding] ?: [NSData data];
                NSString* b64 = [utf8 base64EncodedStringWithOptions:0];
                const char* method = nil;
                NSString* unityPayload = nil;
                if ([type caseInsensitiveCompare:@"scriptRequest"] == NSOrderedSame) {
                    // Script kind is implied by the method name — keep the
                    // legacy two-field payload to avoid touching
                    // OnApprovedScriptRequest's parser.
                    unityPayload = [NSString stringWithFormat:@"%@|%@", mid, b64];
                    method = "OnApprovedScriptRequest";
                } else if ([type caseInsensitiveCompare:@"dataRequest"] == NSOrderedSame) {
                    NSString* k = kind.length > 0 ? kind : @"";
                    unityPayload = [NSString stringWithFormat:@"%@|%@|%@", mid, k, b64];
                    method = "OnApprovedDataRequest";
                }
                if (method && unityPayload) {
                    UnitySendMessage("BugpunchClient", method, [unityPayload UTF8String]);
                }
            }
        }
        // Refresh from server so the canonical state replaces our optimistic stub.
        dispatch_async(dispatch_get_main_queue(), ^{ [self fetchMessages:YES]; });
    }];
}

// ── Native screenshot fulfilment ─────────────────────────────────
//
// Capture the key window via Bugpunch_CaptureScreenshot, upload the JPEG
// to /api/v1/chat/upload, and POST the captured ref onto the request
// message via /api/v1/chat/request/result. Reuses the existing
// uploadAttachmentSync:/http: helpers so retries + auth headers stay
// consistent with the player-composer attachment path.
- (void)performNativeScreenshotForRequest:(NSString*)mid {
    if (mid.length == 0) return;

    NSString* dir = NSTemporaryDirectory();
    NSString* filename = [NSString stringWithFormat:@"bp_chat_%@_%lld.jpg",
        mid, (long long)([NSDate date].timeIntervalSince1970 * 1000)];
    NSString* path = [dir stringByAppendingPathComponent:filename];

    Bugpunch_CaptureScreenshot([mid UTF8String], [path UTF8String], 85,
        ^(const char* requestId, int success, const char* payloadOrErr) {
            if (!success) {
                NSString* err = payloadOrErr
                    ? [NSString stringWithFormat:@"native screenshot capture failed: %s", payloadOrErr]
                    : @"native screenshot capture failed";
                [self postRequestResult:mid status:@"error" log:nil error:err attachments:nil];
                return;
            }

            // Upload + result POST go off the main queue — uploadAttachmentSync:
            // blocks on a semaphore for the multipart response.
            dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
                BPChatPendingAttachment* a = [[BPChatPendingAttachment alloc] init];
                a.type = @"image";
                a.path = path;
                NSString* ref = [self uploadAttachmentSync:a];

                // Best-effort cleanup of the temp JPEG.
                [[NSFileManager defaultManager] removeItemAtPath:path error:nil];

                if (ref.length == 0) {
                    [self postRequestResult:mid
                                     status:@"error"
                                        log:nil
                                      error:@"Screenshot upload failed"
                                attachments:nil];
                    return;
                }

                NSDictionary* attachment = @{ @"type": @"image", @"ref": ref, @"mime": @"image/jpeg" };
                [self postRequestResult:mid
                                 status:@"ok"
                                    log:@"Captured screenshot"
                                  error:nil
                            attachments:@[ attachment ]];
            });
        });
}

// POST /api/v1/chat/request/result with optional attachments. Server-side
// (chatService.recordRequestResult) writes the descriptors onto the
// message row's attachments column so the dashboard renders them inline.
- (void)postRequestResult:(NSString*)mid
                   status:(NSString*)status
                      log:(NSString*)log
                    error:(NSString*)err
              attachments:(NSArray*)attachments {
    NSMutableDictionary* body = [NSMutableDictionary dictionary];
    body[@"messageId"] = mid;
    body[@"status"]    = status ?: @"ok";
    if (log)  body[@"log"]   = log;
    if (err)  body[@"error"] = err;
    if (attachments.count > 0) body[@"attachments"] = attachments;
    [self http:@"POST" path:@"/api/v1/chat/request/result"
        jsonBody:body
      completion:^(NSDictionary* resp, NSInteger httpStatus) {
        // Refresh so the canonical row (with the attachment ref + result_log)
        // replaces our optimistic state.
        dispatch_async(dispatch_get_main_queue(), ^{ [self fetchMessages:YES]; });
    }];
}

#pragma mark – Send

- (void)sendCurrent {
    if (_disabled) return;
    NSString* body = [_composer.text stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceAndNewlineCharacterSet]] ?: @"";
    if (body.length == 0 && _pending.count == 0) return;
    _composer.text = @"";
    _composerPlaceholder.hidden = NO;
    NSArray<BPChatPendingAttachment*>* attachments = [_pending copy];
    [_pending removeAllObjects];
    [self rebuildAttachmentStrip];
    [self updateSendEnabled];

    // Optimistic add — replaced by canonical version on next poll.
    NSMutableDictionary* pending = [NSMutableDictionary dictionary];
    pending[@"Id"]        = [NSString stringWithFormat:@"_local_%lld", (long long)([NSDate date].timeIntervalSince1970 * 1000)];
    pending[@"Sender"]    = @"sdk";
    pending[@"Body"]      = body;
    pending[@"CreatedAt"] = [self isoNow];
    if (attachments.count > 0) {
        NSMutableArray* arr = [NSMutableArray array];
        for (BPChatPendingAttachment* a in attachments) {
            [arr addObject:@{ @"type": a.type, @"localPath": a.path }];
        }
        pending[@"attachments"] = arr;
    }
    [_messages addObject:pending];
    [self renderMessages];

    // Upload then post in the background.
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        NSMutableArray* uploaded = [NSMutableArray array];
        for (BPChatPendingAttachment* a in attachments) {
            NSString* ref = [self uploadAttachmentSync:a];
            if (ref) [uploaded addObject:@{ @"type": a.type, @"ref": ref }];
        }
        NSMutableDictionary* payload = [NSMutableDictionary dictionary];
        payload[@"body"] = body;
        if (uploaded.count > 0) payload[@"attachments"] = uploaded;
        [self http:@"POST" path:@"/api/v1/chat/message"
            jsonBody:payload
          completion:^(NSDictionary* resp, NSInteger status) {
            NSString* tid = BPChatStr(resp, @"ThreadId", @"threadId", @"");
            if (tid.length > 0) self->_threadId = tid;
            dispatch_async(dispatch_get_main_queue(), ^{ [self fetchMessages:YES]; });
        }];
    });
}

- (NSString*)isoNow {
    static NSDateFormatter* f = nil;
    if (!f) {
        f = [NSDateFormatter new];
        f.dateFormat = @"yyyy-MM-dd'T'HH:mm:ss'Z'";
        f.timeZone = [NSTimeZone timeZoneWithAbbreviation:@"UTC"];
        f.locale = [NSLocale localeWithLocaleIdentifier:@"en_US_POSIX"];
    }
    return [f stringFromDate:[NSDate date]];
}

/** Synchronous (on a background queue) multipart-upload of a single
 *  attachment file. Returns the server-assigned ref string or nil on
 *  failure. Bypasses the native upload queue (BugpunchUploader) — chat
 *  attachments fail-fast rather than retry across launches. */
- (NSString*)uploadAttachmentSync:(BPChatPendingAttachment*)a {
    NSString* base = BPChatServerUrl();
    NSString* key  = BPChatApiKey();
    if (base.length == 0 || key.length == 0) return nil;
    NSString* path = a.path;
    NSData* data = [NSData dataWithContentsOfFile:path];
    if (data.length == 0) return nil;

    NSString* boundary = [NSString stringWithFormat:@"----bp%lld", (long long)([NSDate date].timeIntervalSince1970 * 1000)];
    NSURL* url = [NSURL URLWithString:[base stringByAppendingString:@"/api/v1/chat/upload"]];
    NSMutableURLRequest* req = [NSMutableURLRequest requestWithURL:url];
    req.HTTPMethod = @"POST";
    req.timeoutInterval = 60.0;
    [req setValue:key forHTTPHeaderField:@"X-Api-Key"];
    [req setValue:BPChatDeviceId() forHTTPHeaderField:@"X-Device-Id"];
    [req setValue:[NSString stringWithFormat:@"multipart/form-data; boundary=%@", boundary]
        forHTTPHeaderField:@"Content-Type"];

    NSString* mime = [a.type isEqualToString:@"image"] ? @"image/jpeg" : @"video/mp4";
    NSString* filename = path.lastPathComponent;
    NSMutableData* body = [NSMutableData data];
    NSString* pre = [NSString stringWithFormat:
        @"--%@\r\nContent-Disposition: form-data; name=\"file\"; filename=\"%@\"\r\nContent-Type: %@\r\n\r\n",
        boundary, filename, mime];
    [body appendData:[pre dataUsingEncoding:NSUTF8StringEncoding]];
    [body appendData:data];
    NSString* post = [NSString stringWithFormat:@"\r\n--%@--\r\n", boundary];
    [body appendData:[post dataUsingEncoding:NSUTF8StringEncoding]];

    __block NSString* ref = nil;
    dispatch_semaphore_t sem = dispatch_semaphore_create(0);
    NSURLSessionUploadTask* t = [_session uploadTaskWithRequest:req fromData:body
        completionHandler:^(NSData * _Nullable d, NSURLResponse * _Nullable r, NSError * _Nullable err) {
        NSHTTPURLResponse* http = (NSHTTPURLResponse*)r;
        if (!err && http.statusCode >= 200 && http.statusCode < 300 && d.length > 0) {
            id obj = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
            if ([obj isKindOfClass:[NSDictionary class]]) {
                ref = BPChatStr((NSDictionary*)obj, @"ref", @"Ref", @"");
                if (ref.length == 0) ref = nil;
            }
        }
        dispatch_semaphore_signal(sem);
    }];
    [t resume];
    dispatch_semaphore_wait(sem, DISPATCH_TIME_FOREVER);
    return ref;
}

#pragma mark – HTTP

- (void)http:(NSString*)method path:(NSString*)path
    jsonBody:(NSDictionary*)body
  completion:(void(^)(NSDictionary* resp, NSInteger status))completion {
    NSString* base = BPChatServerUrl();
    NSString* key  = BPChatApiKey();
    if (base.length == 0 || key.length == 0) {
        if (completion) completion(nil, 0);
        return;
    }
    NSURL* url = [NSURL URLWithString:[base stringByAppendingString:path]];
    NSMutableURLRequest* req = [NSMutableURLRequest requestWithURL:url];
    req.HTTPMethod = method;
    req.timeoutInterval = 15.0;
    [req setValue:key forHTTPHeaderField:@"X-Api-Key"];
    [req setValue:BPChatDeviceId() forHTTPHeaderField:@"X-Device-Id"];
    [req setValue:@"application/json" forHTTPHeaderField:@"Accept"];

    if (body) {
        NSError* enc = nil;
        NSData* data = [NSJSONSerialization dataWithJSONObject:body options:0 error:&enc];
        if (enc) { if (completion) completion(nil, 0); return; }
        req.HTTPBody = data;
        [req setValue:@"application/json" forHTTPHeaderField:@"Content-Type"];
    }

    NSURLSessionDataTask* t = [_session dataTaskWithRequest:req
        completionHandler:^(NSData * _Nullable d, NSURLResponse * _Nullable r, NSError * _Nullable err) {
        NSHTTPURLResponse* http = (NSHTTPURLResponse*)r;
        NSInteger status = http.statusCode;
        NSDictionary* obj = nil;
        if (!err && d.length > 0) {
            id parsed = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
            if ([parsed isKindOfClass:[NSDictionary class]]) obj = parsed;
        }
        if (completion) completion(obj, status);
    }];
    [t resume];
}

#pragma mark – Hours / thread / messages / read

- (void)fetchHours {
    [self http:@"GET" path:@"/api/v1/chat/hours" jsonBody:nil
       completion:^(NSDictionary* resp, NSInteger status) {
        if (!resp) return;
        BOOL disabled = [resp[@"disabled"] boolValue]
                    || ![(resp[@"enabled"] ?: @YES) boolValue];
        BOOL offHours = [resp[@"isOffHours"] boolValue]
                    || ([resp[@"status"] isKindOfClass:[NSString class]]
                        && [resp[@"status"] isEqualToString:@"off_hours"]);
        NSString* msg = BPChatStr(resp, @"offHoursMessage", @"message",
            @"Outside support hours — we'll get back to you.");
        dispatch_async(dispatch_get_main_queue(), ^{
            self->_disabled = disabled;
            self->_offHours = offHours;
            self->_hoursMessage = msg;
            if (disabled) {
                self->_disabledOverlay.hidden = NO;
            } else if (offHours) {
                self->_hoursBanner.text = [NSString stringWithFormat:@"  %@  ", msg];
                self->_hoursBanner.hidden = NO;
            } else {
                self->_hoursBanner.hidden = YES;
            }
            [self updateSendEnabled];
        });
    }];
}

- (void)fetchThread {
    [self http:@"GET" path:@"/api/v1/chat/thread" jsonBody:nil
       completion:^(NSDictionary* resp, NSInteger status) {
        if (resp) {
            NSDictionary* thread = [resp[@"thread"] isKindOfClass:[NSDictionary class]]
                ? resp[@"thread"] : resp;
            NSString* tid = BPChatStr(thread, @"Id", @"id", @"");
            if (tid.length > 0) self->_threadId = tid;
        }
        // 404 means "no thread yet" — empty state stays.
        dispatch_async(dispatch_get_main_queue(), ^{ [self fetchMessages:YES]; });
    }];
}

- (void)fetchMessages:(BOOL)fullReplace {
    if (_disabled) return;
    NSString* path = @"/api/v1/chat/messages";
    if (!fullReplace && _lastMessageAt.length > 0) {
        NSString* enc = [_lastMessageAt stringByAddingPercentEncodingWithAllowedCharacters:
            [NSCharacterSet URLQueryAllowedCharacterSet]];
        path = [path stringByAppendingFormat:@"?since=%@", enc ?: @""];
    }
    [self http:@"GET" path:path jsonBody:nil
       completion:^(NSDictionary* resp, NSInteger status) {
        NSArray* arr = BPChatArr(resp, @"messages", @"Messages");
        if (!arr) return;
        dispatch_async(dispatch_get_main_queue(), ^{
            [self mergeMessages:arr fullReplace:fullReplace];
        });
    }];
}

- (void)mergeMessages:(NSArray*)incoming fullReplace:(BOOL)fullReplace {
    if (fullReplace) {
        [_messages removeAllObjects];
    } else {
        // Drop optimistic stubs once a real version arrives.
        NSMutableArray* keep = [NSMutableArray array];
        for (NSDictionary* m in _messages) {
            NSString* mid = BPChatStr(m, @"Id", @"id", @"");
            if (![mid hasPrefix:@"_local_"]) [keep addObject:m];
        }
        [_messages setArray:keep];
    }
    for (id raw in incoming) {
        if (![raw isKindOfClass:[NSDictionary class]]) continue;
        NSDictionary* m = (NSDictionary*)raw;
        NSString* mid = BPChatStr(m, @"Id", @"id", @"");
        BOOL dup = NO;
        for (NSDictionary* existing in _messages) {
            NSString* eid = BPChatStr(existing, @"Id", @"id", @"");
            if (mid.length > 0 && [mid isEqualToString:eid]) { dup = YES; break; }
        }
        if (!dup) [_messages addObject:m];
        NSString* createdAt = BPChatStr(m, @"CreatedAt", @"createdAt", @"");
        if (createdAt.length > 0
            && (_lastMessageAt.length == 0 || [createdAt compare:_lastMessageAt] == NSOrderedDescending)) {
            _lastMessageAt = createdAt;
        }
    }
    [self renderMessages];
    [self markRead];
}

- (void)markRead {
    if (_threadId.length == 0) return;
    [self http:@"POST" path:@"/api/v1/chat/read" jsonBody:@{} completion:nil];
    // Clear the floating-widget badge immediately rather than waiting for
    // the next BugpunchPoller tick to round-trip /chat/unread (#32).
    extern void Bugpunch_SetUnreadCount(int count);
    Bugpunch_SetUnreadCount(0);
}

#pragma mark – Polling

- (void)startPolling {
    [self stopPolling];
    _pollTimer = [NSTimer scheduledTimerWithTimeInterval:kBPChatPollInterval target:self
        selector:@selector(onPollTick) userInfo:nil repeats:YES];
}

- (void)stopPolling {
    [_pollTimer invalidate];
    _pollTimer = nil;
}

- (void)onPollTick {
    [self fetchMessages:NO];
}

#pragma mark – Keyboard

- (void)onKeyboardWillChangeFrame:(NSNotification*)note {
    CGRect end = [note.userInfo[UIKeyboardFrameEndUserInfoKey] CGRectValue];
    CGFloat duration = [note.userInfo[UIKeyboardAnimationDurationUserInfoKey] doubleValue];
    NSInteger curve = [note.userInfo[UIKeyboardAnimationCurveUserInfoKey] integerValue];

    CGRect inWindow = [self.view convertRect:end fromView:nil];
    CGFloat overlap = MAX(0, self.view.bounds.size.height - inWindow.origin.y - self.view.safeAreaInsets.bottom);
    _composerBottom.constant = -overlap;

    [UIView animateWithDuration:duration
                          delay:0
                        options:(UIViewAnimationOptions)(curve << 16)
                     animations:^{ [self.view layoutIfNeeded]; }
                     completion:nil];
}

#pragma mark – Class helpers

/** Walk up to the topmost presentable VC — matches the pattern in
 *  BugpunchReportForm.mm. Used when re-presenting after a screenshot. */
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
// Called by Bugpunch_ShowChatBoard in BugpunchReportOverlay.mm in place of
// the old UnitySendMessage("OnShowChatBoardRequested") bounce. Self-
// contained: builds + presents a fresh full-screen VC every time.

extern "C" void Bugpunch_PresentNativeChatViewController(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        BugpunchChatViewController* vc = [BugpunchChatViewController new];
        vc.modalPresentationStyle = UIModalPresentationFullScreen;
        UIViewController* top = [BugpunchChatViewController topPresentingVC];
        if (!top) return;
        [top presentViewController:vc animated:YES completion:nil];
    });
}
