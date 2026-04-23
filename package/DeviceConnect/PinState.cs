using System;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Read-through accessor for the QA pin flags. On device, all state lives
    /// natively (<c>BugpunchTunnel.java</c> SharedPreferences cache on Android,
    /// <c>BugpunchTunnel.mm</c> Keychain cache on iOS). Pin config arrives on
    /// the native report tunnel in the <c>registered</c> ack or a live
    /// <c>pinUpdate</c> frame, is parsed + stored natively, and C# simply
    /// queries via the P/Invoke + JNI surface on <c>BugpunchNative</c>.
    ///
    /// <para>In the Editor there's no native tunnel, so pin state stays at the
    /// local PlayerPrefs cache (set manually or via <see cref="ApplyFromJson"/>
    /// from test fixtures). The managed <see cref="IdeTunnel"/> is a Remote
    /// IDE RPC channel only — it does not carry pinConfig.</para>
    ///
    /// <para>Consent is the final gate on both paths. Pins read false unless
    /// <see cref="Consent"/> is <c>accepted</c>.</para>
    /// </summary>
    public static class PinState
    {
        const string PP_LOG     = "Bugpunch.pin.alwaysLog";
        const string PP_REMOTE  = "Bugpunch.pin.alwaysRemote";
        const string PP_DEBUG   = "Bugpunch.pin.alwaysDebug";
        const string PP_CONSENT = "Bugpunch.pin.consent";

#if UNITY_EDITOR
        // Editor-only mirrors fed by ApplyFromJson for local dev flow.
        static bool s_log, s_remote, s_debug;
        static string s_consent = "unknown";
#endif

        public static bool IsAlwaysLog
        {
            get
            {
#if UNITY_EDITOR
                return s_consent == "accepted" && s_log;
#else
                return BugpunchNative.PinAlwaysLog();
#endif
            }
        }

        public static bool IsAlwaysRemote
        {
            get
            {
#if UNITY_EDITOR
                return s_consent == "accepted" && s_remote;
#else
                return BugpunchNative.PinAlwaysRemote();
#endif
            }
        }

        public static bool IsAlwaysDebug
        {
            get
            {
#if UNITY_EDITOR
                return s_consent == "accepted" && s_debug;
#else
                return BugpunchNative.PinAlwaysDebug();
#endif
            }
        }

        public static string Consent
        {
            get
            {
#if UNITY_EDITOR
                return s_consent;
#else
                return BugpunchNative.PinConsent();
#endif
            }
        }

        /// <summary>Fires whenever the editor-side cached pin state changes.</summary>
        public static event Action OnChanged;

#if UNITY_EDITOR
        static PinState()
        {
            s_log     = PlayerPrefs.GetInt(PP_LOG,    0) != 0;
            s_remote  = PlayerPrefs.GetInt(PP_REMOTE, 0) != 0;
            s_debug   = PlayerPrefs.GetInt(PP_DEBUG,  0) != 0;
            s_consent = PlayerPrefs.GetString(PP_CONSENT, "unknown");
        }
#endif

        /// <summary>
        /// Editor-only: parse a signed pin config JSON blob from the managed
        /// tunnel and apply it. On device, the native tunnel does this work
        /// directly — <see cref="ApplyFromJson"/> is a no-op there.
        /// </summary>
        public static void ApplyFromJson(string configJson)
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(configJson)) return;

            var consent = ExtractString(configJson, "consent") ?? "unknown";
            bool apply  = consent == "accepted";
            bool log    = apply && ExtractBool(configJson, "alwaysLog");
            bool remote = apply && ExtractBool(configJson, "alwaysRemote");
            bool debug  = apply && ExtractBool(configJson, "alwaysDebug");

            bool changed = log != s_log || remote != s_remote
                || debug != s_debug || consent != s_consent;
            if (!changed) return;

            s_log = log;
            s_remote = remote;
            s_debug = debug;
            s_consent = consent;

            PlayerPrefs.SetInt(PP_LOG,    log    ? 1 : 0);
            PlayerPrefs.SetInt(PP_REMOTE, remote ? 1 : 0);
            PlayerPrefs.SetInt(PP_DEBUG,  debug  ? 1 : 0);
            PlayerPrefs.SetString(PP_CONSENT, consent);
            PlayerPrefs.Save();

            try { OnChanged?.Invoke(); }
            catch (Exception e) { Debug.LogWarning($"[Bugpunch.PinState] PinState.OnChanged handler failed: {e.Message}"); }
#else
            // On device the native tunnel owns state. Ignore.
#endif
        }

#if UNITY_EDITOR
        static bool ExtractBool(string json, string key)
        {
            var needle = "\"" + key + "\":";
            int idx = json.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0) return false;
            idx += needle.Length;
            while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
            return idx < json.Length - 3 &&
                json[idx] == 't' && json[idx + 1] == 'r' && json[idx + 2] == 'u' && json[idx + 3] == 'e';
        }

        static string ExtractString(string json, string key)
        {
            var needle = "\"" + key + "\":\"";
            int idx = json.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0) return null;
            idx += needle.Length;
            int end = json.IndexOf('"', idx);
            return end < 0 ? null : json.Substring(idx, end - idx);
        }
#endif
    }
}
