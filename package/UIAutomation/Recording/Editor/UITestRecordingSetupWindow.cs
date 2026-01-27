using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace ODDGames.UIAutomation.Editor
{
    public class UITestRecordingSetupWindow : EditorWindow
    {
        enum TestDataMode { Current, Custom }

        static readonly string[] testDataModeLabels = { "Current", "Custom" };

        static Action<string> onStartCallback;

        string recordingName;
        string testDataPath;
        TestDataMode testDataMode;
        bool isValidPath;

        public static void ShowWindow(Action<string> onStart)
        {
            onStartCallback = onStart;
            var window = GetWindow<UITestRecordingSetupWindow>(true, "Record UI Test", true);
            window.minSize = new Vector2(400, 230);
            window.maxSize = new Vector2(500, 280);
            window.ShowUtility();
        }

        void OnEnable()
        {
            recordingName = $"Recording_{DateTime.Now:yyyyMMdd_HHmm}";
            testDataPath = UITestSettings.TestDataPath;
            testDataMode = (TestDataMode)UITestSettings.TestDataMode;
            ValidatePath();
        }

        void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("UI Test Recording Setup", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            EditorGUILayout.HelpBox(
                "Click 'Start Recording' to enter Play Mode and begin capturing UI interactions.\n\n" +
                "The persistent data will be saved with the recording for reproducibility.",
                MessageType.Info);

            EditorGUILayout.Space(10);

            recordingName = EditorGUILayout.TextField("Recording Name", recordingName);

            EditorGUILayout.Space(5);

            EditorGUI.BeginChangeCheck();
            testDataMode = (TestDataMode)EditorGUILayout.Popup("Persistent Data", (int)testDataMode, testDataModeLabels);
            if (EditorGUI.EndChangeCheck())
            {
                SavePrefs();
            }

            if (testDataMode == TestDataMode.Custom)
            {
                EditorGUILayout.BeginHorizontal();
                testDataPath = EditorGUILayout.TextField("Path", testDataPath);

                if (GUILayout.Button("Folder", GUILayout.Width(50)))
                {
                    string path = EditorUtility.OpenFolderPanel("Select Test Data Folder", testDataPath, "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        testDataPath = path;
                        ValidatePath();
                        SavePrefs();
                    }
                }

                if (GUILayout.Button(".zip", GUILayout.Width(40)))
                {
                    string path = EditorUtility.OpenFilePanel("Select Test Data Zip", "", "zip");
                    if (!string.IsNullOrEmpty(path))
                    {
                        testDataPath = path;
                        ValidatePath();
                        SavePrefs();
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(testDataPath))
                {
                    if (isValidPath)
                    {
                        EditorGUILayout.HelpBox("Custom data will replace persistentDataPath and be saved with the recording.", MessageType.None);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Invalid path - file or folder not found.", MessageType.Warning);
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Current persistentDataPath will be copied to the recording.", MessageType.None);
            }

            EditorGUILayout.Space(20);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Cancel", GUILayout.Width(80), GUILayout.Height(30)))
            {
                Close();
            }

            GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
            bool canStart = testDataMode == TestDataMode.Current || isValidPath;
            EditorGUI.BeginDisabledGroup(!canStart);
            if (GUILayout.Button("Start Recording", GUILayout.Width(120), GUILayout.Height(30)))
            {
                StartRecording();
            }
            EditorGUI.EndDisabledGroup();
            GUI.backgroundColor = Color.white;

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        void ValidatePath()
        {
            if (string.IsNullOrEmpty(testDataPath))
            {
                isValidPath = false;
                return;
            }

            isValidPath = Directory.Exists(testDataPath) ||
                         (File.Exists(testDataPath) && testDataPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }

        void SavePrefs()
        {
            UITestSettings.TestDataPath = testDataPath;
            UITestSettings.TestDataMode = (int)testDataMode;
        }

        void StartRecording()
        {
            SavePrefs();

            if (string.IsNullOrWhiteSpace(recordingName))
                recordingName = $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}";

            string recordingFolder = Path.Combine(Application.dataPath, "UIAutomationTests", "GeneratedTests", recordingName);
            Directory.CreateDirectory(recordingFolder);

            UITestSettings.PendingRecordingFolder = recordingFolder;

            try
            {
                if (testDataMode == TestDataMode.Custom && isValidPath)
                {
                    CopyTestDataToPersistentPath();
                    CopyTestDataToRecordingFolder(recordingFolder);
                    UITestSettings.PendingTestDataSource = testDataPath;
                }
                else
                {
                    CopyCurrentPersistentDataToRecordingFolder(recordingFolder);
                    UITestSettings.PendingTestDataSource = "";
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to copy test data:\n{ex.Message}", "OK");
                return;
            }

            Close();
            onStartCallback?.Invoke(recordingName);
        }

        void CopyTestDataToPersistentPath()
        {
            string targetPath = Path.Combine(Application.persistentDataPath, "data");

            if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, true);
            }

            if (testDataPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[UITestRecorder] Extracting test data from: {testDataPath}");
                ZipFile.ExtractToDirectory(testDataPath, Application.persistentDataPath);
            }
            else
            {
                Debug.Log($"[UITestRecorder] Copying test data from: {testDataPath}");
                CopyDirectory(testDataPath, targetPath);
            }

            Debug.Log($"[UITestRecorder] Test data copied to: {targetPath}");
        }

        void CopyTestDataToRecordingFolder(string recordingFolder)
        {
            string zipPath = Path.Combine(recordingFolder, "testdata.zip");

            if (testDataPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(testDataPath, zipPath, true);
                Debug.Log($"[UITestRecorder] Test data zip copied to recording: {zipPath}");
            }
            else
            {
                ZipFile.CreateFromDirectory(testDataPath, zipPath, System.IO.Compression.CompressionLevel.Optimal, false);
                Debug.Log($"[UITestRecorder] Test data zipped to recording: {zipPath}");
            }
        }

        void CopyCurrentPersistentDataToRecordingFolder(string recordingFolder)
        {
            string persistentDataFolder = Path.Combine(Application.persistentDataPath, "data");
            if (!Directory.Exists(persistentDataFolder))
            {
                Debug.Log("[UITestRecorder] No existing persistent data folder to copy");
                return;
            }

            string zipPath = Path.Combine(recordingFolder, "testdata.zip");
            ZipFile.CreateFromDirectory(persistentDataFolder, zipPath, System.IO.Compression.CompressionLevel.Optimal, false);
            Debug.Log($"[UITestRecorder] Current persistent data zipped to recording: {zipPath}");
        }

        static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, targetFile, true);
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string targetSubDir = Path.Combine(targetDir, Path.GetFileName(dir));
                CopyDirectory(dir, targetSubDir);
            }
        }
    }
}
