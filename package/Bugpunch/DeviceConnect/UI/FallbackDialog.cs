using System;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect.UI
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
}
