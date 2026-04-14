using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Handles /databases/* tunnel requests — parses Siaqodb and Odin Serializer
    /// files on the device using reflection so the server can display them in the
    /// database viewer without needing .NET parsers in Node.js.
    /// </summary>
    public class DatabaseService
    {
        // Reflection caches — resolved once on first use.
        Type _siaqodbType;
        bool _siaqodbChecked;
        Type _odinSerializationUtility;
        Type _odinDataFormat;
        bool _odinChecked;

        public string Parse(string path, string provider)
        {
            if (string.IsNullOrEmpty(path))
                return Error("path is required");
            if (string.IsNullOrEmpty(provider))
                return Error("provider is required");

            try
            {
                switch (provider)
                {
                    case "sqo":  return ParseSiaqodb(path);
                    case "odin": return ParseOdin(path);
                    default:     return Error("Unknown provider: " + provider);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch] DatabaseService.Parse error: {ex}");
                return Error(ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // Siaqodb
        // -----------------------------------------------------------------

        string ParseSiaqodb(string filePath)
        {
            if (!_siaqodbChecked)
            {
                _siaqodbChecked = true;
                _siaqodbType = FindType("Sqo.Siaqodb");
            }

            if (_siaqodbType == null)
                return Error("Siaqodb assembly not found in this project");

            // Siaqodb operates on a directory containing .sqo files.
            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return Error("Directory not found: " + dir);

            object db = null;
            try
            {
                // var db = new Siaqodb(directoryPath);
                db = Activator.CreateInstance(_siaqodbType, dir);

                // Get stored types: db.GetAllTypes() → IList<Type>
                var getAllTypes = _siaqodbType.GetMethod("GetAllTypes",
                    BindingFlags.Public | BindingFlags.Instance);
                if (getAllTypes == null)
                    return Error("Siaqodb.GetAllTypes() not found");

                var storedTypes = getAllTypes.Invoke(db, null) as System.Collections.IList;
                if (storedTypes == null || storedTypes.Count == 0)
                    return Ok("[]");

                // Build a "table" per stored type.
                var sb = new StringBuilder();
                sb.Append("{\"ok\":true,\"tables\":[");

                // Generic LoadAll<T> method
                var loadAllGeneric = _siaqodbType.GetMethod("LoadAll",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, Type.EmptyTypes, null);

                bool firstTable = true;
                foreach (Type storedType in storedTypes)
                {
                    if (!firstTable) sb.Append(",");
                    firstTable = false;

                    var tableName = storedType.Name;
                    var fields = storedType.GetFields(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var props = storedType.GetProperties(
                        BindingFlags.Public | BindingFlags.Instance);

                    // Build columns
                    sb.Append("{\"name\":\"").Append(Esc(tableName)).Append("\",\"columns\":[");
                    bool firstCol = true;
                    var members = new List<MemberInfo>();
                    foreach (var f in fields)
                    {
                        if (f.IsNotSerialized) continue;
                        if (!firstCol) sb.Append(",");
                        firstCol = false;
                        sb.Append("{\"name\":\"").Append(Esc(f.Name))
                          .Append("\",\"type\":\"").Append(Esc(TypeName(f.FieldType)))
                          .Append("\",\"nullable\":true}");
                        members.Add(f);
                    }
                    foreach (var p in props)
                    {
                        if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                        if (!firstCol) sb.Append(",");
                        firstCol = false;
                        sb.Append("{\"name\":\"").Append(Esc(p.Name))
                          .Append("\",\"type\":\"").Append(Esc(TypeName(p.PropertyType)))
                          .Append("\",\"nullable\":true}");
                        members.Add(p);
                    }
                    sb.Append("],\"rows\":[");

                    // Load objects
                    try
                    {
                        var loadAll = loadAllGeneric?.MakeGenericMethod(storedType);
                        var objects = loadAll?.Invoke(db, null) as System.Collections.IEnumerable;
                        bool firstRow = true;
                        int rowCount = 0;
                        const int maxRows = 5000;

                        if (objects != null)
                        {
                            foreach (var obj in objects)
                            {
                                if (rowCount >= maxRows) break;
                                if (!firstRow) sb.Append(",");
                                firstRow = false;
                                sb.Append("{");
                                bool firstVal = true;
                                foreach (var m in members)
                                {
                                    if (!firstVal) sb.Append(",");
                                    firstVal = false;
                                    object val = null;
                                    try
                                    {
                                        val = m is FieldInfo fi ? fi.GetValue(obj)
                                            : ((PropertyInfo)m).GetValue(obj);
                                    }
                                    catch { /* skip inaccessible */ }
                                    sb.Append("\"").Append(Esc(m.Name)).Append("\":");
                                    AppendJsonValue(sb, val);
                                }
                                sb.Append("}");
                                rowCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Bugpunch] Failed to load {tableName}: {ex.Message}");
                        // Empty rows for this table — columns still visible
                    }

                    sb.Append("]}");
                }

                sb.Append("]}");
                return sb.ToString();
            }
            finally
            {
                if (db is IDisposable d) d.Dispose();
            }
        }

        // -----------------------------------------------------------------
        // Odin Serializer
        // -----------------------------------------------------------------

        string ParseOdin(string filePath)
        {
            if (!_odinChecked)
            {
                _odinChecked = true;
                _odinSerializationUtility = FindType("Sirenix.Serialization.SerializationUtility");
                _odinDataFormat = FindType("Sirenix.Serialization.DataFormat");
            }

            if (_odinSerializationUtility == null)
                return Error("Odin Serializer assembly not found in this project");

            if (!File.Exists(filePath))
                return Error("File not found: " + filePath);

            byte[] bytes;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                bytes = new byte[fs.Length];
                fs.Read(bytes, 0, bytes.Length);
            }

            // Try DeserializeValueWeak(bytes, DataFormat.Binary)
            // Odin supports Binary, JSON, Nodes — try Binary first, then JSON.
            object deserialized = null;
            var formats = new[] { "Binary", "JSON" };

            foreach (var fmt in formats)
            {
                try
                {
                    var formatVal = Enum.Parse(_odinDataFormat, fmt);
                    var method = _odinSerializationUtility.GetMethod("DeserializeValueWeak",
                        new[] { typeof(byte[]), _odinDataFormat });
                    if (method != null)
                    {
                        deserialized = method.Invoke(null, new[] { bytes, formatVal });
                        if (deserialized != null) break;
                    }
                }
                catch { /* try next format */ }
            }

            if (deserialized == null)
                return Error("Failed to deserialize Odin file — could not parse as Binary or JSON");

            // Convert the deserialized object into a single-table result.
            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"tables\":[");

            var objType = deserialized.GetType();

            // If it's a collection, present as rows
            if (deserialized is System.Collections.IList list && list.Count > 0)
            {
                var elemType = list[0]?.GetType() ?? typeof(object);
                sb.Append("{\"name\":\"").Append(Esc(elemType.Name)).Append("\",\"columns\":[");
                var members = GetReadableMembers(elemType);
                AppendColumns(sb, members);
                sb.Append("],\"rows\":[");

                bool firstRow = true;
                int count = 0;
                const int maxRows = 5000;
                foreach (var item in list)
                {
                    if (count >= maxRows) break;
                    if (!firstRow) sb.Append(",");
                    firstRow = false;
                    AppendRow(sb, item, members);
                    count++;
                }
                sb.Append("]}");
            }
            // If it's a dictionary, present keys/values as rows
            else if (deserialized is System.Collections.IDictionary dict)
            {
                sb.Append("{\"name\":\"root\",\"columns\":[");
                sb.Append("{\"name\":\"key\",\"type\":\"string\",\"nullable\":false},");
                sb.Append("{\"name\":\"value\",\"type\":\"string\",\"nullable\":true}");
                sb.Append("],\"rows\":[");
                bool firstRow = true;
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    if (!firstRow) sb.Append(",");
                    firstRow = false;
                    sb.Append("{\"key\":");
                    AppendJsonValue(sb, entry.Key);
                    sb.Append(",\"value\":");
                    AppendJsonValue(sb, entry.Value);
                    sb.Append("}");
                }
                sb.Append("]}");
            }
            // Single object — present fields as columns, one row
            else
            {
                sb.Append("{\"name\":\"").Append(Esc(objType.Name)).Append("\",\"columns\":[");
                var members = GetReadableMembers(objType);
                AppendColumns(sb, members);
                sb.Append("],\"rows\":[");
                AppendRow(sb, deserialized, members);
                sb.Append("]}");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { /* skip unloadable assemblies */ }
            }
            return null;
        }

        static List<MemberInfo> GetReadableMembers(Type type)
        {
            var list = new List<MemberInfo>();
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                list.Add(f);
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.CanRead && p.GetIndexParameters().Length == 0)
                    list.Add(p);
            }
            return list;
        }

        static void AppendColumns(StringBuilder sb, List<MemberInfo> members)
        {
            for (int i = 0; i < members.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var m = members[i];
                var mType = m is FieldInfo fi ? fi.FieldType
                          : ((PropertyInfo)m).PropertyType;
                sb.Append("{\"name\":\"").Append(Esc(m.Name))
                  .Append("\",\"type\":\"").Append(Esc(TypeName(mType)))
                  .Append("\",\"nullable\":true}");
            }
        }

        static void AppendRow(StringBuilder sb, object obj, List<MemberInfo> members)
        {
            sb.Append("{");
            for (int i = 0; i < members.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var m = members[i];
                object val = null;
                try
                {
                    val = m is FieldInfo fi ? fi.GetValue(obj)
                        : ((PropertyInfo)m).GetValue(obj);
                }
                catch { /* inaccessible */ }
                sb.Append("\"").Append(Esc(m.Name)).Append("\":");
                AppendJsonValue(sb, val);
            }
            sb.Append("}");
        }

        static void AppendJsonValue(StringBuilder sb, object val)
        {
            if (val == null)
            {
                sb.Append("null");
                return;
            }

            if (val is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }

            if (val is int || val is long || val is short || val is byte
                || val is uint || val is ulong || val is ushort || val is sbyte)
            {
                sb.Append(val.ToString());
                return;
            }

            if (val is float f)
            {
                sb.Append(f.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            }

            if (val is double d)
            {
                sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            }

            if (val is decimal dec)
            {
                sb.Append(dec.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            }

            // Everything else → string representation
            sb.Append("\"").Append(Esc(val.ToString())).Append("\"");
        }

        static string TypeName(Type t)
        {
            if (t == typeof(int)) return "int";
            if (t == typeof(long)) return "long";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string";
            if (t == typeof(byte[])) return "bytes";
            return t.Name;
        }

        static string Ok(string tablesJson)
        {
            return $"{{\"ok\":true,\"tables\":{tablesJson}}}";
        }

        static string Error(string message)
        {
            return $"{{\"ok\":false,\"error\":\"{Esc(message)}\"}}";
        }

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"")
              .Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") ?? "";
    }
}
