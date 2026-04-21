using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using ODDGames.Recorder.Internal;
using System.Threading.Tasks;

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

            string timestamp = System.DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            _outputPath = System.IO.Path.Combine(dir, $"recording_{timestamp}");

            if (!string.IsNullOrEmpty(settings.OutputPath))
            {
                _outputPath = settings.OutputPath;
                string outputDir = System.IO.Path.GetDirectoryName(_outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !System.IO.Directory.Exists(outputDir))
                    System.IO.Directory.CreateDirectory(outputDir);
            }

            // Match the Recorder Window's global defaults exactly
            _movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            _movieSettings.name = "UIAutomation Recorder";
            _movieSettings.Enabled = true;

            _movieSettings.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Low
            };

            // Always record at a fixed safe resolution — avoids Game View resize
            // (which can crash complex render pipelines) and avoids encoder issues
            // with arbitrary Game View sizes. 1280x720 (16:9, even) gives clear readability.
            var imageInput = new GameViewInputSettings();
            imageInput.OutputWidth = 1280;
            imageInput.OutputHeight = 720;
            _movieSettings.ImageInputSettings = imageInput;

            // Must use the property setter (CaptureAudio) — setting
            // AudioInputSettings.PreserveAudio directly does NOT update
            // the serialized captureAudio field that GetRecordingContext() reads.
            _movieSettings.CaptureAudio = settings.IncludeAudio;
            _movieSettings.OutputFile = _outputPath;

            var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            controllerSettings.AddRecorderSettings(_movieSettings);
            controllerSettings.SetRecordModeToManual();
            // Variable playback: encoder receives real timestamps instead of
            // fixed-rate slots. Avoids MF rate-control stalls that occur when
            // frames arrive faster than the declared constant FPS.
            controllerSettings.FrameRatePlayback = FrameRatePlayback.Variable;
            controllerSettings.FrameRate = 15;
            controllerSettings.CapFrameRate = false;

            _controller = new RecorderController(controllerSettings);
        }

        public void Start()
        {
            _controller.PrepareRecording();
            bool success = _controller.StartRecording();
            _isRecording = success;

            if (!success)
            {
                // Stop the controller to release the prepared capture pipeline —
                // leaving it in "prepared" state causes per-frame overhead even without recording.
                try { _controller.StopRecording(); } catch { }
                Debug.LogError("[Recorder] Failed to start Unity Recorder. Is Play Mode active?");
            }
            else
            {
                Debug.Log("[Recorder] Recording started (Editor/Unity Recorder)");
            }
        }

        public Task<string> StopAsync()
        {
            if (!_isRecording)
                return Task.FromResult<string>(null);

            _isRecording = false;

            _controller.StopRecording();

            string mp4Path = _outputPath + ".mp4";
            Debug.Log($"[Recorder] Recording saved: {mp4Path}");

            return Task.FromResult(mp4Path);
        }

        public void Dispose()
        {
            try { _controller?.StopRecording(); } catch { }
            _isRecording = false;
            _controller = null;
        }
    }
}
