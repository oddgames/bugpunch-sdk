using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.RemoteIDE
{
    /// <summary>
    /// Watches variables on GameObjects/Components, samples every FixedUpdate,
    /// and buffers time-series data for the dashboard to poll.
    ///
    /// <para>Field names are dotted paths resolved against the component. For example
    /// <c>attachedRigidbody.velocity</c> on a <c>Collider</c> walks the
    /// <c>attachedRigidbody</c> property then reads <c>velocity</c>.</para>
    /// </summary>
    public class WatchService : MonoBehaviour
    {
        public struct WatchEntry
        {
            public string id;           // unique watch ID
            public int instanceId;      // GameObject instance ID
            public int componentId;     // Component instance ID
            public string fieldName;    // dotted path: "foo" or "foo.bar.baz"
            public string typeName;     // terminal C# type name
            public bool isProperty;     // terminal member kind (informational)
            public string gameObjectName;
            public string componentName;
            public string hierarchyPath;

            // Declared-watch metadata — populated only for entries added via [Watch] attribute scan.
            public bool isDeclared;
            public string group;        // null when not specified
            public float min;
            public float max;
            public string ownerName;
            public int ownerInstanceId;
        }

        struct Sample
        {
            public string watchId;
            public float time;
            public int frame;
            public string value; // serialized value
        }

        readonly Dictionary<string, WatchEntry> _watches = new();
        readonly List<Sample> _sampleBuffer = new();
        int _nextId;
        const int MaxBufferedSamples = 5000; // prevent runaway memory

        // Cache resolved references to avoid reflection every frame.
        // Chain is the ordered list of MemberInfo walked from the component down.
        readonly Dictionary<string, (Component comp, MemberInfo[] chain)> _resolvedCache = new();

        // Declared-watch tracking: IDs of entries added via [Watch] scan. Lets Rescan()
        // clear stale declared entries without nuking user-pinned watches.
        readonly HashSet<string> _declaredIds = new();

        // Per-Type cache of [Watch]-marked members. Process-lifetime; recreated on domain reload.
        static readonly Dictionary<Type, MemberInfo[]> _declaredMembersByType = new();

        // Set on scene load/unload, consumed lazily by the next Rescan call.
        bool _declaredDirty = true;

        const BindingFlags MemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;

        void Start()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
            // Initial scan is deferred — _declaredDirty defaults to true and
            // Poll() rescans on first read. Walking the boot scene at startup
            // (GetComponents on every GameObject + reflection per type) is
            // 50–200ms on real devices and the first /watch/poll only fires
            // when the dashboard opens the Watch panel.
        }

        void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => _declaredDirty = true;
        void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene) => _declaredDirty = true;

        void FixedUpdate()
        {
            if (_watches.Count == 0) return;

            float t = Time.time;
            int frame = Time.frameCount;

            foreach (var kv in _watches)
            {
                var entry = kv.Value;
                var val = ReadValue(entry);
                if (_sampleBuffer.Count < MaxBufferedSamples)
                {
                    _sampleBuffer.Add(new Sample
                    {
                        watchId = entry.id,
                        time = t,
                        frame = frame,
                        value = val
                    });
                }
            }
        }

        string ReadValue(WatchEntry entry)
        {
            if (!_resolvedCache.TryGetValue(entry.id, out var resolved))
            {
                resolved = Resolve(entry);
                _resolvedCache[entry.id] = resolved;
            }

            if (resolved.comp == null)
            {
                // Component was destroyed — try re-resolve once
                resolved = Resolve(entry);
                _resolvedCache[entry.id] = resolved;
                if (resolved.comp == null) return "null";
            }

            if (resolved.chain == null) return "null";

            try
            {
                var value = ReadThroughChain(resolved.comp, resolved.chain);
                return SerializeValue(value);
            }
            catch
            {
                return "null";
            }
        }

        (Component comp, MemberInfo[] chain) Resolve(WatchEntry entry)
        {
            var go = FindByInstanceId(entry.instanceId);
            if (go == null) return (null, null);

            Component comp = null;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c != null && c.GetInstanceID() == entry.componentId)
                {
                    comp = c;
                    break;
                }
            }
            if (comp == null) return (null, null);

            var segments = SplitPath(entry.fieldName);
            if (!TryBuildChain(comp.GetType(), segments, out var chain, out _))
                return (comp, null);

            return (comp, chain);
        }

        // ── Public API ──

        /// <summary>
        /// Search all active GameObjects for fields/properties matching a query.
        /// Supports dotted paths: e.g. "attachedRigidbody.velocity" matches components
        /// where the head segment exists on the component and the remaining segments
        /// resolve via the resulting type chain.
        /// Returns JSON array of matching variables.
        /// </summary>
        public string Search(string query, int maxResults = 50)
        {
            if (string.IsNullOrWhiteSpace(query)) return "[]";

            var parts = SplitPath(query);
            var head = parts[0].ToLowerInvariant();
            var tail = parts.Length > 1 ? parts.Skip(1).ToArray() : Array.Empty<string>();
            bool dotted = tail.Length > 0;

            var sb = new StringBuilder();
            sb.Append("[");
            int count = 0;

            // Search all root GameObjects in all loaded scenes + DontDestroyOnLoad
            var rootObjects = new List<GameObject>();
            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (scene.isLoaded) rootObjects.AddRange(scene.GetRootGameObjects());
            }

            foreach (var root in rootObjects)
            {
                if (count >= maxResults) break;
                SearchRecursive(root, head, tail, dotted, sb, ref count, maxResults);
            }

            sb.Append("]");
            return sb.ToString();
        }

        void SearchRecursive(GameObject go, string head, string[] tail, bool dotted, StringBuilder sb, ref int count, int max)
        {
            if (count >= max) return;

            var components = go.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                if (count >= max) break;

                var type = comp.GetType();
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

                // Search properties
                foreach (var p in type.GetProperties(flags))
                {
                    if (count >= max) break;
                    if (p.GetIndexParameters().Length > 0) continue;
                    if (!p.CanRead) continue;
                    if (IsNoisyDeclaringType(p.DeclaringType)) continue;

                    if (!HeadMatches(p.Name, type.Name, go.name, head, dotted)) continue;

                    TryEmitResult(sb, ref count, go, comp, p, tail, dotted);
                }

                // Search fields
                foreach (var f in type.GetFields(flags))
                {
                    if (count >= max) break;
                    if (IsNoisyDeclaringType(f.DeclaringType)) continue;

                    if (!HeadMatches(f.Name, type.Name, go.name, head, dotted)) continue;

                    TryEmitResult(sb, ref count, go, comp, f, tail, dotted);
                }
            }

            // Recurse children
            for (int i = 0; i < go.transform.childCount && count < max; i++)
                SearchRecursive(go.transform.GetChild(i).gameObject, head, tail, dotted, sb, ref count, max);
        }

        void TryEmitResult(StringBuilder sb, ref int count, GameObject go, Component comp, MemberInfo head, string[] tail, bool dotted)
        {
            MemberInfo[] chain;
            Type terminalType;
            string path;

            if (dotted)
            {
                var headType = GetMemberType(head);
                if (headType == null) return;
                if (!TryBuildChain(headType, tail, out var sub, out terminalType)) return;
                chain = new MemberInfo[1 + sub.Length];
                chain[0] = head;
                sub.CopyTo(chain, 1);
                path = head.Name + "." + string.Join(".", tail);
            }
            else
            {
                chain = new[] { head };
                terminalType = GetMemberType(head);
                path = head.Name;
            }

            var terminal = chain[chain.Length - 1];
            bool isProperty = terminal is PropertyInfo;
            bool canWrite = terminal is PropertyInfo tp ? tp.CanWrite : terminal is FieldInfo;

            if (count > 0) sb.Append(",");
            AppendSearchResult(sb, go, comp, path, terminalType?.Name ?? "Unknown", isProperty, canWrite, chain);
            count++;
        }

        static bool HeadMatches(string memberName, string componentName, string goName, string head, bool dotted)
        {
            var m = memberName.ToLowerInvariant();
            if (dotted)
            {
                // In dotted mode, only match the first segment against member names —
                // matching on GO/component name doesn't make sense for a drill-down path.
                return m.Contains(head);
            }
            return m.Contains(head)
                || componentName.ToLowerInvariant().Contains(head)
                || goName.ToLowerInvariant().Contains(head);
        }

        static bool IsNoisyDeclaringType(Type t)
        {
            return t == typeof(UnityEngine.Object)
                || t == typeof(Component)
                || t == typeof(Behaviour)
                || t == typeof(MonoBehaviour);
        }

        void AppendSearchResult(StringBuilder sb, GameObject go, Component comp, string fieldPath, string typeName, bool isProperty, bool canWrite, MemberInfo[] chain)
        {
            object value = null;
            try { value = ReadThroughChain(comp, chain); }
            catch { }

            sb.Append("{");
            sb.Append($"\"instanceId\":{go.GetInstanceID()},");
            sb.Append($"\"componentId\":{comp.GetInstanceID()},");
            sb.Append($"\"gameObject\":\"{BugpunchJson.Esc(go.name)}\",");
            sb.Append($"\"component\":\"{BugpunchJson.Esc(comp.GetType().Name)}\",");
            sb.Append($"\"field\":\"{BugpunchJson.Esc(fieldPath)}\",");
            sb.Append($"\"type\":\"{BugpunchJson.Esc(typeName)}\",");
            sb.Append($"\"isProperty\":{(isProperty ? "true" : "false")},");
            sb.Append($"\"canWrite\":{(canWrite ? "true" : "false")},");
            sb.Append($"\"value\":{SerializeValue(value)},");
            sb.Append($"\"path\":\"{BugpunchJson.Esc(GetHierarchyPath(go))}\"");
            sb.Append("}");
        }

        /// <summary>
        /// Add a variable to the watch list. <paramref name="fieldName"/> may be a
        /// dotted path. Returns JSON with the watch ID.
        /// </summary>
        public string AddWatch(string instanceIdStr, string componentIdStr, string fieldName, string isPropertyStr)
        {
            if (!int.TryParse(instanceIdStr, out var instanceId))
                return "{\"ok\":false,\"error\":\"Invalid instanceId\"}";
            if (!int.TryParse(componentIdStr, out var componentId))
                return "{\"ok\":false,\"error\":\"Invalid componentId\"}";

            bool isProperty = isPropertyStr == "true";

            // Check for duplicate
            foreach (var w in _watches.Values)
            {
                if (w.instanceId == instanceId && w.componentId == componentId && w.fieldName == fieldName)
                    return $"{{\"ok\":true,\"id\":\"{w.id}\",\"duplicate\":true}}";
            }

            var go = FindByInstanceId(instanceId);
            if (go == null) return "{\"ok\":false,\"error\":\"GameObject not found\"}";

            Component comp = null;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c != null && c.GetInstanceID() == componentId)
                {
                    comp = c;
                    break;
                }
            }
            if (comp == null) return "{\"ok\":false,\"error\":\"Component not found\"}";

            // Validate path resolves against the component's type.
            var segments = SplitPath(fieldName);
            if (!TryBuildChain(comp.GetType(), segments, out var chain, out var terminalType))
                return $"{{\"ok\":false,\"error\":\"Path not found: {BugpunchJson.Esc(fieldName)}\"}}";

            var terminal = chain[chain.Length - 1];
            // Infer isProperty from the terminal member if the caller didn't pin it.
            if (terminal is PropertyInfo) isProperty = true;
            else if (terminal is FieldInfo) isProperty = false;

            var id = $"w{_nextId++}";
            var entry = new WatchEntry
            {
                id = id,
                instanceId = instanceId,
                componentId = componentId,
                fieldName = fieldName,
                typeName = terminalType?.Name ?? "Unknown",
                isProperty = isProperty,
                gameObjectName = go.name,
                componentName = comp.GetType().Name,
                hierarchyPath = GetHierarchyPath(go)
            };

            _watches[id] = entry;
            _resolvedCache[id] = (comp, chain);

            return $"{{\"ok\":true,\"id\":\"{id}\"}}";
        }

        /// <summary>
        /// Remove a watch by ID.
        /// </summary>
        public string RemoveWatch(string watchId)
        {
            if (_watches.Remove(watchId))
            {
                _resolvedCache.Remove(watchId);
                _declaredIds.Remove(watchId);
                return "{\"ok\":true}";
            }
            return "{\"ok\":false,\"error\":\"Watch not found\"}";
        }

        /// <summary>
        /// Clear all watches. Declared watches are re-registered automatically
        /// on the next poll/list call (the dirty flag triggers a rescan), so
        /// this effectively wipes only user-pinned ones from the user's POV.
        /// </summary>
        public string ClearAll()
        {
            _watches.Clear();
            _resolvedCache.Clear();
            _declaredIds.Clear();
            _sampleBuffer.Clear();
            _declaredDirty = true;
            return "{\"ok\":true}";
        }

        /// <summary>
        /// Re-scan all loaded scenes for fields/properties marked with <c>[Watch]</c>
        /// and ensure they're registered as declared watches. Existing declared entries
        /// are dropped and replaced; user-pinned watches are left untouched.
        /// </summary>
        public string Rescan()
        {
            // Drop previous declared entries.
            foreach (var id in _declaredIds)
            {
                _watches.Remove(id);
                _resolvedCache.Remove(id);
            }
            _declaredIds.Clear();

            int added = 0;
            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    ScanGameObject(root, ref added);
            }

            _declaredDirty = false;
            return $"{{\"ok\":true,\"count\":{added}}}";
        }

        void ScanGameObject(GameObject go, ref int added)
        {
            var components = go.GetComponents<MonoBehaviour>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                var members = GetDeclaredMembers(comp.GetType());
                if (members.Length == 0) continue;

                foreach (var m in members)
                {
                    var attr = (WatchAttribute)Attribute.GetCustomAttribute(m, typeof(WatchAttribute));
                    if (attr == null) continue;

                    var (ownerGo, ownerName) = ResolveOwner(comp, attr.Owner);
                    if (TryAddDeclared(go, comp, m, attr, ownerGo, ownerName))
                        added++;
                }
            }

            for (int i = 0; i < go.transform.childCount; i++)
                ScanGameObject(go.transform.GetChild(i).gameObject, ref added);
        }

        static MemberInfo[] GetDeclaredMembers(Type t)
        {
            if (_declaredMembersByType.TryGetValue(t, out var cached)) return cached;

            var list = new List<MemberInfo>();
            foreach (var p in t.GetProperties(MemberFlags))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                if (Attribute.IsDefined(p, typeof(WatchAttribute))) list.Add(p);
            }
            foreach (var f in t.GetFields(MemberFlags))
            {
                if (Attribute.IsDefined(f, typeof(WatchAttribute))) list.Add(f);
            }

            var arr = list.ToArray();
            _declaredMembersByType[t] = arr;
            return arr;
        }

        static (GameObject ownerGo, string ownerName) ResolveOwner(Component comp, WatchOwner owner)
        {
            switch (owner)
            {
                case WatchOwner.Parent:
                    var p = comp.transform.parent;
                    if (p != null) return (p.gameObject, p.gameObject.name);
                    return (comp.gameObject, comp.gameObject.name);
                case WatchOwner.Root:
                    var r = comp.transform.root.gameObject;
                    return (r, r.name);
                default:
                    return (comp.gameObject, comp.gameObject.name);
            }
        }

        bool TryAddDeclared(GameObject go, Component comp, MemberInfo member, WatchAttribute attr, GameObject ownerGo, string ownerName)
        {
            var fieldName = member.Name;
            var segments = SplitPath(fieldName);
            if (!TryBuildChain(comp.GetType(), segments, out var chain, out var terminalType))
                return false;

            bool isProperty = member is PropertyInfo;

            var id = $"w{_nextId++}";
            var entry = new WatchEntry
            {
                id = id,
                instanceId = go.GetInstanceID(),
                componentId = comp.GetInstanceID(),
                fieldName = fieldName,
                typeName = terminalType?.Name ?? "Unknown",
                isProperty = isProperty,
                gameObjectName = go.name,
                componentName = comp.GetType().Name,
                hierarchyPath = GetHierarchyPath(go),

                isDeclared = true,
                group = attr.Group,
                min = attr.Min,
                max = attr.Max,
                ownerName = ownerName,
                ownerInstanceId = ownerGo != null ? ownerGo.GetInstanceID() : go.GetInstanceID(),
            };

            _watches[id] = entry;
            _resolvedCache[id] = (comp, chain);
            _declaredIds.Add(id);
            return true;
        }

        /// <summary>
        /// Get current watch list.
        /// </summary>
        public string GetWatchList()
        {
            // Same lazy-rebuild trigger Poll uses — covers the case where the
            // IDE hits /watch/list as its first call when opening the panel,
            // which is the typical path on a fresh tunnel connection.
            if (_declaredDirty) Rescan();

            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;
            foreach (var entry in _watches.Values)
            {
                if (!first) sb.Append(",");
                first = false;

                var currentValue = ReadValue(entry);
                sb.Append("{");
                sb.Append($"\"id\":\"{entry.id}\",");
                sb.Append($"\"instanceId\":{entry.instanceId},");
                sb.Append($"\"componentId\":{entry.componentId},");
                sb.Append($"\"field\":\"{BugpunchJson.Esc(entry.fieldName)}\",");
                sb.Append($"\"type\":\"{BugpunchJson.Esc(entry.typeName)}\",");
                sb.Append($"\"isProperty\":{(entry.isProperty ? "true" : "false")},");
                sb.Append($"\"gameObject\":\"{BugpunchJson.Esc(entry.gameObjectName)}\",");
                sb.Append($"\"component\":\"{BugpunchJson.Esc(entry.componentName)}\",");
                sb.Append($"\"path\":\"{BugpunchJson.Esc(entry.hierarchyPath)}\",");
                if (entry.isDeclared)
                {
                    sb.Append("\"isDeclared\":true,");
                    if (entry.group != null) sb.Append($"\"group\":\"{BugpunchJson.Esc(entry.group)}\",");
                    sb.Append($"\"min\":{entry.min.ToString("G", System.Globalization.CultureInfo.InvariantCulture)},");
                    sb.Append($"\"max\":{entry.max.ToString("G", System.Globalization.CultureInfo.InvariantCulture)},");
                    sb.Append($"\"ownerName\":\"{BugpunchJson.Esc(entry.ownerName)}\",");
                    sb.Append($"\"ownerInstanceId\":{entry.ownerInstanceId},");
                }
                sb.Append($"\"value\":{currentValue}");
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Poll buffered samples since last poll. Clears the buffer.
        /// Returns JSON with samples array and current values.
        /// </summary>
        public string Poll()
        {
            // Lazy rebuild after scene load — keeps the panel's view of declared
            // watches consistent without forcing the client to call /watch/rescan.
            if (_declaredDirty) Rescan();

            var sb = new StringBuilder();
            sb.Append("{\"samples\":[");

            for (int i = 0; i < _sampleBuffer.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var s = _sampleBuffer[i];
                sb.Append($"[\"{s.watchId}\",{s.time:F4},{s.frame},{s.value}]");
            }

            sb.Append("],\"current\":");
            sb.Append(GetWatchList());
            sb.Append("}");

            _sampleBuffer.Clear();
            return sb.ToString();
        }

        /// <summary>
        /// Apply a value to a watched variable. <paramref name="fieldName"/> may be a
        /// dotted path — the chain is walked to the penultimate object, then the
        /// terminal member is assigned. Intermediate struct (value-type) mutation
        /// won't propagate back to the component — writes only stick cleanly when
        /// intermediates are reference types (Rigidbody, Transform, etc.).
        /// </summary>
        public string ApplyValue(string instanceIdStr, string componentIdStr, string fieldName, string valueJson)
        {
            if (!int.TryParse(instanceIdStr, out var instanceId))
                return "{\"ok\":false,\"error\":\"Invalid instanceId\"}";
            if (!int.TryParse(componentIdStr, out var componentId))
                return "{\"ok\":false,\"error\":\"Invalid componentId\"}";

            var go = FindByInstanceId(instanceId);
            if (go == null) return "{\"ok\":false,\"error\":\"GameObject not found\"}";

            Component comp = null;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c != null && c.GetInstanceID() == componentId)
                {
                    comp = c;
                    break;
                }
            }
            if (comp == null) return "{\"ok\":false,\"error\":\"Component not found\"}";

            try
            {
                var segments = SplitPath(fieldName);
                if (!TryBuildChain(comp.GetType(), segments, out var chain, out var terminalType))
                    return $"{{\"ok\":false,\"error\":\"Path not found: {BugpunchJson.Esc(fieldName)}\"}}";

                // Walk to penultimate
                object parent = comp;
                for (int i = 0; i < chain.Length - 1; i++)
                {
                    if (parent == null)
                        return "{\"ok\":false,\"error\":\"Intermediate value is null\"}";
                    var m = chain[i];
                    if (m is PropertyInfo pi) parent = pi.GetValue(parent);
                    else if (m is FieldInfo fi) parent = fi.GetValue(parent);
                }
                if (parent == null)
                    return "{\"ok\":false,\"error\":\"Parent is null\"}";

                // Parse the incoming JSON value
                var wrapper = $"{{\"__v\":{valueJson}}}";
                var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(wrapper);
                if (dict == null || !dict.ContainsKey("__v"))
                    return "{\"ok\":false,\"error\":\"Failed to parse value\"}";
                var converted = ConvertJsonValue(dict["__v"], terminalType);

                var terminal = chain[chain.Length - 1];
                if (terminal is PropertyInfo pp)
                {
                    if (!pp.CanWrite)
                        return "{\"ok\":false,\"error\":\"Property not writable\"}";
                    pp.SetValue(parent, converted);
                }
                else if (terminal is FieldInfo ff)
                {
                    ff.SetValue(parent, converted);
                }
                else
                {
                    return "{\"ok\":false,\"error\":\"Unsupported terminal member\"}";
                }

                // Invalidate cache so next read gets fresh value
                foreach (var w in _watches.Values)
                {
                    if (w.instanceId == instanceId && w.componentId == componentId && w.fieldName == fieldName)
                        _resolvedCache.Remove(w.id);
                }

                return "{\"ok\":true}";
            }
            catch (Exception ex)
            {
                return $"{{\"ok\":false,\"error\":\"{BugpunchJson.Esc(ex.Message)}\"}}";
            }
        }

        /// <summary>
        /// Apply a batch of values. Body is JSON array:
        /// [{ instanceId, componentId, field, value }, ...]
        /// </summary>
        public string ApplyBatch(string json)
        {
            try
            {
                var items = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
                if (items == null) return "{\"ok\":false,\"error\":\"Invalid JSON\"}";

                int applied = 0;
                var errors = new StringBuilder();

                foreach (var item in items)
                {
                    var instId = item.ContainsKey("instanceId") ? Convert.ToInt32(item["instanceId"]) : 0;
                    var compId = item.ContainsKey("componentId") ? Convert.ToInt32(item["componentId"]) : 0;
                    var field = item.ContainsKey("field") ? item["field"]?.ToString() : null;
                    var value = item.ContainsKey("value") ? item["value"] : null;

                    if (instId == 0 || compId == 0 || string.IsNullOrEmpty(field))
                    {
                        errors.Append($"Missing instanceId/componentId/field; ");
                        continue;
                    }

                    var valueJson = Newtonsoft.Json.JsonConvert.SerializeObject(value);
                    var result = ApplyValue(instId.ToString(), compId.ToString(), field, valueJson);
                    if (result.Contains("\"ok\":true"))
                        applied++;
                    else
                        errors.Append($"{field}: {result}; ");
                }

                if (errors.Length > 0)
                    return $"{{\"ok\":false,\"applied\":{applied},\"error\":\"{BugpunchJson.Esc(errors.ToString())}\"}}";
                return $"{{\"ok\":true,\"applied\":{applied}}}";
            }
            catch (Exception ex)
            {
                return $"{{\"ok\":false,\"error\":\"{BugpunchJson.Esc(ex.Message)}\"}}";
            }
        }

        // ── Helpers ──

        static string[] SplitPath(string path) =>
            string.IsNullOrEmpty(path)
                ? Array.Empty<string>()
                : path.Split('.').Where(s => s.Length > 0).ToArray();

        /// <summary>
        /// Walks a dotted-path chain against a type, resolving each segment to a
        /// property or field. Returns false if any segment can't be resolved.
        /// </summary>
        static bool TryBuildChain(Type startType, string[] segments, out MemberInfo[] chain, out Type terminalType)
        {
            chain = null;
            terminalType = null;
            if (segments == null || segments.Length == 0) return false;

            var members = new List<MemberInfo>(segments.Length);
            var t = startType;
            foreach (var seg in segments)
            {
                if (t == null) return false;

                MemberInfo m = null;
                var prop = t.GetProperty(seg, MemberFlags);
                if (prop != null && prop.CanRead && prop.GetIndexParameters().Length == 0)
                {
                    m = prop;
                    t = prop.PropertyType;
                }
                else
                {
                    var field = t.GetField(seg, MemberFlags);
                    if (field != null)
                    {
                        m = field;
                        t = field.FieldType;
                    }
                }

                if (m == null) return false;
                members.Add(m);
            }

            chain = members.ToArray();
            terminalType = t;
            return true;
        }

        static Type GetMemberType(MemberInfo m) =>
            m switch
            {
                PropertyInfo p => p.PropertyType,
                FieldInfo f => f.FieldType,
                _ => null
            };

        static object ReadThroughChain(object start, MemberInfo[] chain)
        {
            object v = start;
            foreach (var m in chain)
            {
                if (v == null) return null;
                if (m is PropertyInfo p) v = p.GetValue(v);
                else if (m is FieldInfo f) v = f.GetValue(v);
                else return null;
            }
            return v;
        }

        static string GetHierarchyPath(GameObject go)
        {
            var parts = new List<string>();
            var t = go.transform;
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        static GameObject FindByInstanceId(int id)
        {
            // Use Unity's internal fast lookup
            if (_findObjectMethod == null)
            {
                _findObjectMethod = typeof(UnityEngine.Object).GetMethod(
                    "FindObjectFromInstanceID",
                    BindingFlags.NonPublic | BindingFlags.Static);
            }
            if (_findObjectMethod != null)
            {
                try
                {
                    var obj = _findObjectMethod.Invoke(null, new object[] { id }) as UnityEngine.Object;
                    if (obj is GameObject go) return go;
                    if (obj is Component comp) return comp.gameObject;
                }
                catch { }
            }

            // Fallback
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (g.GetInstanceID() == id) return g;
            }
            return null;
        }
        static MethodInfo _findObjectMethod;

        static string SerializeValue(object value)
        {
            if (value == null) return "null";
            if (value is string s) return $"\"{BugpunchJson.Esc(s)}\"";
            if (value is bool b) return b ? "true" : "false";
            if (value is int or float or double or long or short or byte or uint or ulong or ushort or sbyte)
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            if (value is Vector2 v2) return $"{{\"x\":{v2.x:G},\"y\":{v2.y:G}}}";
            if (value is Vector3 v3) return $"{{\"x\":{v3.x:G},\"y\":{v3.y:G},\"z\":{v3.z:G}}}";
            if (value is Vector4 v4) return $"{{\"x\":{v4.x:G},\"y\":{v4.y:G},\"z\":{v4.z:G},\"w\":{v4.w:G}}}";
            if (value is Color c) return $"{{\"r\":{c.r:G},\"g\":{c.g:G},\"b\":{c.b:G},\"a\":{c.a:G}}}";
            if (value is Quaternion q) return $"{{\"x\":{q.x:G},\"y\":{q.y:G},\"z\":{q.z:G},\"w\":{q.w:G}}}";
            if (value is Enum e) return $"\"{e}\"";
            if (value is UnityEngine.Object uo)
            {
                if (uo == null) return "null";
                return $"\"{BugpunchJson.Esc(uo.name)} ({uo.GetType().Name})\"";
            }
            try { return $"\"{BugpunchJson.Esc(value.ToString())}\""; }
            catch { return "\"(error)\""; }
        }

        /// <summary>
        /// Convert JSON-deserialized value to target CLR type.
        /// Mirrors InspectorService.ConvertJsonValue.
        /// </summary>
        static object ConvertJsonValue(object jsonValue, Type targetType)
        {
            if (jsonValue == null) return null;

            var jobj = jsonValue as Newtonsoft.Json.Linq.JObject;

            if (targetType == typeof(Vector2) && jobj != null)
                return new Vector2(jobj.Value<float>("x"), jobj.Value<float>("y"));
            if (targetType == typeof(Vector3) && jobj != null)
                return new Vector3(jobj.Value<float>("x"), jobj.Value<float>("y"), jobj.Value<float>("z"));
            if (targetType == typeof(Vector4) && jobj != null)
                return new Vector4(jobj.Value<float>("x"), jobj.Value<float>("y"), jobj.Value<float>("z"), jobj.Value<float>("w"));
            if (targetType == typeof(Quaternion) && jobj != null)
                return Quaternion.Euler(jobj.Value<float>("x"), jobj.Value<float>("y"), jobj.Value<float>("z"));
            if (targetType == typeof(Color) && jobj != null)
                return new Color(jobj.Value<float>("r"), jobj.Value<float>("g"), jobj.Value<float>("b"), jobj.Value<float>("a"));
            if (targetType == typeof(Rect) && jobj != null)
                return new Rect(jobj.Value<float>("x"), jobj.Value<float>("y"), jobj.Value<float>("width"), jobj.Value<float>("height"));
            if (targetType.IsEnum && jsonValue is string es)
                return Enum.Parse(targetType, es);
            if (targetType == typeof(bool))
                return Convert.ToBoolean(jsonValue);
            if (targetType == typeof(int))
                return Convert.ToInt32(jsonValue);
            if (targetType == typeof(float))
                return Convert.ToSingle(jsonValue);
            if (targetType == typeof(double))
                return Convert.ToDouble(jsonValue);
            if (targetType == typeof(string))
                return Convert.ToString(jsonValue);

            if (jsonValue is Newtonsoft.Json.Linq.JToken jt)
                return jt.ToObject(targetType);

            return Convert.ChangeType(jsonValue, targetType);
        }

    }
}
