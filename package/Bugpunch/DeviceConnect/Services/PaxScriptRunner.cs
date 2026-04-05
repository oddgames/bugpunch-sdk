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
            if (string.IsNullOrWhiteSpace(code))
                return "{\"ok\":true,\"output\":\"\"}";

            try
            {
                var scripter = new PaxScripter();

                // Register all loaded assemblies so scripts can access Unity API, etc.
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { scripter.RegisterAssembly(assembly); }
                    catch { /* skip problematic assemblies */ }
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

                // Phase 1: Compile
                scripter.AddModule("1");
                scripter.AddCode("1", code);

                // Check compile errors (both from event AND direct property)
                if (scripter.HasErrors || errors.Length > 0)
                {
                    // Collect any errors we missed from the event
                    if (errors.Length == 0 && scripter.Error_List != null)
                    {
                        foreach (ScriptError err in scripter.Error_List)
                            errors.AppendLine($"({err.LineNumber}) {err.Message}");
                    }
                    return $"{{\"ok\":false,\"errors\":[{{\"message\":\"{Esc(errors.ToString())}\"}}]}}";
                }

                // Phase 2: Execute
                scripter.Run(RunMode.Run);

                // Check runtime errors
                if (scripter.HasErrors || errors.Length > 0)
                {
                    if (errors.Length == 0 && scripter.Error_List != null)
                    {
                        foreach (ScriptError err in scripter.Error_List)
                            errors.AppendLine($"({err.LineNumber}) {err.Message}");
                    }
                    return $"{{\"ok\":false,\"errors\":[{{\"message\":\"{Esc(errors.ToString())}\"}}]}}";
                }

                return "{\"ok\":true,\"output\":\"\"}";
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
