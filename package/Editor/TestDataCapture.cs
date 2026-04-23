using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Win32;
using UnityEditor;
using UnityEngine;

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Editor tool to capture current game state (persistent data + PlayerPrefs) for test fixtures.
    /// </summary>
    public class TestDataCapture : EditorWindow
    {
        private string _outputPath = "Assets/Resources/TestData";
        private string _captureName = "TestState";

        // File selection
        private List<FileEntry> _fileEntries = new();
        private bool _filesFoldout = true;
        private Vector2 _filesScrollPos;

        // PlayerPrefs selection
        private List<PrefEntry> _prefEntries = new();
        private bool _prefsFoldout = true;
        private Vector2 _prefsScrollPos;
        private string _newKey = "";

        [MenuItem("Window/UI Automation/Capture Test Data")]
        public static void ShowWindow()
        {
            GetWindow<TestDataCapture>("Test Data Capture");
        }

        private void OnEnable()
        {
            RefreshFileList();
#if UNITY_EDITOR_WIN
            LoadPlayerPrefsFromRegistry();
#endif
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Capture Test Data State", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _outputPath = EditorGUILayout.TextField("Output Path", _outputPath);
            _captureName = EditorGUILayout.TextField("Capture Name", _captureName);

            EditorGUILayout.Space();

            // Files section
            DrawFilesSection();

            EditorGUILayout.Space();

            // PlayerPrefs section
            DrawPlayerPrefsSection();

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Capture State", GUILayout.Height(30)))
                {
                    CaptureState();
                }
                if (GUILayout.Button("Refresh", GUILayout.Width(80), GUILayout.Height(30)))
                {
                    RefreshFileList();
                }
            }

            EditorGUILayout.Space();
            DrawQuickActions();
        }

        private void DrawFilesSection()
        {
            _filesFoldout = EditorGUILayout.Foldout(_filesFoldout, $"Persistent Data Files ({_fileEntries.Count(f => f.enabled)}/{_fileEntries.Count} selected)", true);

            if (_filesFoldout)
            {
                EditorGUILayout.LabelField($"Path: {Application.persistentDataPath}", EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select All", EditorStyles.miniButton))
                    {
                        foreach (var f in _fileEntries) f.enabled = true;
                    }
                    if (GUILayout.Button("Select None", EditorStyles.miniButton))
                    {
                        foreach (var f in _fileEntries) f.enabled = false;
                    }
                }

                _filesScrollPos = EditorGUILayout.BeginScrollView(_filesScrollPos, GUILayout.Height(150));

                string currentFolder = null;
                foreach (var entry in _fileEntries)
                {
                    var folder = Path.GetDirectoryName(entry.relativePath);
                    if (folder != currentFolder)
                    {
                        currentFolder = folder;
                        if (!string.IsNullOrEmpty(folder))
                        {
                            EditorGUILayout.LabelField($"📁 {folder}/", EditorStyles.boldLabel);
                        }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        entry.enabled = EditorGUILayout.Toggle(entry.enabled, GUILayout.Width(20));
                        var fileName = Path.GetFileName(entry.relativePath);
                        EditorGUILayout.LabelField($"  {fileName}", GUILayout.ExpandWidth(true));
                        EditorGUILayout.LabelField(entry.size, EditorStyles.miniLabel, GUILayout.Width(70));
                    }
                }

                if (_fileEntries.Count == 0)
                {
                    EditorGUILayout.LabelField("  (no files found)", EditorStyles.miniLabel);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawPlayerPrefsSection()
        {
            _prefsFoldout = EditorGUILayout.Foldout(_prefsFoldout, $"PlayerPrefs Keys ({_prefEntries.Count(p => p.enabled)}/{_prefEntries.Count} selected)", true);

            if (_prefsFoldout)
            {
#if !UNITY_EDITOR_WIN
                EditorGUILayout.HelpBox("Unity doesn't allow enumerating PlayerPrefs. Add known keys below.", MessageType.Info);
#endif

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select All", EditorStyles.miniButton))
                    {
                        foreach (var p in _prefEntries) p.enabled = true;
                    }
                    if (GUILayout.Button("Select None", EditorStyles.miniButton))
                    {
                        foreach (var p in _prefEntries) p.enabled = false;
                    }
#if UNITY_EDITOR_WIN
                    if (GUILayout.Button("Reload from Registry", EditorStyles.miniButton))
                    {
                        LoadPlayerPrefsFromRegistry();
                    }
#endif
                }

                _prefsScrollPos = EditorGUILayout.BeginScrollView(_prefsScrollPos, GUILayout.Height(120));

                for (int i = 0; i < _prefEntries.Count; i++)
                {
                    var entry = _prefEntries[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        entry.enabled = EditorGUILayout.Toggle(entry.enabled, GUILayout.Width(20));
                        entry.key = EditorGUILayout.TextField(entry.key);

                        // Show current value if key exists
                        if (PlayerPrefs.HasKey(entry.key))
                        {
                            var val = GetPrefDisplayValue(entry.key);
                            EditorGUILayout.LabelField(val, EditorStyles.miniLabel, GUILayout.Width(100));
                        }
                        else
                        {
                            EditorGUILayout.LabelField("(not set)", EditorStyles.miniLabel, GUILayout.Width(100));
                        }

                        if (GUILayout.Button("X", GUILayout.Width(25)))
                        {
                            _prefEntries.RemoveAt(i);
                            i--;
                        }
                    }
                }

                EditorGUILayout.EndScrollView();

                using (new EditorGUILayout.HorizontalScope())
                {
                    _newKey = EditorGUILayout.TextField(_newKey);
                    if (GUILayout.Button("Add Key", GUILayout.Width(80)) && !string.IsNullOrEmpty(_newKey))
                    {
                        _prefEntries.Add(new PrefEntry { key = _newKey, enabled = true });
                        _newKey = "";
                    }
                }
            }
        }

        private void DrawQuickActions()
        {
            EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Persistent Data Folder"))
                {
                    EditorUtility.RevealInFinder(Application.persistentDataPath);
                }
                if (GUILayout.Button("Clear Persistent Data"))
                {
                    if (EditorUtility.DisplayDialog("Clear Persistent Data",
                        $"Delete all files in:\n{Application.persistentDataPath}\n\nThis cannot be undone!",
                        "Delete", "Cancel"))
                    {
                        ClearPersistentData();
                        RefreshFileList();
                    }
                }
            }

            if (GUILayout.Button("Clear PlayerPrefs"))
            {
                if (EditorUtility.DisplayDialog("Clear PlayerPrefs",
                    "Delete all PlayerPrefs?\n\nThis cannot be undone!",
                    "Delete", "Cancel"))
                {
                    PlayerPrefs.DeleteAll();
                    PlayerPrefs.Save();
                    Debug.Log("[TestDataCapture] PlayerPrefs cleared");
                }
            }
        }

        private void RefreshFileList()
        {
            _fileEntries.Clear();
            var path = Application.persistentDataPath;

            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                foreach (var file in files.OrderBy(f => f))
                {
                    var info = new FileInfo(file);
                    var relativePath = file.Substring(path.Length).TrimStart('\\', '/');
                    _fileEntries.Add(new FileEntry
                    {
                        relativePath = relativePath,
                        fullPath = file,
                        size = FormatFileSize(info.Length),
                        enabled = true
                    });
                }
            }
        }

        private string GetPrefDisplayValue(string key)
        {
            var stringVal = PlayerPrefs.GetString(key, "");
            if (!string.IsNullOrEmpty(stringVal) && stringVal.Length <= 20)
                return $"\"{stringVal}\"";
            if (!string.IsNullOrEmpty(stringVal))
                return $"\"{stringVal.Substring(0, 17)}...\"";

            var intVal = PlayerPrefs.GetInt(key, int.MinValue);
            if (intVal != int.MinValue)
                return intVal.ToString();

            return PlayerPrefs.GetFloat(key, 0f).ToString("F2");
        }

        private void CaptureState()
        {
            if (!Directory.Exists(_outputPath))
            {
                Directory.CreateDirectory(_outputPath);
            }

            var enabledFiles = _fileEntries.Where(f => f.enabled).ToList();
            var enabledPrefs = _prefEntries.Where(p => p.enabled).ToList();

            if (enabledFiles.Count == 0 && enabledPrefs.Count == 0)
            {
                Debug.LogWarning("[TestDataCapture] Nothing selected to capture");
                return;
            }

            // Capture everything in one zip
            var zipPath = Path.Combine(_outputPath, $"{_captureName}.zip.bytes");
            CaptureAllAsZip(zipPath, enabledFiles, enabledPrefs);

            AssetDatabase.Refresh();
            Debug.Log($"[TestDataCapture] State captured to {zipPath} ({enabledFiles.Count} files, {enabledPrefs.Count} prefs)");
        }

        private void CaptureAllAsZip(string outputPath, List<FileEntry> files, List<PrefEntry> prefs)
        {
            // Delete existing file with retry for sharing violations
            if (File.Exists(outputPath))
            {
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        File.Delete(outputPath);
                        break;
                    }
                    catch (IOException) when (i < 2)
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                }
            }

            using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

            // Add persistent data files under "files/" folder
            foreach (var file in files)
            {
                try
                {
                    var entry = archive.CreateEntry("files/" + file.relativePath);
                    using var entryStream = entry.Open();
                    // Open with FileShare.ReadWrite to handle files that may be in use
                    using var fileStream = new FileStream(file.fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    fileStream.CopyTo(entryStream);
                }
                catch (IOException ex)
                {
                    Debug.LogWarning($"[TestDataCapture] Skipping locked file {file.relativePath}: {ex.Message}");
                }
            }

            // Add PlayerPrefs as JSON at root
            if (prefs.Count > 0)
            {
                var prefsJson = BuildPlayerPrefsJson(prefs);
                var prefsEntry = archive.CreateEntry("playerprefs.json");
                using var prefsStream = prefsEntry.Open();
                using var writer = new StreamWriter(prefsStream);
                writer.Write(prefsJson);
            }
        }

        private string BuildPlayerPrefsJson(List<PrefEntry> prefs)
        {
            var entries = new List<PlayerPrefsEntry>();

            foreach (var pref in prefs)
            {
                if (!PlayerPrefs.HasKey(pref.key)) continue;

                var entry = new PlayerPrefsEntry { key = pref.key };

                // Unity PlayerPrefs type detection:
                // - GetString returns "" for int/float values
                // - GetInt returns 0 for string values (or the default)
                // - GetFloat returns 0 for string values (or the default)
                // Strategy: Check string first, if empty then check int/float
                var stringVal = PlayerPrefs.GetString(pref.key, "\0"); // Use null char as sentinel

                if (stringVal != "\0" && stringVal != "")
                {
                    // Has a non-empty string value
                    entry.type = "string";
                    entry.value = stringVal;
                }
                else
                {
                    // Empty string or no string - it's numeric
                    // Use special defaults to detect which one
                    var intVal = PlayerPrefs.GetInt(pref.key, int.MinValue);
                    var floatVal = PlayerPrefs.GetFloat(pref.key, float.MinValue);

                    // Check if it's a float with decimal part
                    if (floatVal != float.MinValue && Math.Abs(floatVal - Math.Round(floatVal)) > 0.0001)
                    {
                        entry.type = "float";
                        entry.value = floatVal.ToString();
                    }
                    else if (intVal != int.MinValue)
                    {
                        entry.type = "int";
                        entry.value = intVal.ToString();
                    }
                    else if (floatVal != float.MinValue)
                    {
                        // Integer stored as float (e.g., 5.0)
                        entry.type = "int";
                        entry.value = ((int)floatVal).ToString();
                    }
                    else
                    {
                        // Key exists but couldn't read value - store as empty string
                        entry.type = "string";
                        entry.value = "";
                    }
                }

                entries.Add(entry);
            }

            var data = new PlayerPrefsData { entries = entries.ToArray() };
            return JsonUtility.ToJson(data, prettyPrint: true);
        }

        private void ClearPersistentData()
        {
            var path = Application.persistentDataPath;
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); }
                    catch (Exception ex) { Debug.LogWarning($"Failed to delete {file}: {ex.Message}"); }
                }
                foreach (var dir in Directory.GetDirectories(path))
                {
                    try { Directory.Delete(dir, true); }
                    catch (Exception ex) { Debug.LogWarning($"Failed to delete {dir}: {ex.Message}"); }
                }
            }
            Debug.Log("[TestDataCapture] Persistent data cleared");
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

#if UNITY_EDITOR_WIN
        private void LoadPlayerPrefsFromRegistry()
        {
            var companyName = Application.companyName;
            var productName = Application.productName;
            var registryPath = $@"Software\{companyName}\{productName}";

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(registryPath);
                if (key == null)
                {
                    Debug.LogWarning($"[TestDataCapture] Registry key not found: HKCU\\{registryPath}");
                    return;
                }

                var valueNames = key.GetValueNames();
                var addedCount = 0;

                foreach (var valueName in valueNames)
                {
                    // Unity stores keys with a hash suffix like "MyKey_h123456"
                    // Extract the actual key name by removing the hash
                    var actualKey = valueName;
                    var hashIndex = valueName.LastIndexOf("_h");
                    if (hashIndex > 0)
                    {
                        actualKey = valueName.Substring(0, hashIndex);
                    }

                    // Skip Unity system keys (used by Addressables, etc.)
                    if (actualKey.StartsWith("unity."))
                        continue;

                    // Skip if already in list
                    if (_prefEntries.Any(p => p.key == actualKey))
                        continue;

                    _prefEntries.Add(new PrefEntry { key = actualKey, enabled = true });
                    addedCount++;
                }

                Debug.Log($"[TestDataCapture] Loaded {addedCount} PlayerPrefs keys from registry (HKCU\\{registryPath})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TestDataCapture] Failed to read registry: {ex.Message}");
            }
        }
#endif

        private class FileEntry
        {
            public string relativePath;
            public string fullPath;
            public string size;
            public bool enabled;
        }

        private class PrefEntry
        {
            public string key;
            public bool enabled;
        }

        [Serializable]
        private class PlayerPrefsData
        {
            public PlayerPrefsEntry[] entries;
        }

        [Serializable]
        private class PlayerPrefsEntry
        {
            public string key;
            public string value;
            public string type;
        }
    }
}
