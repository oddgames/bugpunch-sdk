using System;

namespace ODDGames.Bugpunch.UI
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
        void ShowCrashReport(CrashReportContext context, Action<CrashReportResult> onSubmit, Action onDismiss);

        /// <summary>
        /// Show a friendly welcome card explaining that we're entering debug/recording mode.
        /// User taps "Got it" to proceed or "Cancel" to dismiss.
        /// </summary>
        void ShowReportWelcome(Action onConfirm, Action onCancel);

        /// <summary>
        /// Show a native floating overlay button for the recording-in-progress state.
        /// Draggable, shows elapsed timer. Tap fires onStopRecording and removes itself.
        /// </summary>
        void ShowRecordingOverlay(Action onStopRecording);

        /// <summary>
        /// Remove the recording overlay if it's currently visible.
        /// </summary>
        void HideRecordingOverlay();

        /// <summary>
        /// Show the 3-button "What would you like to do?" picker used by
        /// <c>Bugpunch.RequestHelp()</c>. The callback fires with one of:
        /// 0 = Record a bug, 1 = Ask for help, 2 = Send feedback.
        /// <paramref name="onCancel"/> fires when the user dismisses the picker
        /// without picking an option (backdrop tap, cancel button, system back).
        /// </summary>
        void ShowRequestHelp(Action<int> onChoice, Action onCancel);

        /// <summary>
        /// Surface the chat board (list of threads + detail view). On Android
        /// and iOS this is a thin native shell that UnitySendMessages back to
        /// <see cref="BugpunchClient"/>, which calls
        /// <see cref="UI.BugpunchChatBoard.Show"/>. On Editor / Standalone
        /// it opens the UIToolkit chat board directly.
        /// </summary>
        void ShowChatBoard();

        /// <summary>
        /// Surface the feedback board (list / detail / submit views with
        /// voting + similarity check). On Android this launches the native
        /// <c>BugpunchFeedbackActivity</c> (HTTP, polling, similarity, image
        /// attachments and vote toggle all in Java — C# is no longer on the
        /// path). iOS / Editor / Standalone fall back to
        /// <see cref="UI.BugpunchFeedbackBoard.Show"/>.
        /// </summary>
        void ShowFeedbackBoard();

        bool IsSupported { get; }
    }
}
