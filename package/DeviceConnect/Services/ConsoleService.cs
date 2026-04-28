using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Captures Unity log messages and (when role==Internal) streams them
    /// through the IDE tunnel to the server's log sink. The tunnel push
    /// mirrors what BugpunchTunnel does on device: same JSON envelope, same
    /// 100 ms / 32 KB batching, same per-session id.
    ///
    /// <para>The in-memory ring is kept so the legacy pull-from-buffer path
    /// (web dashboard polling <c>/log</c> via the IDE proxy) continues to
    /// work without requiring the dashboard to know about log sessions.</para>
    /// </summary>
    public class ConsoleService
    {
        struct LogEntry
        {
            public int id;
            public float time;
            public string type;
            public string message;
            public string stackTrace;
        }

        readonly List<LogEntry> _logs = new();
        int _nextId;
        const int MAX_LOGS = 5000;

        // Push batcher — only flushes when role==Internal AND tunnel connected.
        // Lines captured before either is true land here and ride the next
        // flush, so role flipping mid-session doesn't drop the recent window.
        readonly StringBuilder _pushBuf = new(LOG_FLUSH_BYTES);
        readonly object _pushLock = new();
        const int LOG_FLUSH_BYTES = 32 * 1024;
        const long LOG_FLUSH_INTERVAL_MS = 100;
        // Stopwatch instead of Time.realtimeSinceStartup — OnLog fires from
        // background threads (logMessageReceivedThreaded) and Unity's Time
        // API is main-thread-only.
        readonly Stopwatch _pushClock = Stopwatch.StartNew();
        long _lastPushSchedule;
        bool _pushScheduled;

        // Session ids — matches the native Android/iOS SDK behaviour. The root
        // is minted once per process; the active id rotates whenever Unity
        // reports focus regained after >RESUME_THRESHOLD_MS without focus, and
        // every resume points back at the root so the dashboard can group them
        // under one launch. Mainly relevant on Standalone / Editor; mobile
        // builds rotate inside the native log path instead.
        readonly string _rootSessionId = Guid.NewGuid().ToString();
        string _sessionId;
        string _parentSessionId;
        long _blurredAtMs;
        const long RESUME_THRESHOLD_MS = 30_000;

        IdeTunnel _tunnel;

        public ConsoleService()
        {
            _sessionId = _rootSessionId;
            Application.logMessageReceivedThreaded += OnLog;
            Application.focusChanged += OnFocusChanged;
        }

        ~ConsoleService()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            Application.focusChanged -= OnFocusChanged;
        }

        void OnFocusChanged(bool hasFocus)
        {
            if (!hasFocus)
            {
                _blurredAtMs = _pushClock.ElapsedMilliseconds;
                return;
            }
            long blurred = _blurredAtMs;
            if (blurred <= 0) return;
            long away = _pushClock.ElapsedMilliseconds - blurred;
            _blurredAtMs = 0;
            if (away < RESUME_THRESHOLD_MS) return;
            _sessionId = Guid.NewGuid().ToString();
            _parentSessionId = _rootSessionId;
        }

        /// <summary>
        /// Wire the IDE tunnel for push streaming. Until this is called, logs
        /// captured by <see cref="OnLog"/> just accumulate in the ring + push
        /// buffer; nothing leaves the process.
        /// </summary>
        public void AttachTunnel(IdeTunnel tunnel)
        {
            _tunnel = tunnel;
        }

        void OnLog(string message, string stackTrace, LogType type)
        {
            lock (_logs)
            {
                if (_logs.Count >= MAX_LOGS)
                    _logs.RemoveAt(0);

                _logs.Add(new LogEntry
                {
                    id = _nextId++,
                    time = (float)(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0),
                    type = type.ToString(),
                    message = message,
                    stackTrace = stackTrace
                });
            }

            EnqueueForPush(type, message, stackTrace);
        }

        /// <summary>
        /// Get logs since the given logId as JSON array
        /// </summary>
        public string GetLogs(int sinceId = 0)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;

            lock (_logs)
            {
                foreach (var log in _logs)
                {
                    if (log.id <= sinceId) continue;
                    if (!first) sb.Append(",");
                    first = false;

                    sb.Append("{");
                    sb.Append($"\"recid\":{log.id},");
                    sb.Append($"\"second\":{log.time:F3},");
                    sb.Append($"\"type\":\"{log.type}\",");
                    sb.Append($"\"message\":\"{BugpunchJson.Esc(log.message)}\",");
                    sb.Append($"\"stacktrace\":\"{BugpunchJson.Esc(log.stackTrace)}\"");

                    // Color coding
                    var style = log.type switch
                    {
                        "Error" or "Exception" or "Assert" => "background-color: #ff6666",
                        "Warning" => "background-color: #ffcc00; color: #000",
                        _ => ""
                    };
                    if (!string.IsNullOrEmpty(style))
                        sb.Append($",\"w2ui\":{{\"style\":\"{style}\"}}");

                    sb.Append("}");
                }
            }

            sb.Append("]");
            return sb.ToString();
        }

        // ── Push path ──

        void EnqueueForPush(LogType type, string message, string stackTrace)
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            // Android player: BugpunchLogReader tails logcat (which captures
            // every Unity Debug.Log via tag "Unity") and pushes through the
            // report tunnel. Pushing again from C# would duplicate every
            // entry on the dashboard. iOS player and Editor both fall
            // through to the C# push path below — iOS native deliberately
            // does NOT auto-capture (matches the explicit-API pattern
            // Instabug / Luciq use).
            return;
#else
            // Cheap early-out when the gate is closed — otherwise the buffer
            // grows for the whole session on a non-Internal role.
            if (_tunnel == null || !_tunnel.IsConnected) return;
            if (!RoleState.IsInternal) return;
#endif

            // Format one logcat-ish line so the server's parser handles it
            // the same as Android/iOS native lines. Time + level + tag.
            var ts = DateTime.UtcNow;
            var lvl = type switch
            {
                LogType.Error or LogType.Exception or LogType.Assert => "E",
                LogType.Warning => "W",
                _ => "I",
            };
            var line = string.Concat(
                ts.ToString("MM-dd HH:mm:ss.fff"),
                "     0     0 ", lvl, " Unity: ",
                message ?? "");
            if (!string.IsNullOrEmpty(stackTrace))
            {
                line = string.Concat(line, "\n", stackTrace);
            }

            bool overflow;
            lock (_pushLock)
            {
                _pushBuf.Append(line);
                _pushBuf.Append('\n');
                overflow = _pushBuf.Length >= LOG_FLUSH_BYTES;
                if (!_pushScheduled)
                {
                    _pushScheduled = true;
                    _lastPushSchedule = _pushClock.ElapsedMilliseconds;
                }
            }

            if (overflow) TryFlush();
        }

        /// <summary>
        /// Called from BugpunchClient.Update — checks the flush deadline and
        /// pushes the buffer through the tunnel if ready. Cheap when the
        /// buffer is empty or the gate is closed.
        /// </summary>
        public void Tick()
        {
            if (!_pushScheduled) return;
            if (_pushClock.ElapsedMilliseconds - _lastPushSchedule < LOG_FLUSH_INTERVAL_MS) return;
            TryFlush();
        }

        void TryFlush()
        {
            // Gate: tunnel up + role==Internal. Editor + device both go
            // through this same check — no separate dev-build allowance.
            if (_tunnel == null || !_tunnel.IsConnected) return;
            if (!RoleState.IsInternal) return;

            string text;
            lock (_pushLock)
            {
                if (_pushBuf.Length == 0) { _pushScheduled = false; return; }
                text = _pushBuf.ToString();
                _pushBuf.Length = 0;
                _pushScheduled = false;
            }

            // Hand-built envelope so the raw log text rides verbatim — server
            // writes payload.text bytes to disk with no parsing.
            var sb = new StringBuilder(text.Length + 128);
            sb.Append("{\"type\":\"log\",\"sessionId\":\"").Append(_sessionId).Append('"');
            if (_parentSessionId != null)
                sb.Append(",\"parentSessionId\":\"").Append(_parentSessionId).Append('"');
            sb.Append(",\"text\":\"");
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append("\"}");
            _tunnel.SendRaw(sb.ToString());
        }
    }
}
