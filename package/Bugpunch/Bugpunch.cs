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

        static bool EnsureStarted()
        {
            if (BugpunchClient.Instance != null) return true;
            Debug.LogWarning("[Bugpunch] BugpunchClient not initialized. Call BugpunchClient.StartConnection() first.");
            return false;
        }
    }
}
