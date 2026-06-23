// BugpunchLogReader.h — mmap'd log ring + on-demand OSLogStore snapshot.
//
// Storage: a `MAP_SHARED` mmap'd file under
//   {NSCachesDirectory}/bp_logring.dat
// with a fixed header (magic, version, ring_bytes, _Atomic head, _Atomic
// total) and a byte ring after the header. Kernel writeback persists the
// ring even across SIGKILL / OOM kill / force-stop. The previous launch's
// ring is rotated to `bp_logring.prev.dat` at startup so a next-launch
// drain can recover bytes the signal handler never got to dump.
//
// Producers (in increasing privilege):
//   1. Unity Debug.Log via `Bugpunch_PushLogEntry` → `pushEntryWithType:`
//      → `appendLineLive:`. ALWAYS-ON — the primary ring fill, so crashes
//      carry the game's own log tail regardless of debug mode.
//   2. Native plugins via `Bugpunch_LogMessage` → `appendLineLive:`.
//   3. Passive stdout/stderr capture (`installFdCapture`) — dup2 + a
//      dispatch_source read, echoed back to the console. Catches printf/
//      fprintf native logs. DEBUG-GATED: tees into the ring + tunnel only
//      while a debug tunnel is connected. (On iOS 17+ NSLog/os_log no longer
//      write to stderr, so this is direct-printf only.)
//   4. On-demand OSLogStore pull at report/snapshot time (`snapshotText`) —
//      this is how reports & crashes get os_log history the live producers
//      don't carry, independent of debug mode. (Current-process scope only;
//      can't recover a prior PID.) FILTERED, not a raw dump: keeps the app's
//      own / non-Apple lines plus `com.apple.*` Error/Fault lines, and caps the
//      count — an unfiltered pull returns 10k–100k framework chatter lines that
//      bury the game's own logs and bloat the upload. There is NO recurring
//      OSLogStore poll — constructing an OSLogStore per second re-indexes the
//      unified-log archive (a thermal anti-pattern) for a feed only a connected
//      debug tunnel ever consumed.
//   5. (Crash-only) the signal handler reads the ring atomically and
//      write()s it to the crash report's `logs:` file via the C function
//      `bp_logreader_dump_to_fd`.
//
// Locking model — mirrors Android's `bp_logreader.c`:
//   - Writers (1, 2, 3, and boundary marker) serialize against each other
//     via an Obj-C @synchronized on a single token. After the memcpy, head
//     + total are published via release-store on the `_Atomic` header
//     fields.
//   - The signal-handler dump path holds NO lock. It does atomic
//     acquire-loads of head + total, then `write()`s — both async-signal-
//     safe. Any in-progress writer's bytes that haven't been
//     release-stored yet are simply invisible to the dump (the previous
//     published window appears instead). Same trade-off Android takes.
//
// Compatibility surface: every public Obj-C API on `BPLogReader` is
// preserved verbatim. Callers in `BugpunchDebugMode.mm` /
// `BugpunchTunnel.mm` / `BugpunchCrashHandler.mm` continue to work.

#import <Foundation/Foundation.h>

@interface BPLogReader : NSObject
+ (void)startWithMaxEntries:(NSInteger)n;
+ (void)stop;
+ (NSString* _Nullable)snapshotText;
+ (void)pushEntryWithType:(NSString*)type message:(NSString*)message stackTrace:(NSString*)stackTrace;
// Append a pre-formatted line to the in-memory ring AND tee to the live
// report tunnel. Used by the public `Bugpunch_LogMessage` C API that native
// plugins call explicitly — same role gating as everywhere else lives
// inside Bugpunch_TunnelEnqueueLogLine.
+ (void)appendLineLive:(NSString*)line;
// Snapshot of the live ring only — excludes the OSLogStore 60s pull. Used
// by the tunnel's becameInternal replay so the live viewer doesn't miss
// the pre-handshake window.
+ (NSArray<NSString*>*)bufferedLinesSnapshot;
// Inject a synthetic boundary line into the ring after a report has been
// snapshotted for upload. The dashboard's log viewer collapses everything
// above the most recent boundary into a click-to-expand band.
+ (void)markBoundaryWithType:(NSString*)reportType title:(NSString*)title;
// Snapshot of the first 500 KB captured since startup, frozen once the
// buffer fills or the 60s capture window closes. Bug-report bundling
// pairs this with the tail of the live ring so the dashboard sees both
// boot context and the most-recent activity.
+ (NSData*)snapshotStartBytes;
// Snapshot of the last `maxBytes` of the live ring as UTF-8 bytes. Trims
// on a line boundary. Pass 0 for the full ring.
+ (NSData*)snapshotTailBytes:(NSInteger)maxBytes;
// Path to the file written from `bp_logring.prev.dat` at startup — non-nil
// only if the previous launch left a non-empty ring file behind. Crash
// drain attaches this as `logs:` when a recovered crash record doesn't
// already carry its own logs path (typical for SIGKILL / OOM / force-stop
// where the signal handler never ran).
+ (nullable NSString*)previousLaunchLogPath;
@end

#ifdef __cplusplus
extern "C" {
#endif

// Async-signal-safe ring dump. Reads the ring via C11 acquire-loads and
// `write()`s bytes directly to `fd` — NO Obj-C, NO malloc, NO lock. Safe
// to call from `crash_signal_handler`. Returns the number of bytes
// written, or 0 on no-data / bad-fd.
int bp_logreader_dump_to_fd(int fd);

// Live byte count currently sitting in the ring (i.e. min(total,
// ring capacity)). Used by adaptive drainers to shorten cadence under
// pressure. Bounded by the ring capacity.
long bp_logreader_fill_bytes(void);

// Lifetime byte counter — diagnostics only.
long bp_logreader_total_bytes(void);

// Implemented in BugpunchTunnel.mm. Declared here so BPLogReader can tee
// live log lines to the live-report tunnel without re-declaring it inline.
void Bugpunch_TunnelEnqueueLogLine(const char* _Nonnull line);

#ifdef __cplusplus
}
#endif
