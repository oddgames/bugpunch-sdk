// =============================================================================
// BugpunchDirectives - iOS handler for server-sent "Request More Info"
// directives. Mirrors the Android BugpunchDirectives class.
//
// Two entry points:
//   1. BPDirectives_OnUploadResponse  - fires after a successful /api/crashes
//      POST. Server response carries eventId + matchedDirectives[]. Result of
//      each action is POSTed to /api/crashes/events/{eventId}/enrich.
//   2. BPDirectives_OnPollDirectives  - fires from the native poll loop when
//      /api/device-poll returns pendingDirectives[]. No crash context.
//      Action results are POSTed to /api/directives/{directiveId}/result.
//
// Both paths run through the same handlers (attach_files / run_paxscript /
// ask_user_for_help). The dispatcher builds the result URL once and passes it
// down. enable_debug_tunnel is consumed server-side (flips the upgrade flag
// on poll); no client-side action.
// =============================================================================

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

// Bridges into existing machinery.
extern "C" bool   Bugpunch_SetConfigPassthrough(const char* key);   // not used; we read from config dict
extern "C" void   Bugpunch_EnterDebugMode(int skipConsent);
extern "C" void   Bugpunch_SetCustomData(const char* key, const char* value);
extern "C" void   BugpunchUploader_EnqueueJson(const char* url, const char* apiKey,
                                               const char* jsonBody);

// UnitySendMessage lives in libiPhone-lib.a; declared here so we can call it.
extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

// Global access to the shared config (lives in BugpunchDebugMode.mm).
@interface BPDebugMode : NSObject
@property (nonatomic, strong) NSDictionary* config;
@property (nonatomic, strong) NSMutableDictionary<NSString*, NSString*>* metadata;
@property (nonatomic, assign) BOOL started;
+ (instancetype)shared;
@end

static NSString* const kBPDirectiveDeniedKey = @"bugpunch.directive.denied.";

// Pending paxscript callbacks, keyed by directiveId -> resultUrl. The URL
// encodes both the post target AND the event/directive id, so
// postPaxScriptResult doesn't need to know which flow spawned it.
static NSMutableDictionary<NSString*, NSString*>* gPendingPaxScript;
static dispatch_queue_t gPendingLock;

static void BPEnsurePending(void) {
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        gPendingPaxScript = [NSMutableDictionary dictionary];
        gPendingLock = dispatch_queue_create("au.com.oddgames.bugpunch.directives", DISPATCH_QUEUE_SERIAL);
    });
}

// -- Helpers --

static NSString* BPServerUrl(void) {
    NSString* s = [BPDebugMode shared].config[@"serverUrl"];
    return [s isKindOfClass:[NSString class]] ? s : @"";
}

static NSString* BPApiKey(void) {
    NSString* s = [BPDebugMode shared].config[@"apiKey"];
    return [s isKindOfClass:[NSString class]] ? s : @"";
}

static NSString* BPTrimTrailingSlash(NSString* s) {
    while ([s hasSuffix:@"/"]) s = [s substringToIndex:s.length - 1];
    return s;
}

static NSString* BPEnrichUrl(NSString* eventId) {
    NSString* server = BPTrimTrailingSlash(BPServerUrl());
    if (server.length == 0 || eventId.length == 0) return @"";
    return [NSString stringWithFormat:@"%@/api/crashes/events/%@/enrich", server, eventId];
}

static NSString* BPDirectiveResultUrl(NSString* directiveId) {
    NSString* server = BPTrimTrailingSlash(BPServerUrl());
    if (server.length == 0 || directiveId.length == 0) return @"";
    return [NSString stringWithFormat:@"%@/api/directives/%@/result", server, directiveId];
}

static BOOL BPIsDenied(NSString* fingerprint) {
    if (fingerprint.length == 0) return NO;
    NSString* key = [kBPDirectiveDeniedKey stringByAppendingString:fingerprint];
    return [[NSUserDefaults standardUserDefaults] boolForKey:key];
}

static void BPSetDenied(NSString* fingerprint) {
    if (fingerprint.length == 0) return;
    NSString* key = [kBPDirectiveDeniedKey stringByAppendingString:fingerprint];
    [[NSUserDefaults standardUserDefaults] setBool:YES forKey:key];
}

/** Generic JSON POST through the upload queue. Caller pre-formats the URL. */
static void BPPostJson(NSString* url, NSDictionary* body) {
    NSString* apiKey = BPApiKey();
    if (url.length == 0 || apiKey.length == 0) return;
    NSData* data = [NSJSONSerialization dataWithJSONObject:body options:0 error:nil];
    NSString* jsonStr = data ? [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] : @"{}";
    BugpunchUploader_EnqueueJson([url UTF8String], [apiKey UTF8String], [jsonStr UTF8String]);
}

// -- attach_files: allow-list glob inside BugpunchConfig.attachmentRules --

static NSArray* BPAllowList(void) {
    NSArray* rules = [BPDebugMode shared].config[@"attachmentRules"];
    return [rules isKindOfClass:[NSArray class]] ? rules : @[];
}

/** Minimal glob: only '*' wildcard. Equivalent to Android's matchGlob. */
static BOOL BPMatchGlob(NSString* pattern, NSString* name) {
    if (pattern == nil || [pattern isEqualToString:@"*"]) return YES;
    NSInteger pi = 0, ni = 0, star = -1, match = 0;
    NSInteger pl = pattern.length, nl = name.length;
    while (ni < nl) {
        if (pi < pl && ([pattern characterAtIndex:pi] == [name characterAtIndex:ni]
                         || [pattern characterAtIndex:pi] == '?')) {
            pi++; ni++;
        } else if (pi < pl && [pattern characterAtIndex:pi] == '*') {
            star = pi++; match = ni;
        } else if (star != -1) {
            pi = star + 1; match++; ni = match;
        } else return NO;
    }
    while (pi < pl && [pattern characterAtIndex:pi] == '*') pi++;
    return pi == pl;
}

static void BPHandleAttachFiles(NSString* resultUrl, NSString* directiveId,
                                NSDictionary* action) {
    NSArray* paths = action[@"paths"];
    if (![paths isKindOfClass:[NSArray class]] || paths.count == 0) return;
    long long maxPerFile = [action[@"maxBytesPerFile"] longLongValue];
    if (maxPerFile <= 0) maxPerFile = 4 * 1024 * 1024;
    long long maxTotal = [action[@"maxTotalBytes"] longLongValue];
    if (maxTotal <= 0) maxTotal = 16 * 1024 * 1024;

    NSArray* allow = BPAllowList();
    if (allow.count == 0) return;

    NSMutableArray* attachments = [NSMutableArray array];
    long long running = 0;

    for (NSString* pattern in paths) {
        if (![pattern isKindOfClass:[NSString class]]) continue;
        if ([pattern containsString:@".."]) continue;

        for (NSDictionary* rule in allow) {
            if (![rule isKindOfClass:[NSDictionary class]]) continue;
            NSString* rawPath = rule[@"rawPath"];
            NSString* resolvedRoot = rule[@"path"];
            NSString* rulePattern = rule[@"pattern"] ?: @"*";
            if (![rawPath isKindOfClass:[NSString class]] || ![resolvedRoot isKindOfClass:[NSString class]]) continue;
            if (![pattern hasPrefix:rawPath]) continue;

            NSString* suffix = [pattern substringFromIndex:rawPath.length];
            NSString* effectiveGlob = suffix.length == 0 ? rulePattern : (([suffix hasPrefix:@"/"]) ? [suffix substringFromIndex:1] : suffix);

            NSArray<NSString*>* entries = [[NSFileManager defaultManager]
                contentsOfDirectoryAtPath:resolvedRoot error:nil];
            for (NSString* name in entries) {
                if (!BPMatchGlob(effectiveGlob, name)) continue;
                if (!BPMatchGlob(rulePattern, name)) continue;
                NSString* full = [resolvedRoot stringByAppendingPathComponent:name];
                NSDictionary* attrs = [[NSFileManager defaultManager]
                    attributesOfItemAtPath:full error:nil];
                long long size = [attrs[NSFileSize] longLongValue];
                if (size <= 0 || size > maxPerFile) continue;
                if (running + size > maxTotal) break;
                NSData* data = [NSData dataWithContentsOfFile:full];
                if (!data) continue;
                [attachments addObject:@{
                    @"path": pattern,
                    @"bytes": @(size),
                    @"dataBase64": [data base64EncodedStringWithOptions:0],
                }];
                running += size;
            }
            break;  // matched a rule, don't evaluate subsequent rules for same pattern
        }
    }

    BPPostJson(resultUrl, @{
        @"directiveId": directiveId,
        @"attachments": attachments,
    });
}

// -- run_paxscript: UnitySendMessage + pending callback --

static void BPHandleRunPaxScript(NSString* resultUrl, NSString* directiveId,
                                 NSDictionary* action) {
    NSString* code = action[@"code"];
    if (![code isKindOfClass:[NSString class]] || code.length == 0) return;
    NSInteger timeoutMs = [action[@"timeoutMs"] integerValue];
    if (timeoutMs <= 0) timeoutMs = 2000;

    BPEnsurePending();
    dispatch_sync(gPendingLock, ^{ gPendingPaxScript[directiveId] = resultUrl ?: @""; });

    NSDictionary* payload = @{
        @"directiveId": directiveId,
        @"code": code,
        @"timeoutMs": @(timeoutMs),
    };
    NSData* data = [NSJSONSerialization dataWithJSONObject:payload options:0 error:nil];
    NSString* jsonStr = data ? [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] : @"{}";
    UnitySendMessage("BugpunchClient", "DirectiveRunPaxScript", [jsonStr UTF8String]);
}

// -- ask_user_for_help: UIAlertController + persistent denial --
// fingerprint is empty for device-targeted (poll) directives - denial
// persistence is skipped in that case.

static void BPHandleAskUser(NSString* resultUrl, NSString* fingerprint,
                            NSString* directiveId, NSDictionary* action) {
    if (BPIsDenied(fingerprint)) return;

    NSString* title = action[@"promptTitle"] ?: @"Help us fix this bug";
    NSString* body = action[@"promptBody"] ?: @"We hit an error. Can you help us reproduce it?";
    NSString* accept = action[@"acceptLabel"] ?: @"Help out";
    NSString* decline = action[@"declineLabel"] ?: @"No thanks";

    dispatch_async(dispatch_get_main_queue(), ^{
        UIViewController* root = UIApplication.sharedApplication.keyWindow.rootViewController;
        if (!root) return;
        UIAlertController* alert = [UIAlertController
            alertControllerWithTitle:title message:body
            preferredStyle:UIAlertControllerStyleAlert];
        [alert addAction:[UIAlertAction actionWithTitle:accept style:UIAlertActionStyleDefault
            handler:^(UIAlertAction* _) {
                Bugpunch_EnterDebugMode(1);
                Bugpunch_SetCustomData("bugpunch.repro_attempt", "true");
                BPPostJson(resultUrl, @{
                    @"directiveId": directiveId,
                    @"userPromptResult": @"accepted",
                });
            }]];
        [alert addAction:[UIAlertAction actionWithTitle:decline style:UIAlertActionStyleCancel
            handler:^(UIAlertAction* _) {
                BPSetDenied(fingerprint);
                BPPostJson(resultUrl, @{
                    @"directiveId": directiveId,
                    @"userPromptResult": @"declined",
                });
            }]];
        [root presentViewController:alert animated:YES completion:nil];
    });
}

// -- Shared dispatcher used by both entry points --

static void BPApplyAction(NSString* resultUrl, NSString* fingerprint,
                          NSString* directiveId, NSDictionary* action) {
    NSString* type = action[@"type"];
    @try {
        if ([type isEqualToString:@"attach_files"]) {
            BPHandleAttachFiles(resultUrl, directiveId, action);
        } else if ([type isEqualToString:@"run_paxscript"]) {
            BPHandleRunPaxScript(resultUrl, directiveId, action);
        } else if ([type isEqualToString:@"ask_user_for_help"]) {
            BPHandleAskUser(resultUrl, fingerprint, directiveId, action);
        }
        // enable_debug_tunnel is consumed server-side (flips upgrade flag on
        // poll). No client-side action.
    } @catch (NSException* ex) {
        NSLog(@"[BPDirectives] action failed: %@", ex);
    }
}

// -- Entry points --

/** Crash-path directives: invoked after POST /api/crashes succeeds. */
extern "C" void BPDirectives_OnUploadResponse(const char* urlC, const char* bodyC) {
    if (!urlC || !bodyC) return;
    NSString* url = [NSString stringWithUTF8String:urlC];
    NSString* body = [NSString stringWithUTF8String:bodyC];
    if (![url hasSuffix:@"/api/crashes"]) return;
    if (body.length == 0) return;

    NSData* data = [body dataUsingEncoding:NSUTF8StringEncoding];
    id parsed = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
    if (![parsed isKindOfClass:[NSDictionary class]]) return;
    NSDictionary* resp = parsed;

    NSString* eventId = resp[@"eventId"];
    NSString* fingerprint = resp[@"fingerprint"] ?: @"";
    NSArray* directives = resp[@"matchedDirectives"];
    if (![eventId isKindOfClass:[NSString class]] || eventId.length == 0) return;
    if (![directives isKindOfClass:[NSArray class]]) return;

    NSString* resultUrl = BPEnrichUrl(eventId);
    if (resultUrl.length == 0) return;

    for (NSDictionary* d in directives) {
        if (![d isKindOfClass:[NSDictionary class]]) continue;
        NSString* directiveId = d[@"id"];
        NSArray* actions = d[@"actions"];
        if (![directiveId isKindOfClass:[NSString class]] || ![actions isKindOfClass:[NSArray class]]) continue;
        for (NSDictionary* a in actions) {
            if (![a isKindOfClass:[NSDictionary class]]) continue;
            BPApplyAction(resultUrl, fingerprint, directiveId, a);
        }
    }
}

/**
 * Poll-path directives: invoked from the native poller when /api/device-poll
 * returns pendingDirectives. Input is the raw JSON array:
 *   [ { "id": "...", "actions": [ ... ] }, ... ]
 */
extern "C" void BPDirectives_OnPollDirectives(const char* jsonC) {
    if (!jsonC || !*jsonC) return;
    NSString* body = [NSString stringWithUTF8String:jsonC];
    if (body.length == 0) return;

    NSData* data = [body dataUsingEncoding:NSUTF8StringEncoding];
    id parsed = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
    if (![parsed isKindOfClass:[NSArray class]]) return;

    for (NSDictionary* d in (NSArray*)parsed) {
        if (![d isKindOfClass:[NSDictionary class]]) continue;
        NSString* directiveId = d[@"id"];
        NSArray* actions = d[@"actions"];
        if (![directiveId isKindOfClass:[NSString class]] || directiveId.length == 0) continue;
        if (![actions isKindOfClass:[NSArray class]]) continue;

        NSString* resultUrl = BPDirectiveResultUrl(directiveId);
        if (resultUrl.length == 0) continue;

        for (NSDictionary* a in actions) {
            if (![a isKindOfClass:[NSDictionary class]]) continue;
            BPApplyAction(resultUrl, @"", directiveId, a);
        }
    }
}

extern "C" void Bugpunch_PostPaxScriptResult(const char* directiveIdC, const char* resultJsonC) {
    if (!directiveIdC || !*directiveIdC) return;
    NSString* directiveId = [NSString stringWithUTF8String:directiveIdC];
    NSString* resultJson = resultJsonC ? [NSString stringWithUTF8String:resultJsonC] : @"{}";

    BPEnsurePending();
    __block NSString* resultUrl = nil;
    dispatch_sync(gPendingLock, ^{
        resultUrl = gPendingPaxScript[directiveId];
        [gPendingPaxScript removeObjectForKey:directiveId];
    });
    if (resultUrl.length == 0) return;

    NSData* data = [resultJson dataUsingEncoding:NSUTF8StringEncoding];
    id parsed = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
    NSDictionary* resultDict = [parsed isKindOfClass:[NSDictionary class]] ? parsed
        : @{ @"ok": @NO, @"errors": @[@"bad json from paxscript runner"] };

    BPPostJson(resultUrl, @{
        @"directiveId": directiveId,
        @"paxscript": resultDict,
    });
}
