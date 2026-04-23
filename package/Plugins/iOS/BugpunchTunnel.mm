// BugpunchTunnel.mm — native iOS WebSocket tunnel (N2 of the migration).
//
// Mirrors BugpunchTunnel.java. Starts from BugpunchBootstrap.mm (+load, before
// main()) so the tunnel is up before Unity boots and outlives managed crashes.
//
// Scope for N2: connect + register + heartbeat + reconnect. Captures the
// signed pin config from the `registered` ack and `pinUpdate` frames so N4
// (native pin config) can verify + cache it. Incoming `request` frames park
// until N3 adds the native→C# dispatch bridge; `log` binary frames go native
// in N5.
//
// Uses URLSessionWebSocketTask (iOS 13+) — matches the SDK's existing iOS
// baseline. No third-party dependency.

#import <Foundation/Foundation.h>
#import <sys/sysctl.h>

// Returns the iOS model identifier (e.g. "iPhone15,2"). Falls back to the
// generic UIDevice.model string if sysctl is unavailable.
static NSString* BPTunnelDeviceModelIdentifier(void) {
    size_t size = 0;
    if (sysctlbyname("hw.machine", NULL, &size, NULL, 0) != 0 || size == 0) return nil;
    char* buf = (char*)malloc(size);
    if (!buf) return nil;
    NSString* model = nil;
    if (sysctlbyname("hw.machine", buf, &size, NULL, 0) == 0) {
        model = [NSString stringWithCString:buf encoding:NSUTF8StringEncoding];
    }
    free(buf);
    return model.length ? model : nil;
}

extern "C" const char* Bugpunch_GetStableDeviceId(void);

// Persistent C-string cache for the last pin config so C# (and eventually
// N4's native pin handler) can pull it synchronously without Obj-C blocks.
static char* gLastPinConfigJson = NULL;
static NSLock* gPinConfigLock;

// N4: parsed pin state. Mirrored to Keychain (survives reinstall) so a cold
// start enforces the last-known pins before the tunnel handshake completes.
// Consent is the final gate — pins read false unless consent == "accepted".
static NSString* const kBPPinsKeychainService = @"au.com.oddgames.bugpunch";
static NSString* const kBPPinsKeychainAccount = @"pin_state_v1";
static BOOL gPinAlwaysLog;
static BOOL gPinAlwaysRemote;
static BOOL gPinAlwaysDebug;
static NSString* gConsent = @"unknown";

// ── Pin Keychain helpers ──
//
// Keychain survives app uninstall on iOS by default, matching
// `stable_device_id` (Keychain UUID) and server-side consent keyed on it.
// Stored as a JSON blob under (service=au.com.oddgames.bugpunch, account=pin_state_v1).

static void BPWritePinsKeychain(BOOL log, BOOL remote, BOOL debug, NSString* consent) {
    NSDictionary* state = @{
        @"alwaysLog":    @(log),
        @"alwaysRemote": @(remote),
        @"alwaysDebug":  @(debug),
        @"consent":      consent ?: @"unknown",
    };
    NSData* data = [NSJSONSerialization dataWithJSONObject:state options:0 error:nil];
    if (!data) return;
    NSDictionary* attrs = @{
        (__bridge id)kSecClass:          (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService:    kBPPinsKeychainService,
        (__bridge id)kSecAttrAccount:    kBPPinsKeychainAccount,
        (__bridge id)kSecAttrAccessible: (__bridge id)kSecAttrAccessibleAfterFirstUnlock,
        (__bridge id)kSecValueData:      data,
    };
    OSStatus s = SecItemAdd((__bridge CFDictionaryRef)attrs, NULL);
    if (s == errSecDuplicateItem) {
        NSDictionary* q = @{
            (__bridge id)kSecClass:       (__bridge id)kSecClassGenericPassword,
            (__bridge id)kSecAttrService: kBPPinsKeychainService,
            (__bridge id)kSecAttrAccount: kBPPinsKeychainAccount,
        };
        SecItemUpdate((__bridge CFDictionaryRef)q,
            (__bridge CFDictionaryRef)@{ (__bridge id)kSecValueData: data });
    }
}

static void BPLoadPinsKeychain(void) {
    NSDictionary* q = @{
        (__bridge id)kSecClass:       (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: kBPPinsKeychainService,
        (__bridge id)kSecAttrAccount: kBPPinsKeychainAccount,
        (__bridge id)kSecReturnData:  @YES,
        (__bridge id)kSecMatchLimit:  (__bridge id)kSecMatchLimitOne,
    };
    CFTypeRef result = NULL;
    OSStatus s = SecItemCopyMatching((__bridge CFDictionaryRef)q, &result);
    if (s != errSecSuccess || !result) return;
    NSData* data = (__bridge_transfer NSData*)result;
    NSDictionary* state = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
    if (![state isKindOfClass:[NSDictionary class]]) return;
    gPinAlwaysLog    = [state[@"alwaysLog"]    boolValue];
    gPinAlwaysRemote = [state[@"alwaysRemote"] boolValue];
    gPinAlwaysDebug  = [state[@"alwaysDebug"]  boolValue];
    id c = state[@"consent"];
    gConsent = [c isKindOfClass:[NSString class]] ? c : @"unknown";
}

@interface BPTunnel : NSObject <NSURLSessionWebSocketDelegate>
@property (nonatomic, strong) NSDictionary* config;
@property (nonatomic, copy) NSString* serverUrl;
@property (nonatomic, copy) NSString* apiKey;
@property (nonatomic, copy) NSString* buildChannel;
@property (nonatomic, copy) NSString* stableDeviceId;
@property (nonatomic, copy) NSString* deviceId;
@property (nonatomic, strong) NSURLSession* session;
@property (nonatomic, strong) NSURLSessionWebSocketTask* task;
@property (nonatomic, strong) dispatch_queue_t queue;
@property (nonatomic, strong) dispatch_source_t pingTimer;
@property (nonatomic, assign) BOOL connected;
@property (nonatomic, assign) BOOL stopRequested;
@property (nonatomic, assign) NSTimeInterval backoffSeconds;
+ (instancetype)shared;
- (void)startWithConfig:(NSDictionary*)config stableDeviceId:(NSString*)stableId;
- (void)stop;
@end

@implementation BPTunnel

+ (instancetype)shared {
    static BPTunnel* i; static dispatch_once_t once;
    dispatch_once(&once, ^{
        i = [BPTunnel new];
        i.queue = dispatch_queue_create("au.com.oddgames.bugpunch.tunnel", DISPATCH_QUEUE_SERIAL);
        i.backoffSeconds = 1.0;
    });
    return i;
}

- (NSString*)toWsUrl:(NSString*)base {
    NSString* s = [base stringByTrimmingCharactersInSet:[NSCharacterSet characterSetWithCharactersInString:@"/"]];
    if ([s hasPrefix:@"https://"]) return [@"wss://" stringByAppendingString:[s substringFromIndex:8]];
    if ([s hasPrefix:@"http://"])  return [@"ws://"  stringByAppendingString:[s substringFromIndex:7]];
    return s;
}

- (NSString*)loadOrMintDeviceId {
    NSString* dir = NSSearchPathForDirectoriesInDomains(NSCachesDirectory, NSUserDomainMask, YES).firstObject;
    NSString* path = [dir stringByAppendingPathComponent:@"bugpunch_device_id"];
    NSError* err = nil;
    NSString* existing = [NSString stringWithContentsOfFile:path encoding:NSUTF8StringEncoding error:&err];
    if (existing.length > 0) return [existing stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceAndNewlineCharacterSet]];
    NSString* minted = [[NSUUID UUID] UUIDString];
    [minted writeToFile:path atomically:YES encoding:NSUTF8StringEncoding error:nil];
    return minted;
}

- (void)startWithConfig:(NSDictionary*)config stableDeviceId:(NSString*)stableId {
    dispatch_async(self.queue, ^{
        if (self.task != nil) {
            [self updateConfig:config];
            return;
        }
        self.config = config ?: @{};
        self.serverUrl = self.config[@"serverUrl"] ?: @"";
        self.apiKey = self.config[@"apiKey"] ?: @"";
        self.buildChannel = self.config[@"buildChannel"] ?: @"unknown";
        self.stableDeviceId = stableId ?: @"";
        self.deviceId = [self loadOrMintDeviceId];

        if (self.serverUrl.length == 0 || self.apiKey.length == 0) {
            NSLog(@"[BugpunchTunnel] missing serverUrl or apiKey — tunnel not started");
            return;
        }

        // Phase 6c: compile redaction rules once from the bundled config.
        BPCompileRedactionRules(self.config);

        [self connect];
    });
}

- (void)updateConfig:(NSDictionary*)config {
    self.config = config ?: @{};
    self.serverUrl = self.config[@"serverUrl"] ?: self.serverUrl;
    self.apiKey = self.config[@"apiKey"] ?: self.apiKey;

    BOOL useNativeTunnel = [self.config[@"useNativeTunnel"] boolValue];
    if (useNativeTunnel && !self.connected && !self.task) {
        NSLog(@"[BugpunchTunnel] config updated — re-evaluating tunnel connection");
        self.stopRequested = NO;
        self.backoffSeconds = 1.0;
        [self connect];
    }
}

- (void)stop {
    dispatch_async(self.queue, ^{
        self.stopRequested = YES;
        if (self.pingTimer) { dispatch_source_cancel(self.pingTimer); self.pingTimer = nil; }
        [self.task cancelWithCloseCode:NSURLSessionWebSocketCloseCodeGoingAway reason:nil];
        self.task = nil;
        self.connected = NO;
    });
}

- (void)connect {
    if (self.stopRequested) return;
    BOOL useNativeTunnel = [self.config[@"useNativeTunnel"] boolValue];
    if (!useNativeTunnel) {
        NSLog(@"[BugpunchTunnel] useNativeTunnel is false — tunnel disabled");
        return;
    }
    NSString* url = [[self toWsUrl:self.serverUrl] stringByAppendingString:@"/api/devices/tunnel"];
    NSURLSessionConfiguration* cfg = [NSURLSessionConfiguration defaultSessionConfiguration];
    cfg.waitsForConnectivity = YES;
    self.session = [NSURLSession sessionWithConfiguration:cfg delegate:self delegateQueue:nil];
    self.task = [self.session webSocketTaskWithURL:[NSURL URLWithString:url]];
    [self.task resume];
    [self listen];
    NSLog(@"[BugpunchTunnel] connecting to %@", url);
}

- (void)sendRegister {
    NSDictionary* meta = self.config[@"metadata"];
    NSMutableDictionary* reg = [NSMutableDictionary dictionary];
    reg[@"type"]            = @"register";
    NSString* metaModel = ([meta isKindOfClass:[NSDictionary class]] ? meta[@"deviceModel"] : nil);
    if (metaModel.length == 0) metaModel = BPTunnelDeviceModelIdentifier() ?: @"iOS";
    reg[@"name"]            = metaModel;
    reg[@"platform"]        = @"iOS";
    reg[@"appVersion"]      = ([meta isKindOfClass:[NSDictionary class]] ? meta[@"appVersion"]  : nil) ?: @"";
    reg[@"remoteIdePort"]   = @0;
    reg[@"token"]           = self.apiKey;
    reg[@"deviceId"]        = self.deviceId;
    reg[@"stableDeviceId"]  = self.stableDeviceId;
    reg[@"buildChannel"]    = self.buildChannel;
    NSError* err = nil;
    NSData* data = [NSJSONSerialization dataWithJSONObject:reg options:0 error:&err];
    if (!data) { NSLog(@"[BugpunchTunnel] register payload build failed: %@", err); return; }
    NSString* json = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    [self.task sendMessage:[[NSURLSessionWebSocketMessage alloc] initWithString:json]
         completionHandler:^(NSError* e) {
        if (e) NSLog(@"[BugpunchTunnel] register send failed: %@", e);
    }];
}

- (void)listen {
    __weak BPTunnel* weakSelf = self;
    [self.task receiveMessageWithCompletionHandler:^(NSURLSessionWebSocketMessage* msg, NSError* err) {
        BPTunnel* strong = weakSelf;
        if (!strong) return;
        if (err) {
            NSLog(@"[BugpunchTunnel] receive error: %@", err);
            [strong scheduleReconnect];
            return;
        }
        if (msg.type == NSURLSessionWebSocketMessageTypeString) {
            [strong handleText:msg.string];
        }
        // Binary messages (log chunks, future) are ignored in N2.
        [strong listen];
    }];
}

- (void)handleText:(NSString*)text {
    NSData* d = [text dataUsingEncoding:NSUTF8StringEncoding];
    NSError* err = nil;
    id obj = [NSJSONSerialization JSONObjectWithData:d options:0 error:&err];
    if (![obj isKindOfClass:[NSDictionary class]]) return;
    NSDictionary* msg = obj;
    NSString* type = msg[@"type"] ?: @"";

    if ([type isEqualToString:@"registered"]) {
        id pin = msg[@"pinConfig"];
        if ([pin isKindOfClass:[NSDictionary class]]) [self cachePinConfig:pin];
        NSLog(@"[BugpunchTunnel] registered (pinConfig=%@)", pin ? @"yes" : @"no");
    } else if ([type isEqualToString:@"pinUpdate"]) {
        id pin = msg[@"config"];
        if ([pin isKindOfClass:[NSDictionary class]]) [self cachePinConfig:pin];
        NSLog(@"[BugpunchTunnel] pinUpdate received");
    } else if ([type isEqualToString:@"request"]) {
        // N3: marshal to C# via UnitySendMessage so the existing RequestRouter
        // answers. Response comes back through Bugpunch_TunnelSendResponse.
        extern void UnitySendMessage(const char*, const char*, const char*);
        const char* c = [text UTF8String];
        if (c) {
            @try { UnitySendMessage("[Bugpunch Client]", "OnTunnelRequest", c); }
            @catch (NSException* e) { NSLog(@"[BugpunchTunnel] UnitySendMessage failed: %@", e); }
        }
    } else if ([type isEqualToString:@"pong"] || [type isEqualToString:@"heartbeat"]) {
        // no-op
    }
}

- (void)cachePinConfig:(NSDictionary*)pin {
    NSError* err = nil;
    NSData* data = [NSJSONSerialization dataWithJSONObject:pin options:0 error:&err];
    if (!data) return;
    NSString* json = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    const char* c = [json UTF8String];
    if (!c) return;
    [gPinConfigLock lock];
    if (gLastPinConfigJson) free(gLastPinConfigJson);
    gLastPinConfigJson = strdup(c);
    [gPinConfigLock unlock];

    // N4: parse into typed state and mirror to Keychain. HMAC verification
    // against the bundled pin_signing_secret is a follow-up (N4.2); until
    // then we trust the tunnel (API-key + TLS).
    NSDictionary* pins = pin[@"pins"];
    BOOL log    = [pins isKindOfClass:[NSDictionary class]] && [pins[@"alwaysLog"]    boolValue];
    BOOL remote = [pins isKindOfClass:[NSDictionary class]] && [pins[@"alwaysRemote"] boolValue];
    BOOL debug  = [pins isKindOfClass:[NSDictionary class]] && [pins[@"alwaysDebug"]  boolValue];
    NSString* consent = pin[@"consent"] ?: @"unknown";
    if (![consent isKindOfClass:[NSString class]]) consent = @"unknown";

    gPinAlwaysLog    = log;
    gPinAlwaysRemote = remote;
    gPinAlwaysDebug  = debug;
    gConsent         = consent;
    BPWritePinsKeychain(log, remote, debug, consent);
}

- (void)scheduleReconnect {
    if (self.stopRequested) return;
    self.connected = NO;
    NSTimeInterval delay = self.backoffSeconds;
    self.backoffSeconds = MIN(self.backoffSeconds * 2, 30.0);
    NSLog(@"[BugpunchTunnel] reconnecting in %.0fs", delay);
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(delay * NSEC_PER_SEC)), self.queue, ^{
        self.task = nil;
        [self connect];
    });
}

- (void)startPingTimer {
    if (self.pingTimer) { dispatch_source_cancel(self.pingTimer); self.pingTimer = nil; }
    dispatch_source_t t = dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER, 0, 0, self.queue);
    dispatch_source_set_timer(t, dispatch_time(DISPATCH_TIME_NOW, 10 * NSEC_PER_SEC), 10 * NSEC_PER_SEC, 1 * NSEC_PER_SEC);
    __weak BPTunnel* weakSelf = self;
    dispatch_source_set_event_handler(t, ^{
        BPTunnel* strong = weakSelf;
        if (!strong || !strong.task) return;
        [strong.task sendPingWithPongReceiveHandler:^(NSError* err) {
            if (err) {
                NSLog(@"[BugpunchTunnel] ping failed: %@", err);
                [strong scheduleReconnect];
            }
        }];
    });
    dispatch_resume(t);
    self.pingTimer = t;
}

// ── NSURLSessionWebSocketDelegate ──

- (void)URLSession:(NSURLSession*)session webSocketTask:(NSURLSessionWebSocketTask*)task
  didOpenWithProtocol:(NSString*)protocol {
    dispatch_async(self.queue, ^{
        self.connected = YES;
        self.backoffSeconds = 1.0;
        NSLog(@"[BugpunchTunnel] connected");
        [self sendRegister];
        [self startPingTimer];
    });
}

- (void)URLSession:(NSURLSession*)session webSocketTask:(NSURLSessionWebSocketTask*)task
  didCloseWithCode:(NSURLSessionWebSocketCloseCode)closeCode reason:(NSData*)reason {
    dispatch_async(self.queue, ^{
        NSLog(@"[BugpunchTunnel] closed code=%ld", (long)closeCode);
        [self scheduleReconnect];
    });
}

@end

// ── C entry points ──

extern "C" void Bugpunch_StartTunnel(const char* configJson) {
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        gPinConfigLock = [NSLock new];
        // Apply cached pins immediately so a cold start enforces last-known
        // state before the handshake completes.
        BPLoadPinsKeychain();
    });
    if (!configJson || *configJson == 0) return;
    @autoreleasepool {
        NSData* d = [[NSString stringWithUTF8String:configJson] dataUsingEncoding:NSUTF8StringEncoding];
        id obj = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
        if (![obj isKindOfClass:[NSDictionary class]]) return;
        const char* sid = Bugpunch_GetStableDeviceId();
        NSString* stableId = sid ? [NSString stringWithUTF8String:sid] : @"";
        [[BPTunnel shared] startWithConfig:obj stableDeviceId:stableId];
    }
}

// ── N4: native pin state accessors ──
// Pins only apply when consent == "accepted". Returns 0/1 to keep the
// P/Invoke surface simple.

extern "C" int Bugpunch_PinAlwaysLog(void) {
    return ([gConsent isEqualToString:@"accepted"] && gPinAlwaysLog) ? 1 : 0;
}
extern "C" int Bugpunch_PinAlwaysRemote(void) {
    return ([gConsent isEqualToString:@"accepted"] && gPinAlwaysRemote) ? 1 : 0;
}
extern "C" int Bugpunch_PinAlwaysDebug(void) {
    return ([gConsent isEqualToString:@"accepted"] && gPinAlwaysDebug) ? 1 : 0;
}
extern "C" const char* Bugpunch_PinConsent(void) {
    return gConsent ? [gConsent UTF8String] : "unknown";
}

// ── N5: log sink batcher ──
//
// BPLogReader tees every captured line here. We buffer and flush as a
// single WebSocket frame on a 100 ms / 32 KB cadence. Server writes the
// raw bytes to disk per session — zero parsing on the hot path.

static NSMutableString* gLogBuf;
static NSLock* gLogBufLock;
static BOOL gLogFlushScheduled;
static const NSInteger kLogFlushBytes = 32 * 1024;
static const NSTimeInterval kLogFlushInterval = 0.1;

// Phase 6c: compiled redaction rules — name + NSRegularExpression. Compiled
// once from the bundled config on tunnel start; applied to every log line
// before the line enters the batcher so nothing matching a rule ever
// leaves the process.
@interface BPRedactionRule : NSObject
@property (nonatomic, copy) NSString* name;
@property (nonatomic, strong) NSRegularExpression* regex;
@end
@implementation BPRedactionRule @end
static NSArray<BPRedactionRule*>* gRedactionRules;

static NSString* BPRedactLine(NSString* line) {
    if (!gRedactionRules || gRedactionRules.count == 0 || line.length == 0) return line;
    NSMutableString* out = [line mutableCopy];
    for (BPRedactionRule* r in gRedactionRules) {
        NSString* replacement = [NSString stringWithFormat:@"[redacted:%@]", r.name];
        [r.regex replaceMatchesInString:out options:0
            range:NSMakeRange(0, out.length) withTemplate:replacement];
    }
    return out;
}

static void BPCompileRedactionRules(NSDictionary* config) {
    NSArray* arr = config[@"logRedactionRules"];
    if (![arr isKindOfClass:[NSArray class]] || arr.count == 0) { gRedactionRules = @[]; return; }
    NSMutableArray<BPRedactionRule*>* out = [NSMutableArray arrayWithCapacity:arr.count];
    for (id entry in arr) {
        if (![entry isKindOfClass:[NSDictionary class]]) continue;
        NSString* pat = entry[@"pattern"];
        if (![pat isKindOfClass:[NSString class]] || pat.length == 0) continue;
        NSError* err = nil;
        NSRegularExpression* re = [NSRegularExpression regularExpressionWithPattern:pat
            options:0 error:&err];
        if (!re) {
            NSLog(@"[BugpunchTunnel] skipping bad redaction pattern \"%@\": %@", pat, err);
            continue;
        }
        BPRedactionRule* r = [BPRedactionRule new];
        r.name  = entry[@"name"] ?: @"pii";
        r.regex = re;
        [out addObject:r];
    }
    gRedactionRules = out;
}

static NSString* BPSessionId(void) {
    static NSString* sid;
    static dispatch_once_t once;
    dispatch_once(&once, ^{ sid = [[NSUUID UUID] UUIDString]; });
    return sid;
}

static void BPLogFlushNow(void) {
    NSString* text;
    [gLogBufLock lock];
    gLogFlushScheduled = NO;
    if (gLogBuf.length == 0) { [gLogBufLock unlock]; return; }
    text = [gLogBuf copy];
    [gLogBuf setString:@""];
    [gLogBufLock unlock];

    BPTunnel* t = [BPTunnel shared];
    if (!t.connected || !t.task) return;

    // Hand-built JSON envelope — minimum necessary escaping on the raw log
    // text. Server writes payload.text bytes verbatim to disk.
    NSMutableString* out = [NSMutableString stringWithCapacity:text.length + 128];
    [out appendFormat:@"{\"type\":\"log\",\"sessionId\":\"%@\",\"text\":\"", BPSessionId()];
    for (NSUInteger i = 0; i < text.length; i++) {
        unichar c = [text characterAtIndex:i];
        switch (c) {
            case '\\': [out appendString:@"\\\\"]; break;
            case '"':  [out appendString:@"\\\""]; break;
            case '\n': [out appendString:@"\\n"]; break;
            case '\r': [out appendString:@"\\r"]; break;
            case '\t': [out appendString:@"\\t"]; break;
            default:
                if (c < 0x20) [out appendFormat:@"\\u%04x", c];
                else          [out appendFormat:@"%C", c];
        }
    }
    [out appendString:@"\"}"];

    [t.task sendMessage:[[NSURLSessionWebSocketMessage alloc] initWithString:out]
     completionHandler:^(NSError* err) {
        if (err) NSLog(@"[BugpunchTunnel] log flush failed: %@", err);
    }];
}

extern "C" void Bugpunch_TunnelEnqueueLogLine(const char* line) {
    if (!line || *line == 0) return;
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        gLogBuf = [NSMutableString stringWithCapacity:kLogFlushBytes];
        gLogBufLock = [NSLock new];
    });

    BPTunnel* t = [BPTunnel shared];
    if (!t.connected) return;
    if (!(gPinAlwaysLog && [gConsent isEqualToString:@"accepted"])) return;

    @autoreleasepool {
        NSString* ns = [NSString stringWithUTF8String:line];
        if (!ns) return;
        // Phase 6c: redact before the line enters the batcher.
        ns = BPRedactLine(ns);
        BOOL overflow = NO;
        [gLogBufLock lock];
        [gLogBuf appendString:ns];
        [gLogBuf appendString:@"\n"];
        if (gLogBuf.length >= (NSUInteger)kLogFlushBytes) overflow = YES;
        BOOL schedule = !gLogFlushScheduled && !overflow;
        if (schedule) gLogFlushScheduled = YES;
        [gLogBufLock unlock];

        if (overflow) {
            dispatch_async(t.queue, ^{ BPLogFlushNow(); });
        } else if (schedule) {
            dispatch_after(
                dispatch_time(DISPATCH_TIME_NOW, (int64_t)(kLogFlushInterval * NSEC_PER_SEC)),
                t.queue, ^{ BPLogFlushNow(); });
        }
    }
}

extern "C" void Bugpunch_StopTunnel(void) {
    [[BPTunnel shared] stop];
}

extern "C" bool Bugpunch_TunnelIsConnected(void) {
    return [BPTunnel shared].connected;
}

/// Persistent deviceId the native tunnel is registered under. Empty if not
/// yet started. Pointer is owned by the tunnel singleton — do not free.
extern "C" const char* Bugpunch_TunnelDeviceId(void) {
    static char* cached;
    NSString* id = [BPTunnel shared].deviceId ?: @"";
    const char* c = [id UTF8String];
    if (!c) return "";
    if (cached && strcmp(cached, c) == 0) return cached;
    if (cached) { free(cached); cached = NULL; }
    cached = strdup(c);
    return cached;
}

/// Returns the last signed pin config JSON seen on the tunnel, or NULL.
/// Returned pointer is owned by the tunnel — do not free.
extern "C" const char* Bugpunch_GetLastPinConfig(void) {
    [gPinConfigLock lock];
    const char* out = gLastPinConfigJson;
    [gPinConfigLock unlock];
    return out;
}

/// Ship a response frame back to the server (N3 dispatch bridge). C# hands
/// us a pre-built JSON envelope of shape
/// {type:"response", requestId, status, body, contentType, isBase64?}.
/// Thread-safe; no-op if the tunnel isn't connected.
extern "C" void Bugpunch_TunnelSendResponse(const char* json) {
    if (!json || *json == 0) return;
    @autoreleasepool {
        NSString* s = [NSString stringWithUTF8String:json];
        NSURLSessionWebSocketTask* task = [BPTunnel shared].task;
        if (!task) {
            NSLog(@"[BugpunchTunnel] sendResponse dropped — tunnel not connected");
            return;
        }
        [task sendMessage:[[NSURLSessionWebSocketMessage alloc] initWithString:s]
         completionHandler:^(NSError* err) {
            if (err) NSLog(@"[BugpunchTunnel] sendResponse failed: %@", err);
        }];
    }
}
