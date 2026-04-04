using System.IO;
using UnityEditor;
using UnityEngine;

namespace ODDGames.UIAutomation.AI.Editor
{
    /// <summary>
    /// Editor hooks for AITestSettings — provides asset loading/creation and save functionality.
    /// </summary>
    [InitializeOnLoad]
    static class AITestSettingsEditorHooks
    {
        private const string SettingsPath = "Assets/Editor/AITestSettings.asset";

        static AITestSettingsEditorHooks()
        {
            AITestSettings.EditorLoader = LoadOrCreate;
        }

        private static AITestSettings LoadOrCreate()
        {
            var settings = AssetDatabase.LoadAssetAtPath<AITestSettings>(SettingsPath);

            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<AITestSettings>();

                var directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
            }

            return settings;
        }

        /// <summary>
        /// Saves the settings asset.
        /// </summary>
        public static void Save(AITestSettings settings)
        {
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
    }
}
