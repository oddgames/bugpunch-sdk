using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// Automatically increments the build number in PlayerSettings before each player build.
/// Android bundleVersionCode and iOS buildNumber are kept in sync.
/// Use Tools > Build Version to view or manually bump the current number.
[InitializeOnLoad]
public class BuildVersionIncrementer : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    static BuildVersionIncrementer()
    {
        // Display current build number in title bar after domain reload
        EditorApplication.delayCall += UpdateTitleBar;
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        int current = PlayerSettings.Android.bundleVersionCode;
        int next = current + 1;

        PlayerSettings.Android.bundleVersionCode = next;
        PlayerSettings.iOS.buildNumber = next.ToString();

        AssetDatabase.SaveAssets();
        Debug.Log($"[BuildVersion] Build number incremented: {current} → {next}");
    }

    static void UpdateTitleBar()
    {
        // No-op: just here as an extension point if needed later
    }

    [MenuItem("Tools/Build Version/Show Current")]
    static void ShowCurrent()
    {
        int build = PlayerSettings.Android.bundleVersionCode;
        string version = PlayerSettings.bundleVersion;
        EditorUtility.DisplayDialog("Build Version",
            $"Version: {version}\nBuild Number: {build}", "OK");
    }

    [MenuItem("Tools/Build Version/Increment Now")]
    static void IncrementNow()
    {
        int current = PlayerSettings.Android.bundleVersionCode;
        int next = current + 1;
        PlayerSettings.Android.bundleVersionCode = next;
        PlayerSettings.iOS.buildNumber = next.ToString();
        AssetDatabase.SaveAssets();
        Debug.Log($"[BuildVersion] Manually incremented: {current} → {next}");
    }
}
