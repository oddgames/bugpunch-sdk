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
#ifdef __cplusplus
}
#endif

// Role-gated NSLog. Use in place of NSLog throughout the iOS lane. The per-file
// BPLog / BPLogError / BPLogR convenience macros expand through this, so their
// tag prefixes are preserved while inheriting the gate.
#define BPLOG(fmt, ...) do { if (BugpunchLog_ShouldEmit()) NSLog(fmt, ##__VA_ARGS__); } while (0)
