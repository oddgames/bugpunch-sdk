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

            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            _currentPath = Path.Combine(SnapshotDir, $"memory_{timestamp}.snap");
            _state = SnapshotState.InProgress;
            _error = null;
            _fileSize = 0;
            _startTime = DateTime.UtcNow;

            try
            {
                MemoryProfiler.TakeSnapshot(_currentPath, OnSnapshotComplete);
                Debug.Log($"[Bugpunch.MemorySnapshotService] Memory snapshot started → {_currentPath}");
                return $"{{\"ok\":true,\"state\":\"in_progress\",\"path\":\"{Esc(_currentPath)}\"}}";
            }
            catch (Exception ex)
            {
                _state = SnapshotState.Failed;
                _error = ex.Message;
                Debug.LogError($"[Bugpunch.MemorySnapshotService] Memory snapshot failed to start: {ex.Message}");
                return $"{{\"ok\":false,\"error\":\"{Esc(ex.Message)}\",\"state\":\"failed\"}}";
            }
        }

        void OnSnapshotComplete(string path, bool success)
        {
            if (success && File.Exists(path))
            {
                _fileSize = new FileInfo(path).Length;
                _state = SnapshotState.Done;
                Debug.Log($"[Bugpunch.MemorySnapshotService] Memory snapshot complete: {path} ({_fileSize / 1024}KB)");
            }
            else
            {
                _state = SnapshotState.Failed;
                _error = success ? "File not found after snapshot" : "Snapshot capture failed";
                Debug.LogError($"[Bugpunch.MemorySnapshotService] Memory snapshot failed: {_error}");
            }
        }

        /// <summary>
        /// Check the status of the current/last snapshot.
        /// </summary>
        public string GetStatus()
        {
            var elapsed = _state == SnapshotState.InProgress
                ? (DateTime.UtcNow - _startTime).TotalSeconds
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

        /// <summary>
        /// Get current memory stats as JSON. Polled by the dashboard to show
        /// live memory usage with scene context.
        /// </summary>
        public string GetMemoryStats()
        {
            var totalAllocated = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            var totalReserved = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
            var totalUnused = UnityEngine.Profiling.Profiler.GetTotalUnusedReservedMemoryLong();
            var monoHeap = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
            var monoUsed = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            var gfxTotal = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
            var tempAllocator = UnityEngine.Profiling.Profiler.GetTempAllocatorSize();

            // Current scene
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var sceneName = scene.IsValid() ? scene.name : "Unknown";

            // Texture/mesh/audio memory (count loaded assets)
            var textures = Resources.FindObjectsOfTypeAll<Texture>();
            long texMem = 0;
            foreach (var t in textures)
                if (t != null) texMem += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);

            var meshes = Resources.FindObjectsOfTypeAll<Mesh>();
            long meshMem = 0;
            foreach (var m in meshes)
                if (m != null) meshMem += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m);

            var clips = Resources.FindObjectsOfTypeAll<AudioClip>();
            long audioMem = 0;
            foreach (var c in clips)
                if (c != null) audioMem += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(c);

            var animations = Resources.FindObjectsOfTypeAll<AnimationClip>();
            long animMem = 0;
            foreach (var a in animations)
                if (a != null) animMem += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(a);

            var materials = Resources.FindObjectsOfTypeAll<Material>();
            long matMem = 0;
            foreach (var mat in materials)
                if (mat != null) matMem += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mat);

            return $"{{" +
                $"\"totalAllocatedMB\":{totalAllocated / (1024.0 * 1024.0):F1}," +
                $"\"totalReservedMB\":{totalReserved / (1024.0 * 1024.0):F1}," +
                $"\"totalUnusedMB\":{totalUnused / (1024.0 * 1024.0):F1}," +
                $"\"gcHeapMB\":{monoHeap / (1024.0 * 1024.0):F1}," +
                $"\"gcUsedMB\":{monoUsed / (1024.0 * 1024.0):F1}," +
                $"\"gfxMB\":{gfxTotal / (1024.0 * 1024.0):F1}," +
                $"\"tempAllocatorMB\":{tempAllocator / (1024.0 * 1024.0):F1}," +
                $"\"scene\":\"{Esc(sceneName)}\"," +
                $"\"breakdown\":{{" +
                    $"\"texturesMB\":{texMem / (1024.0 * 1024.0):F1},\"textureCount\":{textures.Length}," +
                    $"\"meshesMB\":{meshMem / (1024.0 * 1024.0):F1},\"meshCount\":{meshes.Length}," +
                    $"\"audioMB\":{audioMem / (1024.0 * 1024.0):F1},\"audioCount\":{clips.Length}," +
                    $"\"animationMB\":{animMem / (1024.0 * 1024.0):F1},\"animationCount\":{animations.Length}," +
                    $"\"materialsMB\":{matMem / (1024.0 * 1024.0):F1},\"materialCount\":{materials.Length}" +
                $"}}" +
            $"}}";
        }

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";
    }
}
