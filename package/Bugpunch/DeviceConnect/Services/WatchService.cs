using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Watches variables on GameObjects/Components, samples every FixedUpdate,
    /// and buffers time-series data for the dashboard to poll.
    /// </summary>
    public class WatchService : MonoBehaviour
    {
        public struct WatchEntry
        {
            public string id;           // unique watch ID
            public int instanceId;      // GameObject instance ID
            public int componentId;     // Component instance ID
            public string fieldName;    // field or property name
            public string typeName;     // C# type name
            public bool isProperty;     // true = property, false = field
            public string gameObjectName;
            public string componentName;
            public string hierarchyPath;
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

        // Cache resolved references to avoid reflection every frame
        readonly Dictionary<string, (Component comp, MemberInfo member)> _resolvedCache = new();

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

            try
            {
                object value;
                if (resolved.member is PropertyInfo pi)
                    value = pi.GetValue(resolved.comp);
                else if (resolved.member is FieldInfo fi)
                    value = fi.GetValue(resolved.comp);
                else
                    return "null";

                return SerializeValue(value);
            }
            catch
            {
                return "null";
            }
        }

        (Component comp, MemberInfo member) Resolve(WatchEntry entry)
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

            var type = comp.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            if (entry.isProperty)
            {
                var pi = type.GetProperty(entry.fieldName, flags);
                if (pi != null && pi.CanRead) return (comp, pi);
            }
            else
            {
                var fi = type.GetField(entry.fieldName, flags);
                if (fi != null) return (comp, fi);
            }

            // Fallback: try both
            var prop = type.GetProperty(entry.fieldName, flags);
            if (prop != null && prop.CanRead) return (comp, prop);
            var field = type.GetField(entry.fieldName, flags);
            if (field != null) return (comp, field);

            return (comp, null);
        }

        // ── Public API ──

        /// <summary>
        /// Search all active GameObjects for fields/properties matching a query.
        /// Returns JSON array of matching variables.
        /// </summary>
        public string Search(string query, int maxResults = 50)
        {
            if (string.IsNullOrWhiteSpace(query)) return "[]";

            var q = query.ToLowerInvariant();
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
                SearchRecursive(root, q, sb, ref count, maxResults);
            }

            sb.Append("]");
            return sb.ToString();
        }

        void SearchRecursive(GameObject go, string query, StringBuilder sb, ref int count, int max)
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
                    // Skip noisy base class
                    if (p.DeclaringType == typeof(UnityEngine.Object) || p.DeclaringType == typeof(Component) || p.DeclaringType == typeof(Behaviour) || p.DeclaringType == typeof(MonoBehaviour))
                        continue;

                    if (MatchesQuery(p.Name, type.Name, go.name, query))
                    {
                        if (count > 0) sb.Append(",");
                        AppendSearchResult(sb, go, comp, p.Name, p.PropertyType.Name, true, p.CanWrite);
                        count++;
                    }
                }

                // Search fields
                foreach (var f in type.GetFields(flags))
                {
                    if (count >= max) break;
                    if (f.DeclaringType == typeof(UnityEngine.Object) || f.DeclaringType == typeof(Component) || f.DeclaringType == typeof(Behaviour) || f.DeclaringType == typeof(MonoBehaviour))
                        continue;

                    if (MatchesQuery(f.Name, type.Name, go.name, query))
                    {
                        if (count > 0) sb.Append(",");
                        AppendSearchResult(sb, go, comp, f.Name, f.FieldType.Name, false, true);
                        count++;
                    }
                }
            }

            // Recurse children
            for (int i = 0; i < go.transform.childCount && count < max; i++)
                SearchRecursive(go.transform.GetChild(i).gameObject, query, sb, ref count, max);
        }

        bool MatchesQuery(string fieldName, string componentName, string goName, string query)
        {
            return fieldName.ToLowerInvariant().Contains(query)
                || componentName.ToLowerInvariant().Contains(query)
                || goName.ToLowerInvariant().Contains(query);
        }

        void AppendSearchResult(StringBuilder sb, GameObject go, Component comp, string fieldName, string typeName, bool isProperty, bool canWrite)
        {
            object value = null;
            try
            {
                if (isProperty)
                    value = comp.GetType().GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(comp);
                else
                    value = comp.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(comp);
            }
            catch { }

            sb.Append("{");
            sb.Append($"\"instanceId\":{go.GetInstanceID()},");
            sb.Append($"\"componentId\":{comp.GetInstanceID()},");
            sb.Append($"\"gameObject\":\"{Esc(go.name)}\",");
            sb.Append($"\"component\":\"{Esc(comp.GetType().Name)}\",");
            sb.Append($"\"field\":\"{Esc(fieldName)}\",");
            sb.Append($"\"type\":\"{Esc(typeName)}\",");
            sb.Append($"\"isProperty\":{(isProperty ? "true" : "false")},");
            sb.Append($"\"canWrite\":{(canWrite ? "true" : "false")},");
            sb.Append($"\"value\":{SerializeValue(value)},");
            sb.Append($"\"path\":\"{Esc(GetHierarchyPath(go))}\"");
            sb.Append("}");
        }

        /// <summary>
        /// Add a variable to the watch list. Returns JSON with the watch ID.
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

            var id = $"w{_nextId++}";
            var entry = new WatchEntry
            {
                id = id,
                instanceId = instanceId,
                componentId = componentId,
                fieldName = fieldName,
                typeName = GetFieldTypeName(comp, fieldName),
                isProperty = isProperty,
                gameObjectName = go.name,
                componentName = comp.GetType().Name,
                hierarchyPath = GetHierarchyPath(go)
            };

            _watches[id] = entry;
            _resolvedCache.Remove(id);

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
                return "{\"ok\":true}";
            }
            return "{\"ok\":false,\"error\":\"Watch not found\"}";
        }

        /// <summary>
        /// Clear all watches.
        /// </summary>
        public string ClearAll()
        {
            _watches.Clear();
            _resolvedCache.Clear();
            _sampleBuffer.Clear();
            return "{\"ok\":true}";
        }

        /// <summary>
        /// Get current watch list.
        /// </summary>
        public string GetWatchList()
        {
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
                sb.Append($"\"field\":\"{Esc(entry.fieldName)}\",");
                sb.Append($"\"type\":\"{Esc(entry.typeName)}\",");
                sb.Append($"\"isProperty\":{(entry.isProperty ? "true" : "false")},");
                sb.Append($"\"gameObject\":\"{Esc(entry.gameObjectName)}\",");
                sb.Append($"\"component\":\"{Esc(entry.componentName)}\",");
                sb.Append($"\"path\":\"{Esc(entry.hierarchyPath)}\",");
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
        /// Apply a value to a watched variable.
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
                // Use the existing apply mechanism from InspectorService
                var json = $"{{\"{fieldName}\":{valueJson}}}";
                var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<
                    Dictionary<string, object>>(json);
                if (dict == null)
                    return "{\"ok\":false,\"error\":\"Failed to parse value\"}";

                var type = comp.GetType();
                foreach (var kv in dict)
                {
                    var prop = type.GetProperty(kv.Key, BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null && prop.CanWrite)
                    {
                        var converted = ConvertJsonValue(kv.Value, prop.PropertyType);
                        prop.SetValue(comp, converted);
                        // Invalidate cache so next read gets fresh value
                        foreach (var w in _watches.Values)
                        {
                            if (w.instanceId == instanceId && w.componentId == componentId && w.fieldName == fieldName)
                                _resolvedCache.Remove(w.id);
                        }
                        return "{\"ok\":true}";
                    }

                    var field = type.GetField(kv.Key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        var converted = ConvertJsonValue(kv.Value, field.FieldType);
                        field.SetValue(comp, converted);
                        foreach (var w in _watches.Values)
                        {
                            if (w.instanceId == instanceId && w.componentId == componentId && w.fieldName == fieldName)
                                _resolvedCache.Remove(w.id);
                        }
                        return "{\"ok\":true}";
                    }

                    return $"{{\"ok\":false,\"error\":\"Field '{Esc(kv.Key)}' not found\"}}";
                }

                return "{\"ok\":false,\"error\":\"Empty value\"}";
            }
            catch (Exception ex)
            {
                return $"{{\"ok\":false,\"error\":\"{Esc(ex.Message)}\"}}";
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
                    return $"{{\"ok\":false,\"applied\":{applied},\"error\":\"{Esc(errors.ToString())}\"}}";
                return $"{{\"ok\":true,\"applied\":{applied}}}";
            }
            catch (Exception ex)
            {
                return $"{{\"ok\":false,\"error\":\"{Esc(ex.Message)}\"}}";
            }
        }

        // ── Helpers ──

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

        string GetFieldTypeName(Component comp, string fieldName)
        {
            var type = comp.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var prop = type.GetProperty(fieldName, flags);
            if (prop != null) return prop.PropertyType.Name;
            var field = type.GetField(fieldName, flags);
            if (field != null) return field.FieldType.Name;
            return "Unknown";
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
            if (value is string s) return $"\"{Esc(s)}\"";
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
                return $"\"{Esc(uo.name)} ({uo.GetType().Name})\"";
            }
            try { return $"\"{Esc(value.ToString())}\""; }
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

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") ?? "";
    }
}
