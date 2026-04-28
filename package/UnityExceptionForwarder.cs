using System;
using System.Collections;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ODDGames.Bugpunch
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
    /// <para><b>Exception.Data support:</b> Developers can attach arbitrary
    /// key-value debug data to any exception via <c>Exception.Data</c>. This
    /// forwarder extracts those entries and sends them as <c>extraJson</c> in
    /// the crash report, where they appear in the dashboard's Custom Data tab.
    /// Because Unity's <c>logMessageReceivedThreaded</c> callback only provides
    /// strings (no Exception object), we use <c>FirstChanceException</c> to
    /// cache the last exception's Data per thread.</para>
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

        // Cache the last exception's Data per thread so OnLogMessage (which
        // only gets strings) can still forward Exception.Data entries.
        // FirstChanceException fires synchronously on the throwing thread
        // before Unity's catch block re-surfaces it via logMessageReceived.
        [ThreadStatic] static string t_cachedDataJson;

        public static void Install()
        {
            if (s_installed) return;
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            Application.logMessageReceivedThreaded += OnLogMessage;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            s_installed = true;
        }

        public static void Uninstall()
        {
            if (!s_installed) return;
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
            Application.logMessageReceivedThreaded -= OnLogMessage;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            s_installed = false;
        }

        static void OnFirstChance(object sender, FirstChanceExceptionEventArgs e)
        {
            // Keep this as cheap as possible — fires for EVERY thrown exception
            // including caught ones. Only serialize when Data has entries.
            try
            {
                var ex = e.Exception;
                if (ex == null) { t_cachedDataJson = null; return; }
                var data = ex.Data;
                if (data == null || data.Count == 0) { t_cachedDataJson = null; return; }
                t_cachedDataJson = SerializeExceptionData(ex);
            }
            catch { t_cachedDataJson = null; }
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
                // Consume cached Exception.Data from FirstChanceException
                var extraJson = t_cachedDataJson;
                t_cachedDataJson = null;
                BugpunchNative.ReportBug("exception", title, stackTrace ?? string.Empty, extraJson);

                // If the exception originated inside SDK code, also surface it on
                // the on-screen diagnostic banner so we're not in the dark when
                // the SDK itself throws (vs. game code throwing through the SDK).
                if (IsSdkOrigin(stackTrace))
                {
                    BugpunchNative.ReportSdkError("UnityExceptionForwarder", title, stackTrace);
                }
            }
            catch { }
            finally { t_inForward = false; }
        }

        /// <summary>
        /// Heuristic: if the stack trace contains any frame from
        /// <c>ODDGames.Bugpunch.</c> the exception originated inside the SDK.
        /// Cheap string scan — runs on the throwing thread and is bounded to
        /// the trace string length.
        /// </summary>
        static bool IsSdkOrigin(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace)) return false;
            return stackTrace.IndexOf("ODDGames.Bugpunch.", StringComparison.Ordinal) >= 0;
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
                var extraJson = SerializeExceptionData(ex);
                var stack = ex.ToString();
                BugpunchNative.ReportBug("exception", title, stack, extraJson);
                if (IsSdkOrigin(stack))
                {
                    BugpunchNative.ReportSdkError("UnityExceptionForwarder", title, stack);
                }
            }
            catch { }
            finally { t_inForward = false; }
        }

        /// <summary>
        /// Serialize Exception.Data entries (and InnerException chain) as a
        /// flat JSON object suitable for the extraJson parameter. Keys are
        /// prefixed to avoid collisions with persistent custom data set via
        /// <see cref="BugpunchNative.SetCustomData"/>.
        ///
        /// <para>Also includes the exception type as <c>exception.type</c>
        /// for exceptions that arrive via the Data path.</para>
        /// </summary>
        static string SerializeExceptionData(Exception ex)
        {
            if (ex == null) return null;

            var data = ex.Data;
            bool hasData = data != null && data.Count > 0;
            bool hasInner = ex.InnerException != null;
            if (!hasData && !hasInner) return null;

            var sb = new StringBuilder(256);
            sb.Append('{');
            bool first = true;

            if (hasData)
            {
                foreach (DictionaryEntry entry in data)
                {
                    if (entry.Key == null) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    JsonField(sb, "exception.data." + entry.Key.ToString(),
                        entry.Value?.ToString() ?? "null");
                }
            }

            // Walk inner exceptions — capture their type + message + Data
            var inner = ex.InnerException;
            int depth = 0;
            while (inner != null && depth < 5)
            {
                if (!first) sb.Append(',');
                first = false;
                var prefix = depth == 0 ? "exception.inner" : $"exception.inner.{depth}";
                JsonField(sb, prefix + ".type", inner.GetType().FullName ?? "Exception");
                sb.Append(',');
                var msg = inner.Message ?? "";
                if (msg.Length > 500) msg = msg.Substring(0, 500);
                JsonField(sb, prefix + ".message", msg);

                var innerData = inner.Data;
                if (innerData != null && innerData.Count > 0)
                {
                    foreach (DictionaryEntry entry in innerData)
                    {
                        if (entry.Key == null) continue;
                        sb.Append(',');
                        JsonField(sb, prefix + ".data." + entry.Key.ToString(),
                            entry.Value?.ToString() ?? "null");
                    }
                }

                inner = inner.InnerException;
                depth++;
            }

            sb.Append('}');
            return sb.ToString();
        }

        static void JsonField(StringBuilder sb, string key, string value)
        {
            sb.Append('"').Append(JsonEsc(key)).Append("\":\"").Append(JsonEsc(value)).Append('"');
        }

        static string JsonEsc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:X4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
