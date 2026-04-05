#if BUGPUNCH_HAS_PAXSCRIPT
using System;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// PaxScripter-based script runner. Works on IL2CPP builds.
    /// PaxScripter interprets its own bytecode — no JIT required.
    /// Requires the au.com.oddgames.paxscript package.
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

                scripter.AddModule("1");
                scripter.AddCode("1", code);

                if (hasError)
                    return $"{{\"ok\":false,\"errors\":[{{\"message\":\"{Esc(errors.ToString())}\"}}]}}";

                // Try to invoke Main if it exists, otherwise just compile
                try
                {
                    var result = scripter.Invoke(RunMode.Run, null, "Main");
                    if (hasError)
                        return $"{{\"ok\":false,\"output\":\"\",\"errors\":[{{\"message\":\"{Esc(errors.ToString())}\"}}]}}";

                    var output = result?.ToString() ?? "";
                    return $"{{\"ok\":true,\"output\":\"{Esc(output)}\"}}";
                }
                catch (Exception ex)
                {
                    // If no Main method, try running as a statement block
                    if (ex.Message.Contains("Main"))
                    {
                        // Wrap code in a Main method and retry
                        try
                        {
                            var scripter2 = new PaxScripter();
                            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                            {
                                try { scripter2.RegisterAssembly(assembly); }
                                catch { }
                            }

                            scripter2.OnChangeState += (sender, e) =>
                            {
                                if (sender.HasErrors)
                                {
                                    hasError = true;
                                    errors.Clear();
                                    foreach (ScriptError err in sender.Error_List)
                                        errors.AppendLine($"({err.LineNumber}) {err.Message}");
                                }
                            };

                            var wrapped = $"using UnityEngine;\nclass Script {{ public static void Main() {{ {code} }} }}";
                            scripter2.AddModule("1");
                            scripter2.AddCode("1", wrapped);
                            scripter2.Invoke(RunMode.Run, null, "Main");

                            if (hasError)
                                return $"{{\"ok\":false,\"errors\":[{{\"message\":\"{Esc(errors.ToString())}\"}}]}}";

                            return $"{{\"ok\":true,\"output\":\"\"}}";
                        }
                        catch (Exception ex2)
                        {
                            return $"{{\"ok\":false,\"errors\":[{{\"message\":\"{Esc(ex2.Message)}\"}}]}}";
                        }
                    }
                    return $"{{\"ok\":false,\"errors\":[{{\"message\":\"{Esc(ex.Message)}\"}}]}}";
                }
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
