// BugpunchRingRecorder.h
// Always-on ring buffer video recorder for Bugpunch SDK (iOS)
//
// Uses ReplayKit RPScreenRecorder.startCapture for zero-permission screen
// capture, VideoToolbox VTCompressionSession for H.264 encoding, and an
// in-memory circular buffer of encoded NAL units trimmed to a configurable
// window (default 30s). On dump(), finds the oldest keyframe boundary and
// writes a valid MP4 via AVAssetWriter.
//
// Copyright (c) ODDGames. All rights reserved.

#pragma once

#include <stdint.h>
#include <stdbool.h>
#include <CoreMedia/CoreMedia.h>

#ifdef __cplusplus
extern "C" {
#endif

/// Configure the ring buffer recorder. Call before BugpunchRing_Start.
/// @param width          Capture width (even number; 0 = auto from screen)
/// @param height         Capture height (even number; 0 = auto from screen)
/// @param fps            Target frame rate (clamped to [1, 60])
/// @param bitrate        H.264 bitrate in bits/sec (e.g. 2000000)
/// @param windowSeconds  Rolling window size in seconds (e.g. 30)
void BugpunchRing_Configure(int width, int height, int fps, int bitrate, int windowSeconds);

/// Start always-on recording. ReplayKit captures screen frames, VideoToolbox
/// encodes to H.264, samples accumulate in the ring buffer. No file is written
/// until BugpunchRing_Dump is called.
/// @return true if capture started successfully
bool BugpunchRing_Start(void);

/// Stop recording and release all resources. The ring buffer is cleared.
void BugpunchRing_Stop(void);

/// Returns true if the recorder is currently capturing.
bool BugpunchRing_IsRunning(void);

/// Returns true if the ring buffer contains at least one keyframe.
bool BugpunchRing_HasFootage(void);

/// Dump the ring buffer contents to an MP4 file. The output is trimmed to
/// start on the oldest keyframe within the configured window.
/// @param outputPath  Absolute path for the MP4 output (will be overwritten)
/// @return true on success
bool BugpunchRing_Dump(const char* outputPath);

/// Returns approximate memory usage of the ring buffer in bytes.
int64_t BugpunchRing_GetBufferSizeBytes(void);

/// Host time (seconds since boot, same clock as UITouch.timestamp) of the
/// first frame in the most recent successful dump. 0 if no dump yet.
double BugpunchRing_GetLastDumpStartHostTime(void);

/// Host time of the last frame in the most recent successful dump.
double BugpunchRing_GetLastDumpEndHostTime(void);

/// Encoded video frame width in pixels (0 until BugpunchRing_Start has resolved
/// dimensions). This is the canonical coordinate frame for touches recorded by
/// BugpunchTouchRecorder — touches and video share the same pixel space.
int BugpunchRing_GetVideoWidth(void);

/// Encoded video frame height in pixels.
int BugpunchRing_GetVideoHeight(void);

// ── Crash-survivable on-disk ring (mirrors Android bp_video.c) ──
//
// While recording, every VideoToolbox-encoded sample is also appended to an
// mmap'd ring file on disk so it survives a crash (the kernel's page-cache
// writeback persists the bytes whether or not the process exits cleanly).
// The crash handler only needs the ring path (cached as a C string) and a
// single async-signal-safe finalize call.

/// Async-signal-safe. Called from the POSIX signal handler (and ANR/Mach
/// paths) to msync the ring header page so the cursors are durable before the
/// process dies. No-op if the ring was never initialized. NO Obj-C, NO malloc,
/// NO Foundation — only msync(). Safe to call from a signal handler.
void BugpunchRing_Finalize(void);

/// Returns the cached absolute path to the on-disk ring file, or NULL/"" if
/// the ring was never started this session. Pre-computed when the recorder
/// begins so the crash handler can write a `video:<path>` line without any
/// allocation. The returned pointer is a stable static C buffer.
const char* BugpunchRing_GetRingPath(void);

// ── Shared screen capture for the live WebRTC streamer ──
//
// ReplayKit allows exactly one in-app capture session per process, so the
// recorder owns it and `BugpunchStreamer` rides along. Two concerns are
// decoupled internally: "ring armed" (write encoded samples to the crash ring,
// driven by BugpunchRing_Start/Stop) and "capture running" (ReplayKit session
// live). Either a recording ring OR a streaming consumer keeps capture alive.
// This mirrors Android, where BugpunchStreamer shares BugpunchRecorder's
// MediaProjection without forcing the crash ring on.

/// Per-frame sink invoked on the ReplayKit callback thread with each raw video
/// `CMSampleBuffer` while capture is running, BEFORE the (optional) ring
/// encode. The streamer wraps the sample's CVPixelBuffer into an RTCVideoFrame.
/// `ctx` is the opaque pointer passed to BugpunchRing_SetVideoFrameSink.
typedef void (*BugpunchVideoFrameSink)(void* ctx, CMSampleBufferRef sampleBuffer);

/// Register (or clear, with NULL) the single video-frame sink. The recorder
/// holds it weakly by value — the caller owns the lifetime of `ctx`.
void BugpunchRing_SetVideoFrameSink(BugpunchVideoFrameSink sink, void* ctx);

/// Ref-counted: ensure the ReplayKit capture session is running so the frame
/// sink receives frames, even when the crash ring is NOT armed. May trigger the
/// iOS screen-recording permission prompt on first use (acceptable for tester
/// roles, mirrors Android's MediaProjection consent). Returns true if capture
/// is (now) live. Balance every call with BugpunchRing_ReleaseStreamingCapture.
bool BugpunchRing_AcquireStreamingCapture(void);

/// Ref-counted release. When no streaming consumers remain AND the ring is not
/// armed, the ReplayKit capture session is stopped.
void BugpunchRing_ReleaseStreamingCapture(void);

#ifdef __cplusplus
}
#endif
