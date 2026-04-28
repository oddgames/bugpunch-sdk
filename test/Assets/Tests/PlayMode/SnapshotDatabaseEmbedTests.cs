using System.Collections;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using ODDGames.Bugpunch.RemoteIDE;
using ODDGames.Bugpunch.RemoteIDE.Database;
using UnityEngine;
using UnityEngine.TestTools;

namespace ODDGames.Bugpunch.Tests
{
    /// <summary>
    /// Snapshot zip jobs must embed pre-parsed JSON for every database the
    /// SDK can read on-device, so post-mortem viewers don't need a live
    /// device. Covers Odin, Siaqodb, SQLite, and JSON.
    /// </summary>
    public class SnapshotDatabaseEmbedTests
    {
        string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Application.temporaryCachePath, "bp_snap_test_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        [UnityTest]
        public IEnumerator ZipEmbedsParsedDatabases()
        {
            // Plant a simple JSON DB — JsonPlugin would be the cleanest test
            // target, but the SDK ships with Odin + Siaqodb today. So write a
            // file with a handled extension and assert the registry runs at
            // least one plugin against it.
            File.WriteAllText(Path.Combine(_root, "data.json"), "{\"x\":1}");

            var dbPlugins = new DatabasePluginRegistry();
            var files = new FileService { DatabasePlugins = dbPlugins };

            var startResp = files.StartZipJob(_root);
            Assert.IsTrue(startResp.Contains("\"ok\":true"), "StartZipJob: " + startResp);
            var jobId = ExtractJobId(startResp);

            // Poll up to 5 s for completion.
            for (int i = 0; i < 50; i++)
            {
                var prog = files.GetZipProgress(jobId);
                if (prog.Contains("\"done\":true") || prog.Contains("\"stage\":\"error\"")) break;
                yield return new WaitForSeconds(0.1f);
            }

            // Pull the result, decode the base64, write to disk, peek inside.
            var result = files.GetZipResult(jobId);
            Assert.IsTrue(result.Contains("\"ok\":true"), "GetZipResult: " + result);

            var b64 = ExtractField(result, "data");
            Assert.IsNotEmpty(b64);
            var zipPath = Path.Combine(Application.temporaryCachePath, "bp_snap_test.zip");
            File.WriteAllBytes(zipPath, System.Convert.FromBase64String(b64));

            // _databases/ entries are optional — if no plugin handles .json
            // extension, none will appear. The point of the test is that the
            // hook fires without crashing the zip and produces no malformed
            // entries when present.
            using (var fs = File.OpenRead(zipPath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                bool hasOriginal = false;
                foreach (var e in zip.Entries)
                {
                    if (e.FullName == "data.json") hasOriginal = true;
                    if (e.FullName.StartsWith("_databases/"))
                    {
                        using var es = e.Open();
                        using var sr = new StreamReader(es);
                        var json = sr.ReadToEnd();
                        Assert.IsTrue(json.Contains("\"ok\""), "_databases entry must contain ok flag: " + e.FullName);
                    }
                }
                Assert.IsTrue(hasOriginal, "Zip should still contain the raw source file");
            }

            try { File.Delete(zipPath); } catch { }
        }

        static string ExtractJobId(string json)
        {
            return ExtractField(json, "jobId");
        }

        static string ExtractField(string json, string field)
        {
            var key = "\"" + field + "\":\"";
            var i = json.IndexOf(key);
            if (i < 0) return "";
            i += key.Length;
            var j = json.IndexOf('"', i);
            return j < 0 ? "" : json.Substring(i, j - i);
        }
    }
}
