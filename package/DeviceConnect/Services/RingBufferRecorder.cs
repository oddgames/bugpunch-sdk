using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Cross-platform always-on ring buffer video recorder.
    ///
    /// Continuously encodes screen frames and maintains a rolling window
    /// (default 30s) of H.264 video in memory. On <see cref="DumpToFile"/>,
    /// writes the buffered footage to an MP4 file starting from the oldest
    /// keyframe.
    ///
    /// Platform backends:
    ///   Android — BugpunchRecorder.java (MediaCodec + VirtualDisplay)
    ///   iOS     — BugpunchRingRecorder.mm (ReplayKit + VideoToolbox)
    ///   Editor  — No-op (ring buffer recording not available in editor)
    ///
    /// This replaces the Android-only <see cref="AndroidScreenRecorder"/>,
    /// providing a single API surface for all platforms.
    /// </summary>
    public class RingBufferRecorder : MonoBehaviour
    {
        /// <summary>Whether the native recorder is currently capturing.</summary>
        public bool IsRecording { get; private set; }

        /// <summary>Raised once recording consent/start succeeds or fails.</summary>
        public event Action<bool> OnStarted;

        // ── iOS native bridge ──

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void BugpunchRing_Configure(int width, int height, int fps, int bitrate, int windowSeconds);

        [DllImport("__Internal")]
        static extern bool BugpunchRing_Start();

        [DllImport("__Internal")]
        static extern void BugpunchRing_Stop();

        [DllImport("__Internal")]
        static extern bool BugpunchRing_IsRunning();

        [DllImport("__Internal")]
        static extern bool BugpunchRing_HasFootage();

        [DllImport("__Internal")]
        static extern bool BugpunchRing_Dump(string outputPath);

        [DllImport("__Internal")]
        static extern long BugpunchRing_GetBufferSizeBytes();
#endif

        // ── Android JNI constants ──

#if UNITY_ANDROID && !UNITY_EDITOR
        const string JAVA_RECORDER = "au.com.oddgames.bugpunch.BugpunchRecorder";
        const string JAVA_REQUEST_ACTIVITY = "au.com.oddgames.bugpunch.BugpunchProjectionRequest";
#endif

        // ── Public API ──

        /// <summary>
        /// Start always-on ring buffer recording. The native backend continuously
        /// encodes screen frames and keeps the most recent <paramref name="windowSeconds"/>
        /// seconds in memory. No file is written until <see cref="DumpToFile"/> is called.
        ///
        /// On Android, this triggers the MediaProjection consent dialog on first call.
        /// On iOS, ReplayKit in-app capture starts immediately (no user prompt).
        /// </summary>
        public void StartRecording(int width, int height, int bitrate, int fps, int windowSeconds)
        {
#if UNITY_EDITOR
            BugpunchLog.Info("RingBufferRecorder", "RingBufferRecorder: not available in editor");
            OnStarted?.Invoke(false);

#elif UNITY_ANDROID
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var request = new AndroidJavaClass(JAVA_REQUEST_ACTIVITY);

                request.CallStatic("request",
                    activity, width, height, bitrate, fps, windowSeconds, (int)Screen.dpi,
                    gameObject.name, nameof(OnNativeStartResult));
            }
            catch (Exception e)
            {
                BugpunchNative.ReportSdkError("RingBufferRecorder.StartRecording.Android", e);
                OnStarted?.Invoke(false);
            }

#elif UNITY_IOS
            try
            {
                BugpunchRing_Configure(width, height, fps, bitrate, windowSeconds);
                bool ok = BugpunchRing_Start();
                IsRecording = ok;
                BugpunchLog.Info("RingBufferRecorder", $"RingBufferRecorder.StartRecording (iOS): {(ok ? "OK" : "FAILED")}");
                OnStarted?.Invoke(ok);
            }
            catch (Exception e)
            {
                BugpunchNative.ReportSdkError("RingBufferRecorder.StartRecording.iOS", e);
                OnStarted?.Invoke(false);
            }

#else
            BugpunchLog.Info("RingBufferRecorder", $"RingBufferRecorder: platform {Application.platform} not supported");
            OnStarted?.Invoke(false);
#endif
        }

        /// <summary>
        /// Stop recording and release native resources.
        /// </summary>
        public void StopRecording()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var recorder = new AndroidJavaClass(JAVA_RECORDER)
                    .CallStatic<AndroidJavaObject>("getInstance");
                recorder.Call("stop");
            }
            catch (Exception e)
            {
                BugpunchNative.ReportSdkError("RingBufferRecorder.StopRecording.Android", e);
            }
#elif UNITY_IOS && !UNITY_EDITOR
            try
            {
                BugpunchRing_Stop();
            }
            catch (Exception e)
            {
                BugpunchNative.ReportSdkError("RingBufferRecorder.StopRecording.iOS", e);
            }
#endif
            IsRecording = false;
        }

        /// <summary>
        /// Flush the ring buffer to an MP4 file, trimmed to start on the
        /// oldest keyframe within the configured window.
        /// </summary>
        /// <param name="outputPath">Absolute path for the MP4 output.</param>
        /// <returns>true on success</returns>
        public bool DumpToFile(string outputPath)
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var recorder = new AndroidJavaClass(JAVA_RECORDER)
                    .CallStatic<AndroidJavaObject>("getInstance");
                return recorder.Call<bool>("dump", outputPath);
            }
            catch (Exception e)
            {
                BugpunchNative.ReportSdkError("RingBufferRecorder.DumpToFile.Android", e);
                return false;
            }

#elif UNITY_IOS && !UNITY_EDITOR
            try
            {
                return BugpunchRing_Dump(outputPath);
            }
            catch (Exception e)
            {
                BugpunchNative.ReportSdkError("RingBufferRecorder.DumpToFile.iOS", e);
                return false;
            }

#else
            return false;
#endif
        }

        /// <summary>
        /// Dumps ring buffer to a temp file and returns the bytes.
        /// Returns null on failure or unsupported platform.
        /// </summary>
        public byte[] DumpToBytes()
        {
            var path = Path.Combine(Application.temporaryCachePath,
                $"bugpunch-dump-{DateTime.UtcNow.Ticks}.mp4");

            if (!DumpToFile(path)) return null;

            try
            {
                var bytes = File.ReadAllBytes(path);
                File.Delete(path);
                return bytes;
            }
            catch (Exception e)
            {
                BugpunchNative.ReportSdkError("RingBufferRecorder.DumpToBytes", e);
                return null;
            }
        }

        /// <summary>
        /// Returns true if the ring buffer contains at least one keyframe
        /// (i.e., a dump would produce a valid MP4).
        /// </summary>
        public bool HasFootage()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var recorder = new AndroidJavaClass(JAVA_RECORDER)
                    .CallStatic<AndroidJavaObject>("getInstance");
                return recorder.Call<bool>("hasFootage");
            }
            catch { return false; }

#elif UNITY_IOS && !UNITY_EDITOR
            try
            {
                return BugpunchRing_HasFootage();
            }
            catch { return false; }

#else
            return false;
#endif
        }

        /// <summary>
        /// Approximate memory usage of the ring buffer in bytes.
        /// Only available on iOS; returns -1 on other platforms.
        /// </summary>
        public long GetBufferSizeBytes()
        {
#if UNITY_IOS && !UNITY_EDITOR
            try { return BugpunchRing_GetBufferSizeBytes(); }
            catch { return -1; }
#else
            return -1;
#endif
        }

        /// <summary>
        /// Whether ring buffer recording is supported on the current platform.
        /// </summary>
        public static bool IsSupported
        {
            get
            {
#if UNITY_EDITOR
                return false;
#elif UNITY_ANDROID || UNITY_IOS
                return true;
#else
                return false;
#endif
            }
        }

        // ── Android callback (called from Java via UnitySendMessage) ──

        void OnNativeStartResult(string result)
        {
            IsRecording = result == "1";
            BugpunchLog.Info("RingBufferRecorder", $"Native recorder start result: {(IsRecording ? "OK" : "FAILED")}");
            OnStarted?.Invoke(IsRecording);
        }
    }
}
