using System;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// The three roles a device can be tagged with. Drives every interactive
    /// SDK feature — role alone replaces the old per-feature pin toggles.
    /// </summary>
    public enum TesterRole
    {
        /// <summary>Consumer build. Crash reporting only.</summary>
        Public,
        /// <summary>Outside testers. Startup debug-mode consent prompt only.</summary>
        External,
        /// <summary>ODD staff. Ambient log + Remote IDE + startup debug prompt.</summary>
        Internal,
    }

    /// <summary>
    /// Read-through accessor for the server-assigned tester role. On device,
    /// state lives natively (SharedPreferences on Android, Keychain on iOS)
    /// and is updated at handshake time from the signed <c>roleConfig</c>
    /// payload.
    ///
    /// <para>In the Editor there's no native tunnel, so role stays at the
    /// PlayerPrefs-backed mirror (settable via <see cref="ApplyFromJson"/>
    /// from test fixtures or an injected value).</para>
    /// </summary>
    public static class RoleState
    {
        const string PP_ROLE = "Bugpunch.role";

#if UNITY_EDITOR
        static TesterRole s_role = TesterRole.Public;
#endif

        /// <summary>The device's current tester role.</summary>
        public static TesterRole Current
        {
            get
            {
#if UNITY_EDITOR
                return s_role;
#else
                return Parse(BugpunchNative.GetTesterRole());
#endif
            }
        }

        /// <summary>True if the device is tagged <c>Internal</c>.</summary>
        public static bool IsInternal => Current == TesterRole.Internal;

        /// <summary>True if role is Internal or External — i.e. eligible for the startup debug-mode consent prompt.</summary>
        public static bool IsTester => Current != TesterRole.Public;

        /// <summary>Fires whenever the editor-side cached role changes.</summary>
        public static event Action OnChanged;

#if UNITY_EDITOR
        // Read PlayerPrefs from a main-thread Editor init hook rather than a
        // static constructor — the static ctor would otherwise run on whichever
        // thread first touched the type (e.g. the IdeTunnel WebSocket receive
        // thread when handling the registered frame), and PlayerPrefs.GetString
        // is main-thread-only. InitializeOnLoadMethod fires on the main thread
        // every time the Editor reloads scripts.
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorInit()
        {
            try { s_role = Parse(PlayerPrefs.GetString(PP_ROLE, "public")); }
            catch { /* leave as Public on first launch */ }
        }
#endif

        /// <summary>
        /// Editor-only: parse a signed role config JSON blob and apply it.
        /// On device the native tunnel owns state — this is a no-op there.
        /// </summary>
        public static void ApplyFromJson(string configJson)
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(configJson)) return;
            var roleStr = ExtractString(configJson, "role") ?? "public";
            var next = Parse(roleStr);
            if (next == s_role) return;
            s_role = next;
            PlayerPrefs.SetString(PP_ROLE, roleStr);
            PlayerPrefs.Save();
            try { OnChanged?.Invoke(); }
            catch (Exception e) { BugpunchLog.Warn("RoleState", $"OnChanged handler failed: {e.Message}"); }
#else
            // On device the native tunnel owns state. Ignore.
#endif
        }

        internal static TesterRole Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return TesterRole.Public;
            switch (raw.ToLowerInvariant())
            {
                case "internal":
                case "admin":      // legacy — pre-collapse tag
                case "developer":  // legacy — pre-collapse tag
                    return TesterRole.Internal;
                case "external":
                    return TesterRole.External;
                default:
                    return TesterRole.Public;
            }
        }

#if UNITY_EDITOR
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
