// BugpunchLogReader.mm — explicit-API log capture (matches production iOS log SDKs).
//
// Three sources feed this module — all push-based, no recurring timers:
//
//   1. **Unity Debug.Log (managed → P/Invoke).** C# subscribes to
//      `Application.logMessageReceivedThreaded` and forwards every entry
//      via `Bugpunch_PushLogEntry` → `pushEntryWithType:` → ring. This
//      populates the crash-dump buffer regardless of role so reports have
//      Unity context even on public builds. Live streaming of Unity logs
//      to the dashboard happens entirely in C# over the IDE tunnel — see
//      `ConsoleService.cs`.
//
//   2. **Native-plugin explicit API (`Bugpunch_LogMessage`).** Plugin
//      authors call the C entry from BugpunchTunnel.mm to ship their own
//      log lines through the report tunnel. Same shape as Instabug's
//      `+[IBGLog log:]` / Luciq's `+[LCQLog log:]` — the developer
//      decides what's worth surfacing. Routes through `appendLineLive:`
//      so it lands on both the crash-dump ring and the live tunnel.
//
//   3. **On-demand OSLogStore pull inside `snapshotText`.** Single 60 s
//      query at crash / bug-report time fills the report attachment with
//      Apple framework chatter and any third-party NSLog/os_log calls
//      that didn't go through (1) or (2). No recurring timer; the query
//      runs only when a report is being assembled.
//
// We deliberately do NOT auto-capture stderr / NSLog / os_log on iOS.
// Production SDKs (Instabug, Luciq, Sentry-cocoa) all settled on the
// explicit-API pattern after various flirtations with stderr redirect and
// fishhook; we follow the same convention. Apple doesn't expose a public
// push hook over the unified logging system, so anything more than the
// explicit API requires private SPI or developer-tool techniques (dup2 of
// STDERR_FILENO, etc.) that aren't appropriate for a published SDK.
//
// Keeps the first kStartupSize entries in a separate array so they're never
// evicted. Recent entries live in a capped ring buffer. On snapshot, emits:
//   [startup entries] + [breaker with skipped count] + [recent entries]

#import "BugpunchLogReader.h"
#import <OSLog/OSLog.h>

@interface BPLogReader ()
+ (void)appendLine:(NSString*)line;
+ (NSString*)levelCharFor:(OSLogEntryLogLevel)level API_AVAILABLE(ios(15.0));
@end

@implementation BPLogReader {
}

static const NSInteger kStartupSize = 2000;
static NSMutableArray<NSString*>* gStartupBuffer;
static NSMutableArray<NSString*>* gLogBuffer;
static NSInteger gMaxEntries = 2000;
static NSInteger gSkippedCount = 0;
static BOOL gStartupFull = NO;
static NSDateFormatter* gLineFmt;

+ (void)startWithMaxEntries:(NSInteger)n {
    gMaxEntries = MAX(50, n);
    gStartupBuffer = [NSMutableArray array];
    gLogBuffer = [NSMutableArray array];
    gStartupFull = NO;
    gSkippedCount = 0;
    // logcat-ish "MM-dd HH:mm:ss.SSS" so the server parser handles one
    // format across Android + iOS. Used by `pushEntryWithType:` (Unity
    // P/Invoke) and the on-demand OSLogStore query in `snapshotText`.
    gLineFmt = [[NSDateFormatter alloc] init];
    gLineFmt.dateFormat = @"MM-dd HH:mm:ss.SSS";
    gLineFmt.timeZone = [NSTimeZone timeZoneWithAbbreviation:@"UTC"];
}

+ (void)stop {
    // No recurring resource to release. Method kept for symmetry with the
    // Bugpunch_StopDebugMode call site.
}

+ (void)appendLineLive:(NSString*)line {
    if (!line) return;
    const char* c = [line UTF8String];
    if (c) Bugpunch_TunnelEnqueueLogLine(c);
    [BPLogReader appendLine:line];
}

+ (void)appendLine:(NSString*)line {
    if (!line) return;
    @synchronized (gLogBuffer) {
        if (!gStartupFull) {
            [gStartupBuffer addObject:line];
            if ((NSInteger)gStartupBuffer.count >= kStartupSize) gStartupFull = YES;
        } else {
            [gLogBuffer addObject:line];
            while ((NSInteger)gLogBuffer.count > gMaxEntries) {
                [gLogBuffer removeObjectAtIndex:0];
                gSkippedCount++;
            }
        }
    }
}

+ (NSString*)levelCharFor:(OSLogEntryLogLevel)level API_AVAILABLE(ios(15.0)) {
    switch (level) {
        case OSLogEntryLogLevelFault: return @"F";
        case OSLogEntryLogLevelError: return @"E";
        case OSLogEntryLogLevelNotice: return @"W";
        default: return @"I";
    }
}

+ (NSString*)snapshotText {
    NSMutableString* out = [NSMutableString stringWithCapacity:8192];
    @synchronized (gLogBuffer) {
        for (NSString* s in gStartupBuffer) { [out appendString:s]; [out appendString:@"\n"]; }
        if (gSkippedCount > 0) {
            [out appendFormat:@"--- %ld log entries omitted ---\n", (long)gSkippedCount];
        }
        for (NSString* s in gLogBuffer) { [out appendString:s]; [out appendString:@"\n"]; }
    }

    // On-demand OSLogStore pull for native iOS framework chatter from the
    // last 60s. Only invoked at snapshot time (bug report or crash upload),
    // never on a timer. The Unity entries above came from the P/Invoke push
    // path; this captures the rest (NSLog from native plugins, framework
    // logs, etc.) so crash reports still see the surrounding native context.
    if (@available(iOS 15.0, *)) {
        NSError* err = nil;
        OSLogStore* store = [OSLogStore storeWithScope:OSLogStoreCurrentProcessIdentifier error:&err];
        if (store) {
            OSLogPosition* pos = [store positionWithDate:[NSDate dateWithTimeIntervalSinceNow:-60]];
            OSLogEnumerator* en = [store entriesEnumeratorWithOptions:0
                position:pos predicate:nil error:&err];
            if (en) {
                [out appendString:@"--- native iOS logs (last 60s) ---\n"];
                for (OSLogEntryLog* e in en) {
                    if (![e isKindOfClass:[OSLogEntryLog class]]) continue;
                    NSString* lvl = [BPLogReader levelCharFor:e.level];
                    NSString* subsystem = e.subsystem.length > 0 ? e.subsystem : @"iOS";
                    NSString* msg = e.composedMessage ?: @"";
                    [out appendFormat:@"%@     0     0 %@ %@: %@\n",
                        [gLineFmt stringFromDate:e.date], lvl, subsystem, msg];
                }
            }
        }
    }
    return out;
}

+ (void)pushEntryWithType:(NSString*)type message:(NSString*)message stackTrace:(NSString*)stackTrace {
    if (!gLogBuffer) return;  // not started yet
    NSString* lvl =
        [type isEqualToString:@"Error"]     ? @"E" :
        [type isEqualToString:@"Exception"] ? @"E" :
        [type isEqualToString:@"Warning"]   ? @"W" : @"I";
    NSString* msg = message ?: @"";
    if (stackTrace.length > 0) {
        // Join stack onto the message line; server parser treats everything
        // after the first newline in a single logical line as stack.
        msg = [msg stringByAppendingFormat:@"\n%@", stackTrace];
    }
    NSString* line = [NSString stringWithFormat:@"%@     0     0 %@ Unity: %@",
        [gLineFmt stringFromDate:[NSDate date]], lvl, msg];
    [BPLogReader appendLine:line];
}
@end
