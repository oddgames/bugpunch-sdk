using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Walks Library/Bee/.../il2cppOutput/cpp/ after a successful build,
    /// extracts a {method_name → cs_file:cs_line} mapping by pairing each
    /// IL2CPP-emitted method with the FIRST `//&lt;source_info:File.cs:Line&gt;`
    /// comment inside (or just above) its body. Output is gzipped JSON,
    /// uploaded to the server alongside the .so symbols and used at crash
    /// time to enrich nearest-symbol-table hits with C# source info.
    ///
    /// Tradeoff vs. full DWARF: we get the method's STARTING source line,
    /// not the exact line the crash happened on. Full per-instruction
    /// resolution requires Player Settings → Symbols = Debugging (multi-GB
    /// libil2cpp.dbg.so), which the symbol upload pipeline currently
    /// rejects to keep uploads tractable.
    /// </summary>
    public static class IL2CppMethodMapBuilder
    {
        // Matches the comment IL2CPP emits when --emit-source-mapping is on.
        // Format: //<source_info:relative/path/Foo.cs:42>
        // Path may contain forward or back slashes depending on OS.
        static readonly Regex s_sourceInfoRegex = new(
            @"//<source_info:([^:>]+):(\d+)>",
            RegexOptions.Compiled);

        // IL2CPP method definition signature. Examples:
        //   IL2CPP_EXTERN_C IL2CPP_METHOD_ATTR void Foo_Bar_m12345...(...)
        //   extern "C" IL2CPP_METHOD_ATTR ReturnType Class_Method_m12345(...)
        // The mangled name always ends in `_m<HEX>` where HEX is the IL2CPP
        // method id. We anchor on that so we don't accidentally pick up
        // forward declarations or unrelated lines.
        static readonly Regex s_methodDefRegex = new(
            @"\b([A-Za-z_][A-Za-z0-9_]*_m[0-9A-F]{6,})\s*\(",
            RegexOptions.Compiled);

        /// <summary>
        /// Returns the path of the gzipped JSON written, or null if no
        /// IL2CPP cpp output was found / no methods were mapped.
        /// </summary>
        public static string BuildAndStage(string projectRoot)
        {
            if (string.IsNullOrEmpty(projectRoot)) return null;
            var cppDir = Path.Combine(projectRoot, "Library", "Bee", "artifacts", "Android", "il2cppOutput", "cpp");
            if (!Directory.Exists(cppDir))
            {
                // iOS / Standalone use different paths under il2cppOutput; for
                // now we only ship the Android path. Keep silent — the upload
                // pipeline is platform-aware and won't ask for this on other
                // platforms.
                return null;
            }

            var map = new Dictionary<string, (string file, int line)>(capacity: 16384);
            int filesScanned = 0;

            foreach (var cppPath in Directory.EnumerateFiles(cppDir, "*.cpp", SearchOption.TopDirectoryOnly))
            {
                filesScanned++;
                ScanFile(cppPath, map);
            }

            if (map.Count == 0)
            {
                Debug.Log($"[Bugpunch.IL2CppMethodMapBuilder] IL2CPP method map: scanned {filesScanned} cpp files but found 0 source_info markers. " +
                          "Was --emit-source-mapping applied? It takes effect on the NEXT build after enabling.");
                return null;
            }

            var outPath = Path.Combine(Path.GetTempPath(), $"bp_il2cpp_mapping_{Guid.NewGuid():N}.json.gz");
            try
            {
                using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var gz = new GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal);
                using var w = new StreamWriter(gz, new UTF8Encoding(false));
                WriteJson(w, map);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.IL2CppMethodMapBuilder] Failed to stage IL2CPP method map: {ex.Message}");
                try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                return null;
            }

            var sizeKb = new FileInfo(outPath).Length / 1024.0;
            Debug.Log($"[Bugpunch.IL2CppMethodMapBuilder] IL2CPP method map: {map.Count} methods from {filesScanned} cpp files, {sizeKb:F1} KB gzipped.");
            return outPath;
        }

        // Single-pass scan: read line by line, remember the most recent
        // method-def name; when a source_info marker is seen, attribute it
        // to the current method (only if not already mapped — first wins).
        static void ScanFile(string path, Dictionary<string, (string file, int line)> map)
        {
            string currentMethod = null;
            try
            {
                using var sr = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    // Cheap pre-filter — most lines aren't interesting.
                    var siIdx = line.IndexOf("//<source_info:", StringComparison.Ordinal);
                    var hasMethodCue = line.IndexOf("_m", StringComparison.Ordinal) >= 0;

                    if (hasMethodCue)
                    {
                        var m = s_methodDefRegex.Match(line);
                        if (m.Success)
                        {
                            // New method begins — wipe slate so we don't carry
                            // a previous method's first-seen source_info into
                            // this one.
                            currentMethod = m.Groups[1].Value;
                        }
                    }

                    if (siIdx >= 0 && currentMethod != null && !map.ContainsKey(currentMethod))
                    {
                        var mm = s_sourceInfoRegex.Match(line, siIdx);
                        if (mm.Success && int.TryParse(mm.Groups[2].Value, out var ln))
                        {
                            // Normalise Windows-style backslashes the user's
                            // C# files might end up referenced with.
                            var file = mm.Groups[1].Value.Replace('\\', '/');
                            map[currentMethod] = (file, ln);
                            // Don't reset currentMethod — methods may have
                            // multiple source_info markers; we only keep the
                            // first per method.
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.IL2CppMethodMapBuilder] IL2CPP cpp scan failed for {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        // Hand-rolled JSON writer to avoid dragging Newtonsoft into Editor
        // assemblies that may not reference it. Simple flat object:
        //   { "Method_m1234": ["Foo.cs", 17], ... }
        static void WriteJson(TextWriter w, Dictionary<string, (string file, int line)> map)
        {
            w.Write("{");
            bool first = true;
            foreach (var kv in map)
            {
                if (!first) w.Write(",");
                first = false;
                w.Write("\"");
                JsonEscape(w, kv.Key);
                w.Write("\":[\"");
                JsonEscape(w, kv.Value.file);
                w.Write("\",");
                w.Write(kv.Value.line);
                w.Write("]");
            }
            w.Write("}");
        }

        static void JsonEscape(TextWriter w, string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                switch (c)
                {
                    case '"':  w.Write("\\\""); break;
                    case '\\': w.Write("\\\\"); break;
                    case '\n': w.Write("\\n"); break;
                    case '\r': w.Write("\\r"); break;
                    case '\t': w.Write("\\t"); break;
                    default:
                        if (c < 0x20) w.Write($"\\u{(int)c:X4}");
                        else w.Write(c);
                        break;
                }
            }
        }
    }
}
