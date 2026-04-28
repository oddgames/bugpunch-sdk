using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ODDGames.Recorder
{
    /// <summary>
    /// Captures per-frame session timestamps during video recording.
    /// Produces a CSV sidecar (video_timestamps.csv) that maps video frame indices
    /// to session-relative wall-clock times, allowing the viewer to accurately sync
    /// video playback with log/event timelines even when the encoder drops frames or stalls.
    ///
    /// Format: frameIndex,sessionTime\n  (no header, times in seconds with 3 decimal places)
    /// </summary>
    internal class VideoTimestampTracker : MonoBehaviour
    {
        float _sessionStartTime;
        readonly List<float> _frameTimes = new();
        bool _tracking;

        static VideoTimestampTracker _instance;

        /// <summary>
        /// Begins tracking. Called once per recording session.
        /// </summary>
        /// <param name="sessionStartTime">Time.realtimeSinceStartup at session start.</param>
        internal static VideoTimestampTracker Begin(float sessionStartTime)
        {
            if (_instance != null)
            {
                Debug.LogWarning("[VideoTimestampTracker] Already tracking — stopping previous tracker.");
                _instance.Stop();
                Destroy(_instance.gameObject);
                _instance = null;
            }

            var go = new GameObject("[VideoTimestampTracker]");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);

            var tracker = go.AddComponent<VideoTimestampTracker>();
            tracker._sessionStartTime = sessionStartTime;
            tracker._tracking = true;
            _instance = tracker;
            return tracker;
        }

        void LateUpdate()
        {
            if (!_tracking) return;
            _frameTimes.Add(Time.realtimeSinceStartup - _sessionStartTime);
        }

        /// <summary>
        /// Stops tracking and writes the CSV sidecar to disk.
        /// </summary>
        /// <param name="outputPath">Full path for the CSV file (e.g. sessionFolder/video_timestamps.csv).</param>
        internal void StopAndWrite(string outputPath)
        {
            Stop();

            if (_frameTimes.Count == 0) return;

            try
            {
                var sb = new StringBuilder(_frameTimes.Count * 16);
                for (int i = 0; i < _frameTimes.Count; i++)
                {
                    sb.Append(i);
                    sb.Append(',');
                    sb.AppendFormat("{0:F3}", _frameTimes[i]);
                    sb.Append('\n');
                }
                File.WriteAllText(outputPath, sb.ToString());
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VideoTimestampTracker] Failed to write sidecar: {ex.Message}");
            }
        }

        void Stop()
        {
            _tracking = false;
            if (_instance == this)
                _instance = null;
        }

        void OnDestroy()
        {
            Stop();
        }
    }
}
