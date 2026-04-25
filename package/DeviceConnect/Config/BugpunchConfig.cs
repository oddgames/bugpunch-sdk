using System;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    [CreateAssetMenu(fileName = "BugpunchConfig", menuName = "ODD Games/Bugpunch Config")]
    public class BugpunchConfig : ScriptableObject
    {
        [Header("Bugpunch")]
        [Tooltip("Server URL")]
        public string serverUrl = "https://bugpunchserver-7lrpxt00.b4a.run";

        [Tooltip("API key (from project settings on the dashboard)")]
        public string apiKey = "";

        [Header("Release Info")]
        [Tooltip("Free-form release branch / channel label (e.g. main, staging, beta-ios). " +
                 "Reported with crash/perf events and usable in Game Config condition rules. " +
                 "Leave blank if not applicable. Can be overridden at runtime via " +
                 "Bugpunch.SetBranch(\"...\") before SDK init — handy for CI pipelines " +
                 "that bake the current git branch into each build.")]
        public string branch = "";

        [Tooltip("Manual changeset identifier for this build (e.g. short commit SHA, CI build number). " +
                 "Set by CI before building — either edit this field programmatically in a pre-build " +
                 "hook, or call Bugpunch.SetChangeset(\"...\") before SDK init. Reported with crash/perf " +
                 "events and usable in Game Config condition rules.")]
        public string changeset = "";

        [Header("Performance Monitoring")]
        [Tooltip("Enable native performance monitoring (FPS, memory, thermals, ANR detection). " +
                 "Runs on a background thread with minimal overhead. Reports are sent alongside " +
                 "crash/bug reports and displayed on the Performance dashboard page.")]
        public bool performanceMonitoring = true;

        [Header("SDK Diagnostics")]
        [Tooltip("Show an on-screen banner whenever the Bugpunch SDK itself swallows an internal " +
                 "error (JNI/P-Invoke failures, JSON parse failures, exceptions thrown inside SDK " +
                 "code, etc.). Tap the banner to see recent errors. Default ON for visibility — " +
                 "turn off in production builds, or toggle live via Bugpunch.SetSdkErrorOverlay(bool).")]
        public bool showSdkErrorOverlay = true;

        [Header("Advanced")]
        [Tooltip("Script permission policy for remote execution")]
        public ScriptPermission scriptPermission = ScriptPermission.Ask;

        [Tooltip("Auto-connect on app start (debug builds / editor). When off, the game must call BugpunchClient.StartConnection() explicitly.")]
        public bool autoStart = false;

        [Tooltip("Declares the intended audience for this build. Internal builds force-elevate the device to the Internal role regardless of dashboard tagging, so QA tooling works out of the box on a fresh install. Beta / Production default to the dashboard-assigned tester role (public unless tagged).")]
        public BuildChannel buildChannel = BuildChannel.Internal;

        public enum BuildChannel { Internal, Beta, Production }

        [Tooltip("Upload the built APK/IPA to the server after each Player build so testers can pull " +
                 "it from the dashboard's Builds page. Default is off — a full build is tens-to-hundreds " +
                 "of MB, and for local/iterative builds you usually don't want to spend the bandwidth. " +
                 "Turn on for CI / release builds.")]
        public bool buildUploadEnabled = false;

        [Tooltip("Upload Android unstripped symbols (and — via upload-ios-symbols.sh — iOS dSYMs) after each Player build. " +
                 "Native crash reports always write build-IDs; enabling this is what makes them resolvable to function " +
                 "names (and source:line at Debugging level) on the dashboard. " +
                 "Works with Player Settings → Publishing Settings → Symbols set to either 'Public' (SymbolTable) or " +
                 "'Debugging' (Full DWARF). Public gives function names via .symtab + IL2CPP method-map. Debugging " +
                 "gives exact source:line per crash frame but produces 1-2 GB per ABI for libil2cpp. Default is off " +
                 "since uploads are bandwidth-heavy — turn on for CI / release / pre-release QA builds.")]
        public bool symbolUploadEnabled = false;

        [Tooltip("How many .so files to upload concurrently. Default 6 covers a typical " +
                 "build's batch (3 ABIs × 2 libs after dedupe) in one wave. Bump higher only " +
                 "if you have lots of native plugins; on a residential pipe bumping past your " +
                 "upstream bandwidth ÷ ~5 Mbps/stream gives diminishing returns. Each in-flight " +
                 "upload also stages its multipart body to /tmp, so for Debugging-level builds " +
                 "(1-2 GB libil2cpp per ABI) high concurrency means high temp-disk usage. " +
                 "Range: 1-16; clamped to that range at runtime.")]
        [Range(1, 16)]
        public int symbolUploadConcurrency = 6;

        [Tooltip("Optional override for large uploads (symbols, builds). Leave blank to use serverUrl. " +
                 "Use this to bypass a CDN (e.g. Cloudflare's 100MB request body cap) by pointing at a DNS-only " +
                 "subdomain that resolves straight to the origin. Example: https://api.bugpunch.com.")]
        public string uploadServerUrl = "";

        [Header("Crash Attachment Allow-list")]
        [Tooltip("Files Bugpunch is allowed to read and upload with a crash report. " +
                 "Server 'Request More Info' directives can only reference paths that " +
                 "match at least one rule here \u2014 the game stays in control of what " +
                 "can leave the device. Supported path tokens: [PersistentDataPath], " +
                 "[TemporaryCachePath], [DataPath].")]
        public AttachmentRule[] attachmentRules = Array.Empty<AttachmentRule>();

        [Header("Log Redaction")]
        [Tooltip("Regex rules applied natively to every captured log line before it leaves the device via " +
                 "the QA log sink. Each match is replaced with [redacted:NAME]. Use this to strip user " +
                 "emails, auth tokens, session IDs, or anything else that shouldn't leave the device even " +
                 "on Internal-tagged devices. Matches are evaluated in order; a line can be " +
                 "touched by multiple rules.")]
        public LogRedactionRule[] logRedactionRules = Array.Empty<LogRedactionRule>();

        [Header("Theme")]
        [Tooltip("Colours, corner radius, and font sizes applied across every Bugpunch UI surface " +
                 "(UI Toolkit modals in C#, native cards on Android + iOS). Serialised into the " +
                 "native config blob at startup so all three surfaces render from the same values. " +
                 "Hex strings accept #RGB, #RRGGBB, or #RRGGBBAA.")]
        public BugpunchTheme Theme = new BugpunchTheme();

        [Header("Strings")]
        [Tooltip("Every user-facing string drawn by the SDK. Defaults are English; add entries to " +
                 "Translations to localise. Strings serialise into the native config blob the same " +
                 "way Theme does, so C# UIToolkit, Android Java, and iOS ObjC all draw from the " +
                 "same source.")]
        public BugpunchStrings Strings = new BugpunchStrings();

        public enum ScriptPermission { Ask, Always, Never }

        [Serializable]
        public class LogRedactionRule
        {
            [Tooltip("Label that replaces each match — shown in the redacted log as [redacted:NAME].")]
            public string name = "pii";

            [Tooltip("Regex pattern. Java / Obj-C syntax (both compile with the same POSIX-ish flavour for common patterns). " +
                     "Example: \\b\\w+@\\w+\\.\\w+\\b for emails, or Bearer\\s+\\S+ for auth headers.")]
            public string pattern = "";
        }

        [Serializable]
        public class AttachmentRule
        {
            [Tooltip("Friendly identifier shown in the dashboard.")]
            public string name = "saves";

            [Tooltip("Directory \u2014 use a token like [PersistentDataPath]/saves.")]
            public string path = "[PersistentDataPath]";

            [Tooltip("Glob pattern, e.g. *.json or save_*.dat. Use * for everything.")]
            public string pattern = "*";

            [Tooltip("Per-file size cap in bytes. Files larger than this are skipped.")]
            public long maxBytes = 1024 * 1024;
        }

        // -- Defaults (not exposed in inspector) --

        // Screen capture
        internal int captureQuality = 75;
        internal float captureScale = 0.5f;
        internal int streamFps = 10;

        // Bug reporting
        internal int videoBufferSeconds = 30;
        internal int bugReportVideoFps = 10;

        // Crash handling
        internal bool enableNativeCrashHandler = true;
        internal int anrTimeoutMs = 5000;

        // Connection
        internal float reconnectDelay = 5f;
        internal float heartbeatInterval = 10f;
        internal float pollInterval = 30f;

        // -- Derived properties --

        public string HttpBaseUrl
        {
            get
            {
                var url = serverUrl.TrimEnd('/');
                if (url.StartsWith("ws://")) url = "http://" + url.Substring(5);
                else if (url.StartsWith("wss://")) url = "https://" + url.Substring(6);
                return url;
            }
        }

        public string EffectiveDeviceName => SystemInfo.deviceName;

        /// <summary>
        /// wss:// URL for the managed C# IDE tunnel — Remote IDE RPC only.
        /// The native report tunnel uses a separate endpoint
        /// (/api/devices/report-tunnel).
        /// </summary>
        public string IdeTunnelUrl
        {
            get
            {
                var baseUrl = serverUrl.TrimEnd('/');
                if (baseUrl.StartsWith("https://"))
                    baseUrl = "wss://" + baseUrl.Substring(8);
                else if (baseUrl.StartsWith("http://"))
                    baseUrl = "ws://" + baseUrl.Substring(7);
                return baseUrl + "/api/devices/ide-tunnel";
            }
        }

        /// <summary>
        /// Resolve a path that may begin with a known Unity token
        /// ([PersistentDataPath], [TemporaryCachePath], [DataPath]) to an
        /// absolute path. Returns null if the token is unknown or unavailable
        /// on the current platform. Caller is responsible for further
        /// validation (rejecting "..", etc).
        /// </summary>
        public static string ResolvePathToken(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (path.StartsWith("[PersistentDataPath]"))
                return Application.persistentDataPath + path.Substring("[PersistentDataPath]".Length);
            if (path.StartsWith("[TemporaryCachePath]"))
                return Application.temporaryCachePath + path.Substring("[TemporaryCachePath]".Length);
            if (path.StartsWith("[DataPath]"))
                return Application.dataPath + path.Substring("[DataPath]".Length);
            return null;
        }

        public static BugpunchConfig Load()
        {
            var config = Resources.Load<BugpunchConfig>("BugpunchConfig");
            if (config == null)
                Debug.LogWarning("[Bugpunch.BugpunchConfig] No BugpunchConfig found in Resources/. Create one via Assets > Create > ODD Games > Bugpunch Config");
            return config;
        }

#if UNITY_EDITOR
        // ─── Default icon defaulting (editor only) ─────────────────────
        //
        // Packaged picker icons live outside Resources/ so they're not
        // auto-included in every build. Instead the asset references on
        // BugpunchTheme.iconBug/iconAsk/iconFeedback pull them in only when
        // the developer has them assigned. Reset() (called by Unity when
        // the asset is created or when the user picks "Reset" from the
        // inspector cog) auto-populates those fields with the defaults so
        // a fresh config picks them up without manual work. If the
        // developer replaces an icon with their own, the default texture
        // becomes unreferenced and Unity's build pipeline strips it.

        void Reset()
        {
            if (Theme == null) Theme = new BugpunchTheme();
            PopulateDefaultIconsIfMissing();
        }

        // Backfill defaults onto an already-existing config (e.g. a project
        // that was created before the icons moved out of Resources/). Only
        // fires when ALL three fields are null — the "config predates
        // defaults" case. If the developer deliberately cleared one icon
        // to drop it from the picker, the other two stay populated and we
        // don't reverse the explicit clear. Use the cog-menu "Reset picker
        // icons to defaults" if you want to force-restore the full set.
        void OnValidate()
        {
            if (Theme == null) return;
            if (Theme.iconBug == null && Theme.iconAsk == null && Theme.iconFeedback == null)
                PopulateDefaultIconsIfMissing();
        }

        [ContextMenu("Reset picker icons to defaults")]
        void ResetIconsToDefaultsMenu()
        {
            if (Theme == null) Theme = new BugpunchTheme();
            Theme.iconBug = Theme.iconAsk = Theme.iconFeedback = null;
            PopulateDefaultIconsIfMissing();
            UnityEditor.EditorUtility.SetDirty(this);
        }

        void PopulateDefaultIconsIfMissing()
        {
            if (Theme.iconBug      == null) Theme.iconBug      = LoadDefaultIcon("bugpunch-help-bug");
            if (Theme.iconAsk      == null) Theme.iconAsk      = LoadDefaultIcon("bugpunch-help-ask");
            if (Theme.iconFeedback == null) Theme.iconFeedback = LoadDefaultIcon("bugpunch-help-feedback");
        }

        static Texture2D LoadDefaultIcon(string name)
        {
            // FindAssets with t:Texture2D matches by base name across the
            // project — works whether the SDK lives under Assets/ or in
            // Packages/au.com.oddgames.bugpunch/Icons/ as a UPM install.
            var guids = UnityEditor.AssetDatabase.FindAssets(name + " t:Texture2D");
            foreach (var guid in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;
                var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null) return tex;
            }
            return null;
        }
#endif
    }
}
