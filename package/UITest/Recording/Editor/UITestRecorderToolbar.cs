#if HAS_TOOLBAR_EXTENDER
using UnityEditor;
using UnityEngine;

namespace ODDGames.UITest.Editor
{
    [InitializeOnLoad]
    public static class UITestRecorderToolbar
    {
        const string RECORD_ON_NEXT_PLAY_KEY = "TOR.UITestRecorder.RecordOnNextPlay";
        const string WAS_RECORDING_KEY = "TOR.UITestRecorder.WasRecording";
        const string PENDING_RECORDING_NAME_KEY = "TOR.UITestRecorder.PendingRecordingName";

        static bool RecordOnNextPlay
        {
            get => SessionState.GetBool(RECORD_ON_NEXT_PLAY_KEY, false);
            set => SessionState.SetBool(RECORD_ON_NEXT_PLAY_KEY, value);
        }

        static bool WasRecordingBeforeStop
        {
            get => SessionState.GetBool(WAS_RECORDING_KEY, false);
            set => SessionState.SetBool(WAS_RECORDING_KEY, value);
        }

        static UITestRecorderToolbar()
        {
            UnityToolbarExtender.ToolbarExtender.RightToolbarGUI.Add(OnToolbarGUI);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnToolbarGUI()
        {
            GUILayout.FlexibleSpace();

            bool isRecording = Application.isPlaying && UITestRecorder.Instance != null && UITestRecorder.Instance.IsRecording;
            bool isInTestSession = WasRecordingBeforeStop || isRecording;

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
            else if (RecordOnNextPlay)
            {
                GUI.backgroundColor = new Color(1f, 0.8f, 0.4f);
                if (GUILayout.Button("Cancel", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    RecordOnNextPlay = false;
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
                SessionState.SetString(PENDING_RECORDING_NAME_KEY, recordingName);
                RecordOnNextPlay = true;
                EditorApplication.isPlaying = true;
            });
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && RecordOnNextPlay)
            {
                RecordOnNextPlay = false;
                WasRecordingBeforeStop = true;
                EditorApplication.delayCall += StartRecordingDelayed;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && WasRecordingBeforeStop)
            {
                if (UITestRecorder.Instance != null && UITestRecorder.Instance.IsRecording)
                {
                    UITestRecorder.Instance.StopRecording();
                }
                WasRecordingBeforeStop = false;
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
                        string recordingName = SessionState.GetString(PENDING_RECORDING_NAME_KEY, null);
                        UITestRecorder.Instance.StartRecording(recordingName);
                    }
                };
            };
        }

        static void StopRecordingAndShowWindow()
        {
            WasRecordingBeforeStop = false;
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
