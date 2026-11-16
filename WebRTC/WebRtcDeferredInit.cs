using System;
using System.Reflection;
using Unity.WebRTC;
using ODDGames.BugpunchSdk;

namespace ODDGames.BugpunchSdk.RemoteIDE
{
    /// <summary>
    /// Starts libwebrtc the first time a stream needs it.
    ///
    /// In a player build the SDK's build step (WebRtcDeferredInitILPostProcessor,
    /// package/CodeGen) takes com.unity.webrtc's boot-time initialiser off the
    /// startup list, so a player who never streams never loads the native
    /// library or runs its threads. This is the call that replaces it.
    ///
    /// Where the boot initialiser did run (the Editor, or a build that opted out
    /// with BUGPUNCH_WEBRTC_EAGER_INIT) the context already exists and this does
    /// nothing. Main thread only — the same thread the package initialises on.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    static class WebRtcDeferredInit
    {
        static bool s_done;

        public static void Ensure()
        {
            if (s_done) return;
            s_done = true;
            try
            {
                const BindingFlags Statics = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
                var context = typeof(WebRTC).GetField("s_context", Statics);
                if (context == null)
                {
                    // A package version laid out differently. Its own boot
                    // initialiser is then also something the build step did not
                    // recognise, so it ran: nothing to do, but worth knowing.
                    BugpunchNative.ReportSdkError("WebRtcDeferredInit",
                        "Unity.WebRTC.WebRTC.s_context not found; assuming the package initialised itself");
                    return;
                }
                if (context.GetValue(null) != null) return;

                var init = typeof(WebRTC).Assembly.GetType("Unity.WebRTC.ContextManager")?.GetMethod("Init", Statics);
                if (init == null)
                {
                    BugpunchNative.ReportSdkError("WebRtcDeferredInit",
                        "Unity.WebRTC.ContextManager.Init not found; live streaming cannot start in this build");
                    return;
                }
                init.Invoke(null, null);
            }
            catch (TargetInvocationException e)
            {
                BugpunchNative.ReportSdkError("WebRtcDeferredInit", e.InnerException ?? e);
            }
            catch (Exception e)
            {
                BugpunchNative.ReportSdkError("WebRtcDeferredInit", e);
            }
        }
    }
}
