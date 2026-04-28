using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.Bugpunch.Editor
{
    public static class TypeDatabaseExporter
    {
        const string HashPrefKey = "Bugpunch.TypeDbHash";

        [MenuItem("ODD Games/Export Type Database")]
        public static void ExportAndUpload() => ExportAndUpload(force: true, showProgress: true);

        /// <summary>
        /// Export and upload only if the TypeDB content has changed since last upload.
        /// Cheap to call from compilation-finished and connect hooks.
        /// </summary>
        public static bool ExportAndUploadIfChanged() => ExportAndUpload(force: false, showProgress: false);

        static bool ExportAndUpload(bool force, bool showProgress)
        {
            var config = ODDGames.Bugpunch.BugpunchConfig.Load();
            if (config == null || string.IsNullOrEmpty(config.apiKey))
            {
                if (force)
                    EditorUtility.DisplayDialog("Bugpunch", "No BugpunchConfig (or missing apiKey) found in Resources/", "OK");
                return false;
            }

            if (showProgress) EditorUtility.DisplayProgressBar("Bugpunch", "Scanning assemblies...", 0f);

            try
            {
                var db = BuildDatabase(showProgress);
                var json = SerializeDatabase(db);

                // Save locally for debugging
                var path = Path.Combine(Application.dataPath, "..", "Library", "BugpunchTypeDb.json");
                File.WriteAllText(path, json);

                var hash = Sha1(json);
                var prevHash = EditorPrefs.GetString(HashPrefKey, "");
                if (!force && hash == prevHash)
                {
                    return false; // unchanged — skip upload
                }

                var jsonBytes = Encoding.UTF8.GetBytes(json);
                var gzBytes = Gzip(jsonBytes);
                BugpunchLog.Info("TypeDatabaseExporter", $"Type database: {jsonBytes.Length / 1024}KB → {gzBytes.Length / 1024}KB gzipped ({db.types.Count} types)");

                if (showProgress) EditorUtility.DisplayProgressBar("Bugpunch", "Uploading to server...", 0.8f);
                if (UploadToServer(config, gzBytes, db))
                {
                    EditorPrefs.SetString(HashPrefKey, hash);
                    return true;
                }
                return false;
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }
        }

        static byte[] Gzip(byte[] bytes)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                gz.Write(bytes, 0, bytes.Length);
            }
            return ms.ToArray();
        }

        static string Sha1(string s)
        {
            using var sha = SHA1.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        // Auto-export on build
        // Implement IPreprocessBuildWithReport if you want automatic export

        struct TypeDatabase
        {
            public List<TypeInfo> types;
            public List<string> namespaces;
            public string unityVersion;
            public string appVersion;
            public string exportedAt;
        }

        struct TypeInfo
        {
            public string name;
            public string fullName;
            public string ns; // namespace
            public string kind; // class, struct, enum, interface, delegate
            public string baseType;
            public List<string> interfaces;
            public List<MemberInfo_> members;
            public List<string> enumValues; // only for enums
            public string xmlDoc; // summary if available
        }

        struct MemberInfo_
        {
            public string name;
            public string kind; // method, property, field, event
            public string returnType;
            public string parameters; // for methods: "int x, string y"
            public List<ParameterInfo_> paramList; // structured params
            public bool isStatic;
            public string xmlDoc;
        }

        struct ParameterInfo_
        {
            public string name;
            public string type;
            public bool hasDefault;
            public string defaultValue;
        }

        static TypeDatabase BuildDatabase(bool showProgress = true)
        {
            var db = new TypeDatabase
            {
                types = new List<TypeInfo>(),
                namespaces = new HashSet<string>().ToList(), // filled below
                unityVersion = Application.unityVersion,
                appVersion = Application.version,
                exportedAt = DateTime.UtcNow.ToString("O")
            };

            var namespaceSet = new HashSet<string>();

            // Target assemblies: skip system/editor-only, include Unity runtime + user code
            var targetAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .Where(a => {
                    var name = a.GetName().Name;
                    // Include: UnityEngine, user assemblies, common packages
                    // Exclude: Editor assemblies, mscorlib internals, test assemblies
                    if (name.Contains(".Editor") && !name.Contains("UIAutomation")) return false;
                    if (name.StartsWith("System.") && !name.StartsWith("System.Collections") && !name.StartsWith("System.Linq")) return false;
                    if (name == "mscorlib") return false;
                    if (name.StartsWith("Mono.")) return false;
                    if (name.StartsWith("nunit")) return false;
                    return true;
                })
                .ToArray();

            float total = targetAssemblies.Length;
            int current = 0;

            foreach (var assembly in targetAssemblies)
            {
                current++;
                if (showProgress)
                    EditorUtility.DisplayProgressBar("Bugpunch", $"Scanning {assembly.GetName().Name}...", current / total * 0.8f);

                try
                {
                    foreach (var type in assembly.GetExportedTypes())
                    {
                        if (type.IsNested && !type.IsNestedPublic) continue;
                        if (type.IsSpecialName || type.Name.StartsWith("<")) continue;

                        if (!string.IsNullOrEmpty(type.Namespace))
                            namespaceSet.Add(type.Namespace);

                        var typeInfo = new TypeInfo
                        {
                            name = type.Name,
                            fullName = type.FullName,
                            ns = type.Namespace ?? "",
                            kind = GetTypeKind(type),
                            baseType = type.BaseType?.Name,
                            interfaces = type.GetInterfaces()
                                .Where(i => i.IsPublic)
                                .Select(i => i.Name)
                                .Take(10) // limit
                                .ToList(),
                            members = new List<MemberInfo_>(),
                            enumValues = type.IsEnum ? Enum.GetNames(type).ToList() : null,
                        };

                        // Methods (skip property accessors, limit count)
                        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                            .Where(m => !m.IsSpecialName)
                            .Take(50); // limit per type

                        foreach (var m in methods)
                        {
                            var parameters = m.GetParameters();
                            typeInfo.members.Add(new MemberInfo_
                            {
                                name = m.Name,
                                kind = "method",
                                returnType = m.ReturnType.Name,
                                parameters = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}")),
                                paramList = parameters.Select(p => new ParameterInfo_
                                {
                                    name = p.Name,
                                    type = p.ParameterType.Name,
                                    hasDefault = p.HasDefaultValue,
                                    defaultValue = p.HasDefaultValue ? p.DefaultValue?.ToString() : null
                                }).ToList(),
                                isStatic = m.IsStatic,
                            });
                        }

                        // Properties
                        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Take(50))
                        {
                            typeInfo.members.Add(new MemberInfo_
                            {
                                name = p.Name,
                                kind = "property",
                                returnType = p.PropertyType.Name,
                                isStatic = p.GetMethod?.IsStatic ?? false,
                            });
                        }

                        // Fields
                        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Take(50))
                        {
                            typeInfo.members.Add(new MemberInfo_
                            {
                                name = f.Name,
                                kind = "field",
                                returnType = f.FieldType.Name,
                                isStatic = f.IsStatic,
                            });
                        }

                        db.types.Add(typeInfo);
                    }
                }
                catch (Exception ex)
                {
                    BugpunchLog.Warn("TypeDatabaseExporter", $"Error scanning {assembly.GetName().Name}: {ex.Message}");
                }
            }

            db.namespaces = namespaceSet.OrderBy(n => n).ToList();
            return db;
        }

        static string GetTypeKind(Type type)
        {
            if (type.IsEnum) return "enum";
            if (type.IsInterface) return "interface";
            if (type.IsValueType) return "struct";
            if (typeof(Delegate).IsAssignableFrom(type)) return "delegate";
            return "class";
        }

        static string SerializeDatabase(TypeDatabase db)
        {
            // Use JsonUtility for the top-level, but it doesn't handle List<T> of structs well
            // So we build JSON manually for control
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"unityVersion\":\"{db.unityVersion}\",");
            sb.Append($"\"appVersion\":\"{db.appVersion}\",");
            sb.Append($"\"exportedAt\":\"{db.exportedAt}\",");
            sb.Append($"\"typeCount\":{db.types.Count},");

            // Namespaces
            sb.Append("\"namespaces\":[");
            for (int i = 0; i < db.namespaces.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{BugpunchJson.Esc(db.namespaces[i])}\"");
            }
            sb.Append("],");

            // Types
            sb.Append("\"types\":[");
            for (int i = 0; i < db.types.Count; i++)
            {
                if (i > 0) sb.Append(",");
                SerializeType(db.types[i], sb);
            }
            sb.Append("]");

            sb.Append("}");
            return sb.ToString();
        }

        static void SerializeType(TypeInfo t, StringBuilder sb)
        {
            sb.Append("{");
            sb.Append($"\"n\":\"{BugpunchJson.Esc(t.name)}\",");
            sb.Append($"\"f\":\"{BugpunchJson.Esc(t.fullName)}\",");
            sb.Append($"\"ns\":\"{BugpunchJson.Esc(t.ns)}\",");
            sb.Append($"\"k\":\"{t.kind}\"");

            if (t.baseType != null)
                sb.Append($",\"base\":\"{BugpunchJson.Esc(t.baseType)}\"");

            if (t.enumValues != null && t.enumValues.Count > 0)
            {
                sb.Append(",\"ev\":[");
                for (int i = 0; i < t.enumValues.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append($"\"{BugpunchJson.Esc(t.enumValues[i])}\"");
                }
                sb.Append("]");
            }

            if (t.members.Count > 0)
            {
                sb.Append(",\"m\":[");
                for (int i = 0; i < t.members.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    SerializeMember(t.members[i], sb);
                }
                sb.Append("]");
            }

            sb.Append("}");
        }

        static void SerializeMember(MemberInfo_ m, StringBuilder sb)
        {
            sb.Append("{");
            sb.Append($"\"n\":\"{BugpunchJson.Esc(m.name)}\",\"k\":\"{m.kind}\",\"t\":\"{BugpunchJson.Esc(m.returnType)}\"");
            if (m.isStatic) sb.Append(",\"s\":true");
            if (!string.IsNullOrEmpty(m.parameters)) sb.Append($",\"p\":\"{BugpunchJson.Esc(m.parameters)}\"");
            sb.Append("}");
        }

        static bool UploadToServer(ODDGames.Bugpunch.BugpunchConfig config, byte[] gzBytes, TypeDatabase db)
        {
            var url = config.serverUrl.TrimEnd('/');
            if (url.StartsWith("ws://")) url = "http://" + url.Substring(5);
            else if (url.StartsWith("wss://")) url = "https://" + url.Substring(6);

            var uploadUrl = $"{url}/api/typedb/upload";

            var request = new UnityWebRequest(uploadUrl, "POST");
            request.uploadHandler = new UploadHandlerRaw(gzBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 30;
            request.SetRequestHeader("Content-Type", "application/gzip");
            request.SetRequestHeader("X-Api-Key", config.apiKey);
            request.SetRequestHeader("X-Unity-Version", db.unityVersion ?? "");
            request.SetRequestHeader("X-App-Version", db.appVersion ?? "");
            request.SetRequestHeader("X-Type-Count", db.types.Count.ToString());
            request.SetRequestHeader("X-Namespace-Count", db.namespaces.Count.ToString());

            var op = request.SendWebRequest();
            while (!op.isDone) Thread.Sleep(50);

            bool ok = request.result == UnityWebRequest.Result.Success;
            if (ok)
                BugpunchLog.Info("TypeDatabaseExporter", $"Type database uploaded to server ({gzBytes.Length / 1024}KB gzipped)");
            else
                BugpunchLog.Error("TypeDatabaseExporter", $"Type database upload failed: {request.error} — {request.downloadHandler.text}");

            request.Dispose();
            return ok;
        }

    }
}
