using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ODDGames.UIAutomation.DeviceConnect
{
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

        public ConsoleService()
        {
            Application.logMessageReceivedThreaded += OnLog;
        }

        ~ConsoleService()
        {
            Application.logMessageReceivedThreaded -= OnLog;
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
                    time = Time.realtimeSinceStartup,
                    type = type.ToString(),
                    message = message,
                    stackTrace = stackTrace
                });
            }
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
                    sb.Append($"\"message\":\"{EscapeJson(log.message)}\",");
                    sb.Append($"\"stacktrace\":\"{EscapeJson(log.stackTrace)}\"");

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

        static string EscapeJson(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") ?? "";
    }
}
