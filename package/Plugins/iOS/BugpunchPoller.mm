// =============================================================================
// BugpunchPoller - iOS native poll client. Replaces the old C# DeviceRegistration.
//
// Owns:
//   - POST /api/devices/register at startup (device token cached in NSUserDefaults)
//   - POST /api/device-poll every pollIntervalSeconds
//   - Parse response:
//       * pendingDirectives  -> BPDirectives_OnPollDirectives
//       * upgradeToWebSocket -> UnitySendMessage("BugpunchClient", "OnPollUpgradeRequested", "")
//       * scripts            -> UnitySendMessage("BugpunchClient", "OnPollScripts", scriptsJson)
//
// Config (serverUrl, apiKey, deviceId, platform, appVersion, installerMode)
// is read from [BPDebugMode shared].config / .metadata which are populated
// when Bugpunch_StartDebugMode runs.
// =============================================================================

#import <Foundation/Foundation.h>

// Bridge into the directive dispatcher (poll path).
extern "C" void BPDirectives_OnPollDirectives(const char* jsonC);
extern "C" void Bugpunch_StartTunnel(const char* configJson);
extern "C" void Bugpunch_TunnelApplyPinConfig(const char* pinJson);
extern "C" bool Bugpunch_TunnelIsConnected(void);
extern "C" const char* Bugpunch_TunnelDeviceId(void);
extern "C" const char* Bugpunch_GetStableDeviceId(void);

// UnitySendMessage lives in libiPhone-lib.a.
extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

// Shared config/metadata, lives in BugpunchDebugMode.mm.
@interface BPDebugMode : NSObject
@property (nonatomic, strong) NSDictionary* config;
@property (nonatomic, strong) NSMutableDictionary<NSString*, NSString*>* metadata;
+ (instancetype)shared;
@end

static NSString* const kBPTokenKey = @"bugpunch.poll.device_token";

static BOOL gPollerStarted = NO;
static BOOL gPollerStopped = NO;
static dispatch_source_t gPollTimer = nil;
static dispatch_queue_t gPollQueue = nil;
static NSString* gDeviceToken = nil;
static BOOL gRegistrationRefreshed = NO;
static NSInteger gPollIntervalSeconds = 30;
static NSString* gScriptPermission = @"ask";
static NSURLSession* gSession = nil;

// Unread-chat tick counter (issue #32). Piggybacks on the existing poll
// loop — every other tick we hit /api/v1/chat/unread to update the
// floating-widget badge.
static NSInteger gUnreadTickCounter = 0;

// Bridges to the debug widget that owns the badge. Lives in
// BugpunchDebugWidget.mm.
extern "C" void Bugpunch_SetUnreadCount(int count);

// -- Helpers ------------------------------------------------------------------

static NSString* BPServerUrl(void) {
    NSString* s = [BPDebugMode shared].config[@"serverUrl"];
    if (![s isKindOfClass:[NSString class]]) return @"";
    while ([s hasSuffix:@"/"]) s = [s substringToIndex:s.length - 1];
    return s;
}

static NSString* BPApiKey(void) {
    NSString* s = [BPDebugMode shared].config[@"apiKey"];
    return [s isKindOfClass:[NSString class]] ? s : @"";
}

static NSString* BPMeta(NSString* key) {
    NSString* v = [BPDebugMode shared].metadata[key];
    return v ? v : @"";
}

static NSString* BPCString(const char* c) {
    return c && *c ? [NSString stringWithUTF8String:c] : @"";
}

static void BPSaveToken(NSString* token) {
    [[NSUserDefaults standardUserDefaults] setObject:(token ?: @"") forKey:kBPTokenKey];
}

static NSString* BPLoadToken(void) {
    return [[NSUserDefaults standardUserDefaults] stringForKey:kBPTokenKey] ?: @"";
}

/** POST JSON, synchronously on the poll queue. Returns (status, body). */
static void BPPostJsonSync2(NSString* url,
                            NSString* headerName, NSString* headerValue,
                            NSString* header2Name, NSString* header2Value,
                            NSString* bodyJson,
                            void (^completion)(NSInteger status, NSString* body)) {
    if (!gSession) {
        NSURLSessionConfiguration* cfg = [NSURLSessionConfiguration defaultSessionConfiguration];
        cfg.timeoutIntervalForRequest = 15.0;
        cfg.timeoutIntervalForResource = 15.0;
        gSession = [NSURLSession sessionWithConfiguration:cfg];
    }

    NSMutableURLRequest* req = [NSMutableURLRequest requestWithURL:[NSURL URLWithString:url]];
    req.HTTPMethod = @"POST";
    [req setValue:@"application/json" forHTTPHeaderField:@"Content-Type"];
    [req setValue:headerValue forHTTPHeaderField:headerName];
    if (header2Name && header2Value && header2Value.length > 0) {
        [req setValue:header2Value forHTTPHeaderField:header2Name];
    }
    req.HTTPBody = [bodyJson dataUsingEncoding:NSUTF8StringEncoding];

    dispatch_semaphore_t sem = dispatch_semaphore_create(0);
    __block NSInteger statusOut = -1;
    __block NSString* bodyOut = @"";
    NSURLSessionDataTask* task = [gSession dataTaskWithRequest:req
        completionHandler:^(NSData* data, NSURLResponse* resp, NSError* error) {
            if (error) {
                NSLog(@"[BugpunchPoller] %@ failed: %@", url, error.localizedDescription);
            } else {
                NSHTTPURLResponse* httpResp = (NSHTTPURLResponse*)resp;
                statusOut = httpResp.statusCode;
                if (data) {
                    bodyOut = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] ?: @"";
                }
            }
            dispatch_semaphore_signal(sem);
        }];
    [task resume];
    dispatch_semaphore_wait(sem, DISPATCH_TIME_FOREVER);
    completion(statusOut, bodyOut);
}

static void BPPostJsonSync(NSString* url, NSString* headerName, NSString* headerValue,
                           NSString* bodyJson, void (^completion)(NSInteger status, NSString* body)) {
    BPPostJsonSync2(url, headerName, headerValue, nil, nil, bodyJson, completion);
}

// -- Register -----------------------------------------------------------------

static void BPEnsureRegistered(void) {
    if (gRegistrationRefreshed && gDeviceToken.length > 0) return;

    NSString* serverUrl = BPServerUrl();
    NSString* apiKey = BPApiKey();
    if (serverUrl.length == 0 || apiKey.length == 0) return;

    NSString* tunnelDeviceId = BPCString(Bugpunch_TunnelDeviceId());
    NSString* deviceId = tunnelDeviceId.length > 0 ? tunnelDeviceId : BPMeta(@"deviceId");
    NSString* stableDeviceId = BPCString(Bugpunch_GetStableDeviceId());
    NSString* buildChannel = [BPDebugMode shared].config[@"buildChannel"];
    if (![buildChannel isKindOfClass:[NSString class]]) buildChannel = @"unknown";

    NSDictionary* body = @{
        @"deviceId":         deviceId ?: @"",
        @"name":             BPMeta(@"deviceModel"),
        @"platform":         @"iOS",
        @"appVersion":       BPMeta(@"appVersion"),
        @"scriptPermission": gScriptPermission ?: @"ask",
        @"installerMode":    BPMeta(@"installerMode"),
        @"stableDeviceId":   stableDeviceId ?: @"",
        @"buildChannel":     buildChannel ?: @"unknown",
    };
    NSData* bodyData = [NSJSONSerialization dataWithJSONObject:body options:0 error:nil];
    NSString* bodyJson = [[NSString alloc] initWithData:bodyData encoding:NSUTF8StringEncoding];

    NSString* url = [serverUrl stringByAppendingString:@"/api/devices/register"];
    // Send the cached token if we have one — lets the server keep our existing
    // token instead of rotating (#226 proof-of-possession).
    BPPostJsonSync2(url, @"X-Api-Key", apiKey, @"X-Device-Token", gDeviceToken ?: @"", bodyJson,
        ^(NSInteger status, NSString* bodyRsp) {
            if (status < 200 || status >= 300) {
                NSLog(@"[BugpunchPoller] register failed: %ld", (long)status);
                return;
            }
            NSData* d = [bodyRsp dataUsingEncoding:NSUTF8StringEncoding];
            id parsed = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
            if (![parsed isKindOfClass:[NSDictionary class]]) return;
            NSString* token = parsed[@"token"];
            if ([token isKindOfClass:[NSString class]] && token.length > 0) {
                gDeviceToken = token;
                BPSaveToken(token);
                gRegistrationRefreshed = YES;
                NSLog(@"[BugpunchPoller] registered");
            }
        });
}

// -- Unread-chat poll (issue #32) --------------------------------------------
//
// Cheap GET /api/v1/chat/unread that the chat router added for the floating-
// button badge. Uses SDK auth (Bearer + X-Device-Id), not the device-poll
// token, so it talks to the same /api/v1/chat/* router the chat VC uses.
// Failures are silent — the badge keeps its previous value until we get a
// successful tick.

static void BPPollUnreadChat(void) {
    NSString* serverUrl = BPServerUrl();
    NSString* apiKey = BPApiKey();
    if (serverUrl.length == 0 || apiKey.length == 0) return;

    NSString* deviceId = BPCString(Bugpunch_GetStableDeviceId());
    if (deviceId.length == 0) return;

    if (!gSession) {
        NSURLSessionConfiguration* cfg = [NSURLSessionConfiguration defaultSessionConfiguration];
        cfg.timeoutIntervalForRequest = 15.0;
        cfg.timeoutIntervalForResource = 15.0;
        gSession = [NSURLSession sessionWithConfiguration:cfg];
    }

    NSString* url = [serverUrl stringByAppendingString:@"/api/v1/chat/unread"];
    NSMutableURLRequest* req = [NSMutableURLRequest requestWithURL:[NSURL URLWithString:url]];
    req.HTTPMethod = @"GET";
    [req setValue:[@"Bearer " stringByAppendingString:apiKey] forHTTPHeaderField:@"Authorization"];
    [req setValue:deviceId forHTTPHeaderField:@"X-Device-Id"];
    [req setValue:@"application/json" forHTTPHeaderField:@"Accept"];

    dispatch_semaphore_t sem = dispatch_semaphore_create(0);
    NSURLSessionDataTask* task = [gSession dataTaskWithRequest:req
        completionHandler:^(NSData* data, NSURLResponse* resp, NSError* error) {
            if (error || !data) { dispatch_semaphore_signal(sem); return; }
            NSHTTPURLResponse* httpResp = (NSHTTPURLResponse*)resp;
            if (httpResp.statusCode < 200 || httpResp.statusCode >= 300) {
                dispatch_semaphore_signal(sem); return;
            }
            id parsed = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
            if ([parsed isKindOfClass:[NSDictionary class]]) {
                id n = parsed[@"count"];
                if ([n isKindOfClass:[NSNumber class]]) {
                    Bugpunch_SetUnreadCount([n intValue]);
                }
            }
            dispatch_semaphore_signal(sem);
        }];
    [task resume];
    dispatch_semaphore_wait(sem, DISPATCH_TIME_FOREVER);
}

// -- Poll ---------------------------------------------------------------------

static void BPDoPoll(void) {
    if (gPollerStopped) return;
    if (gDeviceToken.length == 0) {
        BPEnsureRegistered();
        if (gDeviceToken.length == 0) return;
    }

    NSString* serverUrl = BPServerUrl();
    if (serverUrl.length == 0) return;
    NSString* url = [serverUrl stringByAppendingString:@"/api/device-poll"];

    BPPostJsonSync(url, @"X-Device-Token", gDeviceToken, @"{}",
        ^(NSInteger status, NSString* bodyRsp) {
            if (status == 401) {
                NSLog(@"[BugpunchPoller] poll 401 — clearing token");
                gDeviceToken = @"";
                gRegistrationRefreshed = NO;
                BPSaveToken(@"");
                return;
            }
            if (status < 200 || status >= 300) {
                NSLog(@"[BugpunchPoller] poll failed: %ld", (long)status);
                return;
            }
            NSData* d = [bodyRsp dataUsingEncoding:NSUTF8StringEncoding];
            id parsed = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
            if (![parsed isKindOfClass:[NSDictionary class]]) return;
            NSDictionary* resp = parsed;

            // Pin config also travels on the poll path so release/internal
            // devices can pick up QA enrollment before the report tunnel is live.
            NSDictionary* pinConfig = resp[@"pinConfig"];
            if ([pinConfig isKindOfClass:[NSDictionary class]]) {
                NSData* pinData = [NSJSONSerialization dataWithJSONObject:pinConfig options:0 error:nil];
                NSString* pinJson = pinData ? [[NSString alloc] initWithData:pinData encoding:NSUTF8StringEncoding] : nil;
                if (pinJson.length > 0) Bugpunch_TunnelApplyPinConfig([pinJson UTF8String]);

                NSDictionary* pins = pinConfig[@"pins"];
                BOOL accepted = [pinConfig[@"consent"] isKindOfClass:[NSString class]]
                    && [pinConfig[@"consent"] isEqualToString:@"accepted"];
                BOOL shouldConnect = accepted && [pins isKindOfClass:[NSDictionary class]]
                    && ([pins[@"alwaysLog"] boolValue] || [pins[@"alwaysRemote"] boolValue] || [pins[@"alwaysDebug"] boolValue]);
                if (shouldConnect && !Bugpunch_TunnelIsConnected()) {
                    NSMutableDictionary* cfg = [[BPDebugMode shared].config mutableCopy] ?: [NSMutableDictionary dictionary];
                    cfg[@"useNativeTunnel"] = @YES;
                    NSData* cfgData = [NSJSONSerialization dataWithJSONObject:cfg options:0 error:nil];
                    NSString* cfgJson = cfgData ? [[NSString alloc] initWithData:cfgData encoding:NSUTF8StringEncoding] : nil;
                    if (cfgJson.length > 0) Bugpunch_StartTunnel([cfgJson UTF8String]);
                }
            }

            // 1) Device-targeted directives -> native handler.
            NSArray* pending = resp[@"pendingDirectives"];
            if ([pending isKindOfClass:[NSArray class]] && pending.count > 0) {
                NSData* pd = [NSJSONSerialization dataWithJSONObject:pending options:0 error:nil];
                NSString* pdJson = [[NSString alloc] initWithData:pd encoding:NSUTF8StringEncoding];
                BPDirectives_OnPollDirectives([pdJson UTF8String]);
            }

            // 2) Upgrade-to-WebSocket -> ask C# (tunnel is still C#).
            if ([resp[@"upgradeToWebSocket"] boolValue]) {
                UnitySendMessage("BugpunchClient", "OnPollUpgradeRequested", "");
            }

            // 3) Scheduled scripts -> C# executes via the script runner.
            NSArray* scripts = resp[@"scripts"];
            if ([scripts isKindOfClass:[NSArray class]] && scripts.count > 0) {
                NSData* sd = [NSJSONSerialization dataWithJSONObject:scripts options:0 error:nil];
                NSString* sJson = [[NSString alloc] initWithData:sd encoding:NSUTF8StringEncoding];
                UnitySendMessage("BugpunchClient", "OnPollScripts", [sJson UTF8String]);
            }
        });

    // 4) Unread-chat badge (issue #32). Piggybacks on the same tick so we
    // don't add a second timer. Every other tick at the default 30s cadence
    // keeps chat-server load steady while surfacing replies within ~60s.
    gUnreadTickCounter++;
    if ((gUnreadTickCounter & 1) == 0) {
        BPPollUnreadChat();
    }
}

// -- Entry points -------------------------------------------------------------

extern "C" void Bugpunch_StartPoll(const char* scriptPermissionC, int pollIntervalSeconds) {
    if (gPollerStarted) return;
    NSString* serverUrl = BPServerUrl();
    NSString* apiKey = BPApiKey();
    if (serverUrl.length == 0 || apiKey.length == 0) {
        NSLog(@"[BugpunchPoller] missing serverUrl/apiKey — not starting");
        return;
    }

    gScriptPermission = scriptPermissionC && *scriptPermissionC
        ? [NSString stringWithUTF8String:scriptPermissionC] : @"ask";
    gPollIntervalSeconds = MAX(5, pollIntervalSeconds);
    gDeviceToken = BPLoadToken();
    gRegistrationRefreshed = NO;

    gPollQueue = dispatch_queue_create("au.com.oddgames.bugpunch.poller", DISPATCH_QUEUE_SERIAL);
    gPollerStarted = YES;
    gPollerStopped = NO;

    // Initial register + poll.
    dispatch_async(gPollQueue, ^{
        BPEnsureRegistered();
        BPDoPoll();
    });

    // Recurring poll timer.
    gPollTimer = dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER, 0, 0, gPollQueue);
    dispatch_source_set_timer(gPollTimer,
        dispatch_time(DISPATCH_TIME_NOW, (int64_t)(gPollIntervalSeconds * NSEC_PER_SEC)),
        (uint64_t)(gPollIntervalSeconds * NSEC_PER_SEC),
        (uint64_t)(NSEC_PER_SEC));
    dispatch_source_set_event_handler(gPollTimer, ^{ BPDoPoll(); });
    dispatch_resume(gPollTimer);

    NSLog(@"[BugpunchPoller] started (interval=%ld s)", (long)gPollIntervalSeconds);
}

extern "C" void Bugpunch_StopPoll(void) {
    gPollerStopped = YES;
    if (gPollTimer) {
        dispatch_source_cancel(gPollTimer);
        gPollTimer = nil;
    }
    gPollerStarted = NO;
}

extern "C" void Bugpunch_PollNow(void) {
    if (!gPollerStarted || gPollerStopped || !gPollQueue) return;
    dispatch_async(gPollQueue, ^{ BPDoPoll(); });
}

/**
 * Post a scheduled script's execution result back to the server. Invoked from
 * C# once the script runner finishes. Fires on the poll queue (same serial queue, so
 * no concurrent HTTP from this subsystem).
 */
extern "C" void Bugpunch_PostScriptResult(const char* scheduledScriptIdC,
                                          const char* outputC, const char* errorsC,
                                          bool success, int durationMs) {
    if (!gPollerStarted || gPollerStopped || !gPollQueue) return;
    if (gDeviceToken.length == 0) return;

    NSString* scheduledScriptId = scheduledScriptIdC ? [NSString stringWithUTF8String:scheduledScriptIdC] : @"";
    NSString* output = outputC ? [NSString stringWithUTF8String:outputC] : @"";
    NSString* errors = errorsC ? [NSString stringWithUTF8String:errorsC] : @"";
    BOOL successBool = success;
    int duration = durationMs;

    dispatch_async(gPollQueue, ^{
        NSString* serverUrl = BPServerUrl();
        if (serverUrl.length == 0) return;
        NSDictionary* body = @{
            @"scheduledScriptId": scheduledScriptId,
            @"output":            output,
            @"errors":            errors,
            @"success":           @(successBool),
            @"durationMs":        @(duration),
        };
        NSData* data = [NSJSONSerialization dataWithJSONObject:body options:0 error:nil];
        NSString* bodyJson = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
        NSString* url = [serverUrl stringByAppendingString:@"/api/device-poll/script-result"];
        BPPostJsonSync(url, @"X-Device-Token", gDeviceToken, bodyJson,
            ^(NSInteger status, NSString* bodyRsp) {
                if (status < 200 || status >= 300) {
                    NSLog(@"[BugpunchPoller] script-result failed: %ld", (long)status);
                }
            });
    });
}
