// ============================================================================
// ODDRecorder.cpp
// Native Windows plugin for hardware-accelerated H.264 video encoding
// using Media Foundation. Part of the ODDGames.Recorder package.
//
// Two capture modes:
//   1) C# pass-through: C# does AsyncGPUReadback, calls AppendVideoFrame
//   2) Native capture:  Plugin reads D3D11 backbuffer on the render thread
//
// Thread safety: All sink writer operations are guarded by a mutex.
// ============================================================================

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <codecapi.h>
#include <d3d11.h>
#include <wrl/client.h>

#include "UnityPluginAPI/IUnityGraphicsD3D11.h"
#include <mutex>
#include <string>
#include <atomic>
#include <cstring>
#include <cstdio>
#include <algorithm>
#include <deque>
#include <vector>

#include "ODDRecorder.h"

#pragma comment(lib, "mfreadwrite.lib")
#pragma comment(lib, "mfplat.lib")
#pragma comment(lib, "mfuuid.lib")
#pragma comment(lib, "mf.lib")
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "ole32.lib")

// ============================================================================
// Debug logging
// ============================================================================

static void DebugLog(const char* fmt, ...)
{
    char buf[2048];
    va_list args;
    va_start(args, fmt);
    vsnprintf(buf, sizeof(buf), fmt, args);
    va_end(args);
    OutputDebugStringA("[ODDRecorder] ");
    OutputDebugStringA(buf);
    OutputDebugStringA("\n");
}

// Helper macro: check HRESULT for failure, log and return
#define CHECK_HR(hr, msg)                                                     \
    do {                                                                      \
        if (FAILED(hr)) {                                                     \
            DebugLog("FAILED (0x%08X): %s", (unsigned int)(hr), (msg));       \
            return;                                                           \
        }                                                                     \
    } while (0)

#define CHECK_HR_RET(hr, msg, retval)                                         \
    do {                                                                      \
        if (FAILED(hr)) {                                                     \
            DebugLog("FAILED (0x%08X): %s", (unsigned int)(hr), (msg));       \
            return (retval);                                                  \
        }                                                                     \
    } while (0)

// ============================================================================
// Global recorder state
// ============================================================================

using Microsoft::WRL::ComPtr;

struct RecorderState
{
    // Media Foundation objects
    ComPtr<IMFSinkWriter> sinkWriter;
    DWORD videoStreamIndex = 0;
    DWORD audioStreamIndex = 0;

    // Recording parameters
    int width         = 0;
    int height        = 0;
    int fps           = 30;
    int bitrate       = 8000000;
    bool includeAudio = false;
    std::string outputPath;

    // Frame / sample counters
    int64_t videoFrameCount   = 0;
    int64_t audioSampleOffset = 0;
    int     audioSampleRate   = 48000;
    int     audioChannels     = 2;

    // State flags
    std::atomic<bool> isRecording{false};
    bool nativeCapture = false;

    // Thread safety
    std::mutex writerMutex;

    // D3D11 native capture resources
    ID3D11Device*        d3dDevice  = nullptr;  // Borrowed from Unity, do NOT Release
    ID3D11DeviceContext*  d3dContext = nullptr;  // Borrowed from Unity, do NOT Release
    ComPtr<IMFDXGIDeviceManager> dxgiDeviceManager;
    UINT dxgiResetToken = 0;
    ComPtr<ID3D11Texture2D> stagingTexture;
    ComPtr<ID3D11Texture2D> encoderTexture;
    bool gpuSurfaceInput = false;

    // COM/MF lifecycle tracking
    bool comInitialized = false;
    bool mfStarted      = false;
};

static RecorderState g_state;

// ============================================================================
// In-memory video ring state
// ============================================================================
//
// Ring mode runs the H.264 encoder MFT directly and keeps the last N seconds
// of encoded NAL packets in a std::deque. No disk I/O until RingDump is
// called. The frame source is the same as file mode — C# either pushes BGRA
// frames via AppendVideoFrame, or the render-event callback grabs the
// backbuffer — but instead of going to a Sink Writer, the bytes go through
// BGRA→NV12 conversion, then ProcessInput/ProcessOutput on the encoder MFT,
// and the resulting samples are cloned into the ring.
//
// On RingDump, a fresh Sink Writer is stood up to the target mp4 path with
// H.264 passthrough media types (input == output == encoder's H.264 type)
// and each ring packet is written through as-is. Sink Writer handles mp4
// muxing. The ring keeps running through the dump.

struct RingPacket
{
    int64_t time;       // 100ns presentation timestamp
    int64_t duration;   // 100ns
    bool    isKeyframe;
    std::vector<uint8_t> bytes;
};

struct RingState
{
    std::atomic<bool> active{false};
    int width  = 0;
    int height = 0;
    int fps    = 60;
    int bitrate = 4000000;
    int ringSeconds = 60;
    int keyframeIntervalFrames = 120;

    int64_t frameCount = 0;
    int64_t framesSinceKey = 0;
    int64_t totalDuration100ns = 0;

    ComPtr<IMFTransform> encoder;
    ComPtr<IMFMediaType> encodedOutputType; // captured after first output sample
    DWORD inputStreamId  = 0;
    DWORD outputStreamId = 0;
    MFT_OUTPUT_STREAM_INFO outputInfo = {};
    bool providesOutputSamples = false;

    std::vector<uint8_t> nv12Scratch;

    std::deque<RingPacket> packets;
    std::mutex mutex;
};

static RingState g_ring;

// Forward declarations for ring helpers
static HRESULT RingInitEncoder();
static void    RingShutdownEncoder();
static HRESULT RingFeedBGRA(const uint8_t* src, int srcStride, bool srcIsBGRA);
static HRESULT RingDrainEncoder();
static void    RingTrim();
static HRESULT RingMuxToFileLocked(const std::wstring& outPath);
static void    CaptureBackbufferForRing();

// Unity graphics interfaces (set in UnityPluginLoad)
static IUnityGraphics* g_unityGraphics = nullptr;
static IUnityGraphicsD3D11* g_unityD3D11 = nullptr;

// ============================================================================
// Forward declarations (internal helpers)
// ============================================================================

static HRESULT ConfigureVideoOutput(IMFSinkWriter* writer, DWORD* streamIndex,
                                    int width, int height, int fps, int bitrate);
static HRESULT ConfigureVideoInput(IMFSinkWriter* writer, DWORD streamIndex,
                                   int width, int height, int fps);
static HRESULT ConfigureAudioOutput(IMFSinkWriter* writer, DWORD* streamIndex,
                                    int sampleRate, int channels, int audioBitrate);
static HRESULT ConfigureAudioInput(IMFSinkWriter* writer, DWORD streamIndex,
                                   int sampleRate, int channels);
static void    CleanupRecorder();
static void    CaptureBackbufferD3D11();
static bool    WriteTextureSampleD3D11(ID3D11Texture2D* texture);

// ============================================================================
// Unity plugin lifecycle
// ============================================================================

// IUnityInterfaces is an opaque struct. Unity passes a pointer table; we
// only need the IUnityGraphics sub-interface. The real Unity SDK defines
// IUnityInterfaces with a GetInterface<T> template. Since we declare our
// own minimal version, we use a simple cast-based approach.
//
// In practice Unity's IUnityInterfaces is a vtable with:
//   IUnityInterface* GetInterface(UnityInterfaceGUID guid)
//   void RegisterInterface(UnityInterfaceGUID guid, IUnityInterface* ptr)
//
// For simplicity, we treat it as an opaque pointer and use the known GUID
// for IUnityGraphics to retrieve it. However, since we declared IUnityGraphics
// as a plain struct (not a COM interface), we take a simpler approach:
// Unity actually stores the pointer at a well-known offset. The official SDK
// header does this via template specialization.
//
// To keep this self-contained without the Unity SDK headers, we store the
// raw pointer and cast it when needed. Unity's plugin sample code shows that
// the first interface pointer in the table is typically IUnityGraphics.

// We'll store the raw interfaces pointer and use a helper.
static void* g_unityInterfacesRaw = nullptr;

// GUID for IUnityGraphics: {7CBA0A9CA4DDB544A3A230D3F1D00099}
// This matches Unity's UNITY_REGISTER_INTERFACE_GUID(0x7CBA0A9CA4DDB544ULL, 0xA3A230D3F1D00099ULL, IUnityGraphics)

static void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType);
static void AcquireUnityD3D11Device();

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces)
{
    DebugLog("UnityPluginLoad called");
    g_unityInterfacesRaw = unityInterfaces;
    if (!unityInterfaces)
        return;

    g_unityGraphics = unityInterfaces->Get<IUnityGraphics>();
    g_unityD3D11 = unityInterfaces->Get<IUnityGraphicsD3D11>();

    if (g_unityGraphics)
    {
        g_unityGraphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
        OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
    }
    else
    {
        DebugLog("IUnityGraphics unavailable");
    }
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload()
{
    DebugLog("UnityPluginUnload called");
    if (g_state.isRecording.load())
    {
        DebugLog("Recording still active during unload, forcing stop");
        CleanupRecorder();
    }
    if (g_unityGraphics)
        g_unityGraphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);
    g_unityInterfacesRaw = nullptr;
    g_unityGraphics = nullptr;
    g_unityD3D11 = nullptr;
}

static void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType)
{
    switch (eventType)
    {
    case kUnityGfxDeviceEventInitialize:
        DebugLog("Graphics device initialized");
        AcquireUnityD3D11Device();
        break;
    case kUnityGfxDeviceEventShutdown:
        DebugLog("Graphics device shutting down");
        g_state.d3dDevice  = nullptr;
        g_state.d3dContext = nullptr;
        g_state.stagingTexture.Reset();
        g_state.encoderTexture.Reset();
        break;
    default:
        break;
    }
}

// ============================================================================
// Render event callback (called on Unity's render thread)
// ============================================================================

static void UNITY_INTERFACE_API OnRenderEvent(int eventID)
{
    if (eventID != ODDRECORDER_EVENT_CAPTURE_FRAME)
        return;

    if (g_ring.active.load())
    {
        CaptureBackbufferForRing();
        return;
    }

    if (!g_state.isRecording.load())
        return;

    if (g_state.nativeCapture)
    {
        CaptureBackbufferD3D11();
    }
    // If not nativeCapture, this is a no-op. C# handles readback and calls
    // AppendVideoFrame directly.
}

extern "C" UNITY_INTERFACE_EXPORT UnityRenderingEvent GetRenderEventFunc()
{
    return OnRenderEvent;
}

// ============================================================================
// Media type configuration helpers
// ============================================================================

static HRESULT ConfigureVideoOutput(IMFSinkWriter* writer, DWORD* streamIndex,
                                    int width, int height, int fps, int bitrate)
{
    ComPtr<IMFMediaType> mediaType;
    HRESULT hr = MFCreateMediaType(&mediaType);
    if (FAILED(hr)) return hr;

    hr = mediaType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    if (FAILED(hr)) return hr;

    hr = mediaType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264);
    if (FAILED(hr)) return hr;

    hr = mediaType->SetUINT32(MF_MT_AVG_BITRATE, static_cast<UINT32>(bitrate));
    if (FAILED(hr)) return hr;

    hr = mediaType->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    if (FAILED(hr)) return hr;

    hr = MFSetAttributeSize(mediaType.Get(), MF_MT_FRAME_SIZE,
                            static_cast<UINT32>(width), static_cast<UINT32>(height));
    if (FAILED(hr)) return hr;

    hr = MFSetAttributeRatio(mediaType.Get(), MF_MT_FRAME_RATE,
                             static_cast<UINT32>(fps), 1);
    if (FAILED(hr)) return hr;

    hr = MFSetAttributeRatio(mediaType.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
    if (FAILED(hr)) return hr;

    hr = writer->AddStream(mediaType.Get(), streamIndex);
    return hr;
}

static HRESULT ConfigureVideoInput(IMFSinkWriter* writer, DWORD streamIndex,
                                   int width, int height, int fps)
{
    ComPtr<IMFMediaType> mediaType;
    HRESULT hr = MFCreateMediaType(&mediaType);
    if (FAILED(hr)) return hr;

    hr = mediaType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    if (FAILED(hr)) return hr;

    // MFVideoFormat_RGB32 is BGRA in memory (B=byte0, G=byte1, R=byte2, A=byte3).
    // We convert from Unity's RGBA to this layout in AppendVideoFrame.
    hr = mediaType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
    if (FAILED(hr)) return hr;

    hr = mediaType->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    if (FAILED(hr)) return hr;

    hr = MFSetAttributeSize(mediaType.Get(), MF_MT_FRAME_SIZE,
                            static_cast<UINT32>(width), static_cast<UINT32>(height));
    if (FAILED(hr)) return hr;

    hr = MFSetAttributeRatio(mediaType.Get(), MF_MT_FRAME_RATE,
                             static_cast<UINT32>(fps), 1);
    if (FAILED(hr)) return hr;

    hr = MFSetAttributeRatio(mediaType.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
    if (FAILED(hr)) return hr;

    // Default stride for RGB32 (bottom-up by convention, negative stride).
    // Media Foundation expects top-down for H.264 encoding, so use positive stride.
    hr = mediaType->SetUINT32(MF_MT_DEFAULT_STRIDE, static_cast<UINT32>(width * 4));
    if (FAILED(hr)) return hr;

    hr = writer->SetInputMediaType(streamIndex, mediaType.Get(), NULL);
    return hr;
}

static HRESULT ConfigureAudioOutput(IMFSinkWriter* writer, DWORD* streamIndex,
                                    int sampleRate, int channels, int audioBitrate)
{
    ComPtr<IMFMediaType> mediaType;
    HRESULT hr = MFCreateMediaType(&mediaType);
    if (FAILED(hr)) return hr;

    hr = mediaType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
    if (FAILED(hr)) return hr;

    hr = mediaType->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_AAC);
    if (FAILED(hr)) return hr;

    hr = mediaType->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
    if (FAILED(hr)) return hr;

    hr = mediaType->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, static_cast<UINT32>(sampleRate));
    if (FAILED(hr)) return hr;

    hr = mediaType->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, static_cast<UINT32>(channels));
    if (FAILED(hr)) return hr;

    hr = mediaType->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, static_cast<UINT32>(audioBitrate / 8));
    if (FAILED(hr)) return hr;

    hr = writer->AddStream(mediaType.Get(), streamIndex);
    return hr;
}

static HRESULT ConfigureAudioInput(IMFSinkWriter* writer, DWORD streamIndex,
                                   int sampleRate, int channels)
{
    ComPtr<IMFMediaType> mediaType;
    HRESULT hr = MFCreateMediaType(&mediaType);
    if (FAILED(hr)) return hr;

    hr = mediaType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
    if (FAILED(hr)) return hr;

    // Input: 16-bit PCM. We convert from float in AppendAudioSamples.
    hr = mediaType->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM);
    if (FAILED(hr)) return hr;

    hr = mediaType->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
    if (FAILED(hr)) return hr;

    hr = mediaType->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, static_cast<UINT32>(sampleRate));
    if (FAILED(hr)) return hr;

    hr = mediaType->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, static_cast<UINT32>(channels));
    if (FAILED(hr)) return hr;

    hr = mediaType->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT,
                              static_cast<UINT32>(channels * 2)); // 16-bit = 2 bytes per sample per channel
    if (FAILED(hr)) return hr;

    hr = mediaType->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND,
                              static_cast<UINT32>(sampleRate * channels * 2));
    if (FAILED(hr)) return hr;

    hr = writer->SetInputMediaType(streamIndex, mediaType.Get(), NULL);
    return hr;
}

// ============================================================================
// Cleanup
// ============================================================================

static void CleanupRecorder()
{
    g_state.isRecording.store(false);
    g_state.sinkWriter.Reset();
    g_state.dxgiDeviceManager.Reset();
    g_state.dxgiResetToken = 0;
    g_state.stagingTexture.Reset();
    g_state.encoderTexture.Reset();
    g_state.d3dDevice  = nullptr;
    g_state.d3dContext = nullptr;
    g_state.videoFrameCount   = 0;
    g_state.audioSampleOffset = 0;
    g_state.nativeCapture     = false;
    g_state.gpuSurfaceInput   = false;

    if (g_state.mfStarted)
    {
        MFShutdown();
        g_state.mfStarted = false;
    }

    if (g_state.comInitialized)
    {
        CoUninitialize();
        g_state.comInitialized = false;
    }
}

// ============================================================================
// D3D11 native backbuffer capture
// ============================================================================

static void CaptureBackbufferD3D11()
{
    if (!g_state.d3dDevice || !g_state.d3dContext)
    {
        DebugLog("Native capture: D3D11 device/context not available");
        return;
    }

    if (!g_state.sinkWriter)
        return;

    // Get the current render target (backbuffer)
    ComPtr<ID3D11RenderTargetView> rtv;
    g_state.d3dContext->OMGetRenderTargets(1, &rtv, nullptr);
    if (!rtv)
    {
        DebugLog("Native capture: No render target bound");
        return;
    }

    ComPtr<ID3D11Resource> rtResource;
    rtv->GetResource(&rtResource);
    if (!rtResource)
    {
        DebugLog("Native capture: Could not get render target resource");
        return;
    }

    ComPtr<ID3D11Texture2D> backbuffer;
    HRESULT hr = rtResource.As(&backbuffer);
    if (FAILED(hr))
    {
        DebugLog("Native capture: Render target is not a Texture2D");
        return;
    }

    // Ensure staging texture exists and matches dimensions
    D3D11_TEXTURE2D_DESC bbDesc;
    backbuffer->GetDesc(&bbDesc);

    if (g_state.gpuSurfaceInput)
    {
        if (bbDesc.SampleDesc.Count > 1)
        {
            DebugLog("Native GPU capture: MSAA backbuffer detected (samples=%u); falling back to CPU path",
                     bbDesc.SampleDesc.Count);
        }
        else
        {
            if (bbDesc.Width == static_cast<UINT>(g_state.width) &&
                bbDesc.Height == static_cast<UINT>(g_state.height) &&
                (bbDesc.Format == DXGI_FORMAT_B8G8R8A8_UNORM ||
                 bbDesc.Format == DXGI_FORMAT_B8G8R8A8_TYPELESS))
            {
                if (WriteTextureSampleD3D11(backbuffer.Get()))
                    return;
            }
            else
            {
                if (!g_state.encoderTexture)
                {
                    D3D11_TEXTURE2D_DESC encDesc = {};
                    encDesc.Width              = static_cast<UINT>(g_state.width);
                    encDesc.Height             = static_cast<UINT>(g_state.height);
                    encDesc.MipLevels          = 1;
                    encDesc.ArraySize          = 1;
                    encDesc.Format             = DXGI_FORMAT_B8G8R8A8_UNORM;
                    encDesc.SampleDesc.Count   = 1;
                    encDesc.SampleDesc.Quality = 0;
                    encDesc.Usage              = D3D11_USAGE_DEFAULT;
                    encDesc.BindFlags          = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
                    encDesc.MiscFlags          = 0;
                    HRESULT ehr = g_state.d3dDevice->CreateTexture2D(&encDesc, nullptr,
                                                                     &g_state.encoderTexture);
                    if (FAILED(ehr))
                    {
                        DebugLog("Native GPU capture: failed to create encoder texture (0x%08X); falling back to CPU path",
                                 (unsigned int)ehr);
                    }
                }

                if (g_state.encoderTexture &&
                    bbDesc.Width == static_cast<UINT>(g_state.width) &&
                    bbDesc.Height == static_cast<UINT>(g_state.height))
                {
                    g_state.d3dContext->CopyResource(g_state.encoderTexture.Get(), backbuffer.Get());
                    if (WriteTextureSampleD3D11(g_state.encoderTexture.Get()))
                        return;
                }
                else
                {
                    DebugLog("Native GPU capture: source size/format mismatch (%ux%u fmt=%u), CPU fallback for this frame",
                             bbDesc.Width, bbDesc.Height, (unsigned int)bbDesc.Format);
                }
            }
        }
    }

    if (!g_state.stagingTexture)
    {
        D3D11_TEXTURE2D_DESC stagingDesc = {};
        stagingDesc.Width              = bbDesc.Width;
        stagingDesc.Height             = bbDesc.Height;
        stagingDesc.MipLevels          = 1;
        stagingDesc.ArraySize          = 1;
        stagingDesc.Format             = bbDesc.Format;
        stagingDesc.SampleDesc.Count   = 1;
        stagingDesc.SampleDesc.Quality = 0;
        stagingDesc.Usage              = D3D11_USAGE_STAGING;
        stagingDesc.CPUAccessFlags     = D3D11_CPU_ACCESS_READ;
        stagingDesc.BindFlags          = 0;
        stagingDesc.MiscFlags          = 0;

        hr = g_state.d3dDevice->CreateTexture2D(&stagingDesc, nullptr,
                                                 &g_state.stagingTexture);
        if (FAILED(hr))
        {
            DebugLog("Native capture: Failed to create staging texture (0x%08X)", (unsigned int)hr);
            return;
        }
    }

    // Copy backbuffer to staging texture
    // If the backbuffer is multisampled, we'd need to resolve first.
    // For simplicity, we assume non-MSAA or that Unity resolves before our event.
    if (bbDesc.SampleDesc.Count > 1)
    {
        // Resolve MSAA to staging
        // We need an intermediate non-MSAA texture to resolve into, then copy to staging.
        // For the initial implementation, log a warning.
        DebugLog("Native capture: MSAA backbuffer detected (samples=%u). "
                 "Native capture requires non-MSAA or resolved target.",
                 bbDesc.SampleDesc.Count);
        return;
    }

    g_state.d3dContext->CopyResource(g_state.stagingTexture.Get(), backbuffer.Get());

    // Map the staging texture to CPU memory
    D3D11_MAPPED_SUBRESOURCE mapped;
    hr = g_state.d3dContext->Map(g_state.stagingTexture.Get(), 0, D3D11_MAP_READ, 0, &mapped);
    if (FAILED(hr))
    {
        DebugLog("Native capture: Failed to map staging texture (0x%08X)", (unsigned int)hr);
        return;
    }

    // The backbuffer format is typically DXGI_FORMAT_B8G8R8A8_UNORM (BGRA).
    // Media Foundation's MFVideoFormat_RGB32 is also BGRA, so no conversion needed
    // when the backbuffer is BGRA. If it's RGBA, we need to swap channels.

    const int expectedDataLen = g_state.width * g_state.height * 4;
    const int srcWidth  = static_cast<int>(bbDesc.Width);
    const int srcHeight = static_cast<int>(bbDesc.Height);
    const bool isBGRA   = (bbDesc.Format == DXGI_FORMAT_B8G8R8A8_UNORM ||
                           bbDesc.Format == DXGI_FORMAT_B8G8R8A8_TYPELESS);

    // Create MF buffer and sample
    ComPtr<IMFMediaBuffer> mfBuffer;
    hr = MFCreateMemoryBuffer(expectedDataLen, &mfBuffer);
    if (FAILED(hr))
    {
        g_state.d3dContext->Unmap(g_state.stagingTexture.Get(), 0);
        DebugLog("Native capture: Failed to create MF buffer (0x%08X)", (unsigned int)hr);
        return;
    }

    BYTE* destData = nullptr;
    DWORD maxLen = 0;
    hr = mfBuffer->Lock(&destData, &maxLen, nullptr);
    if (FAILED(hr))
    {
        g_state.d3dContext->Unmap(g_state.stagingTexture.Get(), 0);
        DebugLog("Native capture: Failed to lock MF buffer (0x%08X)", (unsigned int)hr);
        return;
    }

    const uint8_t* srcData = static_cast<const uint8_t*>(mapped.pData);
    const int dstStride = g_state.width * 4;
    const int copyWidth = (std::min)(g_state.width, srcWidth);
    const int copyHeight = (std::min)(g_state.height, srcHeight);

    for (int y = 0; y < copyHeight; ++y)
    {
        const uint8_t* srcRow = srcData + y * mapped.RowPitch;
        uint8_t* dstRow = destData + y * dstStride;

        if (isBGRA)
        {
            // Direct copy, no channel swap needed
            memcpy(dstRow, srcRow, copyWidth * 4);
        }
        else
        {
            // RGBA -> BGRA swap
            for (int x = 0; x < copyWidth; ++x)
            {
                dstRow[x * 4 + 0] = srcRow[x * 4 + 2]; // B <- R
                dstRow[x * 4 + 1] = srcRow[x * 4 + 1]; // G <- G
                dstRow[x * 4 + 2] = srcRow[x * 4 + 0]; // R <- B
                dstRow[x * 4 + 3] = srcRow[x * 4 + 3]; // A <- A
            }
        }
    }

    g_state.d3dContext->Unmap(g_state.stagingTexture.Get(), 0);

    hr = mfBuffer->Unlock();
    if (FAILED(hr))
    {
        DebugLog("Native capture: Failed to unlock MF buffer (0x%08X)", (unsigned int)hr);
        return;
    }

    hr = mfBuffer->SetCurrentLength(static_cast<DWORD>(expectedDataLen));
    if (FAILED(hr))
    {
        DebugLog("Native capture: Failed to set buffer length (0x%08X)", (unsigned int)hr);
        return;
    }

    // Create sample
    ComPtr<IMFSample> sample;
    hr = MFCreateSample(&sample);
    if (FAILED(hr))
    {
        DebugLog("Native capture: Failed to create sample (0x%08X)", (unsigned int)hr);
        return;
    }

    hr = sample->AddBuffer(mfBuffer.Get());
    if (FAILED(hr))
    {
        DebugLog("Native capture: Failed to add buffer to sample (0x%08X)", (unsigned int)hr);
        return;
    }

    // Timestamp in 100-nanosecond units
    int64_t sampleTime = g_state.videoFrameCount * 10000000LL / g_state.fps;
    int64_t duration   = 10000000LL / g_state.fps;

    hr = sample->SetSampleTime(sampleTime);
    if (FAILED(hr))
    {
        DebugLog("Native capture: Failed to set sample time (0x%08X)", (unsigned int)hr);
        return;
    }

    hr = sample->SetSampleDuration(duration);
    if (FAILED(hr))
    {
        DebugLog("Native capture: Failed to set sample duration (0x%08X)", (unsigned int)hr);
        return;
    }

    // Write sample (mutex protected)
    {
        std::lock_guard<std::mutex> lock(g_state.writerMutex);
        if (g_state.sinkWriter && g_state.isRecording.load())
        {
            hr = g_state.sinkWriter->WriteSample(g_state.videoStreamIndex, sample.Get());
            if (FAILED(hr))
            {
                DebugLog("Native capture: WriteSample failed (0x%08X)", (unsigned int)hr);
                return;
            }
            g_state.videoFrameCount++;
        }
    }
}

// ============================================================================
// In-memory video ring implementation
// ============================================================================

static void BGRAToNV12(const uint8_t* src, int srcStride, bool isBGRA,
                       int width, int height, uint8_t* dst)
{
    uint8_t* yPlane  = dst;
    uint8_t* uvPlane = dst + width * height;
    const int chromaH = height / 2;
    const int chromaW = width / 2;

    for (int y = 0; y < height; ++y)
    {
        const uint8_t* sr = src + y * srcStride;
        uint8_t* yr = yPlane + y * width;
        for (int x = 0; x < width; ++x)
        {
            int b, g, r;
            if (isBGRA) { b = sr[x*4 + 0]; g = sr[x*4 + 1]; r = sr[x*4 + 2]; }
            else        { r = sr[x*4 + 0]; g = sr[x*4 + 1]; b = sr[x*4 + 2]; }
            int yv = ((66*r + 129*g + 25*b + 128) >> 8) + 16;
            yr[x] = (uint8_t)(yv < 0 ? 0 : (yv > 255 ? 255 : yv));
        }
    }

    for (int y = 0; y < chromaH; ++y)
    {
        const uint8_t* sr0 = src + (y*2 + 0) * srcStride;
        const uint8_t* sr1 = src + (y*2 + 1) * srcStride;
        uint8_t* uvr = uvPlane + y * width;
        for (int x = 0; x < chromaW; ++x)
        {
            int sR = 0, sG = 0, sB = 0;
            const int o0 = (x*2 + 0) * 4;
            const int o1 = (x*2 + 1) * 4;
            if (isBGRA)
            {
                sB += sr0[o0+0] + sr0[o1+0] + sr1[o0+0] + sr1[o1+0];
                sG += sr0[o0+1] + sr0[o1+1] + sr1[o0+1] + sr1[o1+1];
                sR += sr0[o0+2] + sr0[o1+2] + sr1[o0+2] + sr1[o1+2];
            }
            else
            {
                sR += sr0[o0+0] + sr0[o1+0] + sr1[o0+0] + sr1[o1+0];
                sG += sr0[o0+1] + sr0[o1+1] + sr1[o0+1] + sr1[o1+1];
                sB += sr0[o0+2] + sr0[o1+2] + sr1[o0+2] + sr1[o1+2];
            }
            int r = sR >> 2, g = sG >> 2, b = sB >> 2;
            int u = ((-38*r - 74*g + 112*b + 128) >> 8) + 128;
            int v = ((112*r -  94*g -  18*b + 128) >> 8) + 128;
            uvr[x*2 + 0] = (uint8_t)(u < 0 ? 0 : (u > 255 ? 255 : u));
            uvr[x*2 + 1] = (uint8_t)(v < 0 ? 0 : (v > 255 ? 255 : v));
        }
    }
}

static HRESULT RingInitEncoder()
{
    MFT_REGISTER_TYPE_INFO outType = { MFMediaType_Video, MFVideoFormat_H264 };
    MFT_REGISTER_TYPE_INFO inType  = { MFMediaType_Video, MFVideoFormat_NV12 };

    IMFActivate** activates = nullptr;
    UINT32 count = 0;
    HRESULT hr = MFTEnumEx(MFT_CATEGORY_VIDEO_ENCODER,
                           MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG_SORTANDFILTER,
                           &inType, &outType, &activates, &count);
    if (FAILED(hr) || count == 0)
    {
        if (activates) CoTaskMemFree(activates);
        DebugLog("Ring: no sync H.264 encoder MFT found (hr=0x%08X count=%u)", (unsigned)hr, count);
        return FAILED(hr) ? hr : E_FAIL;
    }

    hr = activates[0]->ActivateObject(IID_PPV_ARGS(&g_ring.encoder));
    for (UINT32 i = 0; i < count; ++i) activates[i]->Release();
    CoTaskMemFree(activates);
    if (FAILED(hr))
    {
        DebugLog("Ring: ActivateObject failed (0x%08X)", (unsigned)hr);
        return hr;
    }

    DWORD inIds[1] = {0};
    DWORD outIds[1] = {0};
    hr = g_ring.encoder->GetStreamIDs(1, inIds, 1, outIds);
    if (hr == E_NOTIMPL) { g_ring.inputStreamId = 0; g_ring.outputStreamId = 0; }
    else if (FAILED(hr)) return hr;
    else { g_ring.inputStreamId = inIds[0]; g_ring.outputStreamId = outIds[0]; }

    // Output type first (required ordering for most encoder MFTs)
    ComPtr<IMFMediaType> outMT;
    hr = MFCreateMediaType(&outMT);
    if (FAILED(hr)) return hr;
    outMT->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    outMT->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264);
    outMT->SetUINT32(MF_MT_AVG_BITRATE, (UINT32)g_ring.bitrate);
    MFSetAttributeSize(outMT.Get(), MF_MT_FRAME_SIZE,
                       (UINT32)g_ring.width, (UINT32)g_ring.height);
    MFSetAttributeRatio(outMT.Get(), MF_MT_FRAME_RATE, (UINT32)g_ring.fps, 1);
    MFSetAttributeRatio(outMT.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
    outMT->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    outMT->SetUINT32(MF_MT_MPEG2_PROFILE, eAVEncH264VProfile_Main);

    hr = g_ring.encoder->SetOutputType(g_ring.outputStreamId, outMT.Get(), 0);
    if (FAILED(hr))
    {
        DebugLog("Ring: SetOutputType failed (0x%08X)", (unsigned)hr);
        return hr;
    }

    ComPtr<IMFMediaType> inMT;
    hr = MFCreateMediaType(&inMT);
    if (FAILED(hr)) return hr;
    inMT->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    inMT->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12);
    MFSetAttributeSize(inMT.Get(), MF_MT_FRAME_SIZE,
                       (UINT32)g_ring.width, (UINT32)g_ring.height);
    MFSetAttributeRatio(inMT.Get(), MF_MT_FRAME_RATE, (UINT32)g_ring.fps, 1);
    MFSetAttributeRatio(inMT.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
    inMT->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);

    hr = g_ring.encoder->SetInputType(g_ring.inputStreamId, inMT.Get(), 0);
    if (FAILED(hr))
    {
        DebugLog("Ring: SetInputType failed (0x%08X)", (unsigned)hr);
        return hr;
    }

    hr = g_ring.encoder->GetOutputStreamInfo(g_ring.outputStreamId, &g_ring.outputInfo);
    if (FAILED(hr)) return hr;
    g_ring.providesOutputSamples =
        (g_ring.outputInfo.dwFlags & (MFT_OUTPUT_STREAM_PROVIDES_SAMPLES |
                                      MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES)) != 0;

    g_ring.encoder->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0);
    g_ring.encoder->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);

    g_ring.nv12Scratch.assign((size_t)g_ring.width * g_ring.height * 3 / 2, 0);

    DebugLog("Ring: encoder initialized (%dx%d @ %d fps, %d bps, %ds buffer, providesSamples=%d)",
             g_ring.width, g_ring.height, g_ring.fps, g_ring.bitrate,
             g_ring.ringSeconds, g_ring.providesOutputSamples ? 1 : 0);
    return S_OK;
}

static void RingShutdownEncoder()
{
    if (g_ring.encoder)
    {
        g_ring.encoder->ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM, 0);
        g_ring.encoder->ProcessMessage(MFT_MESSAGE_NOTIFY_END_STREAMING, 0);
    }
    g_ring.encoder.Reset();
    g_ring.encodedOutputType.Reset();
    g_ring.nv12Scratch.clear();
    g_ring.nv12Scratch.shrink_to_fit();
    g_ring.packets.clear();
    g_ring.frameCount = 0;
    g_ring.framesSinceKey = 0;
    g_ring.totalDuration100ns = 0;
}

static HRESULT RingFeedBGRA(const uint8_t* src, int srcStride, bool isBGRA)
{
    // Caller holds g_ring.mutex.
    if (!g_ring.encoder) return E_FAIL;

    BGRAToNV12(src, srcStride, isBGRA, g_ring.width, g_ring.height,
               g_ring.nv12Scratch.data());

    ComPtr<IMFMediaBuffer> buf;
    DWORD nv12Size = (DWORD)g_ring.nv12Scratch.size();
    HRESULT hr = MFCreateMemoryBuffer(nv12Size, &buf);
    if (FAILED(hr)) return hr;

    BYTE* dst = nullptr;
    hr = buf->Lock(&dst, nullptr, nullptr);
    if (FAILED(hr)) return hr;
    memcpy(dst, g_ring.nv12Scratch.data(), nv12Size);
    buf->Unlock();
    buf->SetCurrentLength(nv12Size);

    ComPtr<IMFSample> sample;
    hr = MFCreateSample(&sample);
    if (FAILED(hr)) return hr;
    sample->AddBuffer(buf.Get());

    const int64_t pts = g_ring.frameCount * 10000000LL / g_ring.fps;
    const int64_t dur = 10000000LL / g_ring.fps;
    sample->SetSampleTime(pts);
    sample->SetSampleDuration(dur);
    g_ring.frameCount++;

    bool forceKey = (g_ring.framesSinceKey >= g_ring.keyframeIntervalFrames) ||
                    (g_ring.frameCount == 1);
    if (forceKey)
    {
        sample->SetUINT32(MFSampleExtension_CleanPoint, 1);
        g_ring.framesSinceKey = 0;
    }
    g_ring.framesSinceKey++;

    hr = g_ring.encoder->ProcessInput(g_ring.inputStreamId, sample.Get(), 0);
    if (FAILED(hr))
    {
        DebugLog("Ring: ProcessInput failed (0x%08X)", (unsigned)hr);
        return hr;
    }

    return RingDrainEncoder();
}

static HRESULT RingDrainEncoder()
{
    while (true)
    {
        MFT_OUTPUT_DATA_BUFFER outBuf = {};
        DWORD status = 0;

        ComPtr<IMFSample> outSample;
        if (!g_ring.providesOutputSamples)
        {
            HRESULT hr = MFCreateSample(&outSample);
            if (FAILED(hr)) return hr;
            DWORD sz = g_ring.outputInfo.cbSize > 0
                ? g_ring.outputInfo.cbSize
                : (DWORD)(g_ring.width * g_ring.height * 2);
            ComPtr<IMFMediaBuffer> mb;
            hr = MFCreateMemoryBuffer(sz, &mb);
            if (FAILED(hr)) return hr;
            outSample->AddBuffer(mb.Get());
            outBuf.pSample = outSample.Get();
        }

        HRESULT hr = g_ring.encoder->ProcessOutput(g_ring.outputStreamId, 1, &outBuf, &status);
        if (hr == MF_E_TRANSFORM_NEED_MORE_INPUT)
        {
            if (outBuf.pEvents) outBuf.pEvents->Release();
            break;
        }
        if (hr == MF_E_TRANSFORM_STREAM_CHANGE)
        {
            // Re-negotiate output type, retry
            ComPtr<IMFMediaType> newType;
            HRESULT thr = g_ring.encoder->GetOutputAvailableType(g_ring.outputStreamId, 0, &newType);
            if (SUCCEEDED(thr))
            {
                g_ring.encoder->SetOutputType(g_ring.outputStreamId, newType.Get(), 0);
                g_ring.encodedOutputType = newType;
            }
            if (outBuf.pEvents) outBuf.pEvents->Release();
            continue;
        }
        if (FAILED(hr))
        {
            DebugLog("Ring: ProcessOutput failed (0x%08X)", (unsigned)hr);
            if (outBuf.pEvents) outBuf.pEvents->Release();
            return hr;
        }

        IMFSample* samp = outBuf.pSample;
        if (samp)
        {
            if (!g_ring.encodedOutputType)
            {
                ComPtr<IMFMediaType> ot;
                if (SUCCEEDED(g_ring.encoder->GetOutputCurrentType(g_ring.outputStreamId, &ot)))
                    g_ring.encodedOutputType = ot;
            }

            ComPtr<IMFMediaBuffer> mb;
            if (SUCCEEDED(samp->ConvertToContiguousBuffer(&mb)))
            {
                BYTE* p = nullptr;
                DWORD len = 0;
                if (SUCCEEDED(mb->Lock(&p, nullptr, &len)))
                {
                    RingPacket pkt;
                    pkt.bytes.assign(p, p + len);
                    int64_t st = 0, dur = 0;
                    samp->GetSampleTime(&st);
                    samp->GetSampleDuration(&dur);
                    pkt.time = st;
                    pkt.duration = dur > 0 ? dur : (10000000LL / g_ring.fps);
                    UINT32 kp = 0;
                    if (FAILED(samp->GetUINT32(MFSampleExtension_CleanPoint, &kp))) kp = 0;
                    pkt.isKeyframe = (kp != 0);
                    g_ring.totalDuration100ns += pkt.duration;
                    g_ring.packets.push_back(std::move(pkt));
                    mb->Unlock();
                }
            }
        }

        if (outBuf.pEvents) outBuf.pEvents->Release();
        if (g_ring.providesOutputSamples && outBuf.pSample)
            outBuf.pSample->Release();

        RingTrim();
    }
    return S_OK;
}

static void RingTrim()
{
    const int64_t maxDuration = (int64_t)g_ring.ringSeconds * 10000000LL;
    while (g_ring.packets.size() > 1 && g_ring.totalDuration100ns > maxDuration)
    {
        size_t firstKey = 0;
        for (size_t i = 1; i < g_ring.packets.size(); ++i)
        {
            if (g_ring.packets[i].isKeyframe) { firstKey = i; break; }
        }
        if (firstKey == 0) break; // no future keyframe → can't trim without breaking decode
        for (size_t i = 0; i < firstKey; ++i)
        {
            g_ring.totalDuration100ns -= g_ring.packets.front().duration;
            g_ring.packets.pop_front();
        }
    }
}

static HRESULT RingMuxToFileLocked(const std::wstring& outPath)
{
    if (g_ring.packets.empty())
    {
        DebugLog("Ring mux: no packets to write");
        return E_FAIL;
    }
    if (!g_ring.encodedOutputType)
    {
        DebugLog("Ring mux: encoder output type not yet captured");
        return E_FAIL;
    }

    ComPtr<IMFAttributes> attrs;
    MFCreateAttributes(&attrs, 1);
    attrs->SetUINT32(MF_LOW_LATENCY, TRUE);

    ComPtr<IMFSinkWriter> writer;
    HRESULT hr = MFCreateSinkWriterFromURL(outPath.c_str(), nullptr, attrs.Get(), &writer);
    if (FAILED(hr))
    {
        DebugLog("Ring mux: MFCreateSinkWriterFromURL failed (0x%08X)", (unsigned)hr);
        return hr;
    }

    DWORD streamIdx = 0;
    hr = writer->AddStream(g_ring.encodedOutputType.Get(), &streamIdx);
    if (FAILED(hr))
    {
        DebugLog("Ring mux: AddStream failed (0x%08X)", (unsigned)hr);
        return hr;
    }
    // Passthrough: tell the sink writer the input is already encoded H.264.
    hr = writer->SetInputMediaType(streamIdx, g_ring.encodedOutputType.Get(), nullptr);
    if (FAILED(hr))
    {
        DebugLog("Ring mux: SetInputMediaType (passthrough) failed (0x%08X)", (unsigned)hr);
        return hr;
    }
    hr = writer->BeginWriting();
    if (FAILED(hr))
    {
        DebugLog("Ring mux: BeginWriting failed (0x%08X)", (unsigned)hr);
        return hr;
    }

    const int64_t baseTime = g_ring.packets.front().time;
    for (const auto& pkt : g_ring.packets)
    {
        ComPtr<IMFMediaBuffer> mb;
        if (FAILED(MFCreateMemoryBuffer((DWORD)pkt.bytes.size(), &mb))) continue;
        BYTE* dst = nullptr;
        if (FAILED(mb->Lock(&dst, nullptr, nullptr))) continue;
        memcpy(dst, pkt.bytes.data(), pkt.bytes.size());
        mb->Unlock();
        mb->SetCurrentLength((DWORD)pkt.bytes.size());

        ComPtr<IMFSample> samp;
        MFCreateSample(&samp);
        samp->AddBuffer(mb.Get());
        samp->SetSampleTime(pkt.time - baseTime);
        samp->SetSampleDuration(pkt.duration);
        if (pkt.isKeyframe) samp->SetUINT32(MFSampleExtension_CleanPoint, 1);

        HRESULT whr = writer->WriteSample(streamIdx, samp.Get());
        if (FAILED(whr))
            DebugLog("Ring mux: WriteSample failed (0x%08X)", (unsigned)whr);
    }

    hr = writer->Finalize();
    if (FAILED(hr))
        DebugLog("Ring mux: Finalize failed (0x%08X)", (unsigned)hr);
    return hr;
}

static void CaptureBackbufferForRing()
{
    // Called on the render thread when ring mode is active.
    if (!g_state.d3dDevice || !g_state.d3dContext) return;
    if (!g_ring.active.load()) return;

    ComPtr<ID3D11RenderTargetView> rtv;
    g_state.d3dContext->OMGetRenderTargets(1, &rtv, nullptr);
    if (!rtv) return;
    ComPtr<ID3D11Resource> rtResource;
    rtv->GetResource(&rtResource);
    if (!rtResource) return;
    ComPtr<ID3D11Texture2D> backbuffer;
    if (FAILED(rtResource.As(&backbuffer))) return;

    D3D11_TEXTURE2D_DESC bbDesc;
    backbuffer->GetDesc(&bbDesc);
    if (bbDesc.SampleDesc.Count > 1) return; // skip MSAA frames

    if ((int)bbDesc.Width < g_ring.width || (int)bbDesc.Height < g_ring.height)
        return; // backbuffer smaller than ring target; skip

    if (!g_state.stagingTexture)
    {
        D3D11_TEXTURE2D_DESC sd = {};
        sd.Width = bbDesc.Width;
        sd.Height = bbDesc.Height;
        sd.MipLevels = 1;
        sd.ArraySize = 1;
        sd.Format = bbDesc.Format;
        sd.SampleDesc.Count = 1;
        sd.Usage = D3D11_USAGE_STAGING;
        sd.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        if (FAILED(g_state.d3dDevice->CreateTexture2D(&sd, nullptr, &g_state.stagingTexture)))
            return;
    }

    g_state.d3dContext->CopyResource(g_state.stagingTexture.Get(), backbuffer.Get());

    D3D11_MAPPED_SUBRESOURCE mapped;
    HRESULT hr = g_state.d3dContext->Map(g_state.stagingTexture.Get(), 0,
                                          D3D11_MAP_READ, 0, &mapped);
    if (FAILED(hr)) return;

    const bool isBGRA = (bbDesc.Format == DXGI_FORMAT_B8G8R8A8_UNORM ||
                         bbDesc.Format == DXGI_FORMAT_B8G8R8A8_TYPELESS);

    {
        std::lock_guard<std::mutex> lock(g_ring.mutex);
        if (g_ring.active.load())
            RingFeedBGRA((const uint8_t*)mapped.pData, (int)mapped.RowPitch, isBGRA);
    }

    g_state.d3dContext->Unmap(g_state.stagingTexture.Get(), 0);
}

// ============================================================================
// Exported API: Start
// ============================================================================

static void StartRecordingInternal(const char* outputPath, int width, int height,
                                   int fps, int bitrate, bool includeAudio,
                                   bool nativeCapture)
{
    if (g_state.isRecording.load())
    {
        DebugLog("Already recording, call Stop first");
        return;
    }

    if (!outputPath || width <= 0 || height <= 0 || fps <= 0 || bitrate <= 0)
    {
        DebugLog("Invalid parameters: path=%s w=%d h=%d fps=%d bitrate=%d",
                 outputPath ? outputPath : "(null)", width, height, fps, bitrate);
        return;
    }

    DebugLog("Starting recording: %s (%dx%d @ %d fps, %d bps, audio=%s, nativeCapture=%s)",
             outputPath, width, height, fps, bitrate,
             includeAudio ? "yes" : "no",
             nativeCapture ? "yes" : "no");

    // Initialize COM
    HRESULT hr = CoInitializeEx(NULL, COINIT_MULTITHREADED);
    if (hr == S_OK || hr == S_FALSE)
    {
        g_state.comInitialized = true;
    }
    else if (hr == RPC_E_CHANGED_MODE)
    {
        // COM already initialized with a different threading model. This is fine
        // for MF operations; we just won't call CoUninitialize.
        DebugLog("COM already initialized (different mode), proceeding");
        g_state.comInitialized = false;
    }
    else
    {
        DebugLog("CoInitializeEx failed (0x%08X)", (unsigned int)hr);
        return;
    }

    // Initialize Media Foundation
    hr = MFStartup(MF_VERSION);
    CHECK_HR(hr, "MFStartup");
    g_state.mfStarted = true;

    // Store parameters
    g_state.width         = width;
    g_state.height        = height;
    g_state.fps           = fps;
    g_state.bitrate       = bitrate;
    g_state.includeAudio  = includeAudio;
    g_state.nativeCapture = nativeCapture;
    g_state.outputPath    = outputPath;
    g_state.videoFrameCount   = 0;
    g_state.audioSampleOffset = 0;

    // Convert output path to wide string for MF
    int wideLen = MultiByteToWideChar(CP_UTF8, 0, outputPath, -1, NULL, 0);
    if (wideLen <= 0)
    {
        DebugLog("Failed to convert output path to wide string");
        CleanupRecorder();
        return;
    }

    std::wstring widePath(wideLen, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, outputPath, -1, &widePath[0], wideLen);

    // Create sink writer attributes for hardware acceleration
    ComPtr<IMFAttributes> writerAttributes;
    hr = MFCreateAttributes(&writerAttributes, 3);
    if (SUCCEEDED(hr))
    {
        // Enable hardware MFT (Media Foundation Transform) for H.264 encoding
        hr = writerAttributes->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, TRUE);
        if (FAILED(hr))
        {
            DebugLog("Warning: Could not enable hardware transforms (0x%08X)", (unsigned int)hr);
            // Non-fatal, continue with software encoding
        }

        // Set low-latency mode
        hr = writerAttributes->SetUINT32(MF_LOW_LATENCY, TRUE);
        if (FAILED(hr))
        {
            DebugLog("Warning: Could not set low latency mode (0x%08X)", (unsigned int)hr);
        }

        if (nativeCapture && g_state.d3dDevice)
        {
            hr = MFCreateDXGIDeviceManager(&g_state.dxgiResetToken, &g_state.dxgiDeviceManager);
            if (SUCCEEDED(hr))
                hr = g_state.dxgiDeviceManager->ResetDevice(g_state.d3dDevice, g_state.dxgiResetToken);
            if (SUCCEEDED(hr))
            {
                hr = writerAttributes->SetUnknown(MF_SINK_WRITER_D3D_MANAGER, g_state.dxgiDeviceManager.Get());
                if (SUCCEEDED(hr))
                {
                    g_state.gpuSurfaceInput = true;
                    DebugLog("D3D manager attached to sink writer; native capture will use GPU texture samples");
                }
            }
            if (FAILED(hr))
            {
                g_state.dxgiDeviceManager.Reset();
                g_state.dxgiResetToken = 0;
                g_state.gpuSurfaceInput = false;
                DebugLog("Warning: D3D manager setup failed (0x%08X); falling back to CPU readback", (unsigned int)hr);
            }
        }
    }

    // Create sink writer
    hr = MFCreateSinkWriterFromURL(widePath.c_str(), NULL, writerAttributes.Get(),
                                   &g_state.sinkWriter);
    if (FAILED(hr))
    {
        DebugLog("MFCreateSinkWriterFromURL failed (0x%08X). Path: %s", (unsigned int)hr, outputPath);
        CleanupRecorder();
        return;
    }

    // Configure video output stream (H.264)
    hr = ConfigureVideoOutput(g_state.sinkWriter.Get(), &g_state.videoStreamIndex,
                              width, height, fps, bitrate);
    if (FAILED(hr))
    {
        DebugLog("ConfigureVideoOutput failed (0x%08X)", (unsigned int)hr);
        CleanupRecorder();
        return;
    }

    // Configure video input type (RGB32/BGRA)
    hr = ConfigureVideoInput(g_state.sinkWriter.Get(), g_state.videoStreamIndex,
                             width, height, fps);
    if (FAILED(hr))
    {
        DebugLog("ConfigureVideoInput failed (0x%08X)", (unsigned int)hr);
        CleanupRecorder();
        return;
    }

    // Configure audio streams if requested
    if (includeAudio)
    {
        int audioSampleRate = 48000;
        int audioChannels   = 2;
        int audioBitrate    = 192000; // 192 kbps AAC

        hr = ConfigureAudioOutput(g_state.sinkWriter.Get(), &g_state.audioStreamIndex,
                                  audioSampleRate, audioChannels, audioBitrate);
        if (FAILED(hr))
        {
            DebugLog("ConfigureAudioOutput failed (0x%08X)", (unsigned int)hr);
            CleanupRecorder();
            return;
        }

        hr = ConfigureAudioInput(g_state.sinkWriter.Get(), g_state.audioStreamIndex,
                                 audioSampleRate, audioChannels);
        if (FAILED(hr))
        {
            DebugLog("ConfigureAudioInput failed (0x%08X)", (unsigned int)hr);
            CleanupRecorder();
            return;
        }

        g_state.audioSampleRate = audioSampleRate;
        g_state.audioChannels   = audioChannels;
    }

    // Begin writing
    hr = g_state.sinkWriter->BeginWriting();
    if (FAILED(hr))
    {
        DebugLog("BeginWriting failed (0x%08X)", (unsigned int)hr);
        CleanupRecorder();
        return;
    }

    g_state.isRecording.store(true);
    DebugLog("Recording started successfully. Video stream=%u, Audio stream=%u",
             g_state.videoStreamIndex, g_state.audioStreamIndex);
}

extern "C" UNITY_INTERFACE_EXPORT void ODDRecorder_Start(
    const char* outputPath, int width, int height, int fps, int bitrate, bool includeAudio)
{
    StartRecordingInternal(outputPath, width, height, fps, bitrate, includeAudio, false);
}

extern "C" UNITY_INTERFACE_EXPORT void ODDRecorder_StartWithCapture(
    const char* outputPath, int width, int height, int fps, int bitrate,
    bool includeAudio, bool nativeCapture)
{
    StartRecordingInternal(outputPath, width, height, fps, bitrate, includeAudio, nativeCapture);

    if (nativeCapture && g_state.isRecording.load())
    {
        DebugLog("Native capture mode enabled. D3D11 device will be acquired on render thread.");
        // NOTE: The D3D11 device pointer must be set before native capture can work.
        // In a full implementation, the C# side should call a SetD3D11Device function
        // or we retrieve it via IUnityGraphicsD3D11 in UnityPluginLoad.
        // For now, the device is set to nullptr and will be populated on the first
        // render event if IUnityGraphicsD3D11 was captured during plugin load.
        //
        // If using the C# pass-through path (nativeCapture=false), this is not needed.
    }
}

// ============================================================================
// Exported API: Stop
// ============================================================================

extern "C" UNITY_INTERFACE_EXPORT void ODDRecorder_Stop(char* outPath, int outPathLen)
{
    if (!g_state.isRecording.load())
    {
        DebugLog("Not recording, nothing to stop");
        if (outPath && outPathLen > 0)
            outPath[0] = '\0';
        return;
    }

    DebugLog("Stopping recording (%lld video frames written)", g_state.videoFrameCount);

    // Prevent further writes
    g_state.isRecording.store(false);

    // Finalize under lock
    {
        std::lock_guard<std::mutex> lock(g_state.writerMutex);
        if (g_state.sinkWriter)
        {
            HRESULT hr = g_state.sinkWriter->Finalize();
            if (FAILED(hr))
            {
                DebugLog("SinkWriter Finalize failed (0x%08X)", (unsigned int)hr);
            }
            else
            {
                DebugLog("SinkWriter finalized successfully");
            }
        }
    }

    // Copy output path to caller's buffer
    if (outPath && outPathLen > 0)
    {
        size_t copyLen = (std::min)(g_state.outputPath.size(), static_cast<size_t>(outPathLen - 1));
        memcpy(outPath, g_state.outputPath.c_str(), copyLen);
        outPath[copyLen] = '\0';
    }

    std::string savedPath = g_state.outputPath;
    CleanupRecorder();
    DebugLog("Recording stopped. Output: %s", savedPath.c_str());
}

// ============================================================================
// Exported API: IsRecording
// ============================================================================

extern "C" UNITY_INTERFACE_EXPORT bool ODDRecorder_IsRecording()
{
    return g_state.isRecording.load();
}

// ============================================================================
// Exported API: AppendVideoFrame
// ============================================================================

extern "C" UNITY_INTERFACE_EXPORT void ODDRecorder_AppendVideoFrame(
    const uint8_t* rgbaData, int dataLength)
{
    if (g_ring.active.load())
    {
        if (!rgbaData || dataLength <= 0) return;
        const int expectedRing = g_ring.width * g_ring.height * 4;
        if (dataLength != expectedRing)
        {
            DebugLog("Ring AppendVideoFrame: length mismatch (got %d, expected %d)",
                     dataLength, expectedRing);
            return;
        }
        std::lock_guard<std::mutex> lock(g_ring.mutex);
        if (g_ring.active.load())
            RingFeedBGRA(rgbaData, g_ring.width * 4, /*isBGRA=*/false);
        return;
    }

    if (!g_state.isRecording.load())
        return;

    if (!rgbaData || dataLength <= 0)
    {
        DebugLog("AppendVideoFrame: null data or invalid length (%d)", dataLength);
        return;
    }

    const int expectedLength = g_state.width * g_state.height * 4;
    if (dataLength != expectedLength)
    {
        DebugLog("AppendVideoFrame: data length mismatch (got %d, expected %d)",
                 dataLength, expectedLength);
        return;
    }

    // Create MF memory buffer
    ComPtr<IMFMediaBuffer> mfBuffer;
    HRESULT hr = MFCreateMemoryBuffer(static_cast<DWORD>(dataLength), &mfBuffer);
    CHECK_HR(hr, "MFCreateMemoryBuffer for video frame");

    // Lock buffer and copy pixel data with RGBA -> BGRA conversion
    BYTE* destData = nullptr;
    DWORD maxLen = 0;
    hr = mfBuffer->Lock(&destData, &maxLen, nullptr);
    CHECK_HR(hr, "Lock video buffer");

    // Unity gives us RGBA (R=byte0, G=byte1, B=byte2, A=byte3).
    // Media Foundation MFVideoFormat_RGB32 expects BGRA (B=byte0, G=byte1, R=byte2, A=byte3).
    // Swap R and B channels.
    //
    // Unity's readback data may be bottom-up (flipped). The caller (C# side) is
    // responsible for flipping if needed before passing to this function. The encoder
    // expects top-down scanline order matching the positive stride we configured.
    const int pixelCount = g_state.width * g_state.height;
    for (int i = 0; i < pixelCount; ++i)
    {
        const int offset = i * 4;
        destData[offset + 0] = rgbaData[offset + 2]; // B <- R
        destData[offset + 1] = rgbaData[offset + 1]; // G <- G
        destData[offset + 2] = rgbaData[offset + 0]; // R <- B
        destData[offset + 3] = rgbaData[offset + 3]; // A <- A
    }

    hr = mfBuffer->Unlock();
    CHECK_HR(hr, "Unlock video buffer");

    hr = mfBuffer->SetCurrentLength(static_cast<DWORD>(dataLength));
    CHECK_HR(hr, "SetCurrentLength for video buffer");

    // Create sample
    ComPtr<IMFSample> sample;
    hr = MFCreateSample(&sample);
    CHECK_HR(hr, "MFCreateSample for video frame");

    hr = sample->AddBuffer(mfBuffer.Get());
    CHECK_HR(hr, "AddBuffer for video sample");

    // Timestamps in 100-nanosecond units (Media Foundation standard)
    int64_t sampleTime = g_state.videoFrameCount * 10000000LL / g_state.fps;
    int64_t duration   = 10000000LL / g_state.fps;

    hr = sample->SetSampleTime(sampleTime);
    CHECK_HR(hr, "SetSampleTime for video sample");

    hr = sample->SetSampleDuration(duration);
    CHECK_HR(hr, "SetSampleDuration for video sample");

    // Write to sink (mutex protected)
    {
        std::lock_guard<std::mutex> lock(g_state.writerMutex);
        if (g_state.sinkWriter && g_state.isRecording.load())
        {
            hr = g_state.sinkWriter->WriteSample(g_state.videoStreamIndex, sample.Get());
            if (FAILED(hr))
            {
                DebugLog("WriteSample (video) failed (0x%08X) at frame %lld",
                         (unsigned int)hr, g_state.videoFrameCount);
                return;
            }
            g_state.videoFrameCount++;
        }
    }
}

static void AcquireUnityD3D11Device()
{
    if (!g_unityGraphics)
        return;

    UnityGfxRenderer renderer = g_unityGraphics->GetRenderer();
    if (renderer != kUnityGfxRendererD3D11)
    {
        DebugLog("Unity renderer is not D3D11 (%d); native GPU capture disabled", (int)renderer);
        return;
    }

    if (!g_unityD3D11)
    {
        DebugLog("IUnityGraphicsD3D11 unavailable despite D3D11 renderer");
        return;
    }

    ID3D11Device* device = g_unityD3D11->GetDevice();
    if (!device)
    {
        DebugLog("IUnityGraphicsD3D11::GetDevice returned null");
        return;
    }

    g_state.d3dDevice = device;
    g_state.d3dDevice->GetImmediateContext(&g_state.d3dContext);
    DebugLog("Acquired Unity D3D11 device (0x%p), context (0x%p)",
             g_state.d3dDevice, g_state.d3dContext);
}

static bool WriteTextureSampleD3D11(ID3D11Texture2D* texture)
{
    if (!texture || !g_state.sinkWriter)
        return false;

    ComPtr<IMFMediaBuffer> buffer;
    HRESULT hr = MFCreateDXGISurfaceBuffer(__uuidof(ID3D11Texture2D), texture, 0, FALSE, &buffer);
    if (FAILED(hr))
    {
        DebugLog("Native GPU capture: MFCreateDXGISurfaceBuffer failed (0x%08X)", (unsigned int)hr);
        return false;
    }

    ComPtr<IMFSample> sample;
    hr = MFCreateSample(&sample);
    if (FAILED(hr))
    {
        DebugLog("Native GPU capture: MFCreateSample failed (0x%08X)", (unsigned int)hr);
        return false;
    }

    hr = sample->AddBuffer(buffer.Get());
    if (FAILED(hr))
    {
        DebugLog("Native GPU capture: AddBuffer failed (0x%08X)", (unsigned int)hr);
        return false;
    }

    int64_t sampleTime = g_state.videoFrameCount * 10000000LL / g_state.fps;
    int64_t duration   = 10000000LL / g_state.fps;

    hr = sample->SetSampleTime(sampleTime);
    if (FAILED(hr))
    {
        DebugLog("Native GPU capture: SetSampleTime failed (0x%08X)", (unsigned int)hr);
        return false;
    }

    hr = sample->SetSampleDuration(duration);
    if (FAILED(hr))
    {
        DebugLog("Native GPU capture: SetSampleDuration failed (0x%08X)", (unsigned int)hr);
        return false;
    }

    {
        std::lock_guard<std::mutex> lock(g_state.writerMutex);
        if (!g_state.sinkWriter || !g_state.isRecording.load())
            return false;

        hr = g_state.sinkWriter->WriteSample(g_state.videoStreamIndex, sample.Get());
        if (FAILED(hr))
        {
            DebugLog("Native GPU capture: WriteSample failed (0x%08X) at frame %lld",
                     (unsigned int)hr, g_state.videoFrameCount);
            return false;
        }
        g_state.videoFrameCount++;
    }

    return true;
}

// ============================================================================
// Exported API: AppendAudioSamples
// ============================================================================

extern "C" UNITY_INTERFACE_EXPORT void ODDRecorder_AppendAudioSamples(
    const float* pcmData, int sampleCount, int channels, int sampleRate)
{
    if (!g_state.isRecording.load())
        return;

    if (!g_state.includeAudio)
    {
        DebugLog("AppendAudioSamples: audio not enabled for this recording");
        return;
    }

    if (!pcmData || sampleCount <= 0 || channels <= 0 || sampleRate <= 0)
    {
        DebugLog("AppendAudioSamples: invalid parameters (samples=%d, ch=%d, rate=%d)",
                 sampleCount, channels, sampleRate);
        return;
    }

    // Convert float PCM [-1.0, 1.0] to 16-bit signed PCM
    const int totalSamples = sampleCount * channels;
    const int bufferSize   = totalSamples * sizeof(int16_t);

    ComPtr<IMFMediaBuffer> mfBuffer;
    HRESULT hr = MFCreateMemoryBuffer(static_cast<DWORD>(bufferSize), &mfBuffer);
    CHECK_HR(hr, "MFCreateMemoryBuffer for audio");

    BYTE* destData = nullptr;
    DWORD maxLen = 0;
    hr = mfBuffer->Lock(&destData, &maxLen, nullptr);
    CHECK_HR(hr, "Lock audio buffer");

    int16_t* destPCM = reinterpret_cast<int16_t*>(destData);
    for (int i = 0; i < totalSamples; ++i)
    {
        // Clamp to [-1, 1] then scale to int16 range
        float sample = pcmData[i];
        if (sample > 1.0f) sample = 1.0f;
        if (sample < -1.0f) sample = -1.0f;
        destPCM[i] = static_cast<int16_t>(sample * 32767.0f);
    }

    hr = mfBuffer->Unlock();
    CHECK_HR(hr, "Unlock audio buffer");

    hr = mfBuffer->SetCurrentLength(static_cast<DWORD>(bufferSize));
    CHECK_HR(hr, "SetCurrentLength for audio buffer");

    // Create sample
    ComPtr<IMFSample> sample;
    hr = MFCreateSample(&sample);
    CHECK_HR(hr, "MFCreateSample for audio");

    hr = sample->AddBuffer(mfBuffer.Get());
    CHECK_HR(hr, "AddBuffer for audio sample");

    // Audio timestamp: based on cumulative sample count
    // Each sample represents 1/sampleRate seconds = 10000000/sampleRate in 100ns units
    int64_t sampleTime = g_state.audioSampleOffset * 10000000LL / sampleRate;
    int64_t duration   = static_cast<int64_t>(sampleCount) * 10000000LL / sampleRate;

    hr = sample->SetSampleTime(sampleTime);
    CHECK_HR(hr, "SetSampleTime for audio sample");

    hr = sample->SetSampleDuration(duration);
    CHECK_HR(hr, "SetSampleDuration for audio sample");

    // Write to sink (mutex protected)
    {
        std::lock_guard<std::mutex> lock(g_state.writerMutex);
        if (g_state.sinkWriter && g_state.isRecording.load())
        {
            hr = g_state.sinkWriter->WriteSample(g_state.audioStreamIndex, sample.Get());
            if (FAILED(hr))
            {
                DebugLog("WriteSample (audio) failed (0x%08X) at sample offset %lld",
                         (unsigned int)hr, g_state.audioSampleOffset);
                return;
            }
            g_state.audioSampleOffset += sampleCount;
        }
    }
}

// ============================================================================
// Optional: Set D3D11 device from C# (for native capture without IUnityGraphics)
// This allows C# to pass the device pointer obtained via SystemInfo or reflection.
// ============================================================================

extern "C" UNITY_INTERFACE_EXPORT void ODDRecorder_SetD3D11Device(void* devicePtr)
{
    if (!devicePtr)
    {
        DebugLog("SetD3D11Device: null device pointer");
        return;
    }

    g_state.d3dDevice = static_cast<ID3D11Device*>(devicePtr);
    g_state.d3dDevice->GetImmediateContext(&g_state.d3dContext);

    DebugLog("D3D11 device set (0x%p), context (0x%p)",
             g_state.d3dDevice, g_state.d3dContext);
}

// ============================================================================
// Exported API: In-memory video ring
// ============================================================================

extern "C" UNITY_INTERFACE_EXPORT void ODDRecorder_RingStart(
    int width, int height, int fps, int bitrate, int ringSeconds)
{
    if (g_ring.active.load())
    {
        DebugLog("Ring: already active");
        return;
    }
    if (g_state.isRecording.load())
    {
        DebugLog("Ring: file recording active; call Stop first");
        return;
    }
    if (width <= 0 || height <= 0 || fps <= 0 || bitrate <= 0 || ringSeconds <= 0)
    {
        DebugLog("Ring: invalid params (w=%d h=%d fps=%d bps=%d secs=%d)",
                 width, height, fps, bitrate, ringSeconds);
        return;
    }
    if ((width & 1) || (height & 1))
    {
        DebugLog("Ring: width/height must be even (got %dx%d)", width, height);
        return;
    }

    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (hr == S_OK || hr == S_FALSE) g_state.comInitialized = true;
    else if (hr != RPC_E_CHANGED_MODE)
    {
        DebugLog("Ring: CoInitializeEx failed (0x%08X)", (unsigned)hr);
        return;
    }
    if (!g_state.mfStarted)
    {
        hr = MFStartup(MF_VERSION);
        if (FAILED(hr))
        {
            DebugLog("Ring: MFStartup failed (0x%08X)", (unsigned)hr);
            return;
        }
        g_state.mfStarted = true;
    }

    std::lock_guard<std::mutex> lock(g_ring.mutex);
    g_ring.width = width;
    g_ring.height = height;
    g_ring.fps = fps;
    g_ring.bitrate = bitrate;
    g_ring.ringSeconds = ringSeconds;
    g_ring.keyframeIntervalFrames = (fps * 2) > 1 ? (fps * 2) : 1;
    g_ring.frameCount = 0;
    g_ring.framesSinceKey = 0;
    g_ring.totalDuration100ns = 0;
    g_ring.packets.clear();

    hr = RingInitEncoder();
    if (FAILED(hr))
    {
        RingShutdownEncoder();
        DebugLog("Ring: init failed");
        return;
    }
    g_ring.active.store(true);
}

extern "C" UNITY_INTERFACE_EXPORT bool ODDRecorder_RingDump(
    const char* outPath, char* outPathBuf, int outPathBufLen)
{
    if (!outPath) return false;
    if (!g_ring.active.load())
    {
        DebugLog("Ring: dump called but ring not active");
        return false;
    }

    int wideLen = MultiByteToWideChar(CP_UTF8, 0, outPath, -1, nullptr, 0);
    if (wideLen <= 0) return false;
    std::wstring widePath(wideLen, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, outPath, -1, &widePath[0], wideLen);
    while (!widePath.empty() && widePath.back() == L'\0') widePath.pop_back();

    HRESULT hr;
    {
        std::lock_guard<std::mutex> lock(g_ring.mutex);
        hr = RingMuxToFileLocked(widePath);
    }
    if (FAILED(hr)) return false;

    if (outPathBuf && outPathBufLen > 0)
    {
        size_t srcLen = strlen(outPath);
        size_t cl = srcLen < (size_t)(outPathBufLen - 1) ? srcLen : (size_t)(outPathBufLen - 1);
        memcpy(outPathBuf, outPath, cl);
        outPathBuf[cl] = '\0';
    }
    return true;
}

extern "C" UNITY_INTERFACE_EXPORT void ODDRecorder_RingStop()
{
    if (!g_ring.active.load()) return;
    g_ring.active.store(false);
    std::lock_guard<std::mutex> lock(g_ring.mutex);
    RingShutdownEncoder();
    DebugLog("Ring: stopped");
}

extern "C" UNITY_INTERFACE_EXPORT bool ODDRecorder_RingIsActive()
{
    return g_ring.active.load();
}
