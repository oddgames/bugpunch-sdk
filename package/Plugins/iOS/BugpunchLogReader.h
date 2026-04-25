// BugpunchLogReader.h — log ring buffer + on-demand OSLogStore snapshot.
//
// Three sources feed BPLogReader:
//   1. Unity Debug.Log via Bugpunch_PushLogEntry → pushEntryWithType:
//   2. Native plugins via Bugpunch_LogMessage → appendLineLive:
//   3. On-demand OSLogStore pull inside snapshotText (last 60 s)
//
// Uses @synchronized on the log buffer — fine for managed callers but NOT
// async-signal-safe. Never call from a crash signal handler.

#import <Foundation/Foundation.h>

@interface BPLogReader : NSObject
+ (void)startWithMaxEntries:(NSInteger)n;
+ (void)stop;
+ (NSString*)snapshotText;
+ (void)pushEntryWithType:(NSString*)type message:(NSString*)message stackTrace:(NSString*)stackTrace;
// Append a pre-formatted line to the in-memory ring AND tee to the live
// report tunnel. Used by the public `Bugpunch_LogMessage` C API that native
// plugins call explicitly — same role gating as everywhere else lives
// inside Bugpunch_TunnelEnqueueLogLine.
+ (void)appendLineLive:(NSString*)line;
@end

// Implemented in BugpunchTunnel.mm. Declared here so BPLogReader can tee
// live log lines to the live-report tunnel without re-declaring it inline.
extern "C" void Bugpunch_TunnelEnqueueLogLine(const char* line);
