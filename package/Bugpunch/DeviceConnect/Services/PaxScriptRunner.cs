using System;
using System.Text;
using UnityEngine;
using PaxScript.Net;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// PaxScripter-based script runner. Works on IL2CPP builds.
    /// PaxScripter interprets its own bytecode — no JIT required.
    /// Requires the au.com.oddgames.paxscript package (v1.5.0+).
    /// </summary>
    public class PaxScriptRunner : IScriptRunner
    {
        public bool IsAvailable => true;

        public string Execute(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "{\"ok\":true,\"output\":\"\"}";

            try
            {
                var scripter = new PaxScripter();

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { scripter.RegisterAssembly(assembly); }
                    catch { }
                }

                var errors = new StringBuilder();

                scripter.OnChangeState += (sender, e) =>
                {
                    if (sender.HasErrors)
                    {
                        foreach (ScriptError err in sender.Error_List)
                            errors.AppendLine($"({err.LineNumber}) {err.Message}");
                    }
                };

                scripter.OnPaxException += (sender, ex) =>
                {
                    errors.AppendLine(ex.Message);
                };

                scripter.AddModule("1");
                scripter.AddCode("1", code);

                // Explicit compile → link → run for proper error detection at each phase
                scripter.Compile();
                if (scripter.HasErrors || errors.Length > 0)
                {
                    CollectErrors(scripter, errors);
                    return BuildErrorResponse(scripter, errors);
                }

                scripter.Link();
                if (scripter.HasErrors || errors.Length > 0)
                {
                    CollectErrors(scripter, errors);
                    return BuildErrorResponse(scripter, errors);
                }

                scripter.Run(RunMode.Run);
                if (scripter.HasErrors || errors.Length > 0)
                {
                    CollectErrors(scripter, errors);
                    return BuildErrorResponse(scripter, errors);
                }

                return "{\"ok\":true,\"output\":\"\"}";
            }
            catch (Exception ex)
            {
                return $"{{\"ok\":false,\"errors\":[{{\"message\":\"{Esc(ex.Message)}\"}}]}}";
            }
        }

        static void CollectErrors(PaxScripter scripter, StringBuilder errors)
        {
            if (errors.Length > 0) return;
            if (scripter.Error_List == null) return;
            foreach (ScriptError err in scripter.Error_List)
                errors.AppendLine($"({err.LineNumber}) {err.Message}");
        }

        static string BuildErrorResponse(PaxScripter scripter, StringBuilder errorText)
        {
            var sb = new StringBuilder();
            sb.Append("{\"ok\":false,\"errors\":[");
            bool first = true;

            // Structured errors with line numbers
            if (scripter.Error_List != null)
            {
                foreach (ScriptError err in scripter.Error_List)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append($"{{\"line\":{err.LineNumber},\"message\":\"{Esc(err.Message)}\"}}");
                }
            }

            // Fallback: parse (N) prefix from collected error text
            if (first && errorText.Length > 0)
            {
                sb.Append($"{{\"message\":\"{Esc(errorText.ToString())}\"}}");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        static string Error(StringBuilder errors) =>
            $"{{\"ok\":false,\"errors\":[{{\"message\":\"{Esc(errors.ToString())}\"}}]}}";

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") ?? "";
    }
}
