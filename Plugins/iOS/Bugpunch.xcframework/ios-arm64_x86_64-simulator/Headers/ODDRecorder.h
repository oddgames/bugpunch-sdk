// ODDRecorder.h
// Native recording bridge for ODDGames.Recorder
//
// AVAssetWriter encoder fed by manual RGBA/PCM frames from C#
// (ODDRecorder_AppendVideoFrame / _AppendAudioSamples). The iOS ReplayKit
// auto-capture path is removed — video capture is GPU-only SDK-wide.
//
// Copyright (c) ODDGames. All rights reserved.

#pragma once

#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/// Start recording. Frames are fed manually via ODDRecorder_AppendVideoFrame
/// (no auto-capture — the iOS ReplayKit path is retired).
/// @param outputPath Full file path for the .mp4 output (without extension - .mp4 will be appended)
/// @param width      Target video width (0 = screen width on iOS, required on macOS)
/// @param height     Target video height (0 = screen height on iOS, required on macOS)
/// @param fps        Target frame rate
/// @param bitrate    Video bitrate in bits/sec
/// @param includeAudio Whether to capture app audio
void ODDRecorder_Start(const char* outputPath, int width, int height,
                       int fps, int bitrate, bool includeAudio);

/// Stop recording and finalize the MP4 file.
/// Blocks until writing is complete.
/// @param outPath    Buffer to receive the final output file path (null-terminated UTF-8)
/// @param outPathLen Size of the outPath buffer in bytes
void ODDRecorder_Stop(char* outPath, int outPathLen);

/// Check if currently recording.
/// @return true if a recording session is active
bool ODDRecorder_IsRecording(void);

/// Append a video frame from raw RGBA pixel data — the sole video input path.
/// @param rgbaData     Pointer to RGBA pixel bytes (4 bytes per pixel)
/// @param dataLength   Length in bytes (must equal width * height * 4)
/// @param width        Frame width in pixels
/// @param height       Frame height in pixels
/// @param timestampNs  Presentation timestamp in nanoseconds since recording start
void ODDRecorder_AppendVideoFrame(const uint8_t* rgbaData, int dataLength,
                                  int width, int height, int64_t timestampNs);

/// Append audio samples from Unity's OnAudioFilterRead — the sole audio input path.
/// @param pcmData      Float PCM samples [-1.0, 1.0], interleaved channels
/// @param sampleCount  Number of samples per channel
/// @param channels     Number of audio channels (typically 1 or 2)
/// @param sampleRate   Sample rate in Hz (typically 44100 or 48000)
/// @param timestampNs  Presentation timestamp in nanoseconds since recording start
void ODDRecorder_AppendAudioSamples(const float* pcmData, int sampleCount,
                                    int channels, int sampleRate, int64_t timestampNs);

#ifdef __cplusplus
}
#endif
