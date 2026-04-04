using UnityEditor;
using UnityEngine;

namespace ODDGames.UIAutomation.Recording.Editor
{
    static class CreateRecorderMenuItem
    {
        [MenuItem("Window/UI Automation/Create Test Recorder")]
        static void CreateRecorder()
        {
            if (UITestRecorder.Instance != null)
            {
                Debug.Log("[UITestRecorder] Recorder already exists");
                return;
            }

            var go = new GameObject("UITestRecorder");
            go.AddComponent<UITestRecorder>();
            Debug.Log("[UITestRecorder] Created recorder instance");
        }
    }
}
