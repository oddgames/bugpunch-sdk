using System;

namespace ODDGames.Bugpunch.DeviceConnect.UI
{
    public enum PermissionResult { AllowOnce, AllowAlways, Deny }

    public class BugReportData
    {
        public string title;
        public string description;
        public string severity; // "low", "medium", "high", "critical"
        public bool includeScreenshot;
        public bool includeLogs;
    }

    /// <summary>
    /// Data passed to the crash report overlay so it can display exception details
    /// and replay the video buffer leading up to the crash.
    /// </summary>
    public class CrashReportContext
    {
        /// <summary>Short exception type + message, e.g. "NullReferenceException: Object reference not set"</summary>
        public string exceptionMessage;

        /// <summary>Full stack trace text.</summary>
        public string stackTrace;

        /// <summary>Absolute path to the ring-buffer MP4 file on disk (may be null if no video available).</summary>
        public string videoPath;

        /// <summary>JPEG screenshot bytes captured at the moment of the crash (may be null).</summary>
        public byte[] screenshotJpg;
    }

    /// <summary>
    /// Result returned from the crash report overlay when the user submits.
    /// </summary>
    public class CrashReportResult
    {
        public string title;
        public string description;
        public string severity; // "low", "medium", "high", "critical"
        public bool includeVideo;
        public bool includeLogs;
    }

    public interface INativeDialog
    {
        void ShowPermission(string scriptName, string scriptDescription, Action<PermissionResult> callback);
        void ShowBugReport(Action<BugReportData> onSubmit, Action onCancel);

        /// <summary>
        /// Show a full-screen crash report overlay with video playback, exception details,
        /// and input fields for the user to describe the issue before submitting.
        /// </summary>
        /// <param name="context">Exception info and video path.</param>
        /// <param name="onSubmit">Called with user input when they hit Submit.</param>
        /// <param name="onDismiss">Called when the user dismisses without submitting.</param>
        void ShowCrashReport(CrashReportContext context, Action<CrashReportResult> onSubmit, Action onDismiss);

        bool IsSupported { get; }
    }
}
