using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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

        // Test scripts are the one Admin surface that must reach the SDK's own testing API,
        // so the default ODDGames.BugpunchSdk denial (which keeps chat-scripts out) is lifted.
        public string CompileTest(string code, ITestLineHook hook, out Func<object> evaluate)
        {
            evaluate = null;
            try
            {
                var options = new ScriptCompileOptions { TrustLevel = ScriptTrustLevel.Admin, DebugHooks = hook != null };
                options.DeniedNamespacePrefixes.Remove("ODDGames.BugpunchSdk");
                bool wrapped = !DeclaresClass(code);
                if (!ScriptCompiler.TryCompile(wrapped ? WrapTestBody(code) : code, out var script, options) || script == null)
                    return BuildErrorResponse(script?.Diagnostics);
                var debugHook = hook == null ? null : new TestDebugHook(hook, wrapped);
                evaluate = () =>
                {
                    // Attach is per thread and an async script keeps the hook across its awaits, so it only
                    // has to be in place while the script starts.
                    using (debugHook == null ? null : ScriptDebugger.Attach(debugHook))
                    using (MethodInvocationGuard.EnterTrustLevel(ScriptTrustLevel.Admin))
                        return script.Evaluate();
                };
                return null;
            }
            catch (Exception ex)
            {
                return $"{{\"ok\":false,\"errors\":[{DescribeError(ex)}]}}";
            }
        }

        static bool DeclaresClass(string code) =>
            Regex.IsMatch(code, @"^\s*(public\s+|static\s+|sealed\s+)*class\s", RegexOptions.Multiline);

        // The VM lowers `await` only inside class methods, so a plain top-level test body is moved
        // into one. The header joins the last leading `using` line and the footer goes on a new
        // final line, so the VM's lines are the author's. A script that declares its own class is
        // compiled as written and must end with an expression yielding its Task.
        const string TestClass = "__BugpunchTest";
        const string TestMethod = "Run";

        static string WrapTestBody(string code)
        {
            var lines = code.Split('\n');
            int lastUsing = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i].Trim();
                if (t.Length == 0 || t.StartsWith("//")) continue;
                if (t.StartsWith("using ") && t.EndsWith(";")) { lastUsing = i; continue; }
                break;
            }

            const string header = "class " + TestClass + " { public async System.Threading.Tasks.Task<object> " + TestMethod + "() { ";
            const string footer = "\nreturn null; } } new " + TestClass + "()." + TestMethod + "();";
            if (lastUsing < 0) return header + code + footer;
            lines[lastUsing] = lines[lastUsing].TrimEnd('\r') + " " + header;
            return string.Join("\n", lines) + footer;
        }

        /// <summary>
        /// Hands the VM's statement and fault reports for the test body to the run's <see cref="ITestLineHook"/>.
        /// Helpers the body calls stay quiet (step over), and their faults are deferred to the body line that
        /// called them, where the run can park. A script with its own class reports every frame.
        /// </summary>
        sealed class TestDebugHook : IScriptDebugHook
        {
            const int MaxValueChars = 200;
            readonly ITestLineHook _hook;
            readonly bool _wrapped;
            // One locals reader per frame rather than a closure per statement.
            ScriptDebugFrame _localsFrame;
            Func<string> _locals;

            public TestDebugHook(ITestLineHook hook, bool wrapped)
            {
                _hook = hook;
                _wrapped = wrapped;
            }

            bool IsBody(ScriptDebugFrame f) => !_wrapped || (f.TypeName == TestClass && f.MethodName == TestMethod);

            public System.Threading.Tasks.Task OnStatement(ScriptDebugFrame frame)
            {
                if (!IsBody(frame)) return System.Threading.Tasks.Task.CompletedTask;
                if (frame != _localsFrame)
                {
                    _localsFrame = frame;
                    _locals = () => LocalsJson(frame);
                }
                return _hook.OnLine(frame.Line, frame.CanPause, _locals);
            }

            public async System.Threading.Tasks.Task<ScriptFaultAction> OnFault(ScriptDebugFrame frame, Exception exception)
            {
                if (!IsBody(frame)) return ScriptFaultAction.Defer;
                var action = await _hook.OnFault(frame.Line, exception, frame.CanPause, () => LocalsJson(frame));
                return (ScriptFaultAction)(int)action;
            }

            static string LocalsJson(ScriptDebugFrame frame)
            {
                var sb = new StringBuilder("{");
                bool first = true;
                foreach (var kv in frame.GetLocals())
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(BugpunchJson.Esc(kv.Key)).Append("\":");
                    if (kv.Value == null) { sb.Append("null"); continue; }
                    string text;
                    try { text = kv.Value.ToString() ?? ""; }
                    catch (Exception e) { text = "<" + e.GetType().Name + ">"; }
                    if (text.Length > MaxValueChars) text = text.Substring(0, MaxValueChars) + "…";
                    sb.Append('"').Append(BugpunchJson.Esc(text)).Append('"');
                }
                return sb.Append('}').ToString();
            }
        }

        public string DescribeError(Exception ex)
        {
            var sb = new StringBuilder("{");
            for (var e = ex; e != null; e = e.InnerException)
                if (e is ScriptRuntimeException rex) { AppendLocation(sb, rex.Location); break; }
            sb.Append($"\"message\":\"{BugpunchJson.Esc(ex.Message)}\"}}");
            return sb.ToString();
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
