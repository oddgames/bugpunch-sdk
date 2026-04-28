/*
 * bp_video.h — async-signal-safe interface for the crash-survivable video
 * ring. bp.c calls bp_video_finalize() from the SIGSEGV/SIGBUS/etc. handler
 * to seal the ring header before the process dies; everything else is
 * driven from Java via the JNI exports in bp_video.c.
 */
#ifndef BP_VIDEO_H
#define BP_VIDEO_H

/*
 * Async-signal-safe finalize. Flushes the ring header (head cursors + valid
 * marker) so the next launch can locate the most recent samples.
 *
 * No-op if the ring was never initialized. Must not be called concurrently
 * with bp_video_close() — the recorder's stop path calls close on a normal
 * shutdown, the signal handler calls finalize on a crash; one or the other,
 * never both.
 */
void bp_video_finalize(void);

#endif /* BP_VIDEO_H */
