namespace ODDGames.Bugpunch.DeviceConnect.UI
{
    public static class NativeDialogFactory
    {
        static INativeDialog _instance;

        public static INativeDialog Create()
        {
            if (_instance != null) return _instance;

#if UNITY_EDITOR
            _instance = new EditorDialogWrapper();
#elif UNITY_ANDROID
            CrashOverlayCallback.EnsureExists();
            _instance = new AndroidDialog();
#elif UNITY_IOS
            _instance = new IOSDialog();
#else
            _instance = new FallbackDialog();
#endif
            return _instance;
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Wrapper that delegates to the Editor assembly's dialog.
    /// Lives in runtime assembly but only compiles in Editor.
    /// </summary>
    internal class EditorDialogWrapper : INativeDialog
    {
        public bool IsSupported => true;

        public void ShowPermission(string scriptName, string scriptDescription, System.Action<PermissionResult> callback)
        {
            // Use EditorUtility.DisplayDialogComplex (available in Editor context)
            int result = UnityEditor.EditorUtility.DisplayDialogComplex(
                "Script Permission",
                $"The server wants to run a script on this device:\n\n{scriptName}\n\n{scriptDescription}",
                "Allow Once", "Deny", "Always Allow"
            );
            switch (result)
            {
                case 0: callback?.Invoke(PermissionResult.AllowOnce); break;
                case 1: callback?.Invoke(PermissionResult.Deny); break;
                case 2: callback?.Invoke(PermissionResult.AllowAlways); break;
            }
        }

        public void ShowBugReport(System.Action<BugReportData> onSubmit, System.Action onCancel)
        {
            // Simple editor dialog — for full Editor window, use the EditorBugReportWindow in the Editor assembly
            bool submit = UnityEditor.EditorUtility.DisplayDialog(
                "Bug Report",
                "Submit a bug report?",
                "Submit", "Cancel"
            );
            if (submit)
                onSubmit?.Invoke(new BugReportData { title = "Editor Bug Report", severity = "medium", includeScreenshot = true, includeLogs = true });
            else
                onCancel?.Invoke();
        }

        public void ShowCrashReport(CrashReportContext context, System.Action<CrashReportResult> onSubmit, System.Action onDismiss)
        {
            // In the Editor, use a simple dialog. For a richer experience, the
            // EditorBugReportWindow in the Editor assembly could be extended.
            bool submit = UnityEditor.EditorUtility.DisplayDialog(
                "Crash Report",
                $"Exception:\n{context.exceptionMessage}\n\nStack trace:\n{(context.stackTrace?.Length > 400 ? context.stackTrace.Substring(0, 400) + "..." : context.stackTrace)}\n\nSubmit crash report?",
                "Submit", "Dismiss"
            );
            if (submit)
                onSubmit?.Invoke(new CrashReportResult
                {
                    title = context.exceptionMessage ?? "Crash Report",
                    description = context.stackTrace ?? "",
                    severity = "critical",
                    includeVideo = !string.IsNullOrEmpty(context.videoPath),
                    includeLogs = true
                });
            else
                onDismiss?.Invoke();
        }
    }
#endif
}
