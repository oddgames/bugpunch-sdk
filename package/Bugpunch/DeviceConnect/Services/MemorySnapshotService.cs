using System;
using System.IO;
using UnityEngine;
using Unity.Profiling.Memory;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Triggers Unity Memory Profiler snapshots and tracks their completion.
    /// The resulting .snap files can be downloaded via the File service and
    /// opened in Unity's Memory Profiler window.
    /// </summary>
    public class MemorySnapshotService
    {
        enum SnapshotState { Idle, InProgress, Done, Failed }

        SnapshotState _state = SnapshotState.Idle;
        string _currentPath;
        string _error;
        long _fileSize;
        DateTime _startTime;

        /// <summary>
        /// Directory where snapshots are saved. Uses the app's persistent data path
        /// so they survive across sessions and are accessible via the file browser.
        /// </summary>
        string SnapshotDir
        {
            get
            {
                var dir = Path.Combine(Application.persistentDataPath, "bugpunch_snapshots");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>
        /// Start a memory snapshot capture. Returns JSON with status.
        /// </summary>
        public string TakeSnapshot()
        {
            if (_state == SnapshotState.InProgress)
                return "{\"ok\":false,\"error\":\"Snapshot already in progress\",\"state\":\"in_progress\"}";

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            _currentPath = Path.Combine(SnapshotDir, $"memory_{timestamp}.snap");
            _state = SnapshotState.InProgress;
            _error = null;
            _fileSize = 0;
            _startTime = DateTime.Now;

            try
            {
                MemoryProfiler.TakeSnapshot(_currentPath, OnSnapshotComplete);
                Debug.Log($"[Bugpunch] Memory snapshot started → {_currentPath}");
                return $"{{\"ok\":true,\"state\":\"in_progress\",\"path\":\"{Esc(_currentPath)}\"}}";
            }
            catch (Exception ex)
            {
                _state = SnapshotState.Failed;
                _error = ex.Message;
                Debug.LogError($"[Bugpunch] Memory snapshot failed to start: {ex.Message}");
                return $"{{\"ok\":false,\"error\":\"{Esc(ex.Message)}\",\"state\":\"failed\"}}";
            }
        }

        void OnSnapshotComplete(string path, bool success)
        {
            if (success && File.Exists(path))
            {
                _fileSize = new FileInfo(path).Length;
                _state = SnapshotState.Done;
                Debug.Log($"[Bugpunch] Memory snapshot complete: {path} ({_fileSize / 1024}KB)");
            }
            else
            {
                _state = SnapshotState.Failed;
                _error = success ? "File not found after snapshot" : "Snapshot capture failed";
                Debug.LogError($"[Bugpunch] Memory snapshot failed: {_error}");
            }
        }

        /// <summary>
        /// Check the status of the current/last snapshot.
        /// </summary>
        public string GetStatus()
        {
            var elapsed = _state == SnapshotState.InProgress
                ? (DateTime.Now - _startTime).TotalSeconds
                : 0;

            switch (_state)
            {
                case SnapshotState.Idle:
                    return "{\"state\":\"idle\"}";

                case SnapshotState.InProgress:
                    return $"{{\"state\":\"in_progress\",\"elapsed\":{elapsed:F1}}}";

                case SnapshotState.Done:
                    return $"{{\"state\":\"done\",\"path\":\"{Esc(_currentPath)}\",\"sizeKB\":{_fileSize / 1024}}}";

                case SnapshotState.Failed:
                    return $"{{\"state\":\"failed\",\"error\":\"{Esc(_error ?? "Unknown error")}\"}}";

                default:
                    return "{\"state\":\"unknown\"}";
            }
        }

        /// <summary>
        /// List previously saved snapshots.
        /// </summary>
        public string ListSnapshots()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[");
            bool first = true;

            var dir = SnapshotDir;
            if (Directory.Exists(dir))
            {
                var files = Directory.GetFiles(dir, "*.snap");
                Array.Sort(files);
                Array.Reverse(files); // newest first
                foreach (var f in files)
                {
                    var info = new FileInfo(f);
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append($"{{\"path\":\"{Esc(f)}\",\"name\":\"{Esc(info.Name)}\",\"sizeKB\":{info.Length / 1024},\"created\":\"{info.CreationTime:yyyy-MM-ddTHH:mm:ss}\"}}");
                }
            }

            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Delete a snapshot file.
        /// </summary>
        public string DeleteSnapshot(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return "{\"ok\":false,\"error\":\"File not found\"}";

            // Safety: only delete files in our snapshot directory
            if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(SnapshotDir)))
                return "{\"ok\":false,\"error\":\"Path outside snapshot directory\"}";

            try
            {
                File.Delete(path);
                return "{\"ok\":true}";
            }
            catch (Exception ex)
            {
                return $"{{\"ok\":false,\"error\":\"{Esc(ex.Message)}\"}}";
            }
        }

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";
    }
}
