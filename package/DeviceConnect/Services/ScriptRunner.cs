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
    ///   { "ok": true,  "output": "..." }
    ///   { "ok": false, "errors": [ { "line": N, "column": M, "length": L, "message": "..." } ] }
    /// column/length are omitted when unknown (e.g. catch-all exception path).
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
                var sb = new StringBuilder();
                sb.Append("{\"ok\":false,\"errors\":[{");
                AppendLocation(sb, rex.Location);
                sb.Append($"\"message\":\"{Esc(rex.Message)}\"}}]}}");
                return sb.ToString();
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
                    sb.Append('{');
                    AppendLocation(sb, d.Location);
                    sb.Append($"\"message\":\"{Esc(d.Message)}\"}}");
                }
            }
            if (first) sb.Append("{\"message\":\"compilation failed\"}");
            sb.Append("]}");
            return sb.ToString();
        }

        static void AppendLocation(StringBuilder sb, SourceLocation? loc)
        {
            if (!loc.HasValue) return;
            AppendLocation(sb, loc.Value);
        }

        static void AppendLocation(StringBuilder sb, SourceLocation loc)
        {
            if (loc.Line > 0) sb.Append($"\"line\":{loc.Line},");
            if (loc.Column > 0) sb.Append($"\"column\":{loc.Column},");
            if (loc.Length > 0) sb.Append($"\"length\":{loc.Length},");
        }

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") ?? "";
    }
}
