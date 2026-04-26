using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Enumerates, reads, writes, and deletes PlayerPrefs.
    /// Unity's PlayerPrefs API has no enumeration — this service reads the
    /// underlying platform storage directly to discover all keys.
    /// </summary>
    public class PlayerPrefsService
    {
        // -----------------------------------------------------------------
        // Enumerate all keys (platform-specific)
        // -----------------------------------------------------------------

        /// <summary>
        /// Return all PlayerPrefs as JSON array:
        /// [{ "key":"k", "type":"string|int|float", "value":"..." }, ...]
        /// </summary>
        public string GetAll()
        {
            try
            {
                var keys = EnumerateKeys();
                var sb = new StringBuilder();
                sb.Append("[");
                bool first = true;

                foreach (var entry in keys)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("{");
                    sb.Append($"\"key\":\"{BugpunchJson.Esc(entry.Key)}\",");
                    sb.Append($"\"type\":\"{entry.Type}\",");
                    if (entry.Type == "string")
                        sb.Append($"\"value\":\"{BugpunchJson.Esc(entry.StringValue)}\"");
                    else if (entry.Type == "float")
                        sb.Append($"\"value\":{entry.FloatValue.ToString("G9", CultureInfo.InvariantCulture)}");
                    else
                        sb.Append($"\"value\":{entry.IntValue}");
                    sb.Append("}");
                }

                sb.Append("]");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                BugpunchNative.ReportSdkError("PlayerPrefsService.Enumerate", ex);
                return $"{{\"error\":\"{BugpunchJson.Esc(ex.Message)}\"}}";
            }
        }

        /// <summary>
        /// Set a PlayerPref value. Body: { "key":"k", "type":"string|int|float", "value":"..." }
        /// </summary>
        public string SetPref(string key, string type, string value)
        {
            if (string.IsNullOrEmpty(key))
                return "{\"ok\":false,\"error\":\"Key is required\"}";

            try
            {
                switch (type)
                {
                    case "int":
                        PlayerPrefs.SetInt(key, int.Parse(value));
                        break;
                    case "float":
                        PlayerPrefs.SetFloat(key, float.Parse(value, CultureInfo.InvariantCulture));
                        break;
                    default:
                        PlayerPrefs.SetString(key, value ?? "");
                        break;
                }
                PlayerPrefs.Save();
                return "{\"ok\":true}";
            }
            catch (Exception ex)
            {
                return $"{{\"ok\":false,\"error\":\"{BugpunchJson.Esc(ex.Message)}\"}}";
            }
        }

        /// <summary>
        /// Delete a single PlayerPref key.
        /// </summary>
        public string DeletePref(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "{\"ok\":false,\"error\":\"Key is required\"}";
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            return "{\"ok\":true}";
        }

        /// <summary>
        /// Delete ALL PlayerPrefs.
        /// </summary>
        public string DeleteAll()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            return "{\"ok\":true}";
        }

        // -----------------------------------------------------------------
        // Platform-specific key enumeration
        // -----------------------------------------------------------------

        struct PrefEntry
        {
            public string Key;
            public string Type; // "string", "int", "float"
            public string StringValue;
            public int IntValue;
            public float FloatValue;
        }

        List<PrefEntry> EnumerateKeys()
        {
            var entries = new List<PrefEntry>();

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            EnumerateWindows(entries);
#elif UNITY_ANDROID && !UNITY_EDITOR
            EnumerateAndroid(entries);
#elif (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX) && !UNITY_ANDROID
            EnumerateMacOS(entries);
#else
            // Fallback: no enumeration available on this platform
            BugpunchLog.Warn("PlayerPrefsService", "PlayerPrefs enumeration not supported on this platform");
#endif
            return entries;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        void EnumerateWindows(List<PrefEntry> entries)
        {
            // Editor: HKCU\Software\Unity\UnityEditor\{company}\{product}
            // Build:  HKCU\Software\{company}\{product}
            string regPath;
#if UNITY_EDITOR
            regPath = $"Software\\Unity\\UnityEditor\\{Application.companyName}\\{Application.productName}";
#else
            regPath = $"Software\\{Application.companyName}\\{Application.productName}";
#endif

            // Reflection-based registry read. Direct `Microsoft.Win32.Registry`
            // usage forces the asmdef to reference `Microsoft.Win32.Registry.dll`,
            // and that reference doesn't resolve cleanly across every Unity API
            // compatibility level (.NET Framework bakes these types into mscorlib;
            // .NET Standard 2.1 ships them in a separate assembly whose location
            // varies between Editor + target platform). Reflection reaches the
            // type wherever the loaded BCL exposes it — Windows-only code path,
            // so the type is always present at runtime here.
            var registryType =
                Type.GetType("Microsoft.Win32.Registry, Microsoft.Win32.Registry") ??
                Type.GetType("Microsoft.Win32.Registry, mscorlib");
            if (registryType == null) return;
            var currentUser = registryType
                .GetProperty("CurrentUser", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?
                .GetValue(null);
            if (currentUser == null) return;
            var openSubKey = currentUser.GetType().GetMethod("OpenSubKey", new[] { typeof(string) });
            var key = openSubKey?.Invoke(currentUser, new object[] { regPath });
            if (key == null) return;
            try
            {
                var getValueNames = key.GetType().GetMethod("GetValueNames");
                var valueNames = getValueNames?.Invoke(key, null) as string[];
                if (valueNames == null) return;
                foreach (var valueName in valueNames)
                {
                    // Unity appends _hXXXXXX hash suffix to registry value names
                    var prefKey = valueName;
                    var lastUnderscore = prefKey.LastIndexOf('_');
                    if (lastUnderscore > 0 && prefKey.Length - lastUnderscore == 9)
                    {
                        // Check if suffix is _hXXXXXXXX (8 hex chars)
                        bool isHash = true;
                        for (int i = lastUnderscore + 2; i < prefKey.Length && isHash; i++)
                        {
                            var c = prefKey[i];
                            isHash = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                        }
                        if (isHash && prefKey[lastUnderscore + 1] == 'h')
                            prefKey = prefKey.Substring(0, lastUnderscore);
                    }

                    if (!PlayerPrefs.HasKey(prefKey)) continue;

                    var entry = ClassifyPref(prefKey);
                    entries.Add(entry);
                }
            }
            finally
            {
                (key as IDisposable)?.Dispose();
            }
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        void EnumerateAndroid(List<PrefEntry> entries)
        {
            // PlayerPrefs on Android: /data/data/{bundleId}/shared_prefs/unity.{bundleId}.v2.playerprefs.xml
            // Fallback to legacy path if v2 doesn't exist
            var bundleId = Application.identifier;
            var basePath = $"/data/data/{bundleId}/shared_prefs";
            var xmlPath = $"{basePath}/unity.{bundleId}.v2.playerprefs.xml";

            if (!File.Exists(xmlPath))
            {
                // Try legacy format
                xmlPath = $"{basePath}/{bundleId}.v2.playerprefs.xml";
                if (!File.Exists(xmlPath))
                    return;
            }

            var xml = File.ReadAllText(xmlPath);
            ParseAndroidPrefsXml(xml, entries);
        }

        void ParseAndroidPrefsXml(string xml, List<PrefEntry> entries)
        {
            // Simple XML parser for SharedPreferences format:
            // <string name="key">value</string>
            // <int name="key" value="123" />
            // <float name="key" value="1.5" />
            int pos = 0;
            while (pos < xml.Length)
            {
                var tagStart = xml.IndexOf('<', pos);
                if (tagStart < 0) break;
                var tagEnd = xml.IndexOf('>', tagStart);
                if (tagEnd < 0) break;

                var tag = xml.Substring(tagStart + 1, tagEnd - tagStart - 1).Trim();
                pos = tagEnd + 1;

                if (tag.StartsWith("string "))
                {
                    var name = ExtractXmlAttr(tag, "name");
                    if (name == null) continue;
                    var closeTag = xml.IndexOf("</string>", pos, StringComparison.Ordinal);
                    var value = closeTag > pos ? xml.Substring(pos, closeTag - pos) : "";
                    pos = closeTag > pos ? closeTag + 9 : pos;
                    entries.Add(new PrefEntry { Key = name, Type = "string", StringValue = UnescapeXml(value) });
                }
                else if (tag.StartsWith("int "))
                {
                    var name = ExtractXmlAttr(tag, "name");
                    var val = ExtractXmlAttr(tag, "value");
                    if (name != null && int.TryParse(val, out var iv))
                        entries.Add(new PrefEntry { Key = name, Type = "int", IntValue = iv });
                }
                else if (tag.StartsWith("float "))
                {
                    var name = ExtractXmlAttr(tag, "name");
                    var val = ExtractXmlAttr(tag, "value");
                    if (name != null && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv))
                        entries.Add(new PrefEntry { Key = name, Type = "float", FloatValue = fv });
                }
            }
        }

        static string ExtractXmlAttr(string tag, string attr)
        {
            var needle = attr + "=\"";
            var idx = tag.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0) return null;
            var start = idx + needle.Length;
            var end = tag.IndexOf('"', start);
            return end > start ? tag.Substring(start, end - start) : null;
        }

        static string UnescapeXml(string s)
        {
            return s?.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'") ?? "";
        }
#endif

#if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX) && !UNITY_ANDROID
        void EnumerateMacOS(List<PrefEntry> entries)
        {
            // macOS: ~/Library/Preferences/unity.{company}.{product}.plist
            var company = Application.companyName;
            var product = Application.productName;
            var plistPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                "Library", "Preferences",
                $"unity.{company}.{product}.plist"
            );

            if (!File.Exists(plistPath))
            {
                // Try to use 'defaults' command to export keys
                try
                {
                    var domain = $"unity.{company}.{product}";
                    var psi = new System.Diagnostics.ProcessStartInfo("defaults", $"export \"{domain}\" -")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = System.Diagnostics.Process.Start(psi);
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(5000);

                    // Parse the plist XML output for keys
                    ParsePlistKeys(output, entries);
                }
                catch
                {
                    BugpunchLog.Warn("PlayerPrefsService", "Failed to enumerate PlayerPrefs on macOS");
                }
                return;
            }

            // If file exists, read and parse
            try
            {
                var plist = File.ReadAllText(plistPath);
                ParsePlistKeys(plist, entries);
            }
            catch (Exception ex)
            {
                BugpunchNative.ReportSdkError("PlayerPrefsService.ReadPlist", ex);
            }
        }

        void ParsePlistKeys(string plistXml, List<PrefEntry> entries)
        {
            // Plist format: <key>name</key> followed by <string>val</string> or <integer>N</integer> or <real>F</real>
            int pos = 0;
            while (pos < plistXml.Length)
            {
                var keyStart = plistXml.IndexOf("<key>", pos, StringComparison.Ordinal);
                if (keyStart < 0) break;
                var keyEnd = plistXml.IndexOf("</key>", keyStart, StringComparison.Ordinal);
                if (keyEnd < 0) break;
                var key = plistXml.Substring(keyStart + 5, keyEnd - keyStart - 5);
                pos = keyEnd + 6;

                // Skip unity's internal keys
                if (key.StartsWith("unity.")) continue;

                // The key exists in PlayerPrefs — classify it
                if (PlayerPrefs.HasKey(key))
                    entries.Add(ClassifyPref(key));
            }
        }
#endif

        /// <summary>
        /// Determine the type of a PlayerPref by probing.
        /// Unity doesn't store type info, so we try all three.
        /// </summary>
        PrefEntry ClassifyPref(string key)
        {
            // Heuristic: try string first (most common), then check if it looks
            // like it could be int or float by also reading those.
            var strVal = PlayerPrefs.GetString(key, "\x01\x02SENTINEL");
            var intVal = PlayerPrefs.GetInt(key, int.MinValue);
            var floatVal = PlayerPrefs.GetFloat(key, float.MinValue);

            // If GetString returns our sentinel, it's not stored as string
            bool hasString = strVal != "\x01\x02SENTINEL";
            bool hasInt = intVal != int.MinValue;
            bool hasFloat = Math.Abs(floatVal - float.MinValue) > 0.001f;

            // On Windows registry, all prefs return values for all types.
            // Best heuristic: if GetString returns a non-empty non-numeric value, it's string.
            // Otherwise prefer int over float.
            if (hasString && !string.IsNullOrEmpty(strVal))
            {
                // Check if the string value is actually just the int/float representation
                bool isNumericStr = int.TryParse(strVal, out var parsedInt) && parsedInt == intVal;
                bool isFloatStr = !isNumericStr && float.TryParse(strVal, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedFloat)
                    && Math.Abs(parsedFloat - floatVal) < 0.001f;

                if (!isNumericStr && !isFloatStr)
                    return new PrefEntry { Key = key, Type = "string", StringValue = strVal };
            }

            if (hasInt)
                return new PrefEntry { Key = key, Type = "int", IntValue = intVal };

            if (hasFloat)
                return new PrefEntry { Key = key, Type = "float", FloatValue = floatVal };

            // Fallback: treat as empty string
            return new PrefEntry { Key = key, Type = "string", StringValue = hasString ? strVal : "" };
        }

    }
}
