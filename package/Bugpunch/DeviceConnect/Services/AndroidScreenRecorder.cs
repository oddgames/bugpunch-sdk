using System;
using System.IO;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Thin Unity wrapper around the native Android BugpunchRecorder plugin.
    /// Handles MediaProjection consent and exposes a simple Start/Stop/Dump API.
    ///
    /// All actual recording is done in native Java. Unity just kicks it off
    /// and tells it where to write the trimmed MP4 on exception.
    /// </summary>
    public class AndroidScreenRecorder : MonoBehaviour
    {
        const string JAVA_RECORDER = "au.com.oddgames.bugpunch.BugpunchRecorder";
        const string JAVA_REQUEST_ACTIVITY = "au.com.oddgames.bugpunch.BugpunchProjectionRequest";

        /// <summary>Whether the native recorder is currently capturing.</summary>
        public bool IsRecording { get; private set; }

        /// <summary>Raised once MediaProjection consent succeeds or fails.</summary>
        public event Action<bool> OnStarted;

        // Config — set from BugReporter before Start()
        int _width, _height, _bitrate, _fps, _windowSeconds;

        /// <summary>
        /// Request MediaProjection consent and start continuous recording.
        /// On success, the native recorder maintains a rolling window of the
        /// last <paramref name="windowSeconds"/> seconds of encoded video.
        ///
        /// The first call triggers a system consent dialog. Subsequent calls
        /// no-op if already running.
        /// </summary>
        public void StartRecording(int width, int height, int bitrate, int fps, int windowSeconds)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _width = width; _height = height; _bitrate = bitrate; _fps = fps; _windowSeconds = windowSeconds;

            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var request = new AndroidJavaClass(JAVA_REQUEST_ACTIVITY);

                // Fire-and-forget: the native side will UnitySendMessage us the result
                request.CallStatic("request",
                    activity, width, height, bitrate, fps, windowSeconds, (int)Screen.dpi,
                    gameObject.name, nameof(OnNativeStartResult));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Bugpunch] AndroidScreenRecorder.StartRecording failed: {e.Message}");
                OnStarted?.Invoke(false);
            }
#else
            Debug.Log("[Bugpunch] AndroidScreenRecorder: not Android, no-op");
            OnStarted?.Invoke(false);
#endif
        }

        /// <summary>
        /// Stop recording and release the MediaProjection.
        /// Next <see cref="StartRecording"/> will re-prompt for consent.
        /// </summary>
        public void StopRecording()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var recorder = new AndroidJavaClass(JAVA_RECORDER).CallStatic<AndroidJavaObject>("getInstance");
                recorder.Call("stop");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Bugpunch] AndroidScreenRecorder.StopRecording failed: {e.Message}");
            }
#endif
            IsRecording = false;
        }

        /// <summary>
        /// Flush the ring buffer to an MP4 file. The native side trims the
        /// output to start on the oldest keyframe within the configured window.
        /// </summary>
        /// <param name="outputPath">Absolute path for the MP4 output. Will be overwritten.</param>
        /// <returns>true on success</returns>
        public bool DumpToFile(string outputPath)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using var recorder = new AndroidJavaClass(JAVA_RECORDER).CallStatic<AndroidJavaObject>("getInstance");
                return recorder.Call<bool>("dump", outputPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Bugpunch] AndroidScreenRecorder.DumpToFile failed: {e.Message}");
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// Convenience: dumps to a temp file under Application.temporaryCachePath and returns the bytes.
        /// </summary>
        public byte[] DumpToBytes()
        {
            var path = Path.Combine(Application.temporaryCachePath, $"bugpunch-dump-{DateTime.UtcNow.Ticks}.mp4");
            if (!DumpToFile(path)) return null;
            try
            {
                var bytes = File.ReadAllBytes(path);
                File.Delete(path);
                return bytes;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Bugpunch] DumpToBytes read failed: {e.Message}");
                return null;
            }
        }

        // Called from Java via UnitySendMessage — must match the method name passed in StartRecording.
        void OnNativeStartResult(string result)
        {
            IsRecording = result == "1";
            Debug.Log($"[Bugpunch] Native recorder start result: {(IsRecording ? "OK" : "FAILED")}");
            OnStarted?.Invoke(IsRecording);
        }
    }
}
