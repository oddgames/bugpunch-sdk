using System;
using System.Runtime.InteropServices;
using ODDGames.Bugpunch;
using UnityEngine;

/// <summary>
/// Game-specific debug tools registered via attributes. The DebugToolsBridge
/// discovers these at startup and shows them in the native tools panel.
///
/// Add [DebugButton], [DebugToggle], [DebugSlider] to any static member in
/// any script — no manual registration needed.
/// </summary>
public static class GameDebugTools
{
    // ── Bugpunch ──

    [DebugButton("Bugpunch", "Enter Debug Mode", "Start video recording with consent", icon: "play")]
    static void EnterDebug() => Bugpunch.EnterDebugMode();

    [DebugButton("Bugpunch", "Send Bug Report", "File a user bug report", icon: "send")]
    static void SendReport() => Bugpunch.Report("Test report", "Triggered from debug tools panel");

    [DebugButton("Bugpunch", "Send Feedback", "Submit user feedback", icon: "flag")]
    static void SendFeedback() => Bugpunch.Feedback("Test feedback from debug tools panel");

    // ── Storyboard Demo ──

    [DebugButton("Storyboard Demo", "Open Demo Menus", "Fake menu tree — click through to generate a storyboard", icon: "layout")]
    static void OpenDemoMenus() => StoryboardDemo.Open();

    [DebugButton("Storyboard Demo", "Close Demo Menus", "Close the demo overlay", icon: "x")]
    static void CloseDemoMenus() => StoryboardDemo.Close();

    // ── Crash Testing ──

    [DebugButton("Crash Testing", "Throw Exception", "Unhandled C# exception", icon: "alert-triangle")]
    static void ThrowException()
    {
        var go = new GameObject("Thrower");
        go.AddComponent<DeferredThrower>();
    }

    [DebugButton("Crash Testing", "Crash: Null Deref", "Write to null pointer (SIGSEGV)", icon: "zap")]
    static void CrashNullDeref()
    {
        var go = new GameObject("NullDeref");
        go.AddComponent<DeferredNullDeref>();
    }

    [DebugButton("Crash Testing", "Crash: Stack Overflow", "Infinite recursion", icon: "zap")]
    static void CrashStackOverflow()
    {
        var go = new GameObject("StackOverflow");
        go.AddComponent<DeferredStackOverflow>();
    }

    // ── Rendering ──

    [DebugToggle("Rendering", "VSync", "Toggle vertical sync", icon: "eye")]
    public static bool VSync
    {
        get => QualitySettings.vSyncCount > 0;
        set => QualitySettings.vSyncCount = value ? 1 : 0;
    }

    [DebugSlider("Rendering", 15, 120, "Target FPS", "Set application target frame rate", icon: "activity")]
    public static float TargetFPS
    {
        get => Application.targetFrameRate > 0 ? Application.targetFrameRate : 60;
        set => Application.targetFrameRate = Mathf.RoundToInt(value);
    }

    // ── Time ──

    [DebugSlider("Time", 0, 4, "Time Scale", "Slow motion / fast forward", icon: "clock")]
    public static float TimeScale
    {
        get => Time.timeScale;
        set => Time.timeScale = value;
    }

    // ── Deferred crash helpers ──

    class DeferredThrower : MonoBehaviour
    {
        void Update()
        {
            Destroy(gameObject);
            throw new InvalidOperationException(
                "Bugpunch test: synthetic exception for reporting verification");
        }
    }

    class DeferredNullDeref : MonoBehaviour
    {
        void Update() { Marshal.WriteInt32(IntPtr.Zero, 0); }
    }

    class DeferredStackOverflow : MonoBehaviour
    {
        void Update() { Recurse(0); }
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static int Recurse(int depth)
        {
            var pad = new byte[256]; pad[0] = (byte)(depth & 0xFF);
            return Recurse(depth + 1) + pad[0];
        }
    }
}
