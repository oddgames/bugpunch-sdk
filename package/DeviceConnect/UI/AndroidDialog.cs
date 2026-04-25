#if UNITY_ANDROID
using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect.UI
{
    public class AndroidDialog : INativeDialog
    {
        public bool IsSupported => Application.platform == RuntimePlatform.Android;

        // Static callback holders for the native crash overlay (BugpunchCrashActivity
        // calls back via UnitySendMessage).
        internal static Action<CrashReportResult> _crashSubmitCallback;
        internal static Action _crashDismissCallback;

        public void ShowPermission(string scriptName, string scriptDescription, Action<PermissionResult> callback)
        {
            using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity");

            // Pull labels via BugpunchStrings so a customer-supplied translation
            // / re-branded copy applies here too. We can't theme the
            // AlertDialog itself (system Activity theme) without subclassing,
            // but the strings are the user-visible part.
            var strings = BugpunchStrings.Current;
            string title       = strings.Text("permissionTitle",       "Script Permission");
            string allowOnce   = strings.Text("permissionAllowOnce",   "Allow Once");
            string allowAlways = strings.Text("permissionAllowAlways", "Always Allow");
            string deny        = strings.Text("permissionDeny",        "Deny");

            activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                using var builder = new AndroidJavaObject("android.app.AlertDialog$Builder", activity);
                builder.Call<AndroidJavaObject>("setTitle", title);
                builder.Call<AndroidJavaObject>("setMessage",
                    $"The server wants to run a script:\n\n{scriptName}\n\n{scriptDescription}");
                builder.Call<AndroidJavaObject>("setCancelable", false);

                builder.Call<AndroidJavaObject>("setPositiveButton", allowOnce,
                    new DialogClickListener(() => MainThread.Enqueue(() => callback?.Invoke(PermissionResult.AllowOnce))));
                builder.Call<AndroidJavaObject>("setNeutralButton", allowAlways,
                    new DialogClickListener(() => MainThread.Enqueue(() => callback?.Invoke(PermissionResult.AllowAlways))));
                builder.Call<AndroidJavaObject>("setNegativeButton", deny,
                    new DialogClickListener(() => MainThread.Enqueue(() => callback?.Invoke(PermissionResult.Deny))));

                builder.Call<AndroidJavaObject>("show");
            }));
        }

        public void ShowBugReport(Action<BugReportData> onSubmit, Action onCancel)
        {
            using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity");

            var strings = BugpunchStrings.Current;
            string formTitle  = strings.Text("reportFormTitle",       "Bug Report");
            string titleHint  = strings.Text("reportFormTitleHint",   "Title — brief description");
            string descHint   = strings.Text("reportFormDescHint",    "Description (optional)");
            string submitText = strings.Text("reportFormSubmit",      "Submit");
            string cancelText = strings.Text("reportFormCancel",      "Cancel");

            activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                using var context = activity;
                using var layout = new AndroidJavaObject("android.widget.LinearLayout", context);
                layout.Call("setOrientation", 1);
                int pad = DpToPx(context, 24);
                layout.Call("setPadding", pad, pad, pad, pad);

                using var titleInput = new AndroidJavaObject("android.widget.EditText", context);
                titleInput.Call("setHint", titleHint);
                titleInput.Call("setSingleLine", true);
                layout.Call("addView", titleInput);

                using var descInput = new AndroidJavaObject("android.widget.EditText", context);
                descInput.Call("setHint", descHint);
                descInput.Call("setMinLines", 3);
                layout.Call("addView", descInput);

                using var builder = new AndroidJavaObject("android.app.AlertDialog$Builder", context);
                builder.Call<AndroidJavaObject>("setTitle", formTitle);
                builder.Call<AndroidJavaObject>("setView", layout);

                var tRef = titleInput;
                var dRef = descInput;

                builder.Call<AndroidJavaObject>("setPositiveButton", submitText,
                    new DialogClickListener(() => MainThread.Enqueue(() =>
                        onSubmit?.Invoke(new BugReportData
                        {
                            title = tRef.Call<string>("getText")?.ToString() ?? "",
                            description = dRef.Call<string>("getText")?.ToString() ?? "",
                            severity = "medium",
                            includeScreenshot = true,
                            includeLogs = true
                        }))));

                builder.Call<AndroidJavaObject>("setNegativeButton", cancelText,
                    new DialogClickListener(() => MainThread.Enqueue(() => onCancel?.Invoke())));

                builder.Call<AndroidJavaObject>("show");
            }));
        }

        // ── Report flow overlay callbacks ──
        internal static Action _welcomeConfirmCallback;
        internal static Action _welcomeCancelCallback;
        internal static Action _reportTappedCallback;
        internal static Action<int> _requestHelpChoiceCallback;
        internal static Action _requestHelpCancelCallback;

        public void ShowReportWelcome(Action onConfirm, Action onCancel)
        {
            _welcomeConfirmCallback = onConfirm;
            _welcomeCancelCallback = onCancel;

            using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity");
            using var overlayClass = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchReportOverlay");
            overlayClass.CallStatic("showWelcome", activity);
        }

        public void ShowRecordingOverlay(Action onStopRecording)
        {
            _reportTappedCallback = onStopRecording;

            using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity");
            using var overlayClass = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchReportOverlay");
            overlayClass.CallStatic("showRecordingOverlay", activity);
        }

        public void HideRecordingOverlay()
        {
            using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity");
            using var overlayClass = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchReportOverlay");
            overlayClass.CallStatic("hideRecordingOverlay", activity);
        }

        public void ShowRequestHelp(Action<int> onChoice, Action onCancel)
        {
            _requestHelpChoiceCallback = onChoice;
            _requestHelpCancelCallback = onCancel;

            using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity");
            using var overlayClass = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchReportOverlay");
            overlayClass.CallStatic("showRequestHelp", activity);
        }

        public void ShowChatBoard()
        {
            // Native chat "shell" — the actual UI is C# (see BugpunchChatBoard)
            // because the chat board is complex and we don't want to reinvent
            // a full chat widget in Java. Native just forwards the request
            // back into Unity via UnitySendMessage.
            using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity");
            using var overlayClass = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchReportOverlay");
            overlayClass.CallStatic("showChatBoard", activity);
        }

        public void ShowCrashReport(CrashReportContext context, Action<CrashReportResult> onSubmit, Action onDismiss)
        {
            _crashSubmitCallback = onSubmit;
            _crashDismissCallback = onDismiss;

            // Launch the native BugpunchCrashActivity which provides a full-screen
            // overlay with video playback, exception details, and input fields.
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var crashClass = new AndroidJavaClass("au.com.oddgames.bugpunch.BugpunchCrashActivity");

            crashClass.CallStatic("launch",
                activity,
                context.exceptionMessage ?? "",
                context.stackTrace ?? "",
                context.videoPath ?? "");
        }

        /// <summary>
        /// Called from BugpunchCrashActivity via UnitySendMessage when the user submits.
        /// The message is a pipe-delimited string: title|description|severity|includeVideo|includeLogs
        /// </summary>
        static void OnCrashReportSubmit(string message)
        {
            var cb = _crashSubmitCallback;
            _crashSubmitCallback = null;
            _crashDismissCallback = null;
            if (cb == null) return;

            var parts = message.Split('|');
            cb.Invoke(new CrashReportResult
            {
                title = parts.Length > 0 ? parts[0] : "",
                description = parts.Length > 1 ? parts[1] : "",
                severity = parts.Length > 2 ? parts[2] : "high",
                includeVideo = parts.Length > 3 && parts[3] == "1",
                includeLogs = parts.Length > 4 && parts[4] == "1"
            });
        }

        /// <summary>
        /// Called from BugpunchCrashActivity via UnitySendMessage when the user dismisses.
        /// </summary>
        static void OnCrashReportDismiss(string message)
        {
            var cb = _crashDismissCallback;
            _crashSubmitCallback = null;
            _crashDismissCallback = null;
            cb?.Invoke();
        }

        static int DpToPx(AndroidJavaObject context, int dp)
        {
            using var res = context.Call<AndroidJavaObject>("getResources");
            using var metrics = res.Call<AndroidJavaObject>("getDisplayMetrics");
            return (int)(dp * metrics.Get<float>("density") + 0.5f);
        }
    }

    /// <summary>
    /// Hidden GameObject that receives UnitySendMessage callbacks from BugpunchCrashActivity.
    /// Must exist in the scene before the native activity sends messages.
    /// </summary>
    class CrashOverlayCallback : MonoBehaviour
    {
        static CrashOverlayCallback _instance;

        public static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject("BugpunchCrashCallback");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CrashOverlayCallback>();
        }

        // Called by BugpunchCrashActivity via UnitySendMessage("BugpunchCrashCallback", "OnSubmit", data)
        void OnSubmit(string message)
        {
            var cb = AndroidDialog._crashSubmitCallback;
            AndroidDialog._crashSubmitCallback = null;
            AndroidDialog._crashDismissCallback = null;
            if (cb == null) return;

            var parts = message.Split('|');
            MainThread.Enqueue(() => cb.Invoke(new CrashReportResult
            {
                title = parts.Length > 0 ? parts[0] : "",
                description = parts.Length > 1 ? parts[1] : "",
                severity = parts.Length > 2 ? parts[2] : "high",
                includeVideo = parts.Length > 3 && parts[3] == "1",
                includeLogs = parts.Length > 4 && parts[4] == "1"
            }));
        }

        // Called by BugpunchCrashActivity via UnitySendMessage("BugpunchCrashCallback", "OnDismiss", "")
        void OnDismiss(string message)
        {
            var cb = AndroidDialog._crashDismissCallback;
            AndroidDialog._crashSubmitCallback = null;
            AndroidDialog._crashDismissCallback = null;
            if (cb != null) MainThread.Enqueue(() => cb.Invoke());
        }
    }

    /// <summary>
    /// Hidden GameObject that receives UnitySendMessage callbacks from BugpunchReportOverlay.
    /// </summary>
    class ReportOverlayCallback : MonoBehaviour
    {
        static ReportOverlayCallback _instance;

        public static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject("BugpunchReportCallback");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ReportOverlayCallback>();
        }

        void OnWelcomeConfirm(string message)
        {
            var cb = AndroidDialog._welcomeConfirmCallback;
            AndroidDialog._welcomeConfirmCallback = null;
            AndroidDialog._welcomeCancelCallback = null;
            if (cb != null) MainThread.Enqueue(() => cb.Invoke());
        }

        void OnWelcomeCancel(string message)
        {
            var cb = AndroidDialog._welcomeCancelCallback;
            AndroidDialog._welcomeConfirmCallback = null;
            AndroidDialog._welcomeCancelCallback = null;
            if (cb != null) MainThread.Enqueue(() => cb.Invoke());
        }

        void OnReportTapped(string message)
        {
            var cb = AndroidDialog._reportTappedCallback;
            if (cb != null) MainThread.Enqueue(() => cb.Invoke());
        }

        // Request-help picker — native card sends "0", "1" or "2" via UnitySendMessage.
        void OnRequestHelpChoice(string message)
        {
            var cb = AndroidDialog._requestHelpChoiceCallback;
            AndroidDialog._requestHelpChoiceCallback = null;
            AndroidDialog._requestHelpCancelCallback = null;
            if (cb == null) return;
            if (!int.TryParse(message, out var idx)) return;
            MainThread.Enqueue(() => cb.Invoke(idx));
        }

        void OnRequestHelpCancel(string message)
        {
            var cb = AndroidDialog._requestHelpCancelCallback;
            AndroidDialog._requestHelpChoiceCallback = null;
            AndroidDialog._requestHelpCancelCallback = null;
            if (cb != null) MainThread.Enqueue(() => cb.Invoke());
        }

        // Chat board request — native "shell" forwards here so the Unity
        // side can open the UIToolkit chat board. Same GameObject receiver as
        // the rest of the report overlay callbacks, kept consistent so the
        // BugpunchReportOverlay Java class only has to know one target name.
        void OnShowChatBoardRequested(string message)
        {
            MainThread.Enqueue(() => BugpunchChatBoard.Show());
        }

        // Recording-bar shortcut taps — same routing, different entry point.
        void OnRecordingBarChatTapped(string message)
        {
            MainThread.Enqueue(() => BugpunchChatBoard.Show());
        }

        void OnRecordingBarFeedbackTapped(string message)
        {
            MainThread.Enqueue(() => BugpunchFeedbackBoard.Show());
        }

        // Native image picker (BugpunchImagePicker.java) reports back here.
        // Payload is the absolute path on disk (empty / missing → user
        // cancelled). Routed to BugpunchNative which forwards to whoever
        // registered a PickImage callback.
        void OnImagePicked(string message)
        {
            MainThread.Enqueue(() => BugpunchNative.DispatchImagePicked(message));
        }
    }

    class DialogClickListener : AndroidJavaProxy
    {
        readonly Action _callback;
        public DialogClickListener(Action callback) : base("android.content.DialogInterface$OnClickListener")
        {
            _callback = callback;
        }
        void onClick(AndroidJavaObject dialog, int which) => _callback?.Invoke();
    }

    /// <summary>
    /// Dispatches callbacks from Android UI thread to Unity main thread.
    /// </summary>
    static class MainThread
    {
        static readonly ConcurrentQueue<Action> _queue = new();
        static GameObject _go;

        public static void Enqueue(Action action)
        {
            _queue.Enqueue(action);
            EnsureRunner();
        }

        static void EnsureRunner()
        {
            if (_go != null) return;
            _go = new GameObject("Bugpunch_MainThread");
            _go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _go.AddComponent<Runner>();
        }

        class Runner : MonoBehaviour
        {
            void Update()
            {
                while (_queue.TryDequeue(out var a))
                    try { a?.Invoke(); } catch (Exception e) { Debug.LogError(e); }
            }
        }
    }
}
#endif
