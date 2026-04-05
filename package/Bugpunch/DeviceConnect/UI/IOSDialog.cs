#if UNITY_IOS
using System;
using System.Runtime.InteropServices;
using AOT;

namespace ODDGames.Bugpunch.DeviceConnect.UI
{
    public class IOSDialog : INativeDialog
    {
        public bool IsSupported => UnityEngine.Application.platform == UnityEngine.RuntimePlatform.IPhonePlayer;

        static Action<PermissionResult> _permissionCallback;
        static Action<BugReportData> _submitCallback;
        static Action _cancelCallback;

        delegate void PermissionCallbackDelegate(int result);
        delegate void BugSubmitDelegate(string title, string description, string severity);
        delegate void BugCancelDelegate();

        [DllImport("__Internal")] static extern void Bugpunch_ShowPermissionDialog(string title, string message, PermissionCallbackDelegate cb);
        [DllImport("__Internal")] static extern void Bugpunch_ShowBugReportDialog(BugSubmitDelegate onSubmit, BugCancelDelegate onCancel);

        public void ShowPermission(string scriptName, string scriptDescription, Action<PermissionResult> callback)
        {
            _permissionCallback = callback;
            Bugpunch_ShowPermissionDialog("Script Permission",
                $"The server wants to run a script:\n\n{scriptName}\n\n{scriptDescription}",
                OnPermission);
        }

        [MonoPInvokeCallback(typeof(PermissionCallbackDelegate))]
        static void OnPermission(int result)
        {
            var cb = _permissionCallback;
            _permissionCallback = null;
            cb?.Invoke((PermissionResult)result);
        }

        public void ShowBugReport(Action<BugReportData> onSubmit, Action onCancel)
        {
            _submitCallback = onSubmit;
            _cancelCallback = onCancel;
            Bugpunch_ShowBugReportDialog(OnBugSubmit, OnBugCancel);
        }

        [MonoPInvokeCallback(typeof(BugSubmitDelegate))]
        static void OnBugSubmit(string title, string description, string severity)
        {
            var cb = _submitCallback;
            _submitCallback = null;
            cb?.Invoke(new BugReportData
            {
                title = title,
                description = description,
                severity = severity,
                includeScreenshot = true,
                includeLogs = true
            });
        }

        [MonoPInvokeCallback(typeof(BugCancelDelegate))]
        static void OnBugCancel()
        {
            var cb = _cancelCallback;
            _cancelCallback = null;
            cb?.Invoke();
        }
    }
}
#endif
