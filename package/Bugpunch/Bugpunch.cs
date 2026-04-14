using System;
using ODDGames.Bugpunch.DeviceConnect;
using UnityEngine;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Static entry point for Bugpunch bug reporting.
    /// All methods are safe to call even if BugpunchClient hasn't initialized yet.
    /// </summary>
    public static class Bugpunch
    {
        static BugReporter Reporter => BugpunchClient.Instance?.Reporter;

        /// <summary>
        /// Start the bug recording flow: show a friendly welcome screen, start screen
        /// recording, show a floating report button. When the user taps it, capture
        /// everything and show the bug report form.
        /// </summary>
        public static void RecordBug()
        {
            var r = Reporter;
            if (r == null) { Warn(); return; }
            r.StartReportFlow();
        }

        /// <summary>
        /// Immediately capture screenshot, dump video buffer, and send a bug report.
        /// No welcome screen or recording overlay — just capture and send.
        /// </summary>
        public static void QuickReport(string title = null, string description = null,
            BugReportType type = BugReportType.Bug)
        {
            var r = Reporter;
            if (r == null) { Warn(); return; }
            r.StartReport(title, description, type);
        }

        /// <summary>
        /// Send simple feedback (screenshot + text, no video).
        /// </summary>
        public static void Feedback(string message, FeedbackRating rating = FeedbackRating.Neutral)
        {
            var r = Reporter;
            if (r == null) { Warn(); return; }
            r.SendFeedback(message, rating);
        }

        /// <summary>
        /// Attach custom key-value data to subsequent bug reports.
        /// </summary>
        public static void SetCustomData(string key, string value)
        {
            var r = Reporter;
            if (r == null) { Warn(); return; }
            r.SetCustomData(key, value);
        }

        /// <summary>
        /// Remove a custom data entry.
        /// </summary>
        public static void ClearCustomData(string key)
        {
            var r = Reporter;
            if (r == null) { Warn(); return; }
            r.ClearCustomData(key);
        }

        /// <summary>
        /// Start the native video ring buffer recorder. Call once on startup
        /// for alpha/feedback builds. Requires user consent on Android (MediaProjection).
        /// </summary>
        public static void EnableVideoCapture()
        {
            var r = Reporter;
            if (r == null) { Warn(); return; }
            r.EnableVideoCapture();
        }

        static void Warn() =>
            Debug.LogWarning("[Bugpunch] BugpunchClient not initialized. Call BugpunchClient.StartConnection() first.");
    }
}
