// BugpunchMetalWatch.h — Metal GPU command-buffer error capture.
//
// Captures GPU-side failures (command-buffer abort / GPU hang, timeout,
// page fault, out-of-memory, GPU restart — the "Execution of the command
// buffer was aborted" / IOAF-code family) by swizzling the Metal command
// queue's buffer-factory methods and attaching an `addCompletedHandler`.
// Event-driven (fires on the GPU completion thread) — NO polling, NO
// os_log, works on every iOS version, independent of the iOS-17 stderr
// change. These errors are emitted by Unity's native renderer and do NOT
// reliably reach the managed `Debug.Log` path, so this is the only way to
// surface them without scraping the system log.
//
// ALL-USER, like crash reporting: every command buffer gets a shared,
// pre-built completion handler whose body is a single status check on the
// success path — the per-buffer cost is a block retain, negligible vs
// rendering. On error it reports NATIVELY (lightweight ingest POST via
// Bugpunch_ReportGpuError — no logs/screenshot/video, no C# round-trip);
// the verbose live-log tee self-gates on the debug tunnel. Mirror: Android
// GPU errors land in logcat (captured) + the render-freeze watchdog, so no
// Android sibling hook is needed.

#ifdef __cplusplus
extern "C" {
#endif

// Install the Metal command-queue swizzle. Idempotent (dispatch_once).
// Safe to call after Unity has already created its command queue — the
// swizzle targets the queue *class*, so it catches every subsequent
// command buffer regardless of when the queue was made.
void Bugpunch_StartMetalWatch(void);

#ifdef __cplusplus
}
#endif
