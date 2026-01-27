using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using ODDGames.UIAutomation.AI;
using UnityEngine;

namespace ODDGames.UIAutomation.VisualBuilder
{
    /// <summary>
    /// Serializes VisualTest to a human-readable, version-control-friendly YAML-like format.
    /// This format is designed for easy diffing and merging in source control.
    /// </summary>
    public static class VisualTestSerializer
    {
        private const string FormatVersion = "1.0";

        /// <summary>
        /// Serializes a VisualTest to a readable text format.
        /// </summary>
        public static string Serialize(VisualTest test)
        {
            if (test == null) return "";

            var sb = new StringBuilder();

            // Header
            sb.AppendLine("# Visual Test Definition");
            sb.AppendLine($"# Format Version: {FormatVersion}");
            sb.AppendLine();

            // Test metadata
            sb.AppendLine("test:");
            sb.AppendLine($"  name: \"{EscapeString(test.testName)}\"");

            if (!string.IsNullOrEmpty(test.description))
                sb.AppendLine($"  description: \"{EscapeString(test.description)}\"");

            if (!string.IsNullOrEmpty(test.startScene))
                sb.AppendLine($"  startScene: \"{test.startScene}\"");

            if (!string.IsNullOrEmpty(test.originalPrompt))
                sb.AppendLine($"  originalPrompt: \"{EscapeString(test.originalPrompt)}\"");

            sb.AppendLine();

            // Blocks
            if (test.blocks != null && test.blocks.Count > 0)
            {
                sb.AppendLine("blocks:");
                for (int i = 0; i < test.blocks.Count; i++)
                {
                    SerializeBlock(sb, test.blocks[i], i);
                }
            }
            else
            {
                sb.AppendLine("blocks: []");
            }

            // Test data references
            if (test.testDataFiles != null && test.testDataFiles.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("testData:");
                foreach (var dataFile in test.testDataFiles)
                {
                    sb.AppendLine($"  - name: \"{dataFile.name}\"");
                    sb.AppendLine($"    path: \"{dataFile.relativePath}\"");
                    if (!string.IsNullOrEmpty(dataFile.description))
                        sb.AppendLine($"    description: \"{EscapeString(dataFile.description)}\"");
                }
            }

            return sb.ToString();
        }

        private static void SerializeBlock(StringBuilder sb, VisualBlock block, int index)
        {
            sb.AppendLine($"  - # Block {index + 1}");
            sb.AppendLine($"    type: {block.type}");

            if (!string.IsNullOrEmpty(block.comment))
                sb.AppendLine($"    comment: \"{EscapeString(block.comment)}\"");

            switch (block.type)
            {
                case BlockType.Click:
                    SerializeTarget(sb, "target", block.target);
                    break;

                case BlockType.Type:
                    SerializeTarget(sb, "target", block.target);
                    sb.AppendLine($"    text: \"{EscapeString(block.text)}\"");
                    if (block.clearFirst)
                        sb.AppendLine("    clearFirst: true");
                    if (block.pressEnter)
                        sb.AppendLine("    pressEnter: true");
                    break;

                case BlockType.Drag:
                    SerializeTarget(sb, "from", block.target);
                    if (block.dragTarget != null)
                    {
                        SerializeTarget(sb, "to", block.dragTarget);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(block.dragDirection))
                            sb.AppendLine($"    direction: {block.dragDirection}");
                        sb.AppendLine($"    distance: {block.dragDistance}");
                    }
                    if (block.dragDuration != 0.3f)
                        sb.AppendLine($"    duration: {block.dragDuration}");
                    break;

                case BlockType.Scroll:
                    SerializeTarget(sb, "target", block.target);
                    sb.AppendLine($"    direction: {block.scrollDirection}");
                    sb.AppendLine($"    amount: {block.scrollAmount}");
                    break;

                case BlockType.Wait:
                    sb.AppendLine($"    seconds: {block.waitSeconds}");
                    break;

                case BlockType.Assert:
                    sb.AppendLine($"    condition: {block.assertCondition}");
                    SerializeTarget(sb, "target", block.target);
                    if (!string.IsNullOrEmpty(block.assertExpected))
                        sb.AppendLine($"    expected: \"{EscapeString(block.assertExpected)}\"");
                    break;

                case BlockType.ClearPersistentData:
                case BlockType.LoadPersistentData:
                    sb.AppendLine($"    mode: {block.persistentDataMode}");
                    if (!string.IsNullOrEmpty(block.filePattern))
                        sb.AppendLine($"    pattern: \"{block.filePattern}\"");
                    if (!string.IsNullOrEmpty(block.dataSourceName))
                        sb.AppendLine($"    source: \"{block.dataSourceName}\"");
                    break;

                case BlockType.LoadScene:
                    sb.AppendLine($"    scene: \"{block.sceneName}\"");
                    if (block.additiveLoad)
                        sb.AppendLine("    additive: true");
                    break;

                case BlockType.CustomAction:
                    sb.AppendLine($"    action: \"{block.customActionName}\"");
                    if (!string.IsNullOrEmpty(block.customActionParams))
                        sb.AppendLine($"    params: \"{EscapeString(block.customActionParams)}\"");
                    break;
            }
        }

        private static void SerializeTarget(StringBuilder sb, string fieldName, ElementSelector selector)
        {
            if (selector == null || !selector.IsValid())
            {
                sb.AppendLine($"    {fieldName}: null");
                return;
            }

            sb.AppendLine($"    {fieldName}:");
            // Serialize the SearchQuery as JSON using Newtonsoft.Json
            if (selector.query != null)
            {
                var queryJson = JsonConvert.SerializeObject(selector.query, Formatting.None, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });
                sb.AppendLine($"      query: {queryJson}");
            }
            if (!string.IsNullOrEmpty(selector.displayName))
                sb.AppendLine($"      display: \"{EscapeString(selector.displayName)}\"");
        }

        /// <summary>
        /// Deserializes a VisualTest from the text format.
        /// </summary>
        public static VisualTest Deserialize(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;

            var test = ScriptableObject.CreateInstance<VisualTest>();
            var lines = content.Split('\n');
            var context = new ParseContext();

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');

                // Skip comments and empty lines at root level
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;

                ParseLine(test, line, context);
            }

            return test;
        }

        private class ParseContext
        {
            public string Section;
            public VisualBlock CurrentBlock;
            public TestDataFile CurrentDataFile;
            public string TargetField; // "target", "from", "to"
        }

        private static void ParseLine(VisualTest test, string line, ParseContext ctx)
        {
            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            // Top-level sections
            if (indent == 0)
            {
                if (trimmed.StartsWith("test:"))
                    ctx.Section = "test";
                else if (trimmed.StartsWith("blocks:"))
                    ctx.Section = "blocks";
                else if (trimmed.StartsWith("testData:"))
                    ctx.Section = "testData";
                return;
            }

            // Parse based on context
            if (ctx.Section == "test" && indent == 2)
            {
                ParseTestField(test, trimmed);
            }
            else if (ctx.Section == "blocks")
            {
                if (trimmed.StartsWith("- "))
                {
                    // New block
                    ctx.CurrentBlock = new VisualBlock { id = Guid.NewGuid().ToString() };
                    test.blocks.Add(ctx.CurrentBlock);
                    ctx.TargetField = null;
                }
                else if (ctx.CurrentBlock != null)
                {
                    if (indent == 4)
                    {
                        ParseBlockField(ctx.CurrentBlock, trimmed, ctx);
                    }
                    else if (indent == 6 && ctx.TargetField != null)
                    {
                        ParseTargetField(ctx.CurrentBlock, ctx.TargetField, trimmed);
                    }
                }
            }
            else if (ctx.Section == "testData")
            {
                if (trimmed.StartsWith("- "))
                {
                    ctx.CurrentDataFile = new TestDataFile();
                    test.testDataFiles.Add(ctx.CurrentDataFile);
                }
                else if (ctx.CurrentDataFile != null && indent == 4)
                {
                    ParseDataFileField(ctx.CurrentDataFile, trimmed);
                }
            }
        }

        private static void ParseTestField(VisualTest test, string line)
        {
            var (key, value) = SplitKeyValue(line);

            switch (key)
            {
                case "name":
                    test.testName = UnescapeString(value);
                    break;
                case "description":
                    test.description = UnescapeString(value);
                    break;
                case "startScene":
                    test.startScene = UnescapeString(value);
                    break;
                case "originalPrompt":
                    test.originalPrompt = UnescapeString(value);
                    break;
            }
        }

        private static void ParseBlockField(VisualBlock block, string line, ParseContext ctx)
        {
            var (key, value) = SplitKeyValue(line);

            switch (key)
            {
                case "type":
                    block.type = Enum.TryParse<BlockType>(value, out var bt) ? bt : BlockType.Click;
                    break;
                case "comment":
                    block.comment = UnescapeString(value);
                    break;
                case "text":
                    block.text = UnescapeString(value);
                    break;
                case "clearFirst":
                    block.clearFirst = value == "true";
                    break;
                case "pressEnter":
                    block.pressEnter = value == "true";
                    break;
                case "direction":
                    block.dragDirection = value;
                    block.scrollDirection = value;
                    break;
                case "distance":
                    block.dragDistance = float.TryParse(value, out var dist) ? dist : 200f;
                    break;
                case "duration":
                    block.dragDuration = float.TryParse(value, out var dur) ? dur : 0.3f;
                    break;
                case "amount":
                    block.scrollAmount = float.TryParse(value, out var amt) ? amt : 0.3f;
                    break;
                case "seconds":
                    block.waitSeconds = float.TryParse(value, out var sec) ? sec : 1f;
                    break;
                case "condition":
                    block.assertCondition = Enum.TryParse<AssertCondition>(value, out var ac) ? ac : AssertCondition.ElementExists;
                    break;
                case "expected":
                    block.assertExpected = UnescapeString(value);
                    break;
                case "mode":
                    block.persistentDataMode = Enum.TryParse<PersistentDataMode>(value, out var pdm) ? pdm : PersistentDataMode.ClearAll;
                    break;
                case "pattern":
                    block.filePattern = UnescapeString(value);
                    break;
                case "source":
                    block.dataSourceName = UnescapeString(value);
                    break;
                case "scene":
                    block.sceneName = UnescapeString(value);
                    break;
                case "additive":
                    block.additiveLoad = value == "true";
                    break;
                case "action":
                    block.customActionName = UnescapeString(value);
                    break;
                case "params":
                    block.customActionParams = UnescapeString(value);
                    break;
                case "target":
                case "from":
                case "to":
                    ctx.TargetField = key;
                    break;
            }
        }

        private static void ParseTargetField(VisualBlock block, string targetField, string line)
        {
            var (key, value) = SplitKeyValue(line);

            ElementSelector selector = null;

            switch (targetField)
            {
                case "target":
                case "from":
                    selector = block.target ?? (block.target = new ElementSelector());
                    break;
                case "to":
                    selector = block.dragTarget ?? (block.dragTarget = new ElementSelector());
                    break;
            }

            if (selector == null) return;

            switch (key)
            {
                case "query":
                    // Parse the SearchQuery JSON using Newtonsoft.Json
                    try
                    {
                        selector.query = JsonConvert.DeserializeObject<SearchQuery>(value);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[VisualTestSerializer] Failed to parse SearchQuery JSON: {ex.Message}");
                    }
                    break;
                case "display":
                    selector.displayName = UnescapeString(value);
                    break;
                // Legacy support for old format
                case "type":
                case "pattern":
                    // Old format no longer supported - ignore
                    break;
            }
        }

        private static void ParseDataFileField(TestDataFile dataFile, string line)
        {
            var (key, value) = SplitKeyValue(line);

            switch (key)
            {
                case "name":
                    dataFile.name = UnescapeString(value);
                    break;
                case "path":
                    dataFile.relativePath = UnescapeString(value);
                    break;
                case "description":
                    dataFile.description = UnescapeString(value);
                    break;
            }
        }

        private static (string key, string value) SplitKeyValue(string line)
        {
            var colonIdx = line.IndexOf(':');
            if (colonIdx < 0) return (line.Trim(), "");

            var key = line.Substring(0, colonIdx).Trim();
            var value = line.Substring(colonIdx + 1).Trim();

            // Remove quotes if present
            if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
            {
                value = value.Substring(1, value.Length - 2);
            }

            return (key, value);
        }

        private static string EscapeString(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        private static string UnescapeString(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }

        /// <summary>
        /// Saves a VisualTest to a text file.
        /// </summary>
        public static void SaveToFile(VisualTest test, string filePath)
        {
            var content = Serialize(test);
            File.WriteAllText(filePath, content, Encoding.UTF8);
        }

        /// <summary>
        /// Loads a VisualTest from a text file.
        /// </summary>
        public static VisualTest LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            return Deserialize(content);
        }
    }

    /// <summary>
    /// Reference to a test data file that should be bundled with the test.
    /// </summary>
    [Serializable]
    public class TestDataFile
    {
        /// <summary>Display name for the data file</summary>
        public string name;

        /// <summary>Relative path from the test asset folder</summary>
        public string relativePath;

        /// <summary>Description of what this data represents</summary>
        public string description;

        /// <summary>The actual file content (for embedded data)</summary>
        [NonSerialized]
        public byte[] content;
    }
}
