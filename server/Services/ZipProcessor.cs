using System.IO.Compression;
using System.Text.Json;

namespace UIAutomation.Server.Services;

/// <summary>
/// Extracts metadata from diagnostic session zip archives.
/// </summary>
public static class ZipProcessor
{
    /// <summary>
    /// Reads session.json from a zip file and returns it as a parsed JsonDocument.
    /// </summary>
    public static JsonDocument? ReadSessionJson(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.Name.Equals("session.json", StringComparison.OrdinalIgnoreCase));
        if (entry == null) return null;

        using var stream = entry.Open();
        return JsonDocument.Parse(stream);
    }

    /// <summary>
    /// Reads a specific file from a zip archive and returns its bytes.
    /// </summary>
    public static byte[]? ReadFileFromZip(string zipPath, string fileName)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
            e.FullName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return null;

        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Lists all file entries in a zip archive.
    /// </summary>
    public static List<string> ListFiles(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.Select(e => e.FullName).ToList();
    }

    /// <summary>
    /// Determines the test result from session.json.
    /// Prefers the explicit "result" field (set from NUnit TestContext) over event scanning.
    /// This correctly handles tests that intentionally catch expected failures.
    /// </summary>
    public static string DetermineResult(JsonDocument sessionDoc)
    {
        // Prefer explicit result field from NUnit TestContext
        if (sessionDoc.RootElement.TryGetProperty("result", out var resultProp))
        {
            var r = resultProp.GetString();
            if (r == "pass" || r == "fail" || r == "warn") return r;
        }

        // Fallback: scan events (for older sessions without explicit result)
        if (sessionDoc.RootElement.TryGetProperty("events", out var events))
        {
            foreach (var evt in events.EnumerateArray())
            {
                if (evt.TryGetProperty("type", out var type))
                {
                    var t = type.GetString();
                    if (t == "failure") return "fail";
                    if (t == "warn") return "warn";
                }
            }
        }
        return "pass";
    }

    /// <summary>
    /// Checks if the zip contains a video file.
    /// </summary>
    public static bool HasVideo(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.Any(e =>
            e.Name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
    }
}
