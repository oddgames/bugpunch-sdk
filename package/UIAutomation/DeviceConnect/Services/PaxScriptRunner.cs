// PaxScriptRunner — IL2CPP-compatible C# script execution via PaxScripter
//
// PaxScripter is a proprietary scripting engine (paxScript.NET by Alexander Baranovsky).
// It interprets its own bytecode, not IL — so it works under IL2CPP where Roslyn/Mono.CSharp cannot.
//
// This file uses conditional compilation:
// - If ODDDEV_HAS_PAXSCRIPT is defined, it compiles the PaxScripter implementation
// - If not, it provides a stub that reports scripting as unavailable
//
// To enable: add ODDDEV_HAS_PAXSCRIPT to your project's Scripting Define Symbols,
// and ensure PaxScripter assemblies are in your project.

using System;
using System.Text;
using UnityEngine;

namespace ODDGames.UIAutomation.DeviceConnect
{
#if ODDDEV_HAS_PAXSCRIPT
    /// <summary>
    /// PaxScripter-based script runner. Works on IL2CPP builds.
    /// Requires PaxScripter assemblies in the project and ODDDEV_HAS_PAXSCRIPT define.
    /// </summary>
    public class PaxScriptRunner : IScriptRunner
    {
        PaxScripter _scripter;
        readonly StringBuilder _output = new();

        public bool IsAvailable => true;

        public PaxScriptRunner()
        {
            try
            {
                _scripter = new PaxScripter();
                _scripter.scripter.SearchProtected = true;
                _scripter.OnPrint += (sender, args) =>
                {
                    _output.AppendLine(args.Text);
                };
                Debug.Log("[OddDev] PaxScriptRunner initialized — IL2CPP-compatible scripting enabled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OddDev] PaxScriptRunner init failed: {ex.Message}");
                _scripter = null;
            }
        }

        public string Execute(string code)
        {
            if (_scripter == null)
                return "{\"ok\":false,\"errors\":[{\"message\":\"PaxScripter not initialized\"}]}";

            _output.Clear();

            try
            {
                // Reset and compile
                _scripter.ResetModules();
                _scripter.AddModule("script");
                _scripter.AddCode("script", code);
                _scripter.Compile();

                // Check for compilation errors
                if (_scripter.HasErrors)
                {
                    var errors = new StringBuilder();
                    errors.Append("[");
                    bool first = true;
                    foreach (var err in _scripter.Error_List)
                    {
                        if (!first) errors.Append(",");
                        first = false;
                        errors.Append($"{{\"line\":{err.LineNumber},\"message\":\"{Esc(err.Message)}\"}}");
                    }
                    errors.Append("]");

                    return $"{{\"ok\":false,\"errors\":{errors}}}";
                }

                // Execute
                _scripter.Run(RunMode.Run);

                // Check for runtime errors
                if (_scripter.HasErrors)
                {
                    var errors = new StringBuilder();
                    errors.Append("[");
                    bool first = true;
                    foreach (var err in _scripter.Error_List)
                    {
                        if (!first) errors.Append(",");
                        first = false;
                        errors.Append($"{{\"line\":{err.LineNumber},\"message\":\"{Esc(err.Message)}\"}}");
                    }
                    errors.Append("]");

                    return $"{{\"ok\":false,\"output\":\"{Esc(_output.ToString())}\",\"errors\":{errors}}}";
                }

                return $"{{\"ok\":true,\"output\":\"{Esc(_output.ToString())}\"}}";
            }
            catch (Exception ex)
            {
                return $"{{\"ok\":false,\"errors\":[{{\"message\":\"{Esc(ex.Message)}\"}}]}}";
            }
        }

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") ?? "";
    }

#else
    /// <summary>
    /// Stub script runner when PaxScripter is not available.
    /// Add ODDDEV_HAS_PAXSCRIPT to Scripting Define Symbols to enable.
    /// </summary>
    public class PaxScriptRunner : IScriptRunner
    {
        public bool IsAvailable => false;

        public string Execute(string code)
        {
            return "{\"ok\":false,\"errors\":[{\"message\":\"Script execution requires PaxScripter. Add ODDDEV_HAS_PAXSCRIPT to Scripting Define Symbols and include PaxScripter in your project.\"}]}";
        }
    }
#endif
}
