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

#ifdef __cplusplus
}
#endif
