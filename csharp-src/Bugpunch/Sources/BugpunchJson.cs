using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Centralized JSON string escape — single source of truth, backed by
    /// Newtonsoft.Json. Use this instead of rolling a private
    /// <c>static string Esc(string)</c> at the bottom of every service file.
    ///
    /// <para><b>Performance notes</b> — most calls escape short, ASCII,
    /// alphanumeric strings (component names, GameObject paths, scene
    /// names). The fast path detects this and skips both the JSON writer
    /// and the StringBuilder copy entirely:</para>
    /// <list type="bullet">
    /// <item><description><see cref="Esc"/> returns the input string
    /// unchanged when no character needs escaping — zero allocation.</description></item>
    /// <item><description><see cref="EscInto"/> appends straight to the
    /// caller's <see cref="StringBuilder"/> when no escape is needed —
    /// also zero allocation.</description></item>
    /// <item><description>The slow path uses <see cref="JsonTextWriter"/>
    /// writing directly into the target <see cref="StringBuilder"/> via
    /// <see cref="StringWriter"/>, so we never allocate the wrapped
    /// <c>"..."</c> intermediate that <see cref="JsonConvert.ToString(string)"/>
    /// would produce. The two surrounding quotes the writer emits are
    /// trimmed in place.</description></item>
    /// </list>
    /// <para>Default Newtonsoft settings are already optimal for this
    /// path: <c>StringEscapeHandling.Default</c> is the fastest mode
    /// (it does not escape non-ASCII or HTML-sensitive characters).</para>
    /// </summary>
    public static class BugpunchJson
    {
        /// <summary>
        /// Escape <paramref name="s"/> as a JSON string fragment WITHOUT
        /// surrounding quotes. Caller wraps with <c>"..."</c> when
        /// assembling JSON via <see cref="StringBuilder"/>. Returns the
        /// input string unchanged if no escape is needed (fast path,
        /// zero allocation).
        /// </summary>
        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (!NeedsEscape(s)) return s;

            var sb = new StringBuilder(s.Length + 8);
            EscapeIntoSlow(sb, s);
            return sb.ToString();
        }

        /// <summary>
        /// Append the escaped form of <paramref name="s"/> to
        /// <paramref name="sb"/> — no surrounding quotes, no
        /// intermediate allocation. Preferred over <see cref="Esc"/>
        /// when you're already mid-build.
        /// </summary>
        public static void EscInto(StringBuilder sb, string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (!NeedsEscape(s)) { sb.Append(s); return; }
            EscapeIntoSlow(sb, s);
        }

        /// <summary>
        /// Returns <c>"escaped"</c> with surrounding double quotes —
        /// equivalent to <see cref="JsonConvert.ToString(string)"/> but
        /// null-safe (<c>null</c> becomes <c>""</c>, not <c>"null"</c>).
        /// Use when emitting a complete JSON string value.
        /// </summary>
        public static string ToQuoted(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            if (!NeedsEscape(s)) return "\"" + s + "\"";
            return JsonConvert.ToString(s);
        }

        // Slow path: drive Newtonsoft's writer straight into the caller's
        // StringBuilder so the only allocations are JsonTextWriter's small
        // internal buffers. The writer always wraps the value in quotes;
        // we strip those after the write so callers can keep their
        // existing `"key":"<esc>"` shape.
        static void EscapeIntoSlow(StringBuilder sb, string s)
        {
            int startPos = sb.Length;
            using (var sw = new StringWriter(sb))
            using (var writer = new JsonTextWriter(sw))
            {
                writer.WriteValue(s);
            }
            // Trim the trailing quote first (cheap — last char) then the
            // leading one. Two single-char Remove calls on a StringBuilder
            // are O(n) for the leading one but only over the string we
            // just wrote, which is the bound we already paid to write it.
            sb.Remove(sb.Length - 1, 1);
            sb.Remove(startPos, 1);
        }

        // Fast-path detector. Returns true if the string contains any
        // character that <see cref="StringEscapeHandling.Default"/> would
        // need to escape: backslash, double-quote, or any control char
        // below 0x20. We don't escape DEL or non-ASCII because Default
        // mode passes those through verbatim — that's the contract.
        static bool NeedsEscape(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < 0x20 || c == '\\' || c == '"') return true;
            }
            return false;
        }
    }
}
