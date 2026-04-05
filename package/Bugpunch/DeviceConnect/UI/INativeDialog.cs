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

    public interface INativeDialog
    {
        void ShowPermission(string scriptName, string scriptDescription, Action<PermissionResult> callback);
        void ShowBugReport(Action<BugReportData> onSubmit, Action onCancel);
        bool IsSupported { get; }
    }
}
