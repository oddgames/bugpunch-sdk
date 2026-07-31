#pragma once
#import <Foundation/Foundation.h>
#include <stdbool.h>

// Role gate for Bugpunch's own console output. Returns true only for Internal +
// External testers; a Public (consumer) build stays silent so a real player never
// sees a [Bugpunch.*] line in their device logs. Self-errors are still
// auto-reported to the server via the issue pipeline, so nothing is lost.
//
// Mirrors the C# BugpunchLog (gated on RoleState.Current == Public) and the
// Android BugpunchLog (gated on BugpunchTunnel.isTester()).
#ifdef __cplusplus
extern "C" {
#endif
bool BugpunchLog_ShouldEmit(void);
// Tee an already-formatted SDK log line into the captured log ring
// (BPLogReader appendLineLive). Implemented in BugpunchLog.mm.
void BugpunchLog_Capture(NSString* line);
#ifdef __cplusplus
}
#endif

// Role-gated NSLog. Use in place of NSLog throughout the iOS lane. The per-file
// BPLog / BPLogError / BPLogR convenience macros expand through this, so their
// tag prefixes are preserved while inheriting the gate.
//
// Lines ALSO tee into the captured log ring: on-device NSLog goes to os_log
// only (no stderr unless a debugger is attached), and the report-time
// OSLogStore pull doesn't reliably surface it — so without the tee, uploaded
// reports carry NONE of the SDK's own diagnostics (recorder/remuxer warns
// included). Seen in production: a 1-frame video clip whose report logs had
// zero [Bugpunch.*] lines to explain it. Tester-gated like the NSLog itself.
#define BPLOG(fmt, ...) do { if (BugpunchLog_ShouldEmit()) { \
    NSString* _bpl = [NSString stringWithFormat:fmt, ##__VA_ARGS__]; \
    NSLog(@"%@", _bpl); \
    BugpunchLog_Capture(_bpl); \
} } while (0)
