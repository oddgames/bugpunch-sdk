using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Forwards uncaught C# managed exceptions to the native coordinator via
    /// <see cref="BugpunchNative.ReportBug"/>. Subscribes to three Unity / .NET
    /// events because no single hook covers every path:
    /// <list type="bullet">
    ///   <item><c>Application.logMessageReceivedThreaded</c> with
    ///     <c>LogType.Exception</c> — covers the common case of exceptions
    ///     thrown from <c>MonoBehaviour</c> callbacks (Update, coroutines, UGUI
    ///     handlers). Unity catches these internally and only surfaces them
    ///     via the log API; <c>AppDomain.UnhandledException</c> never fires.</item>
    ///   <item><c>AppDomain.UnhandledException</c> — non-terminating managed
    ///     exceptions on background threads not started via Task.</item>
    ///   <item><c>TaskScheduler.UnobservedTaskException</c> — unobserved Task
    ///     faults that are GC'd without being awaited.</item>
    /// </list>
    ///
    /// Native signal/Mach/ANR crashes are handled entirely in native code — no
    /// C# involvement.
    /// </summary>
    public static class UnityExceptionForwarder
    {
        static bool s_installed;
        // Re-entrancy guard: forwarding to native triggers JNI/P-Invoke that
        // can themselves Debug.Log on failure. Without this we'd recurse.
        [ThreadStatic] static bool t_inForward;

        public static void Install()
        {
            if (s_installed) return;
            Application.logMessageReceivedThreaded += OnLogMessage;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            s_installed = true;
        }

        public static void Uninstall()
        {
            if (!s_installed) return;
            Application.logMessageReceivedThreaded -= OnLogMessage;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            s_installed = false;
        }

        static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception) return;
            if (t_inForward) return;
            try
            {
                t_inForward = true;
                var title = condition ?? "Exception";
                if (title.Length > 200) title = title.Substring(0, 200);
                BugpunchNative.ReportBug("exception", title, stackTrace ?? string.Empty, null);
            }
            catch { }
            finally { t_inForward = false; }
        }

        static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try { Forward(e.ExceptionObject as Exception); } catch { }
        }

        static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            try { Forward(e.Exception?.Flatten()); } catch { }
        }

        static void Forward(Exception ex)
        {
            if (ex == null) return;
            if (t_inForward) return;
            try
            {
                t_inForward = true;
                var title = $"{ex.GetType().FullName}: {ex.Message}";
                if (title.Length > 200) title = title.Substring(0, 200);
                BugpunchNative.ReportBug("exception", title, ex.ToString(), null);
            }
            catch { }
            finally { t_inForward = false; }
        }
    }
}
