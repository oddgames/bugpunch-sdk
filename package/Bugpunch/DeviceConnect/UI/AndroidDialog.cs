#if UNITY_ANDROID
using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect.UI
{
    public class AndroidDialog : INativeDialog
    {
        public bool IsSupported => Application.platform == RuntimePlatform.Android;

        public void ShowPermission(string scriptName, string scriptDescription, Action<PermissionResult> callback)
        {
            using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity");

            activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                using var builder = new AndroidJavaObject("android.app.AlertDialog$Builder", activity);
                builder.Call<AndroidJavaObject>("setTitle", "Script Permission");
                builder.Call<AndroidJavaObject>("setMessage",
                    $"The server wants to run a script:\n\n{scriptName}\n\n{scriptDescription}");
                builder.Call<AndroidJavaObject>("setCancelable", false);

                builder.Call<AndroidJavaObject>("setPositiveButton", "Allow Once",
                    new DialogClickListener(() => MainThread.Enqueue(() => callback?.Invoke(PermissionResult.AllowOnce))));
                builder.Call<AndroidJavaObject>("setNeutralButton", "Always Allow",
                    new DialogClickListener(() => MainThread.Enqueue(() => callback?.Invoke(PermissionResult.AllowAlways))));
                builder.Call<AndroidJavaObject>("setNegativeButton", "Deny",
                    new DialogClickListener(() => MainThread.Enqueue(() => callback?.Invoke(PermissionResult.Deny))));

                builder.Call<AndroidJavaObject>("show");
            }));
        }

        public void ShowBugReport(Action<BugReportData> onSubmit, Action onCancel)
        {
            using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity");

            activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                using var context = activity;
                using var layout = new AndroidJavaObject("android.widget.LinearLayout", context);
                layout.Call("setOrientation", 1);
                int pad = DpToPx(context, 24);
                layout.Call("setPadding", pad, pad, pad, pad);

                using var titleInput = new AndroidJavaObject("android.widget.EditText", context);
                titleInput.Call("setHint", "Title — brief description");
                titleInput.Call("setSingleLine", true);
                layout.Call("addView", titleInput);

                using var descInput = new AndroidJavaObject("android.widget.EditText", context);
                descInput.Call("setHint", "Description (optional)");
                descInput.Call("setMinLines", 3);
                layout.Call("addView", descInput);

                using var builder = new AndroidJavaObject("android.app.AlertDialog$Builder", context);
                builder.Call<AndroidJavaObject>("setTitle", "Bug Report");
                builder.Call<AndroidJavaObject>("setView", layout);

                var tRef = titleInput;
                var dRef = descInput;

                builder.Call<AndroidJavaObject>("setPositiveButton", "Submit",
                    new DialogClickListener(() => MainThread.Enqueue(() =>
                        onSubmit?.Invoke(new BugReportData
                        {
                            title = tRef.Call<string>("getText")?.ToString() ?? "",
                            description = dRef.Call<string>("getText")?.ToString() ?? "",
                            severity = "medium",
                            includeScreenshot = true,
                            includeLogs = true
                        }))));

                builder.Call<AndroidJavaObject>("setNegativeButton", "Cancel",
                    new DialogClickListener(() => MainThread.Enqueue(() => onCancel?.Invoke())));

                builder.Call<AndroidJavaObject>("show");
            }));
        }

        static int DpToPx(AndroidJavaObject context, int dp)
        {
            using var res = context.Call<AndroidJavaObject>("getResources");
            using var metrics = res.Call<AndroidJavaObject>("getDisplayMetrics");
            return (int)(dp * metrics.Get<float>("density") + 0.5f);
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
