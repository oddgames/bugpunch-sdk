using System;
using UnityEngine;

namespace ODDGames.Bugpunch.UI
{
    /// <summary>
    /// IMGUI-based fallback for desktop standalone builds.
    /// </summary>
    public class FallbackDialog : INativeDialog
    {
        public bool IsSupported => true;

        public void ShowPermission(string scriptName, string scriptDescription, Action<PermissionResult> callback)
        {
            var go = new GameObject("Bugpunch_Permission");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            var ui = go.AddComponent<PermissionUI>();
            ui.Init(scriptName, scriptDescription, r => { callback?.Invoke(r); UnityEngine.Object.Destroy(go); });
        }

        public void ShowBugReport(Action<BugReportData> onSubmit, Action onCancel)
        {
            var go = new GameObject("Bugpunch_BugReport");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            var ui = go.AddComponent<BugReportUI>();
            ui.Init(
                d => { onSubmit?.Invoke(d); UnityEngine.Object.Destroy(go); },
                () => { onCancel?.Invoke(); UnityEngine.Object.Destroy(go); }
            );
        }

        public void ShowCrashReport(CrashReportContext context, Action<CrashReportResult> onSubmit, Action onDismiss)
        {
            var go = new GameObject("Bugpunch_CrashReport");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            var ui = go.AddComponent<CrashReportUI>();
            ui.Init(context,
                r => { onSubmit?.Invoke(r); UnityEngine.Object.Destroy(go); },
                () => { onDismiss?.Invoke(); UnityEngine.Object.Destroy(go); }
            );
        }

        public void ShowReportWelcome(Action onConfirm, Action onCancel)
        {
            // Desktop fallback: skip welcome, go straight to recording
            BugpunchLog.Info("FallbackDialog", "Report welcome not available on desktop, proceeding directly");
            onConfirm?.Invoke();
        }

        public void ShowRecordingOverlay(Action onStopRecording)
        {
            // Desktop fallback: no native overlay, log instruction
            BugpunchLog.Info("FallbackDialog", "Recording overlay not available on desktop. Press F12 again to submit report.");
        }

        public void HideRecordingOverlay()
        {
            // No-op on desktop
        }

        public void ShowRequestHelp(Action<int> onChoice, Action onCancel)
        {
            // IMGUI fallback defers to the shared UI Toolkit picker — it
            // already handles Editor + Standalone and owns its own host.
            BugpunchRequestHelpPicker.ShowUIToolkitFallback(onChoice, onCancel);
        }

        public void ShowChatBoard()
        {
            // Same story as ShowRequestHelp — the chat board is a full
            // UIToolkit surface already, so the IMGUI fallback just forwards.
            BugpunchLog.Info("FallbackDialog", "ShowChatBoard — delegating to UIToolkit chat board");
            BugpunchChatBoard.Show();
        }

        public void ShowFeedbackBoard()
        {
            // Feedback board is full UIToolkit too — IMGUI fallback delegates
            // straight through. The Android-native Activity is the only port
            // for now (#29 phase A).
            BugpunchLog.Info("FallbackDialog", "ShowFeedbackBoard — delegating to UIToolkit feedback board");
            BugpunchFeedbackBoard.Show();
        }
    }

    class PermissionUI : MonoBehaviour
    {
        string _name, _desc;
        Action<PermissionResult> _cb;

        public void Init(string name, string desc, Action<PermissionResult> cb)
        { _name = name; _desc = desc; _cb = cb; }

        void OnGUI()
        {
            var r = new Rect((Screen.width - 400) / 2f, (Screen.height - 180) / 2f, 400, 180);
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(r, GUI.skin.box);
            GUILayout.Label("Script Permission", GUI.skin.label);
            GUILayout.Space(8);
            GUILayout.Label($"Run: {_name}");
            if (!string.IsNullOrEmpty(_desc)) GUILayout.Label(_desc, GUILayout.MaxHeight(50));
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Allow Once")) _cb?.Invoke(PermissionResult.AllowOnce);
            if (GUILayout.Button("Always Allow")) _cb?.Invoke(PermissionResult.AllowAlways);
            if (GUILayout.Button("Deny")) _cb?.Invoke(PermissionResult.Deny);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }

    class BugReportUI : MonoBehaviour
    {
        Action<BugReportData> _onSubmit;
        Action _onCancel;
        string _title = "", _desc = "";
        int _sev = 1;
        readonly string[] _sevNames = { "low", "medium", "high", "critical" };

        public void Init(Action<BugReportData> onSubmit, Action onCancel)
        { _onSubmit = onSubmit; _onCancel = onCancel; }

        void OnGUI()
        {
            var r = new Rect((Screen.width - 400) / 2f, (Screen.height - 280) / 2f, 400, 280);
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(r, GUI.skin.box);
            GUILayout.Label("Bug Report");
            GUILayout.Space(5);
            GUILayout.Label("Title:");
            _title = GUILayout.TextField(_title);
            GUILayout.Label("Description:");
            _desc = GUILayout.TextArea(_desc, GUILayout.Height(60));
            _sev = GUILayout.SelectionGrid(_sev, new[] { "Low", "Medium", "High", "Critical" }, 4);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Cancel")) _onCancel?.Invoke();
            GUI.enabled = !string.IsNullOrWhiteSpace(_title);
            if (GUILayout.Button("Submit"))
                _onSubmit?.Invoke(new BugReportData { title = _title, description = _desc, severity = _sevNames[_sev], includeScreenshot = true, includeLogs = true });
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }

    /// <summary>
    /// IMGUI-based full-screen crash report overlay for desktop builds.
    /// Shows a screenshot thumbnail (no video playback on desktop), exception text,
    /// input fields for title/description, severity selector, and submit/dismiss buttons.
    /// </summary>
    class CrashReportUI : MonoBehaviour
    {
        Action<CrashReportResult> _onSubmit;
        Action _onDismiss;
        CrashReportContext _context;

        string _title = "", _desc = "";
        int _sev = 2; // default "high" for crashes
        bool _includeVideo = true, _includeLogs = true;
        Vector2 _stackScroll;
        Texture2D _thumbnail;

        readonly string[] _sevNames = { "low", "medium", "high", "critical" };
        readonly string[] _sevLabels = { "Low", "Medium", "High", "Critical" };

        public void Init(CrashReportContext context, Action<CrashReportResult> onSubmit, Action onDismiss)
        {
            _context = context;
            _onSubmit = onSubmit;
            _onDismiss = onDismiss;

            // Pre-fill the title from the exception message
            if (!string.IsNullOrEmpty(context.exceptionMessage))
            {
                _title = context.exceptionMessage.Length > 120
                    ? context.exceptionMessage.Substring(0, 120)
                    : context.exceptionMessage;
            }

            // Load the screenshot as a thumbnail texture
            if (context.screenshotJpg != null && context.screenshotJpg.Length > 0)
            {
                _thumbnail = new Texture2D(2, 2);
                _thumbnail.LoadImage(context.screenshotJpg);
            }
        }

        void OnDestroy()
        {
            if (_thumbnail != null) Destroy(_thumbnail);
        }

        void OnGUI()
        {
            // Full-screen dark overlay
            GUI.color = new Color(0, 0, 0, 0.85f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float panelW = Mathf.Min(700, Screen.width - 40);
            float panelH = Mathf.Min(650, Screen.height - 40);
            var r = new Rect((Screen.width - panelW) / 2f, (Screen.height - panelH) / 2f, panelW, panelH);

            GUILayout.BeginArea(r, GUI.skin.box);

            // Header
            var headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            GUI.color = new Color(1f, 0.3f, 0.3f);
            GUILayout.Label("Crash Report", headerStyle);
            GUI.color = Color.white;
            GUILayout.Space(4);

            // Thumbnail + exception side by side
            GUILayout.BeginHorizontal();

            // Left: screenshot thumbnail
            if (_thumbnail != null)
            {
                float thumbH = 160;
                float thumbW = thumbH * ((float)_thumbnail.width / _thumbnail.height);
                GUILayout.Box(_thumbnail, GUILayout.Width(thumbW), GUILayout.Height(thumbH));
            }
            else if (!string.IsNullOrEmpty(_context.videoPath))
            {
                GUILayout.Box("Video available\n(preview on device)", GUILayout.Width(180), GUILayout.Height(160));
            }

            // Right: exception message + scrollable stack trace
            GUILayout.BeginVertical();
            if (!string.IsNullOrEmpty(_context.exceptionMessage))
            {
                var errStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontStyle = FontStyle.Bold };
                GUI.color = new Color(1f, 0.5f, 0.5f);
                GUILayout.Label(_context.exceptionMessage, errStyle, GUILayout.MaxHeight(40));
                GUI.color = Color.white;
            }
            if (!string.IsNullOrEmpty(_context.stackTrace))
            {
                var traceStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 };
                _stackScroll = GUILayout.BeginScrollView(_stackScroll, GUILayout.Height(110));
                GUILayout.Label(_context.stackTrace, traceStyle);
                GUILayout.EndScrollView();
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Title input
            GUILayout.Label("Title:");
            _title = GUILayout.TextField(_title);

            // Description input
            GUILayout.Label("Description (what were you doing?):");
            _desc = GUILayout.TextArea(_desc, GUILayout.Height(60));

            // Severity selector
            GUILayout.Space(4);
            GUILayout.Label("Severity:");
            _sev = GUILayout.SelectionGrid(_sev, _sevLabels, 4);

            // Toggles
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            _includeVideo = GUILayout.Toggle(_includeVideo, "Include video");
            _includeLogs = GUILayout.Toggle(_includeLogs, "Include logs");
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();

            // Buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Dismiss", GUILayout.Height(35)))
                _onDismiss?.Invoke();
            GUILayout.FlexibleSpace();
            GUI.enabled = !string.IsNullOrWhiteSpace(_title);
            GUI.color = new Color(0.3f, 0.8f, 0.3f);
            if (GUILayout.Button("Submit Report", GUILayout.Width(150), GUILayout.Height(35)))
            {
                _onSubmit?.Invoke(new CrashReportResult
                {
                    title = _title,
                    description = _desc,
                    severity = _sevNames[_sev],
                    includeVideo = _includeVideo,
                    includeLogs = _includeLogs
                });
            }
            GUI.color = Color.white;
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
    }
}
