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

        [Header("Advanced")]
        [Tooltip("Script permission policy for remote execution")]
        public ScriptPermission scriptPermission = ScriptPermission.Ask;

        [Tooltip("Auto-connect on app start (debug builds / editor). When off, the game must call BugpunchClient.StartConnection() explicitly.")]
        public bool autoStart = false;

        [Tooltip("Declares the intended audience for this build. Controls server-side guardrails for QA pins:\n" +
                 "• internal — all pins available (QA builds, alpha testers)\n" +
                 "• beta — alwaysLog and alwaysRemote allowed; alwaysDebug warns\n" +
                 "• production — alwaysDebug is refused (video capture on a shipped build is a privacy disaster). " +
                 "alwaysLog / alwaysRemote still work but require an extra confirm in the dashboard.")]
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
                 "when the admin has enrolled alwaysLog. Matches are evaluated in order; a line can be " +
                 "touched by multiple rules.")]
        public LogRedactionRule[] logRedactionRules = Array.Empty<LogRedactionRule>();

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

        public string TunnelUrl
        {
            get
            {
                var baseUrl = serverUrl.TrimEnd('/');
                if (baseUrl.StartsWith("https://"))
                    baseUrl = "wss://" + baseUrl.Substring(8);
                else if (baseUrl.StartsWith("http://"))
                    baseUrl = "ws://" + baseUrl.Substring(7);
                return baseUrl + "/api/devices/tunnel";
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
                Debug.LogWarning("[Bugpunch] No BugpunchConfig found in Resources/. Create one via Assets > Create > ODD Games > Bugpunch Config");
            return config;
        }
    }
}
