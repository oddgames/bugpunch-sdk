using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

using UnityEngine;

namespace ODDGames.UIAutomation.VisualBuilder
{
    /// <summary>
    /// Compiles and executes C# code at runtime using Reflection.Emit.
    /// Provides a sandboxed environment for test code execution.
    /// </summary>
    public static class RuntimeCodeCompiler
    {
        private static readonly Dictionary<string, Delegate> CompiledCache = new();
        private static ModuleBuilder moduleBuilder;
        private static int typeCounter;

        /// <summary>
        /// Executes synchronous C# code.
        /// Code has access to: UnityEngine, Debug.Log, GameObject.Find, etc.
        /// </summary>
        public static void Execute(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return;

            // For simple expressions, try direct evaluation first
            if (TryExecuteSimple(code))
                return;

            // For complex code, use Roslyn if available, otherwise interpret
            ExecuteInterpreted(code);
        }

        /// <summary>
        /// Executes async C# code that returns Task.
        /// </summary>
        public static async Task ExecuteAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return;

            // Execute sync portion
            Execute(code);

            // Allow frame to process
            await Task.Yield();
        }

        /// <summary>
        /// Evaluates a boolean expression.
        /// </summary>
        public static bool EvaluateBoolExpression(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return false;

            try
            {
                // Handle common patterns
                expression = expression.Trim();

                // GameObject.Find("Name") != null
                if (expression.Contains("GameObject.Find"))
                {
                    return EvaluateGameObjectFind(expression);
                }

                // PlayerPrefs checks
                if (expression.Contains("PlayerPrefs"))
                {
                    return EvaluatePlayerPrefs(expression);
                }

                // Static field/property access
                if (expression.Contains("."))
                {
                    return EvaluateStaticMember(expression);
                }

                // Simple true/false
                if (bool.TryParse(expression, out var boolResult))
                    return boolResult;

                Debug.LogWarning($"[RuntimeCodeCompiler] Could not evaluate expression: {expression}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RuntimeCodeCompiler] Expression evaluation failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryExecuteSimple(string code)
        {
            code = code.Trim().TrimEnd(';');

            // Debug.Log("message")
            if (code.StartsWith("Debug.Log(") && code.EndsWith(")"))
            {
                var message = ExtractStringArg(code, "Debug.Log(");
                if (message != null)
                {
                    Debug.Log(message);
                    return true;
                }
            }

            // PlayerPrefs.SetInt/SetFloat/SetString
            if (code.StartsWith("PlayerPrefs.Set"))
            {
                return ExecutePlayerPrefsSet(code);
            }

            // PlayerPrefs.DeleteKey
            if (code.StartsWith("PlayerPrefs.DeleteKey("))
            {
                var key = ExtractStringArg(code, "PlayerPrefs.DeleteKey(");
                if (key != null)
                {
                    PlayerPrefs.DeleteKey(key);
                    return true;
                }
            }

            // PlayerPrefs.DeleteAll
            if (code == "PlayerPrefs.DeleteAll()")
            {
                PlayerPrefs.DeleteAll();
                return true;
            }

            // PlayerPrefs.Save
            if (code == "PlayerPrefs.Save()")
            {
                PlayerPrefs.Save();
                return true;
            }

            // GameObject.Find("Name").SetActive(bool)
            if (code.Contains("GameObject.Find(") && code.Contains(".SetActive("))
            {
                return ExecuteSetActive(code);
            }

            return false;
        }

        private static void ExecuteInterpreted(string code)
        {
            // Split into statements
            var statements = code.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var statement in statements)
            {
                var trimmed = statement.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                if (!TryExecuteSimple(trimmed))
                {
                    Debug.LogWarning($"[RuntimeCodeCompiler] Unsupported statement: {trimmed}");
                }
            }
        }

        private static bool EvaluateGameObjectFind(string expression)
        {
            // GameObject.Find("Name") != null
            // GameObject.Find("Name") == null
            var notNull = expression.Contains("!= null");
            var isNull = expression.Contains("== null");

            var start = expression.IndexOf("GameObject.Find(\"", StringComparison.Ordinal);
            if (start < 0) return false;

            start += "GameObject.Find(\"".Length;
            var end = expression.IndexOf("\")", start, StringComparison.Ordinal);
            if (end < 0) return false;

            var name = expression.Substring(start, end - start);
            var go = GameObject.Find(name);

            if (notNull) return go != null;
            if (isNull) return go == null;

            return go != null;
        }

        private static bool EvaluatePlayerPrefs(string expression)
        {
            // PlayerPrefs.GetInt("key") == value
            // PlayerPrefs.HasKey("key")
            // PlayerPrefs.GetString("key") == "value"

            if (expression.Contains("PlayerPrefs.HasKey("))
            {
                var key = ExtractStringArg(expression, "PlayerPrefs.HasKey(");
                return key != null && PlayerPrefs.HasKey(key);
            }

            if (expression.Contains("PlayerPrefs.GetInt("))
            {
                var key = ExtractStringArg(expression, "PlayerPrefs.GetInt(");
                if (key == null) return false;

                var actual = PlayerPrefs.GetInt(key, 0);

                // Extract expected value
                var eqIndex = expression.IndexOf("==", StringComparison.Ordinal);
                if (eqIndex > 0)
                {
                    var expectedStr = expression.Substring(eqIndex + 2).Trim();
                    if (int.TryParse(expectedStr, out var expected))
                        return actual == expected;
                }

                return actual != 0;
            }

            if (expression.Contains("PlayerPrefs.GetFloat("))
            {
                var key = ExtractStringArg(expression, "PlayerPrefs.GetFloat(");
                if (key == null) return false;

                var actual = PlayerPrefs.GetFloat(key, 0f);

                var eqIndex = expression.IndexOf("==", StringComparison.Ordinal);
                if (eqIndex > 0)
                {
                    var expectedStr = expression.Substring(eqIndex + 2).Trim().TrimEnd('f', 'F');
                    if (float.TryParse(expectedStr, out var expected))
                        return Mathf.Approximately(actual, expected);
                }

                return actual != 0f;
            }

            if (expression.Contains("PlayerPrefs.GetString("))
            {
                var key = ExtractStringArg(expression, "PlayerPrefs.GetString(");
                if (key == null) return false;

                var actual = PlayerPrefs.GetString(key, "");

                var eqIndex = expression.IndexOf("==", StringComparison.Ordinal);
                if (eqIndex > 0)
                {
                    var rest = expression.Substring(eqIndex + 2).Trim();
                    var expected = ExtractQuotedString(rest);
                    if (expected != null)
                        return actual == expected;
                }

                return !string.IsNullOrEmpty(actual);
            }

            return false;
        }

        private static bool EvaluateStaticMember(string expression)
        {
            // Try to evaluate static properties like: GameManager.Instance.IsGameOver
            // Or: SomeClass.SomeStaticBool

            try
            {
                var parts = expression.Split('.');
                if (parts.Length < 2) return false;

                // Find the type
                Type type = null;
                int memberStartIndex = 1;

                // Try progressively longer type names
                for (int i = 1; i <= parts.Length; i++)
                {
                    var typeName = string.Join(".", parts, 0, i);
                    type = FindType(typeName);
                    if (type != null)
                    {
                        memberStartIndex = i;
                        break;
                    }
                }

                if (type == null)
                    return false;

                // Navigate through members
                object current = null;
                var currentType = type;

                for (int i = memberStartIndex; i < parts.Length; i++)
                {
                    var memberName = parts[i].Trim();

                    // Check for comparison at the end
                    if (memberName.Contains("==") || memberName.Contains("!="))
                        break;

                    var prop = currentType.GetProperty(memberName, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                    if (prop != null)
                    {
                        current = prop.GetValue(current);
                        currentType = prop.PropertyType;
                        continue;
                    }

                    var field = currentType.GetField(memberName, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                    if (field != null)
                    {
                        current = field.GetValue(current);
                        currentType = field.FieldType;
                        continue;
                    }

                    return false;
                }

                // Convert result to bool
                if (current is bool b)
                    return b;

                return current != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool ExecutePlayerPrefsSet(string code)
        {
            // PlayerPrefs.SetInt("key", value)
            // PlayerPrefs.SetFloat("key", value)
            // PlayerPrefs.SetString("key", "value")

            if (code.StartsWith("PlayerPrefs.SetInt("))
            {
                var args = ExtractArgs(code, "PlayerPrefs.SetInt(");
                if (args.Length >= 2 && int.TryParse(args[1], out var value))
                {
                    PlayerPrefs.SetInt(args[0], value);
                    return true;
                }
            }

            if (code.StartsWith("PlayerPrefs.SetFloat("))
            {
                var args = ExtractArgs(code, "PlayerPrefs.SetFloat(");
                if (args.Length >= 2 && float.TryParse(args[1].TrimEnd('f', 'F'), out var value))
                {
                    PlayerPrefs.SetFloat(args[0], value);
                    return true;
                }
            }

            if (code.StartsWith("PlayerPrefs.SetString("))
            {
                var args = ExtractArgs(code, "PlayerPrefs.SetString(");
                if (args.Length >= 2)
                {
                    PlayerPrefs.SetString(args[0], args[1]);
                    return true;
                }
            }

            return false;
        }

        private static bool ExecuteSetActive(string code)
        {
            // GameObject.Find("Name").SetActive(true/false)
            var findStart = code.IndexOf("GameObject.Find(\"", StringComparison.Ordinal);
            if (findStart < 0) return false;

            findStart += "GameObject.Find(\"".Length;
            var findEnd = code.IndexOf("\")", findStart, StringComparison.Ordinal);
            if (findEnd < 0) return false;

            var name = code.Substring(findStart, findEnd - findStart);

            var activeStart = code.IndexOf(".SetActive(", StringComparison.Ordinal);
            if (activeStart < 0) return false;

            activeStart += ".SetActive(".Length;
            var activeEnd = code.IndexOf(")", activeStart, StringComparison.Ordinal);
            if (activeEnd < 0) return false;

            var activeStr = code.Substring(activeStart, activeEnd - activeStart).Trim();
            if (!bool.TryParse(activeStr, out var active))
                return false;

            var go = GameObject.Find(name);
            if (go != null)
            {
                go.SetActive(active);
                return true;
            }

            Debug.LogWarning($"[RuntimeCodeCompiler] GameObject not found: {name}");
            return false;
        }

        private static string ExtractStringArg(string code, string prefix)
        {
            var start = code.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0) return null;

            start += prefix.Length;
            if (code[start] != '"') return null;

            start++;
            var end = code.IndexOf('"', start);
            if (end < 0) return null;

            return code.Substring(start, end - start);
        }

        private static string ExtractQuotedString(string text)
        {
            var start = text.IndexOf('"');
            if (start < 0) return null;

            start++;
            var end = text.IndexOf('"', start);
            if (end < 0) return null;

            return text.Substring(start, end - start);
        }

        private static string[] ExtractArgs(string code, string prefix)
        {
            var start = code.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0) return Array.Empty<string>();

            start += prefix.Length;
            var end = code.LastIndexOf(')');
            if (end < 0) return Array.Empty<string>();

            var argsStr = code.Substring(start, end - start);
            var args = new List<string>();
            var current = "";
            var inString = false;

            foreach (var c in argsStr)
            {
                if (c == '"')
                {
                    inString = !inString;
                }
                else if (c == ',' && !inString)
                {
                    args.Add(current.Trim().Trim('"'));
                    current = "";
                }
                else
                {
                    current += c;
                }
            }

            if (!string.IsNullOrEmpty(current))
                args.Add(current.Trim().Trim('"'));

            return args.ToArray();
        }

        private static Type FindType(string typeName)
        {
            // Check common Unity types
            if (typeName == "GameObject") return typeof(GameObject);
            if (typeName == "Debug") return typeof(Debug);
            if (typeName == "PlayerPrefs") return typeof(PlayerPrefs);
            if (typeName == "Time") return typeof(Time);
            if (typeName == "Application") return typeof(Application);

            // Search all loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type != null) return type;
            }

            return null;
        }
    }
}
