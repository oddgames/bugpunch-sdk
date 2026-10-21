using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using ODDGames.Scripting;
using ODDGames.Scripting.VirtualMachine;
using ODDGames.BugpunchSdk.RemoteIDE;
using UnityEngine;

// Reached only through the RuntimeInitializeOnLoadMethod below (autoReferenced:false), which
// IL2CPP does not reliably keep on its own — see WebRTCStreamerBootstrap.
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace ODDGames.BugpunchSdk.Scripting
{
    /// <summary>
    /// <see cref="IScriptRunner"/> backed by au.com.oddgames.scripting. Compiled only when that
    /// package is installed (BUGPUNCH_SCRIPTING), and registered into <see cref="BugpunchScripting"/>.
    /// Envelopes: <c>{"ok":true,"output":"..."}</c> / <c>{"ok":false,"errors":[{line,column,length,message}]}</c>.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    sealed class ScriptRunner : IScriptRunner
    {
        [UnityEngine.Scripting.Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            // Unregister first: with domain reload disabled this runs every play-mode entry.
            MethodInvocationGuard.Unregister(ProtectedDestroyGuard);
            MethodInvocationGuard.Register(ProtectedDestroyGuard);
            ScriptAsyncBuilder.CoroutinePump = PumpCoroutine;
            BugpunchScripting.Register(new ScriptRunner());
        }

        public bool IsAvailable => true;

        // Remote IDE / server tools / crash directives are authenticated debug surfaces: Admin trust
        // reaches private members; [ScriptProtected(System)] and the hard sandbox still apply.
        public string Execute(string code) => Execute(code, ScriptTrustLevel.Admin);

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

        public bool TryEvaluateUntrusted(string code, out object result, out string error)
        {
            result = null;
            error = null;
            try
            {
                var options = new ScriptCompileOptions { TrustLevel = ScriptTrustLevel.Untrusted };
                if (!ScriptCompiler.TryCompile(code, out var script, options) || script == null)
                {
                    error = "compile failed: " + FirstError(script?.Diagnostics);
                    return false;
                }
                using (MethodInvocationGuard.EnterTrustLevel(ScriptTrustLevel.Untrusted))
                    result = script.Evaluate();
                return true;
            }
            catch (Exception e)
            {
                error = e is ScriptRuntimeException ? "runtime error: " + e.Message : e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        public bool IsNamespaceDenied(string ns) => ScriptNamespaceDenylist.IsDenied(ns);

        public int Warmup() => ScriptCompiler.Warmup();

        static string Execute(string code, ScriptTrustLevel trustLevel)
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
                    return $"{{\"ok\":true,\"output\":\"{BugpunchJson.Esc(result == null ? "" : result.ToString())}\"}}";
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

        // ---- coroutine bridge: `await someCoroutine();` runs on BugpunchClient's MonoBehaviour ----

        static void PumpCoroutine(IEnumerator ie, Action done)
        {
            var host = BugpunchClient.Instance;
            if (host == null)
            {
                // Host not booted yet: drive synchronously; yielded waits are ignored.
                try { while (ie.MoveNext()) { } }
                catch (Exception ex) { Debug.LogException(ex); }
                finally { try { done(); } catch { } }
                return;
            }
            host.StartCoroutine(PumpRoutine(ie, done));
        }

        static IEnumerator PumpRoutine(IEnumerator ie, Action done)
        {
            yield return ie;
            try { done(); } catch (Exception ex) { Debug.LogException(ex); }
        }

        // ---- Destroy guard: no script may destroy a [ScriptProtected] object, at any trust level ----

        static string ProtectedDestroyGuard(MethodInfo method, object instance, object[] args)
        {
            if (method == null || method.DeclaringType != typeof(UnityEngine.Object)) return null;
            if (method.Name != "Destroy" && method.Name != "DestroyImmediate") return null;

            if (IsProtectedTarget(instance)) return DenyMessage(instance);
            if (args != null)
                for (int i = 0; i < args.Length; i++)
                    if (IsProtectedTarget(args[i])) return DenyMessage(args[i]);
            return null;
        }

        static bool IsProtectedTarget(object target)
        {
            if (target == null) return false;
            if (ScriptProtection.IsProtected(target.GetType())) return true;
            if (target is Component c) return c != null && c.gameObject != null && GameObjectHasProtectedComponent(c.gameObject);
            if (target is GameObject go) return GameObjectHasProtectedComponent(go);
            return false;
        }

        static bool GameObjectHasProtectedComponent(GameObject go)
        {
            if (go == null) return false;
            foreach (var comp in go.GetComponents<Component>())
                if (comp != null && ScriptProtection.IsProtected(comp.GetType())) return true;
            return false;
        }

        static string DenyMessage(object target)
        {
            string label;
            if (target is GameObject g) label = g.name;
            else if (target is Component c && c != null) label = c.gameObject != null ? c.gameObject.name + "/" + c.GetType().Name : c.GetType().Name;
            else label = target?.GetType().Name ?? "(null)";
            return "Cannot destroy script-protected object: " + label;
        }

        // ---- envelopes ----

        static string FirstError(IReadOnlyList<ScriptDiagnostic> diagnostics)
        {
            if (diagnostics != null)
                foreach (var d in diagnostics)
                    if (d.Severity == DiagnosticSeverity.Error) return d.Message;
            return "unknown error";
        }

        static string BuildErrorResponse(IReadOnlyList<ScriptDiagnostic> diagnostics)
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

        static string BuildDiagnosticsResponse(IReadOnlyList<ScriptDiagnostic> diagnostics)
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

        static string SeverityName(DiagnosticSeverity s) =>
            s == DiagnosticSeverity.Error ? "error" : s == DiagnosticSeverity.Warning ? "warning" : "info";

        static void AppendLocation(StringBuilder sb, SourceLocation? loc)
        {
            if (loc.HasValue) AppendLocation(sb, loc.Value);
        }

        static void AppendLocation(StringBuilder sb, SourceLocation loc)
        {
            if (loc.Line > 0) sb.Append($"\"line\":{loc.Line},");
            if (loc.Column > 0) sb.Append($"\"column\":{loc.Column},");
            if (loc.Length > 0) sb.Append($"\"length\":{loc.Length},");
        }
    }
}
