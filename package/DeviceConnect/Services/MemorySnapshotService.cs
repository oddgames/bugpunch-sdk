using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
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
                BugpunchLog.Info("MemorySnapshotService", $"Memory snapshot started → {_currentPath}");
                return $"{{\"ok\":true,\"state\":\"in_progress\",\"path\":\"{Esc(_currentPath)}\"}}";
            }
            catch (Exception ex)
            {
                _state = SnapshotState.Failed;
                _error = ex.Message;
                BugpunchNative.ReportSdkError("MemorySnapshotService.Start", ex);
                return $"{{\"ok\":false,\"error\":\"{Esc(ex.Message)}\",\"state\":\"failed\"}}";
            }
        }

        void OnSnapshotComplete(string path, bool success)
        {
            if (success && File.Exists(path))
            {
                _fileSize = new FileInfo(path).Length;
                _state = SnapshotState.Done;
                BugpunchLog.Info("MemorySnapshotService", $"Memory snapshot complete: {path} ({_fileSize / 1024}KB)");
            }
            else
            {
                _state = SnapshotState.Failed;
                _error = success ? "File not found after snapshot" : "Snapshot capture failed";
                BugpunchNative.ReportSdkError("MemorySnapshotService.Complete", _error);
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

        // ------------------------------------------------------------------
        // Asset drill-down: list textures/meshes/materials by memory size,
        // and look up which scene objects reference a given asset.
        // ------------------------------------------------------------------

        /// <summary>
        /// List loaded assets in the given category sorted by runtime memory
        /// size descending. type ∈ { "texture", "mesh", "material" }.
        /// </summary>
        public string ListAssets(string type, int limit)
        {
            if (limit <= 0) limit = 200;
            switch (type)
            {
                case "texture": return ListTexturesByMemory(limit);
                case "mesh": return ListMeshesByMemory(limit);
                case "material": return ListMaterialsByMemory(limit);
            }
            return "[]";
        }

        /// <summary>
        /// Return the scene objects (and shader properties / sprite slots) that
        /// reference the asset with the given instanceId.
        /// </summary>
        public string GetAssetUsers(string type, int instanceId)
        {
            switch (type)
            {
                case "texture": return GetTextureUsers(instanceId);
                case "mesh": return GetMeshUsers(instanceId);
                case "material": return GetMaterialUsers(instanceId);
            }
            return "{\"users\":[]}";
        }

        string ListTexturesByMemory(int limit)
        {
            var all = Resources.FindObjectsOfTypeAll<Texture>();
            var entries = new List<(int id, string name, long sz, int w, int h, string fmt, string typeName)>();
            foreach (var t in all)
            {
                if (t == null) continue;
                if (t.hideFlags.HasFlag(HideFlags.HideAndDontSave)) continue;
                var sz = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
                var name = string.IsNullOrEmpty(t.name) ? "(unnamed)" : t.name;
                var fmt = t is Texture2D t2 ? t2.format.ToString()
                       : t is RenderTexture rt ? rt.format.ToString()
                       : t is Cubemap cm ? cm.format.ToString()
                       : "";
                var typeName = t is RenderTexture ? "RenderTexture"
                            : t is Cubemap ? "Cubemap"
                            : t is Texture2DArray ? "Texture2DArray"
                            : t is Texture3D ? "Texture3D"
                            : t is Texture2D ? "Texture2D"
                            : t.GetType().Name;
                entries.Add((t.GetInstanceID(), name, sz, t.width, t.height, fmt, typeName));
            }
            entries.Sort((a, b) => b.sz.CompareTo(a.sz));

            var sb = new StringBuilder();
            sb.Append("[");
            int n = Mathf.Min(limit, entries.Count);
            for (int i = 0; i < n; i++)
            {
                var e = entries[i];
                if (i > 0) sb.Append(",");
                sb.Append("{");
                sb.Append($"\"id\":{e.id}");
                sb.Append($",\"name\":\"{Esc(e.name)}\"");
                sb.Append($",\"sizeKB\":{(e.sz + 512) / 1024}");
                sb.Append($",\"width\":{e.w}");
                sb.Append($",\"height\":{e.h}");
                sb.Append($",\"format\":\"{Esc(e.fmt)}\"");
                sb.Append($",\"type\":\"{Esc(e.typeName)}\"");
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        string ListMeshesByMemory(int limit)
        {
            var all = Resources.FindObjectsOfTypeAll<Mesh>();
            var entries = new List<(int id, string name, long sz, int verts, int subs, int tris)>();
            foreach (var m in all)
            {
                if (m == null) continue;
                if (m.hideFlags.HasFlag(HideFlags.HideAndDontSave)) continue;
                var sz = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m);
                var name = string.IsNullOrEmpty(m.name) ? "(unnamed)" : m.name;
                int tris = 0;
                try { tris = (int)(m.triangles != null ? m.triangles.Length / 3 : 0); }
                catch { tris = 0; } // not readable
                entries.Add((m.GetInstanceID(), name, sz, m.vertexCount, m.subMeshCount, tris));
            }
            entries.Sort((a, b) => b.sz.CompareTo(a.sz));

            var sb = new StringBuilder();
            sb.Append("[");
            int n = Mathf.Min(limit, entries.Count);
            for (int i = 0; i < n; i++)
            {
                var e = entries[i];
                if (i > 0) sb.Append(",");
                sb.Append("{");
                sb.Append($"\"id\":{e.id}");
                sb.Append($",\"name\":\"{Esc(e.name)}\"");
                sb.Append($",\"sizeKB\":{(e.sz + 512) / 1024}");
                sb.Append($",\"vertexCount\":{e.verts}");
                sb.Append($",\"subMeshCount\":{e.subs}");
                sb.Append($",\"triangleCount\":{e.tris}");
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        string ListMaterialsByMemory(int limit)
        {
            var all = Resources.FindObjectsOfTypeAll<Material>();
            var entries = new List<(int id, string name, long sz, string shader)>();
            foreach (var m in all)
            {
                if (m == null) continue;
                if (m.hideFlags != HideFlags.None) continue;
                var sz = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m);
                var name = string.IsNullOrEmpty(m.name) ? "(unnamed)" : m.name;
                entries.Add((m.GetInstanceID(), name, sz, m.shader != null ? m.shader.name : ""));
            }
            entries.Sort((a, b) => b.sz.CompareTo(a.sz));

            var sb = new StringBuilder();
            sb.Append("[");
            int n = Mathf.Min(limit, entries.Count);
            for (int i = 0; i < n; i++)
            {
                var e = entries[i];
                if (i > 0) sb.Append(",");
                sb.Append("{");
                sb.Append($"\"id\":{e.id}");
                sb.Append($",\"name\":\"{Esc(e.name)}\"");
                sb.Append($",\"sizeKB\":{(e.sz + 512) / 1024}");
                sb.Append($",\"shader\":\"{Esc(e.shader)}\"");
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Reverse lookups
        // ------------------------------------------------------------------

        struct UserEntry
        {
            public string Path;        // GameObject hierarchy path
            public string Component;   // e.g. MeshRenderer, Image, Camera
            public string Via;         // shader prop / material name / "sprite" / etc.
        }

        string GetTextureUsers(int instanceId)
        {
            var users = new List<UserEntry>();
            string targetName = "";

            // Pass 1: find all materials whose shader property bindings reference this texture.
            // Map material instance ID → list of shader properties using this texture.
            var matToProps = new Dictionary<int, List<string>>();
            var allMats = Resources.FindObjectsOfTypeAll<Material>();
            foreach (var m in allMats)
            {
                if (m == null) continue;
                var shader = m.shader;
                if (shader == null) continue;
                int propCount = shader.GetPropertyCount();
                for (int i = 0; i < propCount; i++)
                {
                    if (shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                    var nameId = shader.GetPropertyNameId(i);
                    var t = m.GetTexture(nameId);
                    if (t == null) continue;
                    if (t.GetInstanceID() == instanceId)
                    {
                        if (string.IsNullOrEmpty(targetName)) targetName = t.name ?? "";
                        if (!matToProps.TryGetValue(m.GetInstanceID(), out var props))
                        {
                            props = new List<string>();
                            matToProps[m.GetInstanceID()] = props;
                        }
                        var pn = shader.GetPropertyName(i);
                        if (!props.Contains(pn)) props.Add(pn);
                    }
                }
            }

            // Pass 2: walk renderers; if any sharedMaterial is in the map, attribute usage to that GameObject.
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                foreach (var sm in r.sharedMaterials)
                {
                    if (sm == null) continue;
                    if (matToProps.TryGetValue(sm.GetInstanceID(), out var props))
                    {
                        users.Add(new UserEntry
                        {
                            Path = GetPath(r.transform),
                            Component = r.GetType().Name,
                            Via = $"{sm.name} ({string.Join(",", props)})",
                        });
                    }
                }
            }

            // UI: Image (sprite), RawImage (texture)
            var images = UnityEngine.Object.FindObjectsByType<Image>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var img in images)
            {
                if (img == null) continue;
                var sp = img.sprite;
                if (sp != null && sp.texture != null && sp.texture.GetInstanceID() == instanceId)
                {
                    if (string.IsNullOrEmpty(targetName)) targetName = sp.texture.name ?? "";
                    users.Add(new UserEntry
                    {
                        Path = GetPath(img.transform),
                        Component = "Image",
                        Via = $"sprite ({sp.name})",
                    });
                }
            }
            var rawImages = UnityEngine.Object.FindObjectsByType<RawImage>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var ri in rawImages)
            {
                if (ri == null || ri.texture == null) continue;
                if (ri.texture.GetInstanceID() == instanceId)
                {
                    if (string.IsNullOrEmpty(targetName)) targetName = ri.texture.name ?? "";
                    users.Add(new UserEntry
                    {
                        Path = GetPath(ri.transform),
                        Component = "RawImage",
                        Via = "texture",
                    });
                }
            }

            // Cameras with this as a target / replacement render target
            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var cam in cameras)
            {
                if (cam == null || cam.targetTexture == null) continue;
                if (cam.targetTexture.GetInstanceID() == instanceId)
                {
                    users.Add(new UserEntry
                    {
                        Path = GetPath(cam.transform),
                        Component = "Camera",
                        Via = "targetTexture",
                    });
                }
            }

            // Skybox
            var sky = RenderSettings.skybox;
            if (sky != null && sky.shader != null)
            {
                int propCount = sky.shader.GetPropertyCount();
                for (int i = 0; i < propCount; i++)
                {
                    if (sky.shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                    var t = sky.GetTexture(sky.shader.GetPropertyNameId(i));
                    if (t != null && t.GetInstanceID() == instanceId)
                    {
                        users.Add(new UserEntry
                        {
                            Path = "(RenderSettings.skybox)",
                            Component = "Skybox",
                            Via = sky.name,
                        });
                        break;
                    }
                }
            }

            return BuildUsersJson(instanceId, targetName, users);
        }

        string GetMeshUsers(int instanceId)
        {
            var users = new List<UserEntry>();
            string targetName = "";

            var meshFilters = UnityEngine.Object.FindObjectsByType<MeshFilter>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var mf in meshFilters)
            {
                if (mf == null) continue;
                var sm = mf.sharedMesh;
                if (sm == null || sm.GetInstanceID() != instanceId) continue;
                if (string.IsNullOrEmpty(targetName)) targetName = sm.name ?? "";
                users.Add(new UserEntry
                {
                    Path = GetPath(mf.transform),
                    Component = "MeshFilter",
                    Via = "sharedMesh",
                });
            }

            var skinned = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var s in skinned)
            {
                if (s == null) continue;
                var sm = s.sharedMesh;
                if (sm == null || sm.GetInstanceID() != instanceId) continue;
                if (string.IsNullOrEmpty(targetName)) targetName = sm.name ?? "";
                users.Add(new UserEntry
                {
                    Path = GetPath(s.transform),
                    Component = "SkinnedMeshRenderer",
                    Via = "sharedMesh",
                });
            }

            var psRenderers = UnityEngine.Object.FindObjectsByType<ParticleSystemRenderer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var p in psRenderers)
            {
                if (p == null) continue;
                var meshes = new Mesh[p.meshCount];
                int got = p.GetMeshes(meshes);
                for (int i = 0; i < got; i++)
                {
                    var pm = meshes[i];
                    if (pm == null || pm.GetInstanceID() != instanceId) continue;
                    if (string.IsNullOrEmpty(targetName)) targetName = pm.name ?? "";
                    users.Add(new UserEntry
                    {
                        Path = GetPath(p.transform),
                        Component = "ParticleSystemRenderer",
                        Via = $"mesh[{i}]",
                    });
                }
            }

            return BuildUsersJson(instanceId, targetName, users);
        }

        string GetMaterialUsers(int instanceId)
        {
            var users = new List<UserEntry>();
            string targetName = "";

            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var sms = r.sharedMaterials;
                for (int i = 0; i < sms.Length; i++)
                {
                    var sm = sms[i];
                    if (sm == null || sm.GetInstanceID() != instanceId) continue;
                    if (string.IsNullOrEmpty(targetName)) targetName = sm.name ?? "";
                    users.Add(new UserEntry
                    {
                        Path = GetPath(r.transform),
                        Component = r.GetType().Name,
                        Via = $"sharedMaterials[{i}]",
                    });
                }
            }

            // UI Graphic (Image / RawImage / Text) — material override
            var graphics = UnityEngine.Object.FindObjectsByType<Graphic>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var g in graphics)
            {
                if (g == null) continue;
                var m = g.material;
                if (m == null || m.GetInstanceID() != instanceId) continue;
                if (string.IsNullOrEmpty(targetName)) targetName = m.name ?? "";
                users.Add(new UserEntry
                {
                    Path = GetPath(g.transform),
                    Component = g.GetType().Name,
                    Via = "material",
                });
            }

            return BuildUsersJson(instanceId, targetName, users);
        }

        static string BuildUsersJson(int id, string name, List<UserEntry> users)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"id\":{id}");
            sb.Append($",\"name\":\"{Esc(name)}\"");
            sb.Append(",\"users\":[");
            for (int i = 0; i < users.Count; i++)
            {
                var u = users[i];
                if (i > 0) sb.Append(",");
                sb.Append("{");
                sb.Append($"\"path\":\"{Esc(u.Path)}\"");
                sb.Append($",\"component\":\"{Esc(u.Component)}\"");
                sb.Append($",\"via\":\"{Esc(u.Via)}\"");
                sb.Append("}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static string GetPath(Transform t)
        {
            if (t == null) return "";
            var sb = new StringBuilder(t.name);
            var p = t.parent;
            while (p != null)
            {
                sb.Insert(0, "/");
                sb.Insert(0, p.name);
                p = p.parent;
            }
            return sb.ToString();
        }

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";
    }
}
