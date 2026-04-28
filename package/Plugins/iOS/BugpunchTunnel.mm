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

// Tester role mirrored to Keychain so a cold start enforces the last-known
// role before the tunnel handshake completes. Defaults to "public" → all
// interactive features off until the server tells us otherwise.
static NSString* const kBPRoleKeychainService = @"au.com.oddgames.bugpunch";
static NSString* const kBPRoleKeychainAccount = @"role_state_v1";
static NSString* gTesterRole = @"public";

static NSString* BPNormalizeRole(NSString* raw) {
    if (![raw isKindOfClass:[NSString class]] || raw.length == 0) return @"public";
    if ([raw isEqualToString:@"internal"] ||
        [raw isEqualToString:@"admin"] ||      // legacy
        [raw isEqualToString:@"developer"]) {  // legacy
        return @"internal";
    }
    if ([raw isEqualToString:@"external"]) return @"external";
    return @"public";
}

static void BPWriteRoleKeychain(NSString* role) {
    NSData* data = [(role ?: @"public") dataUsingEncoding:NSUTF8StringEncoding];
    if (!data) return;
    NSDictionary* attrs = @{
        (__bridge id)kSecClass:          (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService:    kBPRoleKeychainService,
        (__bridge id)kSecAttrAccount:    kBPRoleKeychainAccount,
        (__bridge id)kSecAttrAccessible: (__bridge id)kSecAttrAccessibleAfterFirstUnlock,
        (__bridge id)kSecValueData:      data,
    };
    OSStatus s = SecItemAdd((__bridge CFDictionaryRef)attrs, NULL);
    if (s == errSecDuplicateItem) {
        NSDictionary* q = @{
            (__bridge id)kSecClass:       (__bridge id)kSecClassGenericPassword,
            (__bridge id)kSecAttrService: kBPRoleKeychainService,
            (__bridge id)kSecAttrAccount: kBPRoleKeychainAccount,
        };
        SecItemUpdate((__bridge CFDictionaryRef)q,
            (__bridge CFDictionaryRef)@{ (__bridge id)kSecValueData: data });
    }
}

static void BPLoadRoleKeychain(void) {
    NSDictionary* q = @{
        (__bridge id)kSecClass:       (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: kBPRoleKeychainService,
        (__bridge id)kSecAttrAccount: kBPRoleKeychainAccount,
        (__bridge id)kSecReturnData:  @YES,
        (__bridge id)kSecMatchLimit:  (__bridge id)kSecMatchLimitOne,
    };
    CFTypeRef result = NULL;
    OSStatus s = SecItemCopyMatching((__bridge CFDictionaryRef)q, &result);
    if (s != errSecSuccess || !result) return;
    NSData* data = (__bridge_transfer NSData*)result;
    NSString* role = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    gTesterRole = BPNormalizeRole(role);
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
- (void)cacheRoleConfig:(NSDictionary*)cfg;
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
    // Wire foreground/background rotation as soon as the tunnel turns on —
    // notifications fire on the main thread, gSessionId is read under a lock.
    BPHookSessionLifecycle();
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
    // Native tunnel is report-only (crashes / bugs / pin config / log sink /
    // device actions). Remote IDE RPC rides a separate managed WebSocket
    // that C# opens against /api/devices/ide-tunnel.
    NSString* url = [[self toWsUrl:self.serverUrl] stringByAppendingString:@"/api/devices/report-tunnel"];
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
        id cfg = msg[@"roleConfig"];
        if ([cfg isKindOfClass:[NSDictionary class]]) [self cacheRoleConfig:cfg];
        NSLog(@"[BugpunchTunnel] registered (roleConfig=%@)", cfg ? @"yes" : @"no");
    } else if ([type isEqualToString:@"roleUpdate"]) {
        id cfg = msg[@"config"];
        if ([cfg isKindOfClass:[NSDictionary class]]) [self cacheRoleConfig:cfg];
        NSLog(@"[BugpunchTunnel] roleUpdate received");
    } else if ([type isEqualToString:@"pong"] || [type isEqualToString:@"heartbeat"]) {
        // no-op
    }
}

- (void)cacheRoleConfig:(NSDictionary*)cfg {
    NSError* err = nil;
    NSData* data = [NSJSONSerialization dataWithJSONObject:cfg options:0 error:&err];
    if (data) {
        NSString* json = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
        const char* c = [json UTF8String];
        if (c) {
            [gPinConfigLock lock];
            if (gLastPinConfigJson) free(gLastPinConfigJson);
            gLastPinConfigJson = strdup(c);
            [gPinConfigLock unlock];
        }
    }

    NSString* previous = gTesterRole;
    NSString* role = BPNormalizeRole(cfg[@"role"]);
    BOOL becameInternal = ![previous isEqualToString:@"internal"] &&
                          [role isEqualToString:@"internal"];
    BOOL leftInternal   =  [previous isEqualToString:@"internal"] &&
                          ![role isEqualToString:@"internal"];
    gTesterRole = role;
    BPWriteRoleKeychain(role);

    // Mirror the role to the cache-driven auto-prompt key (NSUserDefaults,
    // separate from the keychain role state which is for the role gating
    // itself). Pre-release: writing "external"/"user" verbatim — both
    // behave the same (no auto-prompt) but readers can distinguish later.
    extern void Bugpunch_WriteLastTesterRoleCache(const char*);
    const char* normalized =
        [role isEqualToString:@"internal"] ? "internal" :
        [role isEqualToString:@"external"] ? "external" : "user";
    Bugpunch_WriteLastTesterRoleCache(normalized);

    // No live-replay on role flip: Unity logs flow over the C# IDE-tunnel
    // push path (gated on RoleState.IsInternal there). The native log-reader
    // buffer is now consulted only by snapshotText for crash / bug-report
    // attachments, so there's nothing to live-replay through this tunnel.

    // Cache-driven auto-prompt reconciliation.
    //   - became internal & cache didn't already prompt → prompt now.
    //   - left internal & ring is running from speculative cached prompt
    //     → tear it down. Server's authoritative answer wins.
    if (becameInternal) {
        extern void Bugpunch_OnRoleBecameInternal(void);
        Bugpunch_OnRoleBecameInternal();
    } else if (leftInternal) {
        extern void Bugpunch_OnRoleLeftInternal(void);
        Bugpunch_OnRoleLeftInternal();
    }
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

static void BPEnsureTunnelGlobals(void) {
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        gPinConfigLock = [NSLock new];
        // Apply cached role immediately so a cold start enforces last-known
        // state before the handshake completes.
        BPLoadRoleKeychain();
    });
}

extern "C" void Bugpunch_StartTunnel(const char* configJson) {
    BPEnsureTunnelGlobals();
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

extern "C" void Bugpunch_TunnelApplyRoleConfig(const char* json) {
    BPEnsureTunnelGlobals();
    if (!json || *json == 0) return;
    @autoreleasepool {
        NSData* d = [[NSString stringWithUTF8String:json] dataUsingEncoding:NSUTF8StringEncoding];
        id obj = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
        if (![obj isKindOfClass:[NSDictionary class]]) return;
        [[BPTunnel shared] cacheRoleConfig:(NSDictionary*)obj];
    }
}

// Returns the current tester role: "internal" | "external" | "public".
// Returned pointer is into a static NSString — do not free.
extern "C" const char* Bugpunch_GetTesterRole(void) {
    return gTesterRole ? [gTesterRole UTF8String] : "public";
}

// Forward declaration — BPLogReader lives in BugpunchDebugMode.mm; same iOS
// plugin target so the symbol resolves at link time.
@interface BPLogReader : NSObject
+ (void)appendLineLive:(NSString*)line;
@end

/// Public C ABI for native plugins (Obj-C / Swift via @_silgen_name) to
/// contribute log lines. Same shape as Instabug's `+[IBGLog log:]` /
/// Luciq's `+[LCQLog log:]`: explicit, developer-driven, no auto-capture.
/// Routes through the report tunnel (live, gated on role==internal +
/// connected) and the in-memory ring (always populated for crash dumps).
///
/// Levels: "Verbose" | "Info" | "Warning" | "Error" | "Assert" — matches
/// the level vocabulary the server's log parser already understands. Any
/// other string is treated as Info.
extern "C" void Bugpunch_LogMessage(const char* level, const char* message) {
    if (!message || *message == 0) return;
    @autoreleasepool {
        NSString* lvlIn = (level && *level) ? [NSString stringWithUTF8String:level] : @"Info";
        NSString* msg = [NSString stringWithUTF8String:message] ?: @"";
        NSString* lvl =
            ([lvlIn caseInsensitiveCompare:@"Error"]   == NSOrderedSame ||
             [lvlIn caseInsensitiveCompare:@"Fault"]   == NSOrderedSame) ? @"E" :
            ([lvlIn caseInsensitiveCompare:@"Assert"]  == NSOrderedSame) ? @"F" :
            ([lvlIn caseInsensitiveCompare:@"Warning"] == NSOrderedSame ||
             [lvlIn caseInsensitiveCompare:@"Warn"]    == NSOrderedSame) ? @"W" :
            ([lvlIn caseInsensitiveCompare:@"Verbose"] == NSOrderedSame ||
             [lvlIn caseInsensitiveCompare:@"Debug"]   == NSOrderedSame) ? @"V" : @"I";
        // Logcat-ish line shape so the server parser treats it identically
        // to entries from the Unity P/Invoke push path and the on-demand
        // OSLog snapshot. Tag is the iOS bundle id so dashboard filtering
        // can group by plugin / app.
        NSString* tag = [[NSBundle mainBundle] bundleIdentifier] ?: @"iOS";
        NSDateFormatter* fmt = [[NSDateFormatter alloc] init];
        fmt.dateFormat = @"MM-dd HH:mm:ss.SSS";
        fmt.timeZone = [NSTimeZone timeZoneWithAbbreviation:@"UTC"];
        NSString* line = [NSString stringWithFormat:@"%@     0     0 %@ %@: %@",
            [fmt stringFromDate:[NSDate date]], lvl, tag, msg];
        [BPLogReader appendLineLive:line];
    }
}

// ── Native log sink batcher ──
//
// Currently unused on iOS — Unity logs flow over the C# IDE-tunnel push
// path, and native iOS framework chatter is captured on demand inside
// BPLogReader.snapshotText for crash / bug-report attachments rather than
// streamed live. Kept as a stable C ABI so future native plugins can push
// their own log lines through the report tunnel without changes elsewhere.
// Buffer + flush logic stays at 100 ms / 32 KB cadence with raw-bytes
// passthrough to the server's log sink, matching the Android contract.

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

// ── Session ids — see hookSessionLifecycle below for rotation rules. ──
//
// gRootSessionId is minted once per process; gSessionId starts equal to it
// and rotates whenever the app foregrounds after >RESUME_THRESHOLD_S in the
// background. gParentSessionId is nil on root, set to gRootSessionId on every
// resume so the dashboard can group resumes under a single launch.
static NSString* gRootSessionId;
static NSString* gSessionId;
static NSString* gParentSessionId;
static NSLock*   gSessionLock;
static const NSTimeInterval RESUME_THRESHOLD_S = 30.0;
static NSTimeInterval gBackgroundedAt;
static BOOL gSessionLifecycleHooked;

// Forward declarations for the C symbol exported by BugpunchCrashHandler.mm —
// the Mach exception / signal handler reads s_session_id and stamps it into
// the crash header, so the next-launch drain knows which log_session to tail-
// append. We push every time the runtime mints / rotates the active id.
extern "C" void Bugpunch_SetCrashSessionId(const char* sessionId);

static void BPPushSessionToCrashHandler(NSString* sid) {
    if (!sid) { Bugpunch_SetCrashSessionId(""); return; }
    Bugpunch_SetCrashSessionId([sid UTF8String] ?: "");
}

static void BPInitSessionIdsIfNeeded(void) {
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        gSessionLock = [NSLock new];
        gRootSessionId = [[NSUUID UUID] UUIDString];
        gSessionId = gRootSessionId;
        BPPushSessionToCrashHandler(gSessionId);
    });
}

static NSString* BPSessionId(void) {
    BPInitSessionIdsIfNeeded();
    [gSessionLock lock];
    NSString* sid = gSessionId;
    [gSessionLock unlock];
    return sid;
}

static NSString* BPParentSessionId(void) {
    BPInitSessionIdsIfNeeded();
    [gSessionLock lock];
    NSString* p = gParentSessionId;
    [gSessionLock unlock];
    return p;
}

// Hook the iOS app lifecycle so we can rotate the session id whenever the
// app foregrounds after a long enough background gap. Idempotent.
static void BPHookSessionLifecycle(void) {
    if (gSessionLifecycleHooked) return;
    gSessionLifecycleHooked = YES;
    BPInitSessionIdsIfNeeded();

    NSNotificationCenter* nc = [NSNotificationCenter defaultCenter];
    [nc addObserverForName:UIApplicationDidEnterBackgroundNotification
                    object:nil
                     queue:nil
                usingBlock:^(NSNotification* _) {
        gBackgroundedAt = [NSDate timeIntervalSinceReferenceDate];
    }];
    [nc addObserverForName:UIApplicationWillEnterForegroundNotification
                    object:nil
                     queue:nil
                usingBlock:^(NSNotification* _) {
        NSTimeInterval bg = gBackgroundedAt;
        if (bg <= 0) return;
        NSTimeInterval away = [NSDate timeIntervalSinceReferenceDate] - bg;
        gBackgroundedAt = 0;
        if (away < RESUME_THRESHOLD_S) return;
        [gSessionLock lock];
        gSessionId = [[NSUUID UUID] UUIDString];
        gParentSessionId = gRootSessionId;
        NSString* sidCopy = gSessionId;
        [gSessionLock unlock];
        // Mirror the new id into the native crash handler so a SIGSEGV /
        // Mach exception mid-resume stamps the *current* session into the
        // crash header (not the prior launch's).
        BPPushSessionToCrashHandler(sidCopy);
        NSLog(@"[BugpunchTunnel] session resumed after %.0fs background → %@",
              away, sidCopy);
    }];
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
    [out appendFormat:@"{\"type\":\"log\",\"sessionId\":\"%@\"", BPSessionId()];
    NSString* parent = BPParentSessionId();
    if (parent) [out appendFormat:@",\"parentSessionId\":\"%@\"", parent];
    [out appendString:@",\"text\":\""];
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
    if (![gTesterRole isEqualToString:@"internal"]) return;

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
