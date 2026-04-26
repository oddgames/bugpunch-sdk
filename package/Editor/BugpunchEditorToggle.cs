using UnityEditor;
using UnityEngine;

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Bugpunch connection toggle via the Bugpunch menu.
    /// Default OFF — prevents remote connections while working.
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
            BugpunchLog.Info("BugpunchEditorToggle", $"Connection {(IsEnabled ? "ENABLED" : "DISABLED")}");

            // Actively connect or disconnect if in Play mode
            if (Application.isPlaying)
            {
                if (IsEnabled)
                {
                    var config = DeviceConnect.BugpunchConfig.Load();
                    if (config != null && DeviceConnect.BugpunchClient.Instance == null)
                        DeviceConnect.BugpunchClient.Initialize(config);
                }
                else if (DeviceConnect.BugpunchClient.Instance != null)
                {
                    Object.Destroy(DeviceConnect.BugpunchClient.Instance.gameObject);
                }
            }
        }

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, IsEnabled);
            return true;
        }
    }
}
