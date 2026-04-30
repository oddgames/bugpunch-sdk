#pragma once

// ============================================================================
// ODDRecorder.h
// Native Windows plugin for hardware-accelerated H.264 video encoding
// using Media Foundation. Part of the ODDGames.Recorder package.
// ============================================================================

#include <stdint.h>
#include <stdbool.h>

#include "UnityPluginAPI/IUnityGraphics.h"

// ----------------------------------------------------------------------------
// Render event IDs used with GL.IssuePluginEvent
// ----------------------------------------------------------------------------
#define ODDRECORDER_EVENT_CAPTURE_FRAME  1

// ----------------------------------------------------------------------------
// Exported C API
// ----------------------------------------------------------------------------
extern "C"
{
    // Start recording. Output will be an MP4 file at outputPath.
    // width/height: video dimensions in pixels
    // fps: target frame rate
    // bitrate: H.264 bitrate in bits/sec (e.g. 8000000 for 8 Mbps)
    // includeAudio: if true, sets up an AAC audio stream
    UNITY_INTERFACE_EXPORT void ODDRecorder_Start(
        const char* outputPath,
        int width,
        int height,
        int fps,
        int bitrate,
        bool includeAudio);

    // Start recording with native backbuffer capture on the render thread.
    // Requires a D3D11 device (retrieved via IUnityGraphics in UnityPluginLoad).
    // When nativeCapture is true, frames are captured automatically each
    // render event; C# only needs to call GL.IssuePluginEvent(GetRenderEventFunc(), 1).
    UNITY_INTERFACE_EXPORT void ODDRecorder_StartWithCapture(
        const char* outputPath,
        int width,
        int height,
        int fps,
        int bitrate,
        bool includeAudio,
        bool nativeCapture);

    // Stop recording, finalize the MP4 file, and write the output path to outPath.
    // outPathLen is the size of the outPath buffer.
    UNITY_INTERFACE_EXPORT void ODDRecorder_Stop(char* outPath, int outPathLen);

    // Returns true if currently recording.
    UNITY_INTERFACE_EXPORT bool ODDRecorder_IsRecording();

    // Append a single video frame. rgbaData is width*height*4 bytes of RGBA pixel data.
    // dataLength should equal width * height * 4.
    // Thread-safe: may be called from any thread.
    UNITY_INTERFACE_EXPORT void ODDRecorder_AppendVideoFrame(
        const uint8_t* rgbaData,
        int dataLength);

    // Append audio samples. pcmData contains interleaved float PCM samples in [-1, 1].
    // sampleCount is the number of samples PER CHANNEL.
    // channels: number of audio channels (1 = mono, 2 = stereo).
    // sampleRate: sample rate in Hz (e.g. 44100, 48000).
    // Thread-safe: may be called from any thread.
    UNITY_INTERFACE_EXPORT void ODDRecorder_AppendAudioSamples(
        const float* pcmData,
        int sampleCount,
        int channels,
        int sampleRate);

    // Returns the render event callback function pointer.
    // Usage from C#: GL.IssuePluginEvent(GetRenderEventFunc(), eventId)
    UNITY_INTERFACE_EXPORT UnityRenderingEvent GetRenderEventFunc();

    // Set the D3D11 device pointer for native capture mode.
    // C# can obtain this via SystemInfo.graphicsDevicePtr and pass it here.
    // Must be called before native capture frames are recorded.
    UNITY_INTERFACE_EXPORT void ODDRecorder_SetD3D11Device(void* devicePtr);
}
