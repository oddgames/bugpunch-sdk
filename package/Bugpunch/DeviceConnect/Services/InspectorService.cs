using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    public class InspectorService
    {
        /// <summary>
        /// Get components on a GameObject by instance ID
        /// </summary>
        public string InspectGameObject(string instanceIdStr)
        {
            var go = FindByInstanceId(instanceIdStr);
            if (go == null) return "{\"error\":\"GameObject not found\"}";

            var sb = new StringBuilder();
            sb.Append("[");
            var components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var c = components[i];
                if (c == null) { sb.Append("{\"name\":\"(Missing)\",\"id\":0}"); continue; }
                sb.Append($"{{\"name\":\"{EscapeJson(c.GetType().Name)}\",\"fullName\":\"{EscapeJson(c.GetType().FullName)}\",\"id\":{c.GetInstanceID()}}}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Get serializable fields of a component
        /// </summary>
        public string GetFields(string instanceIdStr, string componentIdStr, bool debug = false)
        {
            var component = FindComponentById(instanceIdStr, componentIdStr);
            if (component == null) return "[]";

            var type = component.GetType();
            var flags = debug
                ? BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                : BindingFlags.Public | BindingFlags.Instance;

            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;

            // Properties first (position, rotation, scale, enabled, etc.)
            foreach (var p in type.GetProperties(flags))
            {
                if (!p.CanRead) continue;
                if (p.GetIndexParameters().Length > 0) continue; // skip indexers
                // Skip noisy base class properties
                if (p.DeclaringType == typeof(UnityEngine.Object) && p.Name == "hideFlags") continue;

                object value = null;
                try { value = p.GetValue(component); } catch { continue; }

                if (!first) sb.Append(",");
                first = false;

                sb.Append($"{{\"name\":\"{EscapeJson(p.Name)}\",\"type\":\"{EscapeJson(p.PropertyType.Name)}\",\"value\":{SerializeValue(value)},\"isPublic\":true,\"isStatic\":false,\"isProperty\":true,\"canWrite\":{(p.CanWrite ? "true" : "false")}}}");
            }

            // Fields
            foreach (var f in type.GetFields(flags))
            {
                if (!debug && !f.IsPublic && f.GetCustomAttribute<SerializeField>() == null) continue;
                if (!first) sb.Append(",");
                first = false;

                object value = null;
                try { value = f.GetValue(component); } catch { }

                sb.Append($"{{\"name\":\"{EscapeJson(f.Name)}\",\"type\":\"{EscapeJson(f.FieldType.Name)}\",\"value\":{SerializeValue(value)},\"isPublic\":{(f.IsPublic ? "true" : "false")},\"isStatic\":{(f.IsStatic ? "true" : "false")}}}");
            }

            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Get methods of a component
        /// </summary>
        public string GetMethods(string instanceIdStr, string componentIdStr, bool debug = false)
        {
            var component = FindComponentById(instanceIdStr, componentIdStr);
            if (component == null) return "[]";

            var type = component.GetType();
            var flags = debug
                ? BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                : BindingFlags.Public | BindingFlags.Instance;

            var methods = type.GetMethods(flags)
                .Where(m => !m.IsSpecialName) // Skip property getters/setters
                .Where(m => m.DeclaringType != typeof(object) && m.DeclaringType != typeof(MonoBehaviour) && m.DeclaringType != typeof(Component) && m.DeclaringType != typeof(Behaviour))
                .ToArray();

            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < methods.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var m = methods[i];
                var paramStr = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                sb.Append($"{{\"name\":\"{EscapeJson(m.Name)}\",\"returnType\":\"{EscapeJson(m.ReturnType.Name)}\",\"parameters\":\"{EscapeJson(paramStr)}\",\"isStatic\":{(m.IsStatic ? "true" : "false")}}}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Get serialized component data as JSON
        /// </summary>
        public string GetComponent(string instanceIdStr, string componentIdStr)
        {
            var component = FindComponentById(instanceIdStr, componentIdStr);
            if (component == null) return "{}";
            return JsonUtility.ToJson(component, true);
        }

        /// <summary>
        /// Invoke a method on a component
        /// </summary>
        public string InvokeMethod(string instanceIdStr, string componentIdStr, string methodName, string argsStr)
        {
            var component = FindComponentById(instanceIdStr, componentIdStr);
            if (component == null) return "{\"ok\":false,\"error\":\"Component not found\"}";

            try
            {
                var type = component.GetType();
                var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (method == null) return $"{{\"ok\":false,\"error\":\"Method '{EscapeJson(methodName)}' not found\"}}";

                // Parse arguments
                object[] args = null;
                if (!string.IsNullOrEmpty(argsStr))
                {
                    var paramInfos = method.GetParameters();
                    var argParts = argsStr.Split(',');
                    args = new object[paramInfos.Length];
                    for (int i = 0; i < paramInfos.Length && i < argParts.Length; i++)
                    {
                        args[i] = Convert.ChangeType(argParts[i].Trim(), paramInfos[i].ParameterType);
                    }
                }

                var result = method.Invoke(method.IsStatic ? null : component, args);
                return $"{{\"ok\":true,\"result\":{SerializeValue(result)}}}";
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                return $"{{\"ok\":false,\"error\":\"{EscapeJson(inner.Message)}\"}}";
            }
        }

        /// <summary>
        /// Get all public types for IntelliSense
        /// </summary>
        public string GetTypes()
        {
            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetExportedTypes())
                    {
                        if (!first) sb.Append(",");
                        first = false;
                        sb.Append($"{{\"name\":\"{EscapeJson(type.Name)}\",\"fullName\":\"{EscapeJson(type.FullName)}\",\"namespace\":\"{EscapeJson(type.Namespace ?? "")}\"}}");
                    }
                }
                catch { } // Some assemblies can't be reflected
            }

            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Get all namespaces for IntelliSense
        /// </summary>
        public string GetNamespaces()
        {
            var namespaces = new HashSet<string>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetExportedTypes())
                    {
                        if (!string.IsNullOrEmpty(type.Namespace))
                            namespaces.Add(type.Namespace);
                    }
                }
                catch { }
            }

            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;
            foreach (var ns in namespaces.OrderBy(n => n))
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\"{EscapeJson(ns)}\"");
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Get members of a type for IntelliSense dot-completion
        /// </summary>
        public string GetMembers(string typeName)
        {
            var type = FindType(typeName);
            if (type == null) return "[]";

            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;
            var seen = new HashSet<string>();

            const BindingFlags allPublic = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

            // Properties
            foreach (var p in type.GetProperties(allPublic))
            {
                if (!seen.Add($"P:{p.Name}")) continue;
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"{{\"name\":\"{EscapeJson(p.Name)}\",\"kind\":\"property\",\"type\":\"{EscapeJson(p.PropertyType.Name)}\",\"returnType\":\"{EscapeJson(p.PropertyType.Name)}\"}}");
            }

            // Fields
            foreach (var f in type.GetFields(allPublic))
            {
                if (!seen.Add($"F:{f.Name}")) continue;
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"{{\"name\":\"{EscapeJson(f.Name)}\",\"kind\":\"field\",\"type\":\"{EscapeJson(f.FieldType.Name)}\",\"returnType\":\"{EscapeJson(f.FieldType.Name)}\"}}");
            }

            // Methods — deduplicate by name, show overload count
            var methodGroups = type.GetMethods(allPublic)
                .Where(m => !m.IsSpecialName)
                .GroupBy(m => m.Name);
            foreach (var group in methodGroups)
            {
                if (!first) sb.Append(",");
                first = false;
                var representative = group.First();
                var paramStr = string.Join(", ", representative.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                var overloads = group.Count();
                var detail = overloads > 1 ? $"(+{overloads - 1} overloads)" : "";
                sb.Append($"{{\"name\":\"{EscapeJson(group.Key)}\",\"kind\":\"method\",\"type\":\"{EscapeJson(representative.ReturnType.Name)}\",\"returnType\":\"{EscapeJson(representative.ReturnType.Name)}\",\"parameters\":\"{EscapeJson(paramStr)}\",\"detail\":\"{EscapeJson(detail)}\"}}");
            }

            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Get method overload signatures for IntelliSense
        /// </summary>
        public string GetSignatures(string typeName, string methodName)
        {
            var type = FindType(typeName);
            if (type == null) return "[]";

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(m => m.Name == methodName)
                .ToArray();

            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < methods.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var m = methods[i];
                var parameters = m.GetParameters();

                sb.Append("{");
                sb.Append($"\"label\":\"{EscapeJson(m.ReturnType.Name)} {m.Name}({string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"))})\",");
                sb.Append("\"parameters\":[");
                for (int j = 0; j < parameters.Length; j++)
                {
                    if (j > 0) sb.Append(",");
                    sb.Append($"{{\"name\":\"{EscapeJson(parameters[j].Name)}\",\"type\":\"{EscapeJson(parameters[j].ParameterType.Name)}\"}}");
                }
                sb.Append("]}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Write field values to a component using JsonUtility.FromJsonOverwrite.
        /// </summary>
        public string ApplyComponent(string instanceIdStr, string componentIdStr, string json)
        {
            var component = FindComponentById(instanceIdStr, componentIdStr);
            if (component == null)
                return "{\"ok\":false,\"error\":\"Component not found\"}";

            try
            {
                JsonUtility.FromJsonOverwrite(json, component);
                return "{\"ok\":true}";
            }
            catch (Exception ex)
            {
                return $"{{\"ok\":false,\"error\":\"{EscapeJson(ex.Message)}\"}}";
            }
        }

        /// <summary>
        /// Resolve a member chain like "GameObject.transform.position" and return the final type.
        /// Supports generic type args: "GameObject.FindFirstObjectByType&lt;Renderer&gt;" resolves to Renderer.
        /// If info=true, return members of the resolved type as well.
        /// </summary>
        public string ResolveChain(string chain, bool info)
        {
            if (string.IsNullOrEmpty(chain))
                return "\"\"";

            var current = ResolveChainToType(chain);
            if (current == null)
                return "\"\"";

            if (info)
            {
                return "{\"type\":\"" + EscapeJson(current.Name) + "\",\"fullName\":\"" + EscapeJson(current.FullName ?? current.Name) +
                       "\",\"isEnum\":" + (current.IsEnum ? "true" : "false") +
                       ",\"isValueType\":" + (current.IsValueType ? "true" : "false") + "}";
            }

            return "\"" + EscapeJson(current.Name) + "\"";
        }

        /// <summary>
        /// Resolve a chain and return the element type if the result is enumerable.
        /// Used for foreach(var x in expr) — returns the type of x.
        /// </summary>
        public string ResolveElementType(string chain)
        {
            if (string.IsNullOrEmpty(chain))
                return "\"\"";

            var collectionType = ResolveChainToType(chain);
            if (collectionType == null)
                return "\"\"";

            var elementType = GetElementType(collectionType);
            if (elementType == null)
                return "\"\"";

            return "\"" + EscapeJson(elementType.FullName ?? elementType.Name) + "\"";
        }

        Type ResolveChainToType(string chain)
        {
            // Split on '.' but preserve generic args: "Type.Method<Arg>.Prop"
            var parts = SplitChain(chain);
            Type current = null;
            int start = 0;

            // Find the starting type from the chain
            for (int i = 0; i < parts.Count; i++)
            {
                current = FindType(parts[i].name);
                if (current != null)
                {
                    start = i + 1;
                    break;
                }
            }

            if (current == null)
                return null;

            var flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy | BindingFlags.IgnoreCase;
            for (int i = start; i < parts.Count; i++)
            {
                var (memberName, genericArg) = parts[i];

                var property = current.GetProperty(memberName, flags);
                if (property != null) { current = property.PropertyType; continue; }
                var field = current.GetField(memberName, flags);
                if (field != null) { current = field.FieldType; continue; }
                var candidates = current.GetMethods(flags)
                    .Where(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase) && !m.IsSpecialName)
                    .ToArray();
                // Prefer generic overload when a generic arg is provided
                var method = genericArg != null
                    ? candidates.FirstOrDefault(m => m.IsGenericMethod) ?? candidates.FirstOrDefault()
                    : candidates.FirstOrDefault(m => !m.IsGenericMethod) ?? candidates.FirstOrDefault();
                if (method != null)
                {
                    if (method.IsGenericMethod && genericArg != null)
                    {
                        var argType = FindType(genericArg);
                        if (argType != null)
                        {
                            try
                            {
                                var concrete = method.MakeGenericMethod(argType);
                                current = concrete.ReturnType;
                                continue;
                            }
                            catch { }
                        }
                    }
                    current = method.ReturnType;
                    continue;
                }
                return null;
            }

            return current;
        }

        /// <summary>
        /// Split a chain like "GameObject.FindObjectsByType&lt;Renderer&gt;.Length" into
        /// [(GameObject, null), (FindObjectsByType, Renderer), (Length, null)]
        /// </summary>
        static List<(string name, string genericArg)> SplitChain(string chain)
        {
            var result = new List<(string, string)>();
            int i = 0;
            while (i < chain.Length)
            {
                // Read member name up to '.', '<', or end
                int nameStart = i;
                while (i < chain.Length && chain[i] != '.' && chain[i] != '<')
                    i++;
                var name = chain.Substring(nameStart, i - nameStart);
                string genericArg = null;

                // Check for generic arg <Type>
                if (i < chain.Length && chain[i] == '<')
                {
                    i++; // skip '<'
                    int argStart = i;
                    while (i < chain.Length && chain[i] != '>')
                        i++;
                    genericArg = chain.Substring(argStart, i - argStart);
                    if (i < chain.Length) i++; // skip '>'
                }

                if (name.Length > 0)
                    result.Add((name, genericArg));

                // Skip '.'
                if (i < chain.Length && chain[i] == '.')
                    i++;
            }
            return result;
        }

        /// <summary>
        /// Get the element type of a collection/array type.
        /// T[] → T, List&lt;T&gt; → T, IEnumerable&lt;T&gt; → T
        /// </summary>
        static Type GetElementType(Type type)
        {
            if (type.IsArray)
                return type.GetElementType();

            // Check IEnumerable<T> interfaces
            foreach (var iface in type.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return iface.GetGenericArguments()[0];
            }

            // Check if the type itself is IEnumerable<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return type.GetGenericArguments()[0];

            return null;
        }

        // ── Helpers ──

        static GameObject FindByInstanceId(string idStr)
        {
            if (!int.TryParse(idStr, out var id)) return null;

            // Fast path: Unity's internal instance ID lookup (no allocation)
            var obj = FindObjectFromInstanceID(id);
            if (obj is GameObject go) return go;
            if (obj is Component comp) return comp.gameObject;

#if UNITY_EDITOR
            var edObj = UnityEditor.EditorUtility.InstanceIDToObject(id) as GameObject;
            if (edObj != null) return edObj;
#endif
            // Last resort fallback (expensive — iterates all objects)
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (g.GetInstanceID() == id) return g;
            }
            return null;
        }

        static UnityEngine.Object FindObjectFromInstanceID(int id)
        {
            // Use reflection to access Unity's internal fast lookup
            if (_findObjectMethod == null)
            {
                _findObjectMethod = typeof(UnityEngine.Object).GetMethod(
                    "FindObjectFromInstanceID",
                    BindingFlags.NonPublic | BindingFlags.Static);
            }
            if (_findObjectMethod != null)
            {
                try { return _findObjectMethod.Invoke(null, new object[] { id }) as UnityEngine.Object; }
                catch { }
            }
            return null;
        }
        static MethodInfo _findObjectMethod;

        static Component FindComponentById(string goIdStr, string compIdStr)
        {
            var go = FindByInstanceId(goIdStr);
            if (go == null) return null;
            if (!int.TryParse(compIdStr, out var compId)) return null;

            foreach (var c in go.GetComponents<Component>())
            {
                if (c != null && c.GetInstanceID() == compId) return c;
            }
            return null;
        }

        static readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();

        static Type FindType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            if (_typeCache.TryGetValue(name, out var cached))
                return cached;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(name);
                    if (type != null) { _typeCache[name] = type; return type; }

                    // Try by name only
                    foreach (var t in assembly.GetExportedTypes())
                    {
                        if (t.Name == name || t.FullName == name)
                        {
                            _typeCache[name] = t;
                            return t;
                        }
                    }
                }
                catch { }
            }

            _typeCache[name] = null;
            return null;
        }

        static string SerializeValue(object value)
        {
            if (value == null) return "null";
            if (value is string s) return $"\"{EscapeJson(s)}\"";
            if (value is bool b) return b ? "true" : "false";
            if (value is int or float or double or long or short or byte)
                return value.ToString();
            if (value is Vector2 v2) return $"\"({v2.x:F3}, {v2.y:F3})\"";
            if (value is Vector3 v3) return $"\"({v3.x:F3}, {v3.y:F3}, {v3.z:F3})\"";
            if (value is Color c) return $"\"({c.r:F3}, {c.g:F3}, {c.b:F3}, {c.a:F3})\"";
            if (value is Quaternion q) return $"\"({q.x:F3}, {q.y:F3}, {q.z:F3}, {q.w:F3})\"";
            if (value is Enum e) return $"\"{e}\"";
            if (value is UnityEngine.Object uo) return $"\"{EscapeJson(uo.name)} ({uo.GetType().Name})\"";

            try { return $"\"{EscapeJson(value.ToString())}\""; }
            catch { return "\"(error)\""; }
        }

        static string EscapeJson(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") ?? "";
    }
}
