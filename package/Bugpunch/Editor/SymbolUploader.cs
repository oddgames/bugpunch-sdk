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
    /// Symbol bytes are kept on disk throughout — the ELF parser reads only
    /// the header + note segment, and the upload streams via UploadHandlerFile
    /// with a multipart body constructed on disk. Peak managed RAM is bounded
    /// regardless of how large libil2cpp.sym.so gets (can be 400MB+).
    ///
    /// iOS symbols (dSYM) are produced by Xcode, not Unity, so this hook
    /// doesn't cover them — see Tools~/upload-ios-symbols.sh.
    /// </summary>
    public class BugpunchSymbolUploader : IPostprocessBuildWithReport
    {
        // Runs after BugpunchPostBuildHook (callbackOrder = 0) so the build
        // artifact + type DB upload happen first. Symbols are optional polish
        // on top — if they fail, the build itself is still usable.
        public int callbackOrder => 10;

        public void OnPostprocessBuild(BuildReport report)
        {
            // Unity's BuildAndRun path leaves result==Unknown during post-build hooks;
            // only bail on explicit failure. The symbols.zip presence is the real signal.
            if (report.summary.result == BuildResult.Failed ||
                report.summary.result == BuildResult.Cancelled) return;
            if (report.summary.platform != BuildTarget.Android) return;

            var config = ODDGames.Bugpunch.DeviceConnect.BugpunchConfig.Load();
            if (config == null || string.IsNullOrEmpty(config.apiKey))
            {
                Debug.Log("[Bugpunch] Skipping symbol upload — no config / API key.");
                return;
            }
            if (!config.symbolUploadEnabled)
            {
                Debug.Log("[Bugpunch] symbolUploadEnabled=false — skipping symbol upload.");
                return;
            }

            var found = new List<SymbolFile>();
            try
            {
                FindAndroidSymbols(report.summary.outputPath, found);
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
            finally
            {
                foreach (var f in found)
                {
                    if (!string.IsNullOrEmpty(f.TempPath))
                    {
                        try { if (File.Exists(f.TempPath)) File.Delete(f.TempPath); } catch { }
                    }
                }
            }
        }

        /// <summary>
        /// Metadata for one extracted symbol file. The .so contents live at
        /// TempPath on disk — never loaded into the managed heap.
        /// </summary>
        struct SymbolFile
        {
            public string BuildId;   // hex, lowercase
            public string Abi;       // "arm64-v8a", "armeabi-v7a", ...
            public string Filename;  // "libil2cpp.sym.so" etc.
            public string TempPath;  // extracted .so on local disk
            public long Size;
        }

        // ── Locate the symbols emitted by Unity for the Android build ──

        /// <summary>
        /// Unity writes one of:
        ///   &lt;outputDir&gt;/&lt;AppName&gt;-&lt;ver&gt;-v&lt;code&gt;.symbols.zip
        ///   &lt;outputDir&gt;/&lt;AppName&gt;-&lt;ver&gt;-v&lt;code&gt;-v2.symbols.zip
        /// depending on "Symbols" mode. Scan the parent directory.
        /// </summary>
        static void FindAndroidSymbols(string outputPath, List<SymbolFile> into)
        {
            if (string.IsNullOrEmpty(outputPath)) return;

            string searchDir = File.Exists(outputPath)
                ? Path.GetDirectoryName(outputPath)
                : Directory.Exists(outputPath) ? outputPath : null;
            if (searchDir == null || !Directory.Exists(searchDir)) return;

            foreach (var zip in Directory.GetFiles(searchDir, "*symbols*.zip", SearchOption.TopDirectoryOnly))
            {
                ExtractSymbolZip(zip, into);
            }
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

                // Stream-copy the zip entry straight to a temp file — never
                // materialises the full .so in managed memory.
                var tempPath = Path.Combine(Path.GetTempPath(), $"bp_sym_{Guid.NewGuid():N}.so");
                using (var es = entry.Open())
                using (var fs = File.Create(tempPath))
                {
                    es.CopyTo(fs);
                }

                string buildId = null;
                try { buildId = ElfBuildId.ReadFromFile(tempPath); }
                catch { /* treat as missing build-id below */ }

                if (string.IsNullOrEmpty(buildId))
                {
                    Debug.LogWarning($"[Bugpunch] {entry.FullName} has no GNU build-id — skipping. " +
                        "Make sure IL2CPP produced an unstripped .so.");
                    try { File.Delete(tempPath); } catch { }
                    continue;
                }

                into.Add(new SymbolFile {
                    BuildId = buildId,
                    Abi = abi,
                    Filename = Path.GetFileName(entry.FullName),
                    TempPath = tempPath,
                    Size = new FileInfo(tempPath).Length,
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
                    totalBytes += f.Size;
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

        /// <summary>
        /// Streams the upload via a disk-backed multipart body. The .so file
        /// is never loaded into Unity's managed heap — UploadHandlerFile reads
        /// straight off disk. Peak managed RAM stays bounded regardless of
        /// libil2cpp's size.
        /// </summary>
        static bool UploadOne(string baseUrl, string apiKey, SymbolFile f)
        {
            var boundary = "----BugpunchBoundary" + Guid.NewGuid().ToString("N");
            var bodyPath = Path.Combine(Path.GetTempPath(), $"bp_body_{Guid.NewGuid():N}.bin");
            try
            {
                using (var bodyFs = File.Create(bodyPath))
                {
                    WriteFormField(bodyFs, boundary, "buildId", f.BuildId);
                    WriteFormField(bodyFs, boundary, "platform", "android");
                    WriteFormField(bodyFs, boundary, "abi", f.Abi ?? "");
                    WriteFormField(bodyFs, boundary, "filename", f.Filename ?? "symbol.sym.so");
                    WriteFileHeader(bodyFs, boundary, "file", f.Filename ?? "symbol.sym.so", "application/octet-stream");
                    using (var soFs = File.OpenRead(f.TempPath))
                    {
                        soFs.CopyTo(bodyFs);
                    }
                    WriteAscii(bodyFs, $"\r\n--{boundary}--\r\n");
                }

                using var req = new UnityWebRequest($"{baseUrl}/api/symbols/upload", "POST");
                req.uploadHandler = new UploadHandlerFile(bodyPath);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", $"multipart/form-data; boundary={boundary}");
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
            finally
            {
                try { if (File.Exists(bodyPath)) File.Delete(bodyPath); } catch { }
            }
        }

        static void WriteFormField(Stream s, string boundary, string name, string value)
        {
            WriteAscii(s, $"--{boundary}\r\n");
            WriteAscii(s, $"Content-Disposition: form-data; name=\"{name}\"\r\n\r\n");
            WriteAscii(s, value);
            WriteAscii(s, "\r\n");
        }

        static void WriteFileHeader(Stream s, string boundary, string name, string filename, string contentType)
        {
            WriteAscii(s, $"--{boundary}\r\n");
            WriteAscii(s, $"Content-Disposition: form-data; name=\"{name}\"; filename=\"{filename}\"\r\n");
            WriteAscii(s, $"Content-Type: {contentType}\r\n\r\n");
        }

        static void WriteAscii(Stream s, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            s.Write(bytes, 0, bytes.Length);
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
    /// Minimal ELF parser for extracting the GNU build-ID from a 64-bit or
    /// 32-bit little-endian Android .so on disk. Supports ELF64 (arm64-v8a,
    /// x86_64) and ELF32 (armeabi-v7a, x86) — the four ABIs Unity targets.
    ///
    /// Reads only the ELF header, program header table, and PT_NOTE segments
    /// (usually &lt;1KB total). The rest of the .so is never touched.
    /// </summary>
    static class ElfBuildId
    {
        const int NT_GNU_BUILD_ID = 3;

        public static string ReadFromFile(string path)
        {
            using var fs = File.OpenRead(path);

            var ehdr = new byte[64];
            if (ReadFull(fs, ehdr, 0, 64) < 64) return null;
            if (ehdr[0] != 0x7F || ehdr[1] != 'E' || ehdr[2] != 'L' || ehdr[3] != 'F') return null;
            bool is64 = ehdr[4] == 2;
            bool isLE = ehdr[5] == 1;
            if (!isLE) return null;

            long phoff = is64 ? (long)ReadU64(ehdr, 32) : ReadU32(ehdr, 28);
            int phentsize = is64 ? ReadU16(ehdr, 54) : ReadU16(ehdr, 42);
            int phnum     = is64 ? ReadU16(ehdr, 56) : ReadU16(ehdr, 44);

            if (phoff <= 0 || phentsize <= 0 || phnum <= 0) return null;
            if (phentsize * (long)phnum > 64 * 1024) return null; // sanity

            fs.Seek(phoff, SeekOrigin.Begin);
            var ph = new byte[phentsize * phnum];
            if (ReadFull(fs, ph, 0, ph.Length) < ph.Length) return null;

            for (int i = 0; i < phnum; i++)
            {
                int off = i * phentsize;
                uint pType = ReadU32(ph, off);
                if (pType != 4 /* PT_NOTE */) continue;

                long pOffset = is64 ? (long)ReadU64(ph, off + 8)  : ReadU32(ph, off + 4);
                long pFilesz = is64 ? (long)ReadU64(ph, off + 32) : ReadU32(ph, off + 16);
                if (pOffset <= 0 || pFilesz <= 0 || pFilesz > 1024 * 1024) continue;

                fs.Seek(pOffset, SeekOrigin.Begin);
                var notes = new byte[pFilesz];
                if (ReadFull(fs, notes, 0, notes.Length) < notes.Length) continue;

                var id = ScanNotesForBuildId(notes);
                if (!string.IsNullOrEmpty(id)) return id;
            }
            return null;
        }

        static int ReadFull(Stream s, byte[] buf, int offset, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buf, offset + read, count - read);
                if (n <= 0) break;
                read += n;
            }
            return read;
        }

        static string ScanNotesForBuildId(byte[] notes)
        {
            int p = 0;
            int end = notes.Length;
            while (p + 12 <= end)
            {
                int namesz = (int)ReadU32(notes, p);
                int descsz = (int)ReadU32(notes, p + 4);
                int type   = (int)ReadU32(notes, p + 8);
                int namePad = (namesz + 3) & ~3;
                int descPad = (descsz + 3) & ~3;
                int descStart = p + 12 + namePad;
                if (type == NT_GNU_BUILD_ID && descsz > 0 && descStart + descsz <= end)
                {
                    var sb = new StringBuilder(descsz * 2);
                    for (int i = 0; i < descsz; i++)
                        sb.Append(notes[descStart + i].ToString("x2"));
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
