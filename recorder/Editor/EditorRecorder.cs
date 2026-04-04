#if UNITY_RECORDER
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using ODDGames.Recorder.Internal;

namespace ODDGames.Recorder.Editor
{
    [InitializeOnLoad]
    internal static class EditorRecorderRegistration
    {
        static EditorRecorderRegistration()
        {
            MediaRecorder.EditorBackendFactory = settings => new EditorRecorderBackend(settings);
        }
    }

    internal class EditorRecorderBackend : IRecorderBackend
    {
        RecorderController _controller;
        MovieRecorderSettings _movieSettings;
        string _outputPath;
        bool _isRecording;

        public bool IsRecording => _isRecording && _controller != null && _controller.IsRecording();

        public EditorRecorderBackend(RecorderSettings settings)
        {
            // Resolve output path
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "Recordings");
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _outputPath = System.IO.Path.Combine(dir, $"recording_{timestamp}");

            if (!string.IsNullOrEmpty(settings.OutputPath))
            {
                _outputPath = settings.OutputPath;
                string outputDir = System.IO.Path.GetDirectoryName(_outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !System.IO.Directory.Exists(outputDir))
                    System.IO.Directory.CreateDirectory(outputDir);
            }

            // Create movie recorder settings
            _movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            _movieSettings.name = "ODDGames Recorder";
            _movieSettings.Enabled = true;
            _movieSettings.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
            _movieSettings.VideoBitRateMode = VideoBitrateMode.High;

            _movieSettings.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = settings.Width > 0 ? settings.Width : Screen.width,
                OutputHeight = settings.Height > 0 ? settings.Height : Screen.height
            };

            _movieSettings.AudioInputSettings.PreserveAudio = settings.IncludeAudio;
            _movieSettings.OutputFile = _outputPath;

            // Create controller settings
            var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            controllerSettings.AddRecorderSettings(_movieSettings);
            controllerSettings.SetRecordModeToManual();
            controllerSettings.FrameRate = settings.FrameRate;

            _controller = new RecorderController(controllerSettings);
        }

        public void Start()
        {
            _controller.PrepareRecording();
            bool success = _controller.StartRecording();
            _isRecording = success;

            if (!success)
                Debug.LogError("[Recorder] Failed to start Unity Recorder. Is Play Mode active?");
            else
                Debug.Log("[Recorder] Recording started (Editor/Unity Recorder)");
        }

        public System.Threading.Tasks.Task<string> StopAsync()
        {
            _isRecording = false;
            _controller.StopRecording();

            string mp4Path = _outputPath + ".mp4";
            Debug.Log($"[Recorder] Recording saved: {mp4Path}");

            return System.Threading.Tasks.Task.FromResult(mp4Path);
        }

        public void Dispose()
        {
            if (_isRecording)
            {
                _controller?.StopRecording();
                _isRecording = false;
            }
        }
    }
}
#else
// When Unity Recorder is not installed, register a warning backend
using UnityEditor;
using UnityEngine;
using ODDGames.Recorder.Internal;

namespace ODDGames.Recorder.Editor
{
    [InitializeOnLoad]
    internal static class EditorRecorderRegistration
    {
        static EditorRecorderRegistration()
        {
            MediaRecorder.EditorBackendFactory = settings =>
            {
                Debug.LogWarning("[Recorder] Install com.unity.recorder package for editor recording support.");
                return new NullBackend();
            };
        }
    }
}
#endif
