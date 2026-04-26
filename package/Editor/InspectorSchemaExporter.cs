using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Emits a per-type whitelist of inspector-visible member names to
    /// Assets/BugpunchResources/Resources/BugpunchInspectorSchema.json.
    /// Runtime side: <see cref="ODDGames.Bugpunch.DeviceConnect.InspectorSchema"/>
    /// loads this TextAsset and merges it on top of the hardcoded built-in map.
    ///
    /// Three entry points, one export body:
    ///   1. Menu item — sync, user-triggered.
    ///   2. Pre-build hook — sync, runs inside the build pipeline.
    ///   3. On-connect hook — <see cref="ExportIfEnabledAsync"/>, runs the
    ///      reflection scan on a background Task so the editor never stalls.
    ///      Hash-cached in EditorPrefs so repeated connects with no type
    ///      changes are a no-op.
    /// </summary>
    public static class InspectorSchemaExporter
    {
        const string SchemaRelDir = "Assets/BugpunchResources/Resources";
        const string SchemaFileName = "BugpunchInspectorSchema.json";
        const string HashPrefKey = "Bugpunch.InspectorSchemaHash";

        [MenuItem("ODD Games/Bugpunch/Generate Inspector Schema Template")]
        public static void GenerateTemplate()
        {
            var json = BuildSchemaJson(out var count);
            WriteSchemaAsset(json);
            EditorPrefs.SetString(HashPrefKey, Sha1(json));
            EditorUtility.DisplayDialog(
                "Bugpunch",
                $"Wrote inspector schema for {count} user types to\n{SchemaRelDir}/{SchemaFileName}\n\n" +
                "Edit the arrays to control which fields show in the remote Inspector's Normal mode. " +
                "Debug mode is unaffected (always shows everything via reflection).",
                "OK");
        }

        /// <summary>
        /// Sync refresh. Used by the pre-build hook — the build pipeline already
        /// waits on this, so blocking briefly here is fine. No-ops if the schema
        /// file doesn't exist (user hasn't opted in).
        /// </summary>
        public static int ExportIfEnabled()
        {
            if (!File.Exists(AbsoluteSchemaPath())) return 0;
            var json = BuildSchemaJson(out var count);
            var hash = Sha1(json);
            if (hash == EditorPrefs.GetString(HashPrefKey, "")) return count; // unchanged
            WriteSchemaAsset(json);
            EditorPrefs.SetString(HashPrefKey, hash);
            return count;
        }

        /// <summary>
        /// Non-blocking refresh — runs reflection on a background Task and hops
        /// back to the main thread only for the tiny asset-write step. Safe to
        /// call from editor-connect hooks without stalling editing.
        /// Returns a Task that completes after the schema is written (or skipped).
        /// </summary>
        public static Task ExportIfEnabledAsync()
        {
            if (!File.Exists(AbsoluteSchemaPath())) return Task.CompletedTask;

            var tcs = new TaskCompletionSource<bool>();
            Task.Run(() =>
            {
                try
                {
                    var json = BuildSchemaJson(out var count);
                    var hash = Sha1(json);
                    EditorApplication.delayCall += () =>
                    {
                        try
                        {
                            if (hash != EditorPrefs.GetString(HashPrefKey, ""))
                            {
                                WriteSchemaAsset(json);
                                EditorPrefs.SetString(HashPrefKey, hash);
                                BugpunchLog.Info("InspectorSchemaExporter", $"Inspector schema refreshed ({count} user types)");
                            }
                            tcs.TrySetResult(true);
                        }
                        catch (Exception ex) { tcs.TrySetException(ex); }
                    };
                }
                catch (Exception ex)
                {
                    // Back to main thread for logging.
                    EditorApplication.delayCall += () =>
                    {
                        BugpunchLog.Warn("InspectorSchemaExporter", $"Inspector schema async export failed: {ex.Message}");
                        tcs.TrySetException(ex);
                    };
                }
            });
            return tcs.Task;
        }

        // ── Body ────────────────────────────────────────────────────────────
        // Two halves: pure reflection (thread-safe) and asset IO (main thread).

        static string BuildSchemaJson(out int typeCount)
        {
            var schema = BuildSchema();
            typeCount = schema.Count;
            return SerializeSchema(schema);
        }

        static void WriteSchemaAsset(string json)
        {
            Directory.CreateDirectory(SchemaRelDir);
            var path = Path.Combine(SchemaRelDir, SchemaFileName);
            File.WriteAllText(path, json);
            AssetDatabase.ImportAsset(path);
        }

        static string AbsoluteSchemaPath() => Path.Combine(SchemaRelDir, SchemaFileName);

        // type FullName → ordered list of member names
        static SortedDictionary<string, List<string>> BuildSchema()
        {
            var result = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Skip Unity / system assemblies. User scripts land in
                // Assembly-CSharp* or in asmdefs the user has written.
                var name = asm.GetName().Name;
                if (IsEngineAssembly(name)) continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || t.IsGenericTypeDefinition) continue;
                    if (!typeof(MonoBehaviour).IsAssignableFrom(t) &&
                        !typeof(ScriptableObject).IsAssignableFrom(t)) continue;

                    var members = CollectSerializedMembers(t);
                    if (members.Count == 0) continue;
                    result[t.FullName] = members;
                }
            }

            return result;
        }

        static bool IsEngineAssembly(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            return name.StartsWith("Unity.") || name.StartsWith("UnityEngine") ||
                   name.StartsWith("UnityEditor") || name.StartsWith("Bee.") ||
                   name == "mscorlib" || name.StartsWith("System") ||
                   name.StartsWith("Microsoft.") || name == "netstandard" ||
                   name.StartsWith("nunit.") || name.StartsWith("ODDGames.Bugpunch");
        }

        static List<string> CollectSerializedMembers(Type t)
        {
            // Same rule the runtime fallback uses — so the generated template
            // equals what the user would get without the file. The user can then
            // trim to match a custom-editor view.
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            var names = new List<string>();
            foreach (var f in t.GetFields(flags))
            {
                if (f.IsStatic || f.IsLiteral || f.IsInitOnly) continue;
                if (Attribute.IsDefined(f, typeof(NonSerializedAttribute))) continue;
                if (!f.IsPublic && !Attribute.IsDefined(f, typeof(SerializeField))) continue;
                names.Add(f.Name);
            }

            if (typeof(Behaviour).IsAssignableFrom(t)) names.Add("enabled");
            return names;
        }

        static string SerializeSchema(SortedDictionary<string, List<string>> schema)
        {
            // Hand-rolled JSON so the file diffs cleanly (one type per line block,
            // stable ordering). Avoids pulling Newtonsoft settings in editor
            // assembly just for formatting.
            var sb = new StringBuilder();
            sb.Append("{\n");
            bool first = true;
            foreach (var kv in schema)
            {
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("  ").Append(JsonString(kv.Key)).Append(": [");
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(JsonString(kv.Value[i]));
                }
                sb.Append("]");
            }
            sb.Append("\n}\n");
            return sb.ToString();
        }

        static string JsonString(string s)
        {
            return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        static string Sha1(string s)
        {
            using var sha = SHA1.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }

    /// <summary>Refreshes the inspector schema JSON before a build.</summary>
    public class BugpunchInspectorSchemaPreBuildHook : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            try
            {
                var count = InspectorSchemaExporter.ExportIfEnabled();
                if (count > 0) BugpunchLog.Info("InspectorSchemaExporter", $"Inspector schema refreshed ({count} user types)");
            }
            catch (Exception ex)
            {
                BugpunchLog.Warn("InspectorSchemaExporter", $"Inspector schema export failed: {ex.Message}");
            }
        }
    }
}
