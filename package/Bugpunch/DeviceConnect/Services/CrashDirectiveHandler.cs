using System;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Managed-side bridge for crash directive work that genuinely requires
    /// the Mono/IL2CPP runtime: executing PaxScript against the running
    /// game's assemblies. Everything else — fetching directives from the
    /// server, caching them, matching upload-queue entries by fingerprint,
    /// globbing attachment files, showing the post-upload "help us fix this"
    /// dialog, and persisting per-fingerprint denial state — lives natively
    /// (Java on Android, Obj-C++ on iOS) next to the existing crash handler
    /// and upload queue.
    ///
    /// Native invokes <see cref="RunPaxScript"/> via
    /// <c>UnitySendMessage("BugpunchClient", "DirectiveRunPaxScript", json)</c>
    /// when a queued crash has a <c>run_paxscript</c> action. The result is
    /// posted back through <see cref="BugpunchNative.PostPaxScriptResult"/>.
    /// </summary>
    public class CrashDirectiveHandler : MonoBehaviour
    {
        public static CrashDirectiveHandler Instance { get; private set; }

        public void Init()
        {
            Instance = this;
        }

        /// <summary>
        /// Invoked from native via UnitySendMessage. Payload shape:
        /// <c>{"directiveId":"...","code":"...","timeoutMs":2000}</c>.
        /// </summary>
        public void RunPaxScript(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return;
            string directiveId = ExtractJsonString(payload, "directiveId");
            string code = ExtractJsonString(payload, "code");
            if (string.IsNullOrEmpty(directiveId) || string.IsNullOrEmpty(code))
            {
                BugpunchNative.PostPaxScriptResult(directiveId ?? "",
                    "{\"ok\":false,\"errors\":[\"missing directiveId or code\"]}");
                return;
            }

            // Every failure mode — runner construction, assembly registration,
            // compile errors, runtime throws — is flattened into a single JSON
            // envelope with ok=false. Native treats this as a directive error
            // and files a companion event under the same crash group so the
            // failure is visible in the dashboard alongside the original
            // crash (rather than silently disappearing).
            string result;
            try
            {
                result = new PaxScriptRunner().Execute(code);
                if (string.IsNullOrEmpty(result))
                    result = "{\"ok\":false,\"errors\":[\"paxscript runner returned empty\"]}";
            }
            catch (Exception e)
            {
                var sb = new StringBuilder(256);
                sb.Append("{\"ok\":false,\"exceptionType\":\"").Append(Escape(e.GetType().FullName ?? "Exception")).Append("\",");
                sb.Append("\"errors\":[\"").Append(Escape(e.Message ?? "(no message)")).Append("\"],");
                sb.Append("\"stackTrace\":\"").Append(Escape(e.StackTrace ?? "")).Append("\"}");
                result = sb.ToString();
            }
            BugpunchNative.PostPaxScriptResult(directiveId, result);
        }

        // Minimal JSON string extractor — avoids pulling in a parser just to
        // read two fields from a native-emitted payload with known shape.
        static string ExtractJsonString(string json, string key)
        {
            var needle = "\"" + key + "\":\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i++];
                if (c == '\\' && i < json.Length)
                {
                    char esc = json[i++];
                    switch (esc)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        default: sb.Append(esc); break;
                    }
                    continue;
                }
                if (c == '"') return sb.ToString();
                sb.Append(c);
            }
            return null;
        }

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
