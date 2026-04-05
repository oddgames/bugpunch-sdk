#if BUGPUNCH_HAS_PAXSCRIPT
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
            try
            {
                var scripter = new PaxScripter();

                // Register all loaded assemblies so scripts can access Unity API, etc.
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { scripter.RegisterAssembly(assembly); }
                    catch { /* skip problematic assemblies */ }
                }

                bool hasError = false;
                var errors = new StringBuilder();
                var output = new StringBuilder();

                scripter.OnChangeState += (sender, e) =>
                {
                    if (sender.HasErrors)
                    {
                        hasError = true;
                        foreach (ScriptError err in sender.Error_List)
                            errors.AppendLine($"({err.LineNumber}) {err.Message}");
                    }
                };

                scripter.OnPaxException += (sender, ex) =>
                {
                    hasError = true;
                    errors.AppendLine(ex.Message);
                };

                // PaxScript v1.5.0+ supports top-level scripts — no Main needed
                scripter.AddModule("1");
                scripter.AddCode("1", code);

                if (hasError)
                    return $"{{\"ok\":false,\"errors\":[{{\"message\":\"{Esc(errors.ToString())}\"}}]}}";

                scripter.Run(RunMode.Run);

                if (hasError)
                    return $"{{\"ok\":false,\"errors\":[{{\"message\":\"{Esc(errors.ToString())}\"}}]}}";

                return $"{{\"ok\":true,\"output\":\"{Esc(output.ToString())}\"}}";
            }
            catch (Exception ex)
            {
                return $"{{\"ok\":false,\"errors\":[{{\"message\":\"{Esc(ex.Message)}\"}}]}}";
            }
        }

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") ?? "";
    }
}
#endif
