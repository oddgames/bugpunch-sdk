using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace ODDGames.Recorder.Internal
{
    /// <summary>
    /// Platform-specific native recorder backends.
    /// </summary>
    internal static class NativeBridge
    {
        static string EnsureRecordingsDirectory()
        {
            string dir = Path.Combine(Application.persistentDataPath, "Recordings");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }

        static string GenerateOutputPath(RecorderSettings settings, string extension)
        {
            if (!string.IsNullOrEmpty(settings.OutputPath))
                return settings.OutputPath;

            string dir = EnsureRecordingsDirectory();
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(dir, $"recording_{timestamp}.{extension}");
        }

        // ─────────────────────────────────────────────
        // Android
        // ─────────────────────────────────────────────

#if UNITY_ANDROID && !UNITY_EDITOR
        public static IRecorderBackend CreateAndroidBackend(RecorderSettings settings)
        {
            try
            {
                return new AndroidRecorderBackend(settings);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Recorder] Failed to create Android backend: {ex.Message}");
                return new NullBackend();
            }
        }

        class AndroidRecorderBackend : IRecorderBackend
        {
            readonly AndroidJavaObject _bridge;
            readonly string _outputPath;
            bool _isRecording;

            public bool IsRecording => _isRecording;

            public AndroidRecorderBackend(RecorderSettings settings)
            {
                _outputPath = GenerateOutputPath(settings, "mp4");

                int width = settings.Width > 0 ? settings.Width : Screen.width;
                int height = settings.Height > 0 ? settings.Height : Screen.height;

                _bridge = new AndroidJavaObject("au.com.oddgames.recorder.MediaCodecBridge");
                _bridge.Call("init", _outputPath, width, height, settings.FrameRate,
                    settings.VideoBitrate, settings.IncludeAudio);
            }

            public void Start()
            {
                _bridge.Call("startRecording");
                _isRecording = true;
                Debug.Log("[Recorder] Recording started (Android/MediaCodec)");
            }

            public Task<string> StopAsync()
            {
                _isRecording = false;
                return Task.Run(() =>
                {
                    _bridge.Call("stopRecording");
                    Debug.Log($"[Recorder] Recording saved: {_outputPath}");
                    return _outputPath;
                });
            }

            public void Dispose()
            {
                if (_isRecording)
                {
                    _bridge?.Call("stopRecording");
                    _isRecording = false;
                }
                _bridge?.Dispose();
            }
        }
#else
        public static IRecorderBackend CreateAndroidBackend(RecorderSettings settings)
        {
            Debug.LogWarning("[Recorder] Android backend not available on this platform.");
            return new NullBackend();
        }
#endif

        // ─────────────────────────────────────────────
        // iOS
        // ─────────────────────────────────────────────

#if UNITY_IOS && !UNITY_EDITOR
        public static IRecorderBackend CreateiOSBackend(RecorderSettings settings)
        {
            try
            {
                return new iOSRecorderBackend(settings);
            }
            catch (DllNotFoundException ex)
            {
                Debug.LogWarning($"[Recorder] iOS native plugin not found: {ex.Message}");
                return new NullBackend();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Recorder] Failed to create iOS backend: {ex.Message}");
                return new NullBackend();
            }
        }

        class iOSRecorderBackend : IRecorderBackend
        {
            [System.Runtime.InteropServices.DllImport("__Internal")]
            static extern void ODDRecorder_Start(string path, int width, int height, int fps,
                int bitrate, bool includeAudio);

            [System.Runtime.InteropServices.DllImport("__Internal")]
            static extern void ODDRecorder_Stop(System.Text.StringBuilder outPath, int maxLen);

            [System.Runtime.InteropServices.DllImport("__Internal")]
            static extern bool ODDRecorder_IsRecording();

            readonly string _outputPath;

            public bool IsRecording => ODDRecorder_IsRecording();

            public iOSRecorderBackend(RecorderSettings settings)
            {
                _outputPath = GenerateOutputPath(settings, "mp4");
                int width = settings.Width > 0 ? settings.Width : Screen.width;
                int height = settings.Height > 0 ? settings.Height : Screen.height;

                // Validate the native plugin is reachable by calling IsRecording
                ODDRecorder_IsRecording();
            }

            public void Start()
            {
                int width = Screen.width;
                int height = Screen.height;
                ODDRecorder_Start(_outputPath, width, height, 30, 10_000_000, true);
                Debug.Log("[Recorder] Recording started (iOS/AVFoundation)");
            }

            public Task<string> StopAsync()
            {
                return Task.Run(() =>
                {
                    var sb = new System.Text.StringBuilder(1024);
                    ODDRecorder_Stop(sb, sb.Capacity);
                    string path = sb.ToString();
                    if (string.IsNullOrEmpty(path))
                        path = _outputPath;
                    Debug.Log($"[Recorder] Recording saved: {path}");
                    return path;
                });
            }

            public void Dispose()
            {
                if (IsRecording)
                {
                    var sb = new System.Text.StringBuilder(1024);
                    ODDRecorder_Stop(sb, sb.Capacity);
                }
            }
        }
#else
        public static IRecorderBackend CreateiOSBackend(RecorderSettings settings)
        {
            Debug.LogWarning("[Recorder] iOS backend not available on this platform.");
            return new NullBackend();
        }
#endif

        // ─────────────────────────────────────────────
        // Windows
        // ─────────────────────────────────────────────

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        public static IRecorderBackend CreateWindowsBackend(RecorderSettings settings)
        {
            try
            {
                return new WindowsRecorderBackend(settings);
            }
            catch (DllNotFoundException ex)
            {
                Debug.LogWarning($"[Recorder] Windows native plugin not found: {ex.Message}");
                return new NullBackend();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Recorder] Failed to create Windows backend: {ex.Message}");
                return new NullBackend();
            }
        }

        class WindowsRecorderBackend : IRecorderBackend
        {
            [System.Runtime.InteropServices.DllImport("ODDRecorder")]
            static extern void ODDRecorder_Start(string path, int width, int height, int fps,
                int bitrate, bool includeAudio);

            [System.Runtime.InteropServices.DllImport("ODDRecorder")]
            static extern void ODDRecorder_Stop(System.Text.StringBuilder outPath, int maxLen);

            [System.Runtime.InteropServices.DllImport("ODDRecorder")]
            static extern bool ODDRecorder_IsRecording();

            readonly string _outputPath;

            public bool IsRecording => ODDRecorder_IsRecording();

            public WindowsRecorderBackend(RecorderSettings settings)
            {
                _outputPath = GenerateOutputPath(settings, "mp4");

                // Validate native plugin is loadable
                ODDRecorder_IsRecording();
            }

            public void Start()
            {
                int width = Screen.width;
                int height = Screen.height;
                ODDRecorder_Start(_outputPath, width, height, 30, 10_000_000, true);
                Debug.Log("[Recorder] Recording started (Windows/Media Foundation)");
            }

            public Task<string> StopAsync()
            {
                return Task.Run(() =>
                {
                    var sb = new System.Text.StringBuilder(1024);
                    ODDRecorder_Stop(sb, sb.Capacity);
                    string path = sb.ToString();
                    if (string.IsNullOrEmpty(path))
                        path = _outputPath;
                    Debug.Log($"[Recorder] Recording saved: {path}");
                    return path;
                });
            }

            public void Dispose()
            {
                if (IsRecording)
                {
                    var sb = new System.Text.StringBuilder(1024);
                    ODDRecorder_Stop(sb, sb.Capacity);
                }
            }
        }
#else
        public static IRecorderBackend CreateWindowsBackend(RecorderSettings settings)
        {
            Debug.LogWarning("[Recorder] Windows backend not available on this platform.");
            return new NullBackend();
        }
#endif

        // ─────────────────────────────────────────────
        // macOS
        // ─────────────────────────────────────────────

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        public static IRecorderBackend CreateMacOSBackend(RecorderSettings settings)
        {
            try
            {
                return new MacOSRecorderBackend(settings);
            }
            catch (DllNotFoundException ex)
            {
                Debug.LogWarning($"[Recorder] macOS native plugin not found: {ex.Message}");
                return new NullBackend();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Recorder] Failed to create macOS backend: {ex.Message}");
                return new NullBackend();
            }
        }

        class MacOSRecorderBackend : IRecorderBackend
        {
            [System.Runtime.InteropServices.DllImport("ODDRecorder")]
            static extern void ODDRecorder_Start(string path, int width, int height, int fps,
                int bitrate, bool includeAudio);

            [System.Runtime.InteropServices.DllImport("ODDRecorder")]
            static extern void ODDRecorder_Stop(System.Text.StringBuilder outPath, int maxLen);

            [System.Runtime.InteropServices.DllImport("ODDRecorder")]
            static extern bool ODDRecorder_IsRecording();

            readonly string _outputPath;

            public bool IsRecording => ODDRecorder_IsRecording();

            public MacOSRecorderBackend(RecorderSettings settings)
            {
                _outputPath = GenerateOutputPath(settings, "mp4");

                // Validate native plugin is loadable
                ODDRecorder_IsRecording();
            }

            public void Start()
            {
                int width = Screen.width;
                int height = Screen.height;
                ODDRecorder_Start(_outputPath, width, height, 30, 10_000_000, true);
                Debug.Log("[Recorder] Recording started (macOS/AVFoundation)");
            }

            public Task<string> StopAsync()
            {
                return Task.Run(() =>
                {
                    var sb = new System.Text.StringBuilder(1024);
                    ODDRecorder_Stop(sb, sb.Capacity);
                    string path = sb.ToString();
                    if (string.IsNullOrEmpty(path))
                        path = _outputPath;
                    Debug.Log($"[Recorder] Recording saved: {path}");
                    return path;
                });
            }

            public void Dispose()
            {
                if (IsRecording)
                {
                    var sb = new System.Text.StringBuilder(1024);
                    ODDRecorder_Stop(sb, sb.Capacity);
                }
            }
        }
#else
        public static IRecorderBackend CreateMacOSBackend(RecorderSettings settings)
        {
            Debug.LogWarning("[Recorder] macOS backend not available on this platform.");
            return new NullBackend();
        }
#endif
    }

    // ─────────────────────────────────────────────
    // Null backend (unsupported platforms / fallback)
    // ─────────────────────────────────────────────

    /// <summary>
    /// No-op backend used when the current platform is not supported or a native plugin is missing.
    /// </summary>
    internal class NullBackend : IRecorderBackend
    {
        public bool IsRecording => false;

        public void Start()
        {
            Debug.LogWarning("[Recorder] NullBackend — recording is not available on this platform.");
        }

        public Task<string> StopAsync()
        {
            return Task.FromResult(string.Empty);
        }

        public void Dispose() { }
    }
}
