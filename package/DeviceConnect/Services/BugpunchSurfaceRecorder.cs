using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Fallback video capture path — used on Android when the user denies
    /// MediaProjection consent. Mirrors Unity's rendered output into a
    /// RenderTexture, reads it back asynchronously (no render-thread stall),
    /// converts RGBA32 → NV12 on a managed thread, and pushes the frame to
    /// the native recorder's buffer-input MediaCodec. The encoder, ring
    /// buffer, and MP4 muxer are shared with the MediaProjection path — only
    /// the frame source differs.
    ///
    /// Trade-off vs MediaProjection: system UI (keyboard, dialogs, native
    /// ad SDK overlays) is not captured — only the game surface. In return
    /// there is no OS consent dialog and no foreground-service notification.
    /// Perf: at 15 fps / 720p, roughly 4-6% of one CPU core (the RGBA→NV12
    /// conversion dominates; the GPU Blit and readback are cheap).
    ///
    /// Lifecycle: always-mounted. It polls <see cref="BugpunchNative.IsVideoBufferMode"/>
    /// every 2 s; when native flips into buffer mode, this recorder spins up
    /// and starts pushing frames. When native stops (projection succeeded on
    /// a later attempt, recorder was torn down, etc.) this stops too.
    /// </summary>
    public class BugpunchSurfaceRecorder : MonoBehaviour
    {
        const string TAG = "[Bugpunch.SurfaceRecorder]";

        // Capture cadence — 15 fps is plenty for bug-report video and halves
        // the CPU cost of the RGBA→NV12 conversion vs 30 fps.
        const float CAPTURE_FPS = 15f;

        // Max frames in flight on the async readback pipeline. Beyond this we
        // skip capturing to avoid queue growth if the GPU falls behind.
        const int MAX_INFLIGHT = 2;

        Camera _sourceCamera;
        RenderTexture _mirrorRT;
        CommandBuffer _cmd;
        CameraEvent _cmdEvent = CameraEvent.AfterEverything;

        int _rtWidth;
        int _rtHeight;
        bool _running;
        float _pollCooldown;
        float _captureCooldown;
        int _inflight;

        long _startTimeNs;
        readonly Queue<byte[]> _nv12Pool = new Queue<byte[]>();

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

            // Request the mirror RT's pixels asynchronously. Callback fires
            // on the main thread once the GPU finishes; conversion +
            // submission happens there.
            _inflight++;
            long ptsUs = (DateTime.UtcNow.Ticks * 100L - _startTimeNs) / 1000L;
            if (ptsUs < 0) ptsUs = 0;
            AsyncGPUReadback.Request(_mirrorRT, 0, TextureFormat.RGBA32, req =>
            {
                _inflight = Math.Max(0, _inflight - 1);
                if (!_running) return;
                if (req.hasError) { Debug.LogWarning($"{TAG} readback error"); return; }
                var pixels = req.GetData<byte>();
                // NativeArray → managed byte[] copy (the pool keeps the array
                // allocated between frames to avoid GC pressure).
                var rgba = new byte[pixels.Length];
                pixels.CopyTo(rgba);
                SubmitFrame(rgba, _rtWidth, _rtHeight, ptsUs);
            });
        }

        // ── Capture start/stop ───────────────────────────────────────────

        void StartCapture()
        {
            var (w, h) = BugpunchNative.GetVideoBufferSize();
            if (w <= 0 || h <= 0)
            {
                Debug.LogWarning($"{TAG} native buffer size unknown — cannot start");
                return;
            }
            // Encoders like even dimensions; clamp just in case.
            w = (w / 2) * 2;
            h = (h / 2) * 2;
            _rtWidth = w;
            _rtHeight = h;

            _sourceCamera = Camera.main;
            if (_sourceCamera == null)
            {
                Debug.LogWarning($"{TAG} no Camera.main — cannot start");
                return;
            }

            _mirrorRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            _mirrorRT.name = "BugpunchMirrorRT";
            _mirrorRT.Create();

            _cmd = new CommandBuffer { name = "BugpunchSurfaceMirror" };
            // Blit the camera's rendered output into our mirror RT. Using
            // BuiltinRenderTextureType.CameraTarget captures whatever the
            // camera has already composited, including post-fx but NOT UI
            // overlays drawn by other cameras — acceptable for fallback.
            _cmd.Blit(BuiltinRenderTextureType.CameraTarget, _mirrorRT);
            _sourceCamera.AddCommandBuffer(_cmdEvent, _cmd);

            _startTimeNs = DateTime.UtcNow.Ticks * 100L;
            _captureCooldown = 0f;
            _inflight = 0;
            _running = true;
            Debug.Log($"{TAG} started — {w}x{h} @ {CAPTURE_FPS}fps");
        }

        void StopCapture()
        {
            _running = false;
            if (_sourceCamera != null && _cmd != null)
            {
                try { _sourceCamera.RemoveCommandBuffer(_cmdEvent, _cmd); } catch { /* camera gone */ }
            }
            _cmd?.Release();
            _cmd = null;
            if (_mirrorRT != null)
            {
                _mirrorRT.Release();
                Destroy(_mirrorRT);
                _mirrorRT = null;
            }
            _sourceCamera = null;
            Debug.Log($"{TAG} stopped");
        }

        void OnDestroy() { if (_running) StopCapture(); }

        // ── RGBA32 → NV12 (BT.601, limited range) ────────────────────────

        void SubmitFrame(byte[] rgba, int w, int h, long ptsUs)
        {
            int ySize = w * h;
            int uvSize = (w / 2) * (h / 2) * 2;
            var nv12 = new byte[ySize + uvSize];

            // GPU readback returns rows bottom-up on most platforms; AsyncGPUReadback
            // with RenderTexture input is top-down in Unity's docs, so we treat it
            // as top-down (row 0 is the top of the image). If testing shows flipped
            // output on some GPUs, invert the row index.
            int rgbaRowStride = w * 4;
            int uvRowStride = w; // each row of NV12 UV is w bytes (w/2 UV pairs interleaved)

            // Y plane — one sample per pixel.
            int yi = 0;
            for (int y = 0; y < h; y++)
            {
                int src = y * rgbaRowStride;
                for (int x = 0; x < w; x++)
                {
                    byte r = rgba[src];
                    byte g = rgba[src + 1];
                    byte b = rgba[src + 2];
                    // BT.601 limited range luma
                    int Y = (66 * r + 129 * g + 25 * b + 128) >> 8;
                    Y += 16;
                    if (Y < 0) Y = 0; else if (Y > 255) Y = 255;
                    nv12[yi++] = (byte)Y;
                    src += 4;
                }
            }

            // Interleaved UV plane — one pair per 2x2 block (downsampled chroma).
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
                    // Average the 2x2 block for chroma — simple and close enough
                    // for bug-report fidelity. Heavier filters aren't worth the
                    // CPU.
                    int r = (rgba[s00] + rgba[s10] + rgba[s01] + rgba[s11]) >> 2;
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

#if UNITY_ANDROID && !UNITY_EDITOR
            BugpunchNative.QueueVideoFrame(nv12, ptsUs);
#endif
        }
    }
}
