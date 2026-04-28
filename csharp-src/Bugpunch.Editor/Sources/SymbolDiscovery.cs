using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using UnityEngine;

namespace ODDGames.Bugpunch.Editor
{
    /// <summary>
    /// Metadata for one extracted symbol file. The .so contents live at
    /// TempPath on disk — never loaded into the managed heap.
    /// </summary>
    internal struct SymbolFile
    {
        public string BuildId;   // hex, lowercase
        public string Abi;       // "arm64-v8a", "armeabi-v7a", ...
        public string Filename;  // "libil2cpp.sym.so" etc.
        public string TempPath;  // extracted .so on local disk
        public long Size;
    }

    /// <summary>
    /// Progress shared between the background extract Task and the main
    /// thread's progress-bar poll. Plain fields — updates are monotonic so
    /// torn reads just show a slightly stale number.
    /// </summary>
    internal class ExtractState
    {
        public string Current = "";
        public long Bytes;
    }

    // ── Locate the symbols emitted by Unity for the Android build ──
    internal static class SymbolDiscovery
    {
        /// <summary>
        /// Two-pass discovery:
        ///   1. Unity's symbols.zip next to the build output — unstripped Unity
        ///      .so files (libunity, libil2cpp, libmain). Best-quality symbols.
        ///   2. Gradle's merged_native_libs folder under Library/Bee — picks up
        ///      every other .so that ships in the APK (third-party AAR deps
        ///      like libgma.so / libwebrtc.so / etc.) so their crash frames
        ///      resolve to function names. Stripped binaries but the dynamic
        ///      symbol table survives, which is what llvm-symbolizer needs.
        ///   Dedupe across passes by GNU build-id.
        /// </summary>
        public static void FindAndroidSymbols(
            string outputPath, string projectRoot,
            List<SymbolFile> into, CancellationToken ct, ExtractState state)
        {
            // Shared across both passes so a build-id that appears in
            // multiple symbols.zip files, under multiple ABI folders of
            // merged_native_libs, or in both sources only gets uploaded once.
            var seen = new HashSet<string>();

            // ── Pass 1: Unity's symbols.zip (unstripped Unity .so files) ──
            if (!string.IsNullOrEmpty(outputPath))
            {
                string searchDir = File.Exists(outputPath)
                    ? Path.GetDirectoryName(outputPath)
                    : Directory.Exists(outputPath) ? outputPath : null;
                if (searchDir != null && Directory.Exists(searchDir))
                {
                    foreach (var zip in Directory.GetFiles(searchDir, "*symbols*.zip", SearchOption.TopDirectoryOnly))
                    {
                        ct.ThrowIfCancellationRequested();
                        ExtractSymbolZip(zip, into, seen, ct, state);
                    }
                }
            }

            // ── Pass 2: Gradle merged_native_libs (third-party .so files) ──
            if (!string.IsNullOrEmpty(projectRoot))
            {
                ScanBuildOutputNativeLibs(projectRoot, into, seen, ct, state);
            }
        }

        /// <summary>
        /// Walks Library/Bee/Android/Prj/.../merged_native_libs/.../lib/&lt;abi&gt;/*.so.
        /// Reads each ELF's GNU build-id straight from disk (no extraction step
        /// needed — these aren't inside a zip), copies to temp so the upload
        /// pipeline owns the file lifetime even if the next build rewrites the
        /// folder. Skips any build-id already collected from symbols.zip — the
        /// symbols.zip versions are unstripped and strictly better.
        /// </summary>
        static void ScanBuildOutputNativeLibs(
            string projectRoot, List<SymbolFile> into, HashSet<string> seen,
            CancellationToken ct, ExtractState state)
        {
            var beeRoot = Path.Combine(projectRoot, "Library", "Bee", "Android", "Prj");
            if (!Directory.Exists(beeRoot)) return;

            foreach (var soPath in Directory.EnumerateFiles(beeRoot, "*.so", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                // Restrict to the merged_native_libs output — there are several
                // intermediate copies (stripped/unstripped/per-task) under Bee
                // and we want the one Gradle hands to the packaging task.
                var norm = soPath.Replace('\\', '/');
                if (!norm.Contains("/merged_native_libs/")) continue;

                var fname = Path.GetFileName(soPath);
                if (state != null) { state.Current = fname; state.Bytes = 0; }

                string buildId;
                try { buildId = ElfBuildId.ReadFromFile(soPath); }
                catch { continue; }
                if (string.IsNullOrEmpty(buildId)) continue;
                if (!seen.Add(buildId)) continue; // already covered by symbols.zip

                var tempPath = Path.Combine(Path.GetTempPath(), $"bp_sym_{Guid.NewGuid():N}.so");
                try { File.Copy(soPath, tempPath, overwrite: false); }
                catch { seen.Remove(buildId); continue; }

                into.Add(new SymbolFile
                {
                    BuildId = buildId,
                    Abi = GuessAbiFromPath(soPath),
                    Filename = fname,
                    TempPath = tempPath,
                    Size = new FileInfo(tempPath).Length,
                });
            }
        }

        static string GuessAbiFromPath(string fullPath)
        {
            // Path ends like ".../merged_native_libs/<variant>/.../out/lib/<abi>/<file>.so"
            var parts = fullPath.Replace('\\', '/').Split('/');
            for (int i = parts.Length - 2; i > 0; i--)
            {
                if (parts[i - 1] == "lib") return parts[i];
            }
            return "";
        }

        static void ExtractSymbolZip(
            string zipPath, List<SymbolFile> into, HashSet<string> seen,
            CancellationToken ct, ExtractState state)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                // Entries look like: <abi>/libunity.sym.so, <abi>/libil2cpp.sym.so, <abi>/libmain.sym.so
                // Some Unity versions omit the .sym prefix. Accept any .so we find.
                if (!entry.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase)) continue;
                var abi = GuessAbi(entry.FullName);
                if (state != null) { state.Current = entry.FullName; state.Bytes = 0; }

                // Stream-copy the zip entry straight to a temp file — never
                // materialises the full .so in managed memory. Chunked so
                // cancellation is observed within ~1MB of work.
                var tempPath = Path.Combine(Path.GetTempPath(), $"bp_sym_{Guid.NewGuid():N}.so");
                try
                {
                    using (var es = entry.Open())
                    using (var fs = File.Create(tempPath))
                    {
                        var buf = new byte[1 << 20];
                        int n;
                        while ((n = es.Read(buf, 0, buf.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            fs.Write(buf, 0, n);
                            if (state != null) state.Bytes += n;
                        }
                    }
                }
                catch
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    throw;
                }

                string buildId = null;
                try { buildId = ElfBuildId.ReadFromFile(tempPath); }
                catch { /* treat as missing build-id below */ }

                if (string.IsNullOrEmpty(buildId))
                {
                    // Lib was linked without --build-id (or had its .note.gnu.build-id
                    // section stripped). Not necessarily a bug — some third-party
                    // prebuilts ship this way. Either way the server can't index it,
                    // because crash reports identify modules by build-id. Frames in
                    // this lib will stay unresolved on the dashboard.
                    BugpunchLog.Info("SymbolUploader", $"{entry.FullName} has no GNU build-id — can't be symbolicated, skipping. " +
                        "Common for vendor-prebuilt libs (e.g. libwebrtc, libjingle); harmless unless you crash inside it.");
                    try { File.Delete(tempPath); } catch { }
                    continue;
                }

                // Same build-id already collected (same zip re-scanned, or
                // duplicate entry across variant zips Unity sometimes emits).
                if (!seen.Add(buildId))
                {
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
    }
}
