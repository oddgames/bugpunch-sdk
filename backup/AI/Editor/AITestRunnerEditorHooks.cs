using UnityEditor;
using UnityEngine;

namespace ODDGames.UIAutomation.AI.Editor
{
    /// <summary>
    /// Editor hooks for AITestRunner — cancels running tests on play mode exit and assembly reload.
    /// </summary>
    [InitializeOnLoad]
    static class AITestRunnerEditorHooks
    {
        static AITestRunnerEditorHooks()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                if (AITestRunner.IsRunning)
                {
                    Debug.Log("[AITest] Exiting play mode - cancelling running AI test");
                    AITestRunner.CancelAll();
                }
            }
        }

        private static void OnBeforeAssemblyReload()
        {
            Debug.Log("[AITest] Assembly reload - cancelling any running tests");
            AITestRunner.CancelAll();
        }
    }
}
