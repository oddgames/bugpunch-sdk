using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Post-build hook: for Android IL2CPP builds, finds the unstripped .so
    /// files Unity emits (when Player Settings → Publishing Settings →
    /// Symbols is enabled), reads each library's GNU build-ID, asks the
    /// server which it's missing, uploads only those.
    ///
    /// Server consumes the symbols later for crash symbolication — each
    /// crash frame's PC is matched to a library by build-ID (see the
    /// ---BUILD_IDS--- section written by <c>bp.c</c>), the corresponding
    /// .sym.so is fetched from the symbol store, and llvm-symbolizer
    /// resolves to source:line.
    ///
    /// iOS symbols (dSYM) are produced by Xcode, not Unity, so this hook
    /// doesn't cover them. A CI-side uploader (post-archive) is the
    /// appropriate place — out of scope here.
    /// </summary>
    public class BugpunchSymbolUploader : IPostprocessBuildWithReport
    {
        // Runs after BugpunchPostBuildHook (callbackOrder = 0) so the build
        // artifact + type DB upload happen first. Symbols are optional polish
        // on top — if they fail, the build itself is still usable.
        public int callbackOrder => 10;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.result != BuildResult.Succeeded) return;
            if (report.summary.platform != BuildTarget.Android) return;

            var config = ODDGames.Bugpunch.DeviceConnect.BugpunchConfig.Load();
            if (config == null || string.IsNullOrEmpty(config.apiKey))
            {
                Debug.Log("[Bugpunch] Skipping symbol upload — no config / API key.");
                return;
            }
            // Feature-flagged off until server-side storage + RAM budget are
            // resolved (bugpunch-server#208, #209). Keep the code intact so
            // flipping the flag re-enables without further work.
            if (!config.symbolUploadEnabled)
            {
                Debug.Log("[Bugpunch] symbolUploadEnabled=false — skipping symbol upload.");
                return;
            }

            try
            {
                var found = FindAndroidSymbols(report.summary.outputPath);
                if (found.Count == 0)
                {
                    Debug.Log("[Bugpunch] No Android symbol files found. " +
                        "Enable Player Settings → Publishing Settings → Symbols " +
                        "(Public/Debugging) to produce symbols.zip alongside builds.");
                    return;
                }
                UploadSymbols(config, found);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch] Symbol upload failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// A single extracted symbol file, kept in memory until we know
        /// whether the server wants it. Unity's symbols.zip is typically
        /// 200–500MB total; we fan each entry out lazily.
        /// </summary>
        struct SymbolFile
        {
            public string BuildId;   // hex, lowercase
            public string Abi;       // "arm64-v8a", "armeabi-v7a", ...
            public string Filename;  // "libil2cpp.sym.so" etc.
            public byte[] Bytes;     // full .so contents
        }

        // ── Locate the symbols emitted by Unity for the Android build ──

        /// <summary>
        /// Unity writes one of:
        ///   &lt;outputDir&gt;/&lt;AppName&gt;-&lt;ver&gt;-v&lt;code&gt;.symbols.zip
        ///   &lt;outputDir&gt;/&lt;AppName&gt;-&lt;ver&gt;-v&lt;code&gt;-v2.symbols.zip
        /// depending on "Symbols" mode. Scan the parent directory.
        /// </summary>
        static List<SymbolFile> FindAndroidSymbols(string outputPath)
        {
            var result = new List<SymbolFile>();
            if (string.IsNullOrEmpty(outputPath)) return result;

            string searchDir = File.Exists(outputPath)
                ? Path.GetDirectoryName(outputPath)
                : Directory.Exists(outputPath) ? outputPath : null;
            if (searchDir == null || !Directory.Exists(searchDir)) return result;

            foreach (var zip in Directory.GetFiles(searchDir, "*symbols*.zip", SearchOption.TopDirectoryOnly))
            {
                ExtractSymbolZip(zip, result);
            }
            return result;
        }

        static void ExtractSymbolZip(string zipPath, List<SymbolFile> into)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                // Entries look like: <abi>/libunity.sym.so, <abi>/libil2cpp.sym.so, <abi>/libmain.sym.so
                // Some Unity versions omit the .sym prefix. Accept any .so we find.
                if (!entry.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase)) continue;
                var abi = GuessAbi(entry.FullName);
                using var es = entry.Open();
                using var ms = new MemoryStream();
                es.CopyTo(ms);
                var bytes = ms.ToArray();
                var buildId = ElfBuildId.Read(bytes);
                if (string.IsNullOrEmpty(buildId))
                {
                    Debug.LogWarning($"[Bugpunch] {entry.FullName} has no GNU build-id — skipping. " +
                        "Make sure IL2CPP produced an unstripped .so.");
                    continue;
                }
                into.Add(new SymbolFile {
                    BuildId = buildId,
                    Abi = abi,
                    Filename = Path.GetFileName(entry.FullName),
                    Bytes = bytes,
                });
            }
        }

        static string GuessAbi(string fullName)
        {
            // Entry path like "arm64-v8a/libil2cpp.sym.so". First segment is ABI.
            var i = fullName.IndexOfAny(new[] { '/', '\\' });
            if (i <= 0) return "";
            return fullName.Substring(0, i);
        }

        // ── Server round-trip: check then upload missing ──

        static void UploadSymbols(
            ODDGames.Bugpunch.DeviceConnect.BugpunchConfig config,
            List<SymbolFile> files)
        {
            var baseUrl = NormalizeBaseUrl(config.serverUrl);
            var missing = QueryMissing(baseUrl, config.apiKey, files);
            if (missing.Count == 0)
            {
                Debug.Log($"[Bugpunch] Symbol store already has all {files.Count} files. Nothing to upload.");
                return;
            }

            int uploaded = 0;
            long totalBytes = 0;
            foreach (var f in files)
            {
                if (!missing.Contains(f.BuildId)) continue;
                if (UploadOne(baseUrl, config.apiKey, f))
                {
                    uploaded++;
                    totalBytes += f.Bytes.LongLength;
                }
            }
            Debug.Log($"[Bugpunch] Uploaded {uploaded}/{missing.Count} missing symbol files " +
                $"({totalBytes / 1024 / 1024}MB). {files.Count - missing.Count} already on server.");
        }

        static HashSet<string> QueryMissing(
            string baseUrl, string apiKey, List<SymbolFile> files)
        {
            // Build { items: [{ buildId, abi, filename }, ...] } manually to
            // avoid a JSON dependency — fields are all simple strings.
            var sb = new StringBuilder();
            sb.Append("{\"items\":[");
            for (int i = 0; i < files.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"buildId\":\"").Append(files[i].BuildId).Append('"')
                  .Append(",\"abi\":\"").Append(JsonEscape(files[i].Abi)).Append('"')
                  .Append(",\"filename\":\"").Append(JsonEscape(files[i].Filename)).Append('"')
                  .Append(",\"platform\":\"android\"}");
            }
            sb.Append("]}");

            using var req = new UnityWebRequest($"{baseUrl}/api/symbols/check", "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(sb.ToString()));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-Api-Key", apiKey);
            var op = req.SendWebRequest();
            while (!op.isDone) { }

            if (req.result != UnityWebRequest.Result.Success)
            {
                throw new Exception($"/api/symbols/check failed: {req.error} — {req.downloadHandler?.text}");
            }

            // Parse { "missing": ["id1", "id2", ...] } with a tiny scanner.
            var body = req.downloadHandler.text ?? "";
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int start = body.IndexOf("\"missing\"", StringComparison.Ordinal);
            if (start < 0) return result;
            int lb = body.IndexOf('[', start);
            int rb = body.IndexOf(']', lb + 1);
            if (lb < 0 || rb < 0) return result;
            foreach (var piece in body.Substring(lb + 1, rb - lb - 1).Split(','))
            {
                var clean = piece.Trim().Trim('"');
                if (clean.Length > 0) result.Add(clean.ToLowerInvariant());
            }
            return result;
        }

        static bool UploadOne(string baseUrl, string apiKey, SymbolFile f)
        {
            var form = new WWWForm();
            form.AddField("buildId", f.BuildId);
            form.AddField("platform", "android");
            form.AddField("abi", f.Abi ?? "");
            form.AddField("filename", f.Filename ?? "symbol.sym.so");
            form.AddBinaryData("file", f.Bytes, f.Filename, "application/octet-stream");

            using var req = UnityWebRequest.Post($"{baseUrl}/api/symbols/upload", form);
            req.SetRequestHeader("X-Api-Key", apiKey);
            var op = req.SendWebRequest();
            while (!op.isDone) { }

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Bugpunch] Symbol upload failed for {f.Filename} " +
                    $"({f.BuildId}): {req.error} — {req.downloadHandler?.text}");
                return false;
            }
            return true;
        }

        static string NormalizeBaseUrl(string url)
        {
            url = (url ?? "").TrimEnd('/');
            if (url.StartsWith("ws://"))  url = "http://" + url.Substring(5);
            if (url.StartsWith("wss://")) url = "https://" + url.Substring(6);
            return url;
        }

        static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Minimal ELF parser for extracting the GNU build-ID from a 64-bit
    /// Android .so in memory. Supports ELF64 little-endian (arm64-v8a,
    /// x86_64) and ELF32 little-endian (armeabi-v7a, x86) — the four ABIs
    /// Unity actually targets.
    /// </summary>
    static class ElfBuildId
    {
        const int NT_GNU_BUILD_ID = 3;

        public static string Read(byte[] elf)
        {
            if (elf == null || elf.Length < 64) return null;
            if (elf[0] != 0x7F || elf[1] != 'E' || elf[2] != 'L' || elf[3] != 'F') return null;
            bool is64 = elf[4] == 2;
            bool isLE = elf[5] == 1;
            if (!isLE) return null;

            int phoffSize = is64 ? 8 : 4;
            int phoff = is64
                ? (int)ReadU64(elf, 32)
                : (int)ReadU32(elf, 28);
            int phentsize = is64 ? ReadU16(elf, 54) : ReadU16(elf, 42);
            int phnum     = is64 ? ReadU16(elf, 56) : ReadU16(elf, 44);

            if (phoff <= 0 || phentsize <= 0 || phnum <= 0) return null;

            for (int i = 0; i < phnum; i++)
            {
                int off = phoff + i * phentsize;
                if (off + phentsize > elf.Length) break;
                uint pType = ReadU32(elf, off);
                if (pType != 4 /* PT_NOTE */) continue;

                int pOffset = is64
                    ? (int)ReadU64(elf, off + 8)
                    : (int)ReadU32(elf, off + 4);
                int pFilesz = is64
                    ? (int)ReadU64(elf, off + 32)
                    : (int)ReadU32(elf, off + 16);

                var hex = ScanNotesForBuildId(elf, pOffset, pFilesz);
                if (hex != null) return hex;
            }
            return null;
        }

        static string ScanNotesForBuildId(byte[] elf, int start, int length)
        {
            int p = start;
            int end = Math.Min(start + length, elf.Length);
            while (p + 12 <= end)
            {
                int namesz = (int)ReadU32(elf, p);
                int descsz = (int)ReadU32(elf, p + 4);
                int type   = (int)ReadU32(elf, p + 8);
                int namePad = (namesz + 3) & ~3;
                int descPad = (descsz + 3) & ~3;
                int descStart = p + 12 + namePad;
                if (type == NT_GNU_BUILD_ID && descsz > 0 && descStart + descsz <= end)
                {
                    var sb = new StringBuilder(descsz * 2);
                    for (int i = 0; i < descsz; i++)
                        sb.Append(elf[descStart + i].ToString("x2"));
                    return sb.ToString();
                }
                p = descStart + descPad;
            }
            return null;
        }

        static ushort ReadU16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));
        static uint ReadU32(byte[] b, int o) =>
            (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        static ulong ReadU64(byte[] b, int o) =>
            ReadU32(b, o) | ((ulong)ReadU32(b, o + 4) << 32);
    }
}
