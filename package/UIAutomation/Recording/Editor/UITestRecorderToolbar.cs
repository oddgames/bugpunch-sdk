#if HAS_TOOLBAR_EXTENDER
using UnityEditor;
using UnityEngine;

namespace ODDGames.UIAutomation.Editor
{
    [InitializeOnLoad]
    public static class UITestRecorderToolbar
    {
        static UITestRecorderToolbar()
        {
            UnityToolbarExtender.ToolbarExtender.RightToolbarGUI.Add(OnToolbarGUI);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnToolbarGUI()
        {
            GUILayout.FlexibleSpace();

            bool isRecording = Application.isPlaying && UITestRecorder.Instance != null && UITestRecorder.Instance.IsRecording;
            bool isInTestSession = UITestSettings.WasRecording || isRecording;

            if (isInTestSession)
            {
                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button("Stop Test", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    StopRecordingAndShowWindow();
                }
                GUI.backgroundColor = Color.white;

                GUI.contentColor = new Color(1f, 0.4f, 0.4f);
                GUILayout.Label("● REC", EditorStyles.toolbarButton, GUILayout.Width(50));
                GUI.contentColor = Color.white;
            }
            else if (UITestSettings.RecordOnNextPlay)
            {
                GUI.backgroundColor = new Color(1f, 0.8f, 0.4f);
                if (GUILayout.Button("Cancel", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    UITestSettings.RecordOnNextPlay = false;
                    if (Application.isPlaying)
                    {
                        EditorApplication.isPlaying = false;
                    }
                }
                GUI.backgroundColor = Color.white;
                GUILayout.Label("Starting...", EditorStyles.toolbarButton, GUILayout.Width(70));
            }
            else
            {
                if (GUILayout.Button("Record Test", EditorStyles.toolbarButton, GUILayout.Width(80)))
                {
                    ShowRecordingSetupWindow();
                }
            }
        }

        static void ShowRecordingSetupWindow()
        {
            UITestRecordingSetupWindow.ShowWindow((recordingName) =>
            {
                UITestSettings.PendingRecordingName = recordingName;
                UITestSettings.RecordOnNextPlay = true;
                EditorApplication.isPlaying = true;
            });
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && UITestSettings.RecordOnNextPlay)
            {
                UITestSettings.RecordOnNextPlay = false;
                UITestSettings.WasRecording = true;
                EditorApplication.delayCall += StartRecordingDelayed;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && UITestSettings.WasRecording)
            {
                if (UITestRecorder.Instance != null && UITestRecorder.Instance.IsRecording)
                {
                    UITestRecorder.Instance.StopRecording();
                }
                UITestSettings.WasRecording = false;
                EditorApplication.delayCall += () => UITestGeneratorWindow.ShowWindowWithLastRecording();
            }
        }

        static void StartRecordingDelayed()
        {
            EditorApplication.delayCall += () =>
            {
                if (UITestRecorder.Instance == null)
                {
                    var go = new GameObject("UITestRecorder");
                    go.AddComponent<UITestRecorder>();
                }

                EditorApplication.delayCall += () =>
                {
                    if (UITestRecorder.Instance != null)
                    {
                        string recordingName = UITestSettings.PendingRecordingName;
                        UITestRecorder.Instance.StartRecording(recordingName);
                    }
                };
            };
        }

        static void StopRecordingAndShowWindow()
        {
            UITestSettings.WasRecording = false;
            if (UITestRecorder.Instance != null && UITestRecorder.Instance.IsRecording)
            {
                UITestRecorder.Instance.StopRecording();
            }
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => UITestGeneratorWindow.ShowWindowWithLastRecording();
        }
    }
}
#endif
