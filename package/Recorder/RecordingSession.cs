using System;
using System.Threading.Tasks;
using ODDGames.Recorder.Internal;

namespace ODDGames.Recorder
{
    /// <summary>
    /// Handle for an active recording session. Dispose or call StopAsync to end the recording.
    /// </summary>
    public class RecordingSession : IDisposable
    {
        readonly IRecorderBackend _backend;
        bool _disposed;

        internal RecordingSession(IRecorderBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        /// <summary>
        /// Whether this session is currently recording.
        /// </summary>
        public bool IsRecording => !_disposed && _backend.IsRecording;

        internal void Start()
        {
            _backend.Start();
        }

        /// <summary>
        /// Stops recording and returns the output file path.
        /// </summary>
        public async Task<string> StopAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecordingSession));

            string path = await _backend.StopAsync();
            return path;
        }

        /// <summary>
        /// Disposes the session. If still recording, stops the recording.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _backend.Dispose();
        }
    }
}
