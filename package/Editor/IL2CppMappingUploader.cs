using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace ODDGames.Bugpunch.Editor
{
    internal static class IL2CppMappingUploader
    {
        public static void UploadIl2cppMappingIfPresent(
            string uploadBaseUrl, string apiKey,
            List<SymbolFile> files, string projectRoot,
            bool interactive, CancellationTokenSource cts)
        {
            var il2cppBuildIds = new List<string>();
            foreach (var f in files)
            {
                if (string.IsNullOrEmpty(f.Filename) || string.IsNullOrEmpty(f.BuildId)) continue;
                if (f.Filename.IndexOf("libil2cpp", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    il2cppBuildIds.Add(f.BuildId);
                }
            }
            if (il2cppBuildIds.Count == 0) return;

            string mappingPath;
            try
            {
                mappingPath = IL2CppMethodMapBuilder.BuildAndStage(projectRoot);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.SymbolUploader] IL2CPP method map build failed: {ex.Message}");
                return;
            }
            if (string.IsNullOrEmpty(mappingPath)) return;

            try
            {
                int mUploaded = 0;
                int mFailed = 0;
                int idx = 0;
                foreach (var bid in il2cppBuildIds)
                {
                    if (cts.IsCancellationRequested) break;
                    idx++;
                    if (interactive)
                    {
                        EditorUtility.DisplayCancelableProgressBar(
                            BugpunchSymbolUploader.PROGRESS_TITLE,
                            $"IL2CPP method map → {bid.Substring(0, Math.Min(12, bid.Length))}… ({idx}/{il2cppBuildIds.Count})",
                            0f);
                    }
                    if (UploadIl2cppMappingOne(uploadBaseUrl, apiKey, bid, mappingPath, cts))
                        mUploaded++;
                    else
                        mFailed++;
                }
                Debug.Log($"[Bugpunch.SymbolUploader] IL2CPP method map uploaded for {mUploaded}/{il2cppBuildIds.Count} libil2cpp build-id(s)" +
                          (mFailed > 0 ? $" ({mFailed} failed)" : "") + ".");
            }
            finally
            {
                try { if (File.Exists(mappingPath)) File.Delete(mappingPath); } catch { }
                // Always clear the IL2CPP mapping progress bar — without
                // this, an exception inside UploadIl2cppMappingOne would
                // leave the bar stuck on screen until the editor restarts.
                if (interactive) EditorUtility.ClearProgressBar();
            }
        }

        static bool UploadIl2cppMappingOne(
            string baseUrl, string apiKey, string buildId, string filePath,
            CancellationTokenSource cts)
        {
            // Same disk-staged multipart pattern as the .so upload — no
            // managed allocation, libcurl streams from disk.
            var bodyPath = StageMappingMultipart(out var boundary, buildId, filePath);
            if (bodyPath == null) return false;

            UnityWebRequest req = null;
            try
            {
                req = new UnityWebRequest($"{baseUrl}/api/symbols/il2cpp-mapping/upload", "POST");
                req.uploadHandler = new UploadHandlerFile(bodyPath);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", $"multipart/form-data; boundary={boundary}");
                req.SetRequestHeader("X-Api-Key", apiKey);
                req.SendWebRequest();

                while (!req.isDone)
                {
                    Thread.Sleep(50);
                    if (cts.IsCancellationRequested) { try { req.Abort(); } catch { } return false; }
                }

                if (req.result == UnityWebRequest.Result.Success) return true;
                Debug.LogWarning($"[Bugpunch.SymbolUploader] IL2CPP map upload failed for {buildId}: " +
                    $"{req.error} — {req.downloadHandler?.text}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch.SymbolUploader] IL2CPP map upload threw for {buildId}: {ex.Message}");
                return false;
            }
            finally
            {
                try { req?.Dispose(); } catch { }
                try { if (File.Exists(bodyPath)) File.Delete(bodyPath); } catch { }
            }
        }

        static string StageMappingMultipart(out string boundary, string buildId, string filePath)
        {
            boundary = "----BugpunchBoundary" + Guid.NewGuid().ToString("N");
            var bodyPath = Path.Combine(Path.GetTempPath(), $"bp_il2map_{Guid.NewGuid():N}.bin");
            try
            {
                using var body = new FileStream(
                    bodyPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    SymbolUploadClient.SymStagingBuffer, FileOptions.SequentialScan);
                SymbolUploadClient.WriteMpField(body, boundary, "buildId", buildId);
                SymbolUploadClient.WriteMpFileHeader(body, boundary, "file", "il2cpp_mapping.json.gz", "application/gzip");
                using (var fs = new FileStream(
                    filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    SymbolUploadClient.SymStagingBuffer, FileOptions.SequentialScan))
                {
                    fs.CopyTo(body, SymbolUploadClient.SymStagingBuffer);
                }
                SymbolUploadClient.WriteMpAscii(body, $"\r\n--{boundary}--\r\n");
                return bodyPath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch.SymbolUploader] Failed to stage IL2CPP mapping body: {ex.Message}");
                try { if (File.Exists(bodyPath)) File.Delete(bodyPath); } catch { }
                return null;
            }
        }
    }
}
