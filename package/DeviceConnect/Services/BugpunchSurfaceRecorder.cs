using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Fallback video capture path — used on Android when the user denies
    /// MediaProjection consent. Mirrors Unity's rendered output into a
    /// RenderTexture, converts RGBA → NV12 on the GPU via a compute shader,
    /// reads the packed byte buffer back asynchronously (no render-thread
    /// stall), and pushes frames to the native recorder's buffer-input
    /// MediaCodec. The encoder, ring buffer, and MP4 muxer are shared with
    /// the MediaProjection path — only the frame source differs.
    ///
    /// On devices without compute-shader support (very old Android OpenGL
    /// ES 3.0) the shader load / dispatch is skipped and a managed CPU
    /// conversion fallback is used instead. In practice every Android 8+
    /// device we ship to supports compute.
    ///
    /// Lifecycle: always-mounted on Android. Polls
    /// <see cref="BugpunchNative.IsVideoBufferMode"/> every 2 s; when native
    /// flips into buffer mode this recorder spins up. When native stops
    /// (projection succeeded on a later attempt, recorder torn down, etc.)
    /// this stops too.
    /// </summary>
    [ODDGames.Scripting.ScriptProtected]
    public class BugpunchSurfaceRecorder : MonoBehaviour
    {
        // Capture cadence. 15 fps is plenty for bug-report video and gives
        // AsyncGPUReadback time to drain on lower-end GPUs.
        const float CAPTURE_FPS = 15f;

        // Max frames in flight on the async readback pipeline.
        const int MAX_INFLIGHT = 2;

        Camera _sourceCamera;
        RenderTexture _mirrorRT;
        CommandBuffer _blitCmd;
        CameraEvent _blitEvent = CameraEvent.AfterEverything;

        ComputeShader _convertShader;
        int _kY;
        int _kUV;
        ComputeBuffer _nv12Buffer;     // GPU-side NV12 bytes (size = ySize + uvSize)
        bool _useCompute;

        int _rtWidth;
        int _rtHeight;
        int _ySize;
        int _uvSize;
        int _totalBytes;

        bool _running;
        float _pollCooldown;
        float _captureCooldown;
        int _inflight;
        long _startTimeNs;

        // ── Singleton bootstrap ──────────────────────────────────────────

        static BugpunchSurfaceRecorder _instance;

        public static void EnsureMounted()
        {
            if (_instance != null) return;
            var go = new GameObject("[Bugpunch Surface Recorder]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BugpunchSurfaceRecorder>();
        }

        // ── Polling + lifecycle ──────────────────────────────────────────

        void Update()
        {
            _pollCooldown -= Time.unscaledDeltaTime;
            if (_pollCooldown > 0f) return;
            _pollCooldown = 2f;

#if UNITY_ANDROID && !UNITY_EDITOR
            bool nativeWants = BugpunchNative.IsVideoBufferMode();
            if (nativeWants && !_running) StartCapture();
            else if (!nativeWants && _running) StopCapture();
#endif
        }

        void LateUpdate()
        {
            if (!_running) return;
            _captureCooldown -= Time.unscaledDeltaTime;
            if (_captureCooldown > 0f) return;
            _captureCooldown = 1f / CAPTURE_FPS;

            if (_inflight >= MAX_INFLIGHT) return;
            if (_mirrorRT == null || _sourceCamera == null) return;

            long ptsUs = (DateTime.UtcNow.Ticks * 100L - _startTimeNs) / 1000L;
            if (ptsUs < 0) ptsUs = 0;

            _inflight++;

            if (_useCompute)
            {
                // GPU: dispatch shader that writes NV12 into _nv12Buffer,
                // then async-read the buffer (already NV12, no CPU conversion).
                DispatchConvert();
                AsyncGPUReadback.Request(_nv12Buffer, _totalBytes, 0, req =>
                {
                    _inflight = Math.Max(0, _inflight - 1);
                    if (!_running) return;
                    if (req.hasError) { BugpunchLog.Warn("SurfaceRecorder", "compute readback error"); return; }
                    var nv12Native = req.GetData<byte>();
                    var nv12 = new byte[nv12Native.Length];
                    nv12Native.CopyTo(nv12);
#if UNITY_ANDROID && !UNITY_EDITOR
                    BugpunchNative.QueueVideoFrame(nv12, ptsUs);
#endif
                });
            }
            else
            {
                // CPU fallback — RGBA readback, managed-thread convert.
                AsyncGPUReadback.Request(_mirrorRT, 0, TextureFormat.RGBA32, req =>
                {
                    _inflight = Math.Max(0, _inflight - 1);
                    if (!_running) return;
                    if (req.hasError) { BugpunchLog.Warn("SurfaceRecorder", "rgba readback error"); return; }
                    var rgbaNative = req.GetData<byte>();
                    var rgba = new byte[rgbaNative.Length];
                    rgbaNative.CopyTo(rgba);
                    var nv12 = ConvertRgbaToNv12Cpu(rgba, _rtWidth, _rtHeight);
#if UNITY_ANDROID && !UNITY_EDITOR
                    BugpunchNative.QueueVideoFrame(nv12, ptsUs);
#endif
                });
            }
        }

        // ── Capture start/stop ───────────────────────────────────────────

        void StartCapture()
        {
            var (w, h) = BugpunchNative.GetVideoBufferSize();
            if (w <= 0 || h <= 0)
            {
                BugpunchLog.Warn("SurfaceRecorder", "native buffer size unknown — cannot start");
                return;
            }
            // Encoder + NV12 both need even dimensions.
            w = (w / 2) * 2;
            h = (h / 2) * 2;
            _rtWidth = w;
            _rtHeight = h;
            _ySize = w * h;
            _uvSize = (w / 2) * (h / 2) * 2;
            _totalBytes = _ySize + _uvSize;

            _sourceCamera = Camera.main;
            if (_sourceCamera == null)
            {
                BugpunchLog.Warn("SurfaceRecorder", "no Camera.main — cannot start");
                return;
            }

            _mirrorRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            _mirrorRT.name = "BugpunchMirrorRT";
            _mirrorRT.enableRandomWrite = false;
            _mirrorRT.Create();

            _blitCmd = new CommandBuffer { name = "BugpunchSurfaceMirror" };
            _blitCmd.Blit(BuiltinRenderTextureType.CameraTarget, _mirrorRT);
            _sourceCamera.AddCommandBuffer(_blitEvent, _blitCmd);

            // Try to set up the compute path. Falls back to CPU conversion if
            // shaders aren't supported or the resource can't be loaded.
            _useCompute = TrySetupCompute(w, h);
            if (!_useCompute)
            {
                BugpunchLog.Warn("SurfaceRecorder", "compute shader unavailable — using CPU RGBA→NV12 fallback");
            }

            _startTimeNs = DateTime.UtcNow.Ticks * 100L;
            _captureCooldown = 0f;
            _inflight = 0;
            _running = true;
            BugpunchLog.Info("SurfaceRecorder", $"started — {w}x{h} @ {CAPTURE_FPS}fps (compute={_useCompute})");
        }

        void StopCapture()
        {
            _running = false;
            if (_sourceCamera != null && _blitCmd != null)
            {
                try { _sourceCamera.RemoveCommandBuffer(_blitEvent, _blitCmd); } catch { /* camera gone */ }
            }
            _blitCmd?.Release();
            _blitCmd = null;
            if (_mirrorRT != null)
            {
                _mirrorRT.Release();
                Destroy(_mirrorRT);
                _mirrorRT = null;
            }
            _nv12Buffer?.Release();
            _nv12Buffer = null;
            _convertShader = null;
            _sourceCamera = null;
            BugpunchLog.Info("SurfaceRecorder", "stopped");
        }

        void OnDestroy() { if (_running) StopCapture(); }

        // ── Compute path ─────────────────────────────────────────────────

        bool TrySetupCompute(int w, int h)
        {
            if (!SystemInfo.supportsComputeShaders) return false;
            try
            {
                _convertShader = Resources.Load<ComputeShader>("BugpunchRgbaToNv12");
                if (_convertShader == null) return false;
                _kY  = _convertShader.FindKernel("KRgbaToY");
                _kUV = _convertShader.FindKernel("KRgbaToUV");
                if (_kY < 0 || _kUV < 0) return false;

                // ByteAddressBuffer: stride=4, count in uints. Total bytes must be a multiple of 4.
                int uintCount = (_totalBytes + 3) / 4;
                _nv12Buffer = new ComputeBuffer(uintCount, 4, ComputeBufferType.Raw);
                return true;
            }
            catch (Exception e)
            {
                BugpunchNative.ReportSdkError("BugpunchSurfaceRecorder.TrySetupCompute", e);
                return false;
            }
        }

        void DispatchConvert()
        {
            _convertShader.SetInt("_Width",  _rtWidth);
            _convertShader.SetInt("_Height", _rtHeight);
            _convertShader.SetTexture(_kY,  "_Src",  _mirrorRT);
            _convertShader.SetTexture(_kUV, "_Src",  _mirrorRT);
            _convertShader.SetBuffer(_kY,   "_Nv12", _nv12Buffer);
            _convertShader.SetBuffer(_kUV,  "_Nv12", _nv12Buffer);

            // Y: 4 output bytes per thread, 8x8 group → dispatch ceil(w/4/8), ceil(h/8).
            int gxY = ((_rtWidth + 3) / 4 + 7) / 8;
            int gyY = (_rtHeight + 7) / 8;
            _convertShader.Dispatch(_kY, gxY, gyY, 1);

            // UV: 4 source pixels wide × 2 source rows tall per thread, 8x8 group
            // → dispatch ceil(w/4/8), ceil(h/2/8).
            int gxUV = ((_rtWidth + 3) / 4 + 7) / 8;
            int gyUV = ((_rtHeight / 2) + 7) / 8;
            _convertShader.Dispatch(_kUV, gxUV, gyUV, 1);
        }

        // ── CPU RGBA32 → NV12 (BT.601 limited range) fallback ────────────

        static byte[] ConvertRgbaToNv12Cpu(byte[] rgba, int w, int h)
        {
            int ySize = w * h;
            int uvSize = (w / 2) * (h / 2) * 2;
            var nv12 = new byte[ySize + uvSize];
            int rgbaRowStride = w * 4;

            int yi = 0;
            for (int y = 0; y < h; y++)
            {
                int src = y * rgbaRowStride;
                for (int x = 0; x < w; x++)
                {
                    byte r = rgba[src];
                    byte g = rgba[src + 1];
                    byte b = rgba[src + 2];
                    int Y = (66 * r + 129 * g + 25 * b + 128) >> 8;
                    Y += 16;
                    if (Y < 0) Y = 0; else if (Y > 255) Y = 255;
                    nv12[yi++] = (byte)Y;
                    src += 4;
                }
            }

            int uvi = ySize;
            for (int y = 0; y < h; y += 2)
            {
                int row0 = y * rgbaRowStride;
                int row1 = row0 + rgbaRowStride;
                for (int x = 0; x < w; x += 2)
                {
                    int s00 = row0 + x * 4;
                    int s10 = row0 + (x + 1) * 4;
                    int s01 = row1 + x * 4;
                    int s11 = row1 + (x + 1) * 4;
                    int r = (rgba[s00]     + rgba[s10]     + rgba[s01]     + rgba[s11])     >> 2;
                    int g = (rgba[s00 + 1] + rgba[s10 + 1] + rgba[s01 + 1] + rgba[s11 + 1]) >> 2;
                    int b = (rgba[s00 + 2] + rgba[s10 + 2] + rgba[s01 + 2] + rgba[s11 + 2]) >> 2;
                    int U = ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
                    int V = ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;
                    if (U < 0) U = 0; else if (U > 255) U = 255;
                    if (V < 0) V = 0; else if (V > 255) V = 255;
                    nv12[uvi++] = (byte)U;
                    nv12[uvi++] = (byte)V;
                }
            }
            return nv12;
        }
    }
}
