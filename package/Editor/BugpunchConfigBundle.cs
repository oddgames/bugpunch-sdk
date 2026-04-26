using System.IO;
using System.Text;
using ODDGames.Bugpunch.DeviceConnect;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Serializes the static parts of <see cref="BugpunchConfig"/> into
    /// <c>bugpunch_config.json</c> and places it where native code can read it
    /// before Unity boots:
    ///   Android → <c>Assets/Plugins/Android/assets/bugpunch_config.json</c>
    ///             (ends up in the APK's <c>assets/</c> directory).
    ///   iOS     → added to the generated Xcode project's main bundle.
    /// </summary>
    public class BugpunchConfigBundle : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        // Run before BugpunchPostBuildHook (callbackOrder=0) so the config is
        // in place for the Gradle / Xcode build, and the cleanup happens after.
        public int callbackOrder => -100;

        // Unity 6+ rejects loose resources in Assets/Plugins/Android/assets —
        // we stage the bundle inside a `.androidlib` directory instead, which
        // Unity treats as an Android Library project and packages its assets/
        // into the final APK's assets/ at the root.
        const string AndroidLibDir     = "Assets/Plugins/Android/bugpunch_config.androidlib";
        const string AndroidLibManifest = AndroidLibDir + "/AndroidManifest.xml";
        const string AndroidLibProjectProps = AndroidLibDir + "/project.properties";
        const string AndroidLibAssetsDir = AndroidLibDir + "/assets";
        const string AndroidLibAssetPath = AndroidLibAssetsDir + "/bugpunch_config.json";
        const string IosStagingDir = "Temp/Bugpunch";
        const string IosStagingFile = IosStagingDir + "/bugpunch_config.json";

        // Minimal AndroidManifest required by a .androidlib directory. No
        // permissions, no activities — we only use this lib to carry the
        // bundled config JSON as an asset.
        const string AndroidLibManifestXml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\"\n" +
            "          package=\"au.com.oddgames.bugpunch.config\">\n" +
            "    <application />\n" +
            "</manifest>\n";

        const string AndroidLibProjectPropsText = "android.library=true\n";

        public void OnPreprocessBuild(BuildReport report)
        {
            var config = BugpunchConfig.Load();
            if (config == null) return;

            // Fetch the project's pin-signing secret once at build time so
            // the bundled config can carry it into native. Missing secret
            // means native can't HMAC-verify pin configs and will reject
            // them (safe fail — device won't apply pins instead of applying
            // unverified ones).
            var signingSecret = FetchPinSigningSecret(config);

            var json = SerializeStatic(config, signingSecret);
            var target = report.summary.platform;

            if (target == BuildTarget.Android)
            {
                Directory.CreateDirectory(AndroidLibAssetsDir);
                var utf8 = new UTF8Encoding(false);
                if (!File.Exists(AndroidLibManifest))
                    File.WriteAllText(AndroidLibManifest, AndroidLibManifestXml, utf8);
                if (!File.Exists(AndroidLibProjectProps))
                    File.WriteAllText(AndroidLibProjectProps, AndroidLibProjectPropsText, utf8);
                File.WriteAllText(AndroidLibAssetPath, json, utf8);
                BugpunchLog.Info("BugpunchConfigBundle", $"Bundled config → {AndroidLibAssetPath}");
            }
            else if (target == BuildTarget.iOS)
            {
                if (!Directory.Exists(IosStagingDir))
                    Directory.CreateDirectory(IosStagingDir);
                File.WriteAllText(IosStagingFile, json, new UTF8Encoding(false));
                BugpunchLog.Info("BugpunchConfigBundle", $"Staged iOS config → {IosStagingFile}");
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            var target = report.summary.platform;

            if (target == BuildTarget.Android)
            {
                // Clean the whole androidlib directory — it only exists during
                // the build. Next build regenerates with fresh secret + config.
                TryDeleteDirRecursive(AndroidLibDir);
                TryDelete(AndroidLibDir + ".meta");
            }
            else if (target == BuildTarget.iOS)
            {
#if UNITY_IOS
                CopyConfigIntoXcodeProject(report.summary.outputPath);
#endif
                TryDelete(IosStagingFile);
            }
        }

        static void TryDeleteDirRecursive(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch { /* best-effort cleanup */ }
        }

#if UNITY_IOS
        static void CopyConfigIntoXcodeProject(string xcodeProjectPath)
        {
            if (!File.Exists(IosStagingFile))
            {
                BugpunchLog.Warn("BugpunchConfigBundle", "iOS staging config missing — bundle skipped");
                return;
            }

            var dstPath = Path.Combine(xcodeProjectPath, "bugpunch_config.json");
            File.Copy(IosStagingFile, dstPath, overwrite: true);

            var pbxPath = PBXProject.GetPBXProjectPath(xcodeProjectPath);
            var pbx = new PBXProject();
            pbx.ReadFromFile(pbxPath);

            var targetGuid = pbx.GetUnityMainTargetGuid();
            var fileGuid = pbx.AddFile("bugpunch_config.json", "bugpunch_config.json", PBXSourceTree.Source);
            pbx.AddFileToBuild(targetGuid, fileGuid);

            // Also add to the framework target if present (UnityFramework), so
            // a `+load` in an embedded framework can read it via mainBundle.
            try
            {
                var frameworkGuid = pbx.GetUnityFrameworkTargetGuid();
                if (!string.IsNullOrEmpty(frameworkGuid))
                    pbx.AddFileToBuild(frameworkGuid, fileGuid);
            }
            catch { /* not all projects have a framework target */ }

            pbx.WriteToFile(pbxPath);
            BugpunchLog.Info("BugpunchConfigBundle", $"Bundled config → {dstPath} (added to Xcode main target)");
        }
#endif

        // ── Config serialization ────────────────────────────────────────────
        //
        // Only the bits that are known at build time go in the bundle. Anything
        // that depends on Unity runtime values (Application.persistentDataPath,
        // SystemInfo tier detection, Application.version) is resolved by C# on
        // first frame and pushed to native as an augmentation. The bundled
        // config is enough to start signal handlers + log ring + upload queue
        // before Unity boots.

        static string SerializeStatic(BugpunchConfig c, string signingSecret)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            Field(sb, "serverUrl",         HttpBase(c.serverUrl));                         sb.Append(',');
            Field(sb, "apiKey",            c.apiKey);                                      sb.Append(',');
            Field(sb, "pinSigningSecret",  signingSecret ?? "");                           sb.Append(',');
            Field(sb, "buildChannel",      c.buildChannel.ToString().ToLowerInvariant()); sb.Append(',');
            var useNativeTunnel = EditorUserBuildSettings.development || c.buildChannel == BugpunchConfig.BuildChannel.Internal;
            sb.Append("\"useNativeTunnel\":").Append(useNativeTunnel ? "true" : "false").Append(',');
            sb.Append("\"anrTimeoutMs\":").Append(c.anrTimeoutMs).Append(',');
            sb.Append("\"autoReportCooldownSeconds\":30,");
            sb.Append("\"shake\":{\"enabled\":false,\"threshold\":2.5},");
            // Leave buffer/fps/bitrate unset so native picks tier-based defaults
            // at runtime (it can read RAM/cores without Unity).
            sb.Append("\"video\":{\"enabled\":true,\"bufferSeconds\":")
                .Append(c.videoBufferSeconds).Append(",\"fps\":")
                .Append(c.bugReportVideoFps).Append("},");

            // metadata.{appVersion,bundleId,unityVersion} are required by the
            // server's register handshake (empty appVersion → close 4000). Bake
            // them in at build time so the native tunnel (which starts from the
            // ContentProvider before Unity boots) has them on its first connect.
            // deviceModel / osVersion are runtime-only — native fills those in.
            sb.Append("\"metadata\":{");
            Field(sb, "appVersion",   Application.version);        sb.Append(',');
            Field(sb, "bundleId",     Application.identifier);     sb.Append(',');
            Field(sb, "unityVersion", Application.unityVersion);
            sb.Append("},");

            sb.Append("\"attachmentRules\":[");
            bool first = true;
            if (c.attachmentRules != null)
            {
                foreach (var r in c.attachmentRules)
                {
                    if (r == null || string.IsNullOrEmpty(r.path)) continue;
                    if (r.path.Contains("..")) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('{');
                    Field(sb, "name", string.IsNullOrEmpty(r.name) ? "rule" : r.name); sb.Append(',');
                    Field(sb, "rawPath", r.path);                                       sb.Append(',');
                    Field(sb, "pattern", string.IsNullOrEmpty(r.pattern) ? "*" : r.pattern);
                    sb.Append(",\"maxBytes\":").Append(r.maxBytes);
                    sb.Append('}');
                }
            }
            sb.Append("],");

            // Log redaction rules applied natively before each line leaves
            // the device via the QA log sink. Patterns stay raw — native
            // compiles them once at tunnel start.
            sb.Append("\"logRedactionRules\":[");
            bool firstRed = true;
            if (c.logRedactionRules != null)
            {
                foreach (var r in c.logRedactionRules)
                {
                    if (r == null || string.IsNullOrEmpty(r.pattern)) continue;
                    if (!firstRed) sb.Append(',');
                    firstRed = false;
                    sb.Append('{');
                    Field(sb, "name", string.IsNullOrEmpty(r.name) ? "pii" : r.name); sb.Append(',');
                    Field(sb, "pattern", r.pattern);
                    sb.Append('}');
                }
            }
            sb.Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        static void Field(StringBuilder sb, string k, string v)
        {
            sb.Append('"').Append(BugpunchJson.Esc(k)).Append("\":\"").Append(BugpunchJson.Esc(v)).Append('"');
        }

        static string HttpBase(string serverUrl)
        {
            if (string.IsNullOrEmpty(serverUrl)) return "";
            var trimmed = serverUrl.TrimEnd('/');
            if (trimmed.StartsWith("wss://", System.StringComparison.OrdinalIgnoreCase))
                return "https://" + trimmed.Substring("wss://".Length);
            if (trimmed.StartsWith("ws://", System.StringComparison.OrdinalIgnoreCase))
                return "http://" + trimmed.Substring("ws://".Length);
            return trimmed;
        }

        static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { /* best-effort cleanup */ }
        }

        // ── Build-time secret fetch ──────────────────────────────────────
        //
        // Hits /api/v1/project/pin-signing-secret with the dev's API key.
        // Server resolves the project from the key, mints a secret if one
        // doesn't exist yet, and returns the raw hex. Blocking is fine here
        // — preprocessor runs once per build and the fetch is ~200 ms.
        //
        // On failure: log a warning and return empty string. Native side
        // reads "" and refuses to apply pin configs, which is the correct
        // safe-fail behaviour — a player shouldn't silently get pinned just
        // because the build machine was offline.

        static string FetchPinSigningSecret(BugpunchConfig c)
        {
            if (string.IsNullOrEmpty(c.serverUrl) || string.IsNullOrEmpty(c.apiKey))
            {
                BugpunchLog.Warn("BugpunchConfigBundle", "pin-signing secret fetch skipped: serverUrl or apiKey missing");
                return "";
            }

            var url = HttpBase(c.serverUrl) + "/api/v1/project/pin-signing-secret";
            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("X-Api-Key", c.apiKey);
            req.timeout = 10;
            req.SendWebRequest();

            var deadline = System.DateTime.UtcNow.AddSeconds(12);
            while (!req.isDone && System.DateTime.UtcNow < deadline)
                System.Threading.Thread.Sleep(50);

            if (!req.isDone || req.result != UnityWebRequest.Result.Success)
            {
                BugpunchLog.Warn("BugpunchConfigBundle", $"pin-signing secret fetch failed: {req.error ?? "timeout"}. " +
                                 "Pin configs will not be applied on device until the next build.");
                return "";
            }

            try
            {
                // Minimal parse — response shape is { "secret": "<hex>" }
                var body = req.downloadHandler.text;
                const string needle = "\"secret\":\"";
                int i = body.IndexOf(needle);
                if (i < 0) return "";
                i += needle.Length;
                int end = body.IndexOf('"', i);
                if (end < 0) return "";
                var secret = body.Substring(i, end - i);
                BugpunchLog.Info("BugpunchConfigBundle", "pin-signing secret fetched and bundled.");
                return secret;
            }
            catch (System.Exception e)
            {
                BugpunchLog.Warn("BugpunchConfigBundle", $"pin-signing secret parse failed: {e.Message}");
                return "";
            }
        }
    }
}
