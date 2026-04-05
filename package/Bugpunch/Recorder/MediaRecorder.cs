using System;
using System.Threading.Tasks;
using ODDGames.Recorder.Internal;
using UnityEngine;

namespace ODDGames.Recorder
{
    /// <summary>
    /// Public static API for starting cross-platform video recordings.
    /// </summary>
    public static class MediaRecorder
    {
        /// <summary>
        /// Factory for creating an editor-specific recorder backend.
        /// Registered by the Editor assembly via [InitializeOnLoad].
        /// </summary>
        internal static Func<RecorderSettings, IRecorderBackend> EditorBackendFactory { get; set; }

        /// <summary>
        /// Starts a recording session with the given settings.
        /// </summary>
        /// <param name="settings">Recording configuration. Null uses defaults.</param>
        /// <returns>A RecordingSession handle to control and stop the recording.</returns>
        public static Task<RecordingSession> StartAsync(RecorderSettings settings = null)
        {
            settings ??= new RecorderSettings();

            IRecorderBackend backend;

#if UNITY_EDITOR
            if (EditorBackendFactory != null)
            {
                backend = EditorBackendFactory(settings);
            }
            else
            {
                Debug.LogWarning("[Recorder] Editor backend not registered. Is the ODDGames.Recorder.Editor assembly loaded?");
                backend = new NullBackend();
            }
#elif UNITY_ANDROID
            backend = NativeBridge.CreateAndroidBackend(settings);
#elif UNITY_IOS
            backend = NativeBridge.CreateiOSBackend(settings);
#elif UNITY_STANDALONE_WIN
            backend = NativeBridge.CreateWindowsBackend(settings);
#elif UNITY_STANDALONE_OSX
            backend = NativeBridge.CreateMacOSBackend(settings);
#else
            Debug.LogWarning("[Recorder] Platform not supported for recording.");
            backend = new NullBackend();
#endif

            var session = new RecordingSession(backend);
            session.Start();
            return Task.FromResult(session);
        }
    }
}
