using ODDGames.Bugpunch.DeviceConnect;
using UnityEngine;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Static entry point for Bugpunch bug reporting. Thin facade over the
    /// native coordinator — see <see cref="BugpunchNative"/>. All methods are
    /// safe to call before <c>BugpunchClient</c> has initialized (they'll
    /// no-op and log a warning).
    /// </summary>
    public static class Bugpunch
    {
        /// <summary>
        /// Enable debug recording (starts the native screen ring buffer).
        /// By default shows a consent sheet with Start / Cancel; pass
        /// <paramref name="skipConsent"/> = true for debug/alpha builds where
        /// the tester has already opted in. Android's OS-level MediaProjection
        /// consent dialog still appears regardless — that can't be bypassed.
        /// No-op if already recording.
        /// </summary>
        public static void EnterDebugMode(bool skipConsent = false)
        {
            if (!EnsureStarted()) return;
            BugpunchNative.EnterDebugMode(skipConsent);
        }

        /// <summary>
        /// File a bug report. Native captures screenshot + dumps video (if
        /// recording) + assembles metadata + enqueues upload. Fire-and-forget.
        /// </summary>
        public static void Report(string title = null, string description = null,
            string type = "bug")
        {
            if (!EnsureStarted()) return;
            BugpunchNative.ReportBug(type, title, description, null);
        }

        /// <summary>
        /// Send a feedback message (native will attach a screenshot).
        /// </summary>
        public static void Feedback(string message)
        {
            if (!EnsureStarted()) return;
            BugpunchNative.ReportBug("feedback", null, message, null);
        }

        /// <summary>
        /// Attach a custom key/value to subsequent reports. Lives in the native
        /// side's custom-data map; persists for the session.
        /// </summary>
        public static void SetCustomData(string key, string value)
        {
            if (!EnsureStarted()) return;
            BugpunchNative.SetCustomData(key, value);
        }

        /// <summary>
        /// Clear a custom data entry.
        /// </summary>
        public static void ClearCustomData(string key)
        {
            if (!EnsureStarted()) return;
            BugpunchNative.SetCustomData(key, null);
        }

        /// <summary>
        /// Add a runtime attachment allow-list rule. Prefer configuring rules
        /// in the BugpunchConfig ScriptableObject (reviewable in the Inspector)
        /// \u2014 this API is the escape hatch for genuinely dynamic paths. Server
        /// "Request More Info" directives can only target paths that match an
        /// allow-list rule, so the game stays in control of what may leave the
        /// device. Call any time; rules take effect on the next config sync.
        /// </summary>
        public static void AddAttachmentRule(string name, string path, string pattern, long maxBytes)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(pattern)) return;
            BugpunchClient.AddRuntimeAttachmentRule(new BugpunchConfig.AttachmentRule
            {
                name = name ?? "rule",
                path = path,
                pattern = pattern,
                maxBytes = maxBytes,
            });
        }

        /// <summary>
        /// Mark a moment in the session with a label. Included in any
        /// subsequent bug report as a trace event (ring buffer, max 50).
        /// No-op if BugpunchClient isn't initialized.
        /// </summary>
        public static void Trace(string label)
        {
            if (BugpunchClient.Instance == null) return;
            BugpunchNative.Trace(label, null);
        }

        /// <summary>
        /// Mark a moment with a label and extra tags.
        /// </summary>
        public static void Trace(string label, System.Collections.Generic.Dictionary<string, string> tags)
        {
            if (BugpunchClient.Instance == null) return;
            BugpunchNative.Trace(label, SerializeTags(tags));
        }

        /// <summary>
        /// Same as <see cref="Trace(string)"/>, but also captures a screenshot
        /// at call time and attaches it to the next bug report.
        /// </summary>
        public static void TraceScreenshot(string label)
        {
            if (BugpunchClient.Instance == null) return;
            BugpunchNative.TraceScreenshot(label, null);
        }

        /// <summary>
        /// Same as Trace with tags, but also captures a screenshot.
        /// </summary>
        public static void TraceScreenshot(string label, System.Collections.Generic.Dictionary<string, string> tags)
        {
            if (BugpunchClient.Instance == null) return;
            BugpunchNative.TraceScreenshot(label, SerializeTags(tags));
        }

        static string SerializeTags(System.Collections.Generic.Dictionary<string, string> tags)
        {
            if (tags == null || tags.Count == 0) return null;
            var sb = new System.Text.StringBuilder(64);
            sb.Append('{');
            bool first = true;
            foreach (var kv in tags)
            {
                if (kv.Key == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(EscJson(kv.Key)).Append("\":\"")
                    .Append(EscJson(kv.Value ?? "")).Append('"');
            }
            sb.Append('}');
            return sb.ToString();
        }

        static string EscJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length + 8);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:X4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // ── Game Config Variables ──

        /// <summary>
        /// Read a game config variable set on the Bugpunch dashboard.
        /// Variables are fetched from the server on startup. Overrides
        /// matched to this device's GPU/memory/screen/platform are
        /// automatically applied — the caller gets the resolved value.
        /// Returns <paramref name="defaultValue"/> if the key is not set
        /// or config hasn't been fetched yet.
        /// </summary>
        public static string GetVariable(string key, string defaultValue = null)
            => BugpunchClient.GetVariable(key, defaultValue);

        /// <summary>Convenience: read a boolean game config variable.</summary>
        public static bool GetVariableBool(string key, bool defaultValue = false)
        {
            var v = GetVariable(key);
            if (v == null) return defaultValue;
            if (v == "true" || v == "1") return true;
            if (v == "false" || v == "0") return false;
            return defaultValue;
        }

        /// <summary>Convenience: read an integer game config variable.</summary>
        public static int GetVariableInt(string key, int defaultValue = 0)
        {
            var v = GetVariable(key);
            return v != null && int.TryParse(v, out var n) ? n : defaultValue;
        }

        /// <summary>Convenience: read a float game config variable.</summary>
        public static float GetVariableFloat(string key, float defaultValue = 0f)
        {
            var v = GetVariable(key);
            return v != null && float.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : defaultValue;
        }

        static bool EnsureStarted()
        {
            if (BugpunchClient.Instance != null) return true;
            Debug.LogWarning("[Bugpunch] BugpunchClient not initialized. Call BugpunchClient.StartConnection() first.");
            return false;
        }
    }
}
