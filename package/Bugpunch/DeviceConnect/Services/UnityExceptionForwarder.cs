using System;
using System.Threading.Tasks;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Forwards uncaught C# managed exceptions to the native coordinator via
    /// <see cref="BugpunchNative.ReportBug"/>. Covers the two paths Unity's
    /// <c>Application.logMessageReceived</c> misses:
    /// <list type="bullet">
    ///   <item><c>AppDomain.UnhandledException</c> (non-terminating managed
    ///     exceptions on background threads)</item>
    ///   <item><c>TaskScheduler.UnobservedTaskException</c> (unobserved Task
    ///     faults)</item>
    /// </list>
    ///
    /// Native signal/Mach/ANR crashes are handled entirely in native code — no
    /// C# involvement.
    /// </summary>
    public static class UnityExceptionForwarder
    {
        static bool s_installed;

        public static void Install()
        {
            if (s_installed) return;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            s_installed = true;
        }

        public static void Uninstall()
        {
            if (!s_installed) return;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            s_installed = false;
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
            var title = $"{ex.GetType().FullName}: {ex.Message}";
            if (title.Length > 200) title = title[..200];
            BugpunchNative.ReportBug("exception", title, ex.ToString(), null);
        }
    }
}
