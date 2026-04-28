using System;
using System.Reflection;
using System.Text;
using ODDGames.Scripting;
using ODDGames.Scripting.VirtualMachine;
using UnityEngine;
using BPScriptProtected = ODDGames.Scripting.ScriptProtectedAttribute;

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
        // Install the protected-destroy guard exactly once per process. The
        // guard refuses Object.Destroy / DestroyImmediate / Destroy<T> calls
        // when any argument resolves to an IScriptProtected component or to
        // a GameObject that owns one — so a QA script can't accidentally
        // (or deliberately) destroy BugpunchClient, BugpunchSceneTick, or
        // any game-side singleton that opted in to the marker.
        static readonly object _guardLock = new object();
        static bool _guardInstalled;

        static ScriptRunner()
        {
            EnsureProtectedDestroyGuard();
        }

        static void EnsureProtectedDestroyGuard()
        {
            lock (_guardLock)
            {
                if (_guardInstalled) return;
                _guardInstalled = true;
                MethodInvocationGuard.Register(ProtectedDestroyGuard);
            }
        }

        static string ProtectedDestroyGuard(MethodInfo method, object instance, object[] args)
        {
            // Only inspect destructive Unity APIs. Anything else passes through
            // for free — keeping the per-call cost negligible.
            if (method == null) return null;
            var declaring = method.DeclaringType;
            if (declaring == null) return null;
            if (declaring != typeof(UnityEngine.Object)) return null;
            var name = method.Name;
            if (name != "Destroy" && name != "DestroyImmediate") return null;

            // Walk every arg + the method receiver looking for a protected
            // GameObject / Component. Static Destroy puts the target in args[0];
            // we still scan the receiver in case of future overloads / DestroyImmediate(self).
            if (IsProtectedTarget(instance)) return DenyMessage(instance);
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (IsProtectedTarget(args[i])) return DenyMessage(args[i]);
                }
            }
            return null;
        }

        static bool IsProtectedTarget(object target)
        {
            if (target == null) return false;
            // Direct attribute on the target type (or any ancestor — the
            // attribute is Inherited).
            if (HasProtectedAttribute(target.GetType())) return true;
            // For Unity scene objects, scan the GameObject's components so
            // destroying one of the SDK's helper components also fails
            // (they're peers of the marker-bearing component, not necessarily
            // marked themselves).
            if (target is Component c)
            {
                if (c == null || c.gameObject == null) return false;
                return GameObjectHasProtectedComponent(c.gameObject);
            }
            if (target is GameObject go)
            {
                return GameObjectHasProtectedComponent(go);
            }
            return false;
        }

        static bool HasProtectedAttribute(Type t)
        {
            // Destroy is unconditionally blocked for any [ScriptProtected]
            // type — we deliberately do NOT consult the attribute's
            // RequiredLevel here. The trust-level mechanism applies to
            // method / property access; destruction has no escape hatch
            // even at System trust. SDK GameObjects, the IDE tunnel host,
            // and anything else marked with the attribute can't be
            // destroyed by a script regardless of who's authenticated.
            if (t == null) return false;
            return t.GetCustomAttribute<BPScriptProtected>(inherit: true) != null;
        }

        static bool GameObjectHasProtectedComponent(GameObject go)
        {
            if (go == null) return false;
            var components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                var comp = components[i];
                if (comp == null) continue; // missing-script slot
                if (HasProtectedAttribute(comp.GetType())) return true;
            }
            return false;
        }

        static string DenyMessage(object target)
        {
            string label;
            if (target is GameObject g) label = g.name;
            else if (target is Component c2 && c2 != null) label = c2.gameObject != null ? c2.gameObject.name + "/" + c2.GetType().Name : c2.GetType().Name;
            else label = target?.GetType().Name ?? "(null)";
            return "Cannot destroy script-protected object: " + label;
        }

        public bool IsAvailable => true;

        public string Execute(string code) => Execute(code, ScriptTrustLevel.Untrusted);

        /// <summary>
        /// Execute the script at the given trust level. The default is
        /// <see cref="ScriptTrustLevel.Untrusted"/> — chat-issued / external
        /// scripts. Hosts that have authenticated an admin connection (e.g.
        /// the Remote IDE attached via secret) raise the level so members
        /// marked <c>[ScriptProtected]</c> become reachable. Members marked
        /// <c>[ScriptProtected(ScriptTrustLevel.System)]</c> still require
        /// engine-level trust; the destroy guard on protected types is
        /// always-on regardless of level.
        /// </summary>
        public string Execute(string code, ScriptTrustLevel trustLevel)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "{\"ok\":true,\"output\":\"\"}";

            try
            {
                var options = new ScriptCompileOptions { TrustLevel = trustLevel };
                if (!ScriptCompiler.TryCompile(code, out var script, options))
                    return BuildErrorResponse(script.Diagnostics);

                using (MethodInvocationGuard.EnterTrustLevel(trustLevel))
                {
                    var result = script.Evaluate();
                    return BuildSuccessResponse(result);
                }
            }
            catch (ScriptRuntimeException rex)
            {
                var sb = new StringBuilder();
                sb.Append("{\"ok\":false,\"errors\":[{");
                AppendLocation(sb, rex.Location);
                sb.Append($"\"message\":\"{BugpunchJson.Esc(rex.Message)}\"}}]}}");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"{{\"ok\":false,\"errors\":[{{\"message\":\"{BugpunchJson.Esc(ex.Message)}\"}}]}}";
            }
        }

        public string Diagnose(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "{\"ok\":true,\"diagnostics\":[]}";

            try
            {
                ScriptCompiler.TryCompile(code, out var script);
                return BuildDiagnosticsResponse(script?.Diagnostics);
            }
            catch (Exception ex)
            {
                return $"{{\"ok\":true,\"diagnostics\":[{{\"severity\":\"error\",\"message\":\"{BugpunchJson.Esc(ex.Message)}\"}}]}}";
            }
        }

        static string BuildSuccessResponse(object result)
        {
            var output = result == null ? "" : result.ToString();
            return $"{{\"ok\":true,\"output\":\"{BugpunchJson.Esc(output)}\"}}";
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
                    sb.Append($"\"message\":\"{BugpunchJson.Esc(d.Message)}\"}}");
                }
            }
            if (first) sb.Append("{\"message\":\"compilation failed\"}");
            sb.Append("]}");
            return sb.ToString();
        }

        static string BuildDiagnosticsResponse(System.Collections.Generic.IReadOnlyList<ScriptDiagnostic> diagnostics)
        {
            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"diagnostics\":[");
            bool first = true;
            if (diagnostics != null)
            {
                foreach (var d in diagnostics)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('{');
                    AppendLocation(sb, d.Location);
                    sb.Append($"\"severity\":\"{SeverityName(d.Severity)}\",");
                    if (!string.IsNullOrEmpty(d.Code)) sb.Append($"\"code\":\"{BugpunchJson.Esc(d.Code)}\",");
                    sb.Append($"\"message\":\"{BugpunchJson.Esc(d.Message)}\"}}");
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static string SeverityName(DiagnosticSeverity s)
        {
            switch (s)
            {
                case DiagnosticSeverity.Error: return "error";
                case DiagnosticSeverity.Warning: return "warning";
                default: return "info";
            }
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

    }
}
