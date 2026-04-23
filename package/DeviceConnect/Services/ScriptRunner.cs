using System;
using System.Text;
using ODDGames.Scripting;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// IL2CPP-safe script runner backed by ODDGames.Scripting (interpreted bytecode VM, no JIT).
    /// Returns a JSON envelope so the result can be posted back through the same upload queue
    /// the rest of Bugpunch uses.
    ///
    /// Envelope:
    ///   { "ok": true,  "output": "..." }                        — success
    ///   { "ok": false, "errors": [ { "line": N, "message": "..." } ] }   — compile/runtime errors
    /// </summary>
    public class ScriptRunner : IScriptRunner
    {
        public bool IsAvailable => true;

        public string Execute(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "{\"ok\":true,\"output\":\"\"}";

            try
            {
                if (!ScriptCompiler.TryCompile(code, out var script))
                    return BuildErrorResponse(script.Diagnostics);

                var result = script.Evaluate();
                return BuildSuccessResponse(result);
            }
            catch (ScriptRuntimeException rex)
            {
                return $"{{\"ok\":false,\"errors\":[{{{LinePart(rex.Location)}\"message\":\"{Esc(rex.Message)}\"}}]}}";
            }
            catch (Exception ex)
            {
                return $"{{\"ok\":false,\"errors\":[{{\"message\":\"{Esc(ex.Message)}\"}}]}}";
            }
        }

        static string BuildSuccessResponse(object result)
        {
            var output = result == null ? "" : result.ToString();
            return $"{{\"ok\":true,\"output\":\"{Esc(output)}\"}}";
        }

        static string BuildErrorResponse(System.Collections.Generic.IReadOnlyList<ScriptDiagnostic> diagnostics)
        {
            var sb = new StringBuilder();
            sb.Append("{\"ok\":false,\"errors\":[");
            bool first = true;
            if (diagnostics != null)
            {
                foreach (var d in diagnostics)
                {
                    if (d.Severity != DiagnosticSeverity.Error) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append($"{{\"line\":{d.Location.Line},\"message\":\"{Esc(d.Message)}\"}}");
                }
            }
            if (first) sb.Append("{\"message\":\"compilation failed\"}");
            sb.Append("]}");
            return sb.ToString();
        }

        static string LinePart(SourceLocation? loc) =>
            loc.HasValue ? $"\"line\":{loc.Value.Line}," : "";

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") ?? "";
    }
}
