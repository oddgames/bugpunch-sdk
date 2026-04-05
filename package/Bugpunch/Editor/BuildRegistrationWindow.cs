using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.Bugpunch.Editor
{
    public class BuildRegistrationWindow : EditorWindow
    {
        [MenuItem("ODD Games/Register Build")]
        static void Open()
        {
            var window = GetWindow<BuildRegistrationWindow>("Register Build");
            window.minSize = new Vector2(500, 700);
            window.CollectMetadata();
        }

        // Auto-collected metadata
        string platform;
        string buildConfig = "release";
        string appVersion;
        string bundleId;
        string unityVersion;
        string scriptingBackend;
        string il2cppConfig;
        string apiCompatibility;
        string managedStripping;
        bool incrementalGC;
        int androidMinSdk;
        int androidTargetSdk;
        string[] targetArchitectures;
        string[] scriptingDefines;
        Dictionary<string, string> packages = new();
        string lastBuildPath;
        long fileSize;

        // User-editable
        string notes = "";
        string changeset = "";
        string branch = "";
        List<SourceEntry> sources = new();

        // State
        Vector2 scrollPos;
        string statusMessage = "";
        bool isRegistering;

        struct SourceEntry
        {
            public string type; // local, gdrive
            public string machineId;
            public string path;
            public string url;
        }

        void CollectMetadata()
        {
            // Platform
            platform = EditorUserBuildSettings.activeBuildTarget switch
            {
                BuildTarget.Android => "android",
                BuildTarget.iOS => "ios",
                BuildTarget.StandaloneWindows64 => "windows",
                BuildTarget.StandaloneOSX => "macos",
                BuildTarget.StandaloneLinux64 => "linux",
                BuildTarget.WebGL => "webgl",
                _ => EditorUserBuildSettings.activeBuildTarget.ToString()
            };

            // App version
            appVersion = PlayerSettings.bundleVersion;
            bundleId = PlayerSettings.applicationIdentifier;
            unityVersion = Application.unityVersion;

            // Scripting
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup)).ToString();
            il2cppConfig = PlayerSettings.GetIl2CppCompilerConfiguration(NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup)).ToString();
            managedStripping = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup)).ToString();
            incrementalGC = PlayerSettings.gcIncremental;

            // Android specific
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                androidMinSdk = (int)PlayerSettings.Android.minSdkVersion;
                androidTargetSdk = (int)PlayerSettings.Android.targetSdkVersion;
                targetArchitectures = new[] { PlayerSettings.Android.targetArchitectures.ToString() };
            }

            // Scripting defines
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup), out var defines);
            scriptingDefines = defines;

            // Packages
            packages.Clear();
            var listRequest = Client.List(true);
            while (!listRequest.IsCompleted) { }
            if (listRequest.Status == StatusCode.Success)
            {
                foreach (var pkg in listRequest.Result)
                {
                    packages[pkg.name] = pkg.version;
                }
            }

            // Last build path
            lastBuildPath = EditorUserBuildSettings.GetBuildLocation(EditorUserBuildSettings.activeBuildTarget);
            if (File.Exists(lastBuildPath))
                fileSize = new FileInfo(lastBuildPath).Length;

            // Plastic changeset/branch
            changeset = GetPlasticChangeset() ?? "";
            branch = GetPlasticBranch() ?? "";

            // Default source: local on this machine
            if (sources.Count == 0 && !string.IsNullOrEmpty(lastBuildPath) && File.Exists(lastBuildPath))
            {
                sources.Add(new SourceEntry
                {
                    type = "local",
                    machineId = SystemInfo.deviceName,
                    path = Path.GetFullPath(lastBuildPath)
                });
            }
        }

        void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            // Header
            EditorGUILayout.LabelField("Register Build", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Build Info (read-only)
            EditorGUILayout.LabelField("Build Info", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Platform", platform);
            EditorGUILayout.LabelField("App Version", appVersion);
            EditorGUILayout.LabelField("Bundle ID", bundleId);
            EditorGUILayout.LabelField("Unity", unityVersion);
            EditorGUILayout.LabelField("Scripting", scriptingBackend);
            EditorGUILayout.LabelField("IL2CPP Config", il2cppConfig);
            EditorGUILayout.LabelField("Stripping", managedStripping);
            if (fileSize > 0)
                EditorGUILayout.LabelField("File Size", $"{fileSize / 1024 / 1024} MB");
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                EditorGUILayout.LabelField("Min SDK", androidMinSdk.ToString());
                EditorGUILayout.LabelField("Target SDK", androidTargetSdk.ToString());
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.Space();

            // Editable fields
            EditorGUILayout.LabelField("Version Control", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            buildConfig = EditorGUILayout.TextField("Build Config", buildConfig);
            changeset = EditorGUILayout.TextField("Changeset", changeset);
            branch = EditorGUILayout.TextField("Branch", branch);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space();

            // Sources
            EditorGUILayout.LabelField("Build Sources", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Add paths/links where agents can find this build.", MessageType.Info);

            for (int i = 0; i < sources.Count; i++)
            {
                var src = sources[i];
                EditorGUILayout.BeginHorizontal();

                // Type selector
                var typeOptions = new[] { "local", "gdrive" };
                var typeIndex = Array.IndexOf(typeOptions, src.type);
                if (typeIndex < 0) typeIndex = 0;
                typeIndex = EditorGUILayout.Popup(typeIndex, typeOptions, GUILayout.Width(80));
                src.type = typeOptions[typeIndex];

                if (src.type == "local")
                {
                    src.machineId = EditorGUILayout.TextField(src.machineId, GUILayout.Width(120));
                    src.path = EditorGUILayout.TextField(src.path);
                    if (GUILayout.Button("Browse", GUILayout.Width(60)))
                    {
                        var picked = EditorUtility.OpenFilePanel("Select Build", Path.GetDirectoryName(src.path ?? ""), "");
                        if (!string.IsNullOrEmpty(picked))
                        {
                            src.path = picked;
                            if (File.Exists(picked))
                                fileSize = new FileInfo(picked).Length;
                        }
                    }
                }
                else
                {
                    src.url = EditorGUILayout.TextField(src.url);
                }

                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    sources.RemoveAt(i);
                    i--;
                    EditorGUILayout.EndHorizontal();
                    continue;
                }

                sources[i] = src;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Local Path"))
            {
                sources.Add(new SourceEntry { type = "local", machineId = SystemInfo.deviceName, path = lastBuildPath ?? "" });
            }
            if (GUILayout.Button("+ Google Drive Link"))
            {
                sources.Add(new SourceEntry { type = "gdrive", url = "" });
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Notes
            EditorGUILayout.LabelField("Notes", EditorStyles.boldLabel);
            notes = EditorGUILayout.TextArea(notes, GUILayout.Height(60));

            EditorGUILayout.Space();

            // Packages (collapsible)
            EditorGUILayout.LabelField($"Packages ({packages.Count})", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            foreach (var kvp in packages.Take(10))
            {
                EditorGUILayout.LabelField(kvp.Key, kvp.Value);
            }
            if (packages.Count > 10)
                EditorGUILayout.LabelField($"... and {packages.Count - 10} more");
            EditorGUI.indentLevel--;

            EditorGUILayout.Space();

            // Register button
            EditorGUI.BeginDisabledGroup(isRegistering || sources.Count == 0);
            if (GUILayout.Button(isRegistering ? "Registering..." : "Register Build", GUILayout.Height(35)))
            {
                RegisterBuild();
            }
            EditorGUI.EndDisabledGroup();

            // Status
            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, statusMessage.StartsWith("Error") ? MessageType.Error : MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        void RegisterBuild()
        {
            var config = ODDGames.Bugpunch.DeviceConnect.BugpunchConfig.Load();
            if (config == null || string.IsNullOrEmpty(config.apiKey))
            {
                statusMessage = "Error: BugpunchConfig not configured. Set server URL and API key.";
                return;
            }

            isRegistering = true;
            statusMessage = "Registering...";

            try
            {
                var url = config.serverUrl.TrimEnd('/');
                if (url.StartsWith("ws://")) url = "http://" + url.Substring(5);
                else if (url.StartsWith("wss://")) url = "https://" + url.Substring(6);

                // Build the registration payload
                var sourcesJson = "[" + string.Join(",", sources.Select(s =>
                {
                    if (s.type == "local")
                        return $"{{\"type\":\"local\",\"machineId\":\"{Esc(s.machineId)}\",\"path\":\"{Esc(s.path)}\"}}";
                    else
                        return $"{{\"type\":\"gdrive\",\"url\":\"{Esc(s.url)}\"}}";
                })) + "]";

                var packagesJson = "{" + string.Join(",", packages.Select(p => $"\"{Esc(p.Key)}\":\"{Esc(p.Value)}\"")) + "}";
                var definesJson = "[" + string.Join(",", (scriptingDefines ?? new string[0]).Select(d => $"\"{Esc(d)}\"")) + "]";

                var body = $@"{{
                    ""platform"":""{Esc(platform)}"",
                    ""buildConfig"":""{Esc(buildConfig)}"",
                    ""appVersion"":""{Esc(appVersion)}"",
                    ""changeset"":""{Esc(changeset)}"",
                    ""branch"":""{Esc(branch)}"",
                    ""notes"":""{Esc(notes)}"",
                    ""sources"":{sourcesJson},
                    ""buildMeta"":{{
                        ""fileSize"":{fileSize},
                        ""bundleId"":""{Esc(bundleId)}"",
                        ""buildMachine"":""{Esc(SystemInfo.deviceName)}"",
                        ""buildTimestamp"":""{DateTime.UtcNow:O}"",
                        ""unity"":{{
                            ""version"":""{Esc(unityVersion)}"",
                            ""scriptingBackend"":""{Esc(scriptingBackend)}"",
                            ""il2cppCompilerConfig"":""{Esc(il2cppConfig)}"",
                            ""managedStripping"":""{Esc(managedStripping)}"",
                            ""incrementalGC"":{(incrementalGC ? "true" : "false")}
                        }},
                        ""android"":{{
                            ""minSdk"":{androidMinSdk},
                            ""targetSdk"":{androidTargetSdk}
                        }},
                        ""packages"":{packagesJson},
                        ""defines"":{definesJson}
                    }}
                }}";

                var request = new UnityWebRequest($"{url}/api/artifacts/register", "POST");
                request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("X-Api-Key", config.apiKey);
                if (!string.IsNullOrEmpty(config.projectId))
                    request.SetRequestHeader("X-Project-Id", config.projectId);

                var op = request.SendWebRequest();
                while (!op.isDone) { }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    statusMessage = $"Build registered! {platform} {appVersion} ({buildConfig})";
                    Debug.Log($"[Bugpunch] Build registered: {request.downloadHandler.text}");
                }
                else
                {
                    statusMessage = $"Error: {request.error} — {request.downloadHandler?.text}";
                }

                request.Dispose();
            }
            catch (Exception ex)
            {
                statusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                isRegistering = false;
            }
        }

        static string GetPlasticChangeset()
        {
            try
            {
                var p = new System.Diagnostics.Process();
                p.StartInfo = new System.Diagnostics.ProcessStartInfo("cm", "wi --machinereadable")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Application.dataPath
                };
                p.Start();
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                var parts = output.Trim().Split('#');
                return parts.Length >= 1 ? parts[0] : null;
            }
            catch { return null; }
        }

        static string GetPlasticBranch()
        {
            try
            {
                var p = new System.Diagnostics.Process();
                p.StartInfo = new System.Diagnostics.ProcessStartInfo("cm", "wi --machinereadable")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Application.dataPath
                };
                p.Start();
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                var parts = output.Trim().Split('#');
                return parts.Length >= 2 ? parts[1] : null;
            }
            catch { return null; }
        }

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";
    }
}
