using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Bugpunch connection toggle on the main toolbar.
    /// Default OFF — prevents remote connections while working.
    /// Chain icon: linked = on, click to toggle.
    /// Toggle appears highlighted/pressed when enabled.
    /// </summary>
    static class BugpunchEditorToggle
    {
        internal const string PrefKey = "Bugpunch_Enabled";
        const string MenuPath = "Bugpunch/Enable Connection";

        public static bool IsEnabled
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem(MenuPath, priority = 0)]
        static void Toggle()
        {
            IsEnabled = !IsEnabled;
            Debug.Log($"[Bugpunch] Connection {(IsEnabled ? "ENABLED" : "DISABLED")}");
        }

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, IsEnabled);
            return true;
        }

        [MainToolbarElement("Bugpunch/Connection",
            defaultDockPosition = MainToolbarDockPosition.Right)]
        static MainToolbarElement CreateToggle()
        {
            // Circle with tick when on, empty circle when off
            // MainToolbarToggle shows highlighted/pressed state when active
            var icon = EditorGUIUtility.IconContent("d_Valid").image as Texture2D;
            var content = new MainToolbarContent("Bugpunch", icon,
                "Toggle Bugpunch remote connection (chain = connected)");

            var toggle = new MainToolbarToggle(content, IsEnabled, OnToggled)
            {
                displayed = true
            };
            return toggle;
        }

        static void OnToggled(bool value)
        {
            IsEnabled = value;
            Debug.Log($"[Bugpunch] Connection {(value ? "ENABLED" : "DISABLED")}");

            // Actively connect or disconnect if in Play mode
            if (Application.isPlaying)
            {
                if (value)
                {
                    var config = DeviceConnect.BugpunchConfig.Load();
                    if (config != null && DeviceConnect.BugpunchClient.Instance == null)
                        DeviceConnect.BugpunchClient.Initialize(config);
                }
                else
                {
                    DeviceConnect.BugpunchClient.Instance?.Disconnect();
                }
            }
        }
    }
}
