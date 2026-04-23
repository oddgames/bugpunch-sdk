using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect.Database
{
    /// <summary>
    /// Database plugin for Odin Serializer. Detected via reflection — only
    /// activates when Sirenix.Serialization is present in the project.
    /// </summary>
    public class OdinPlugin : IDatabasePlugin
    {
        public string ProviderId => "odin";
        public string DisplayName => "Odin Serializer";
        public string[] Extensions => new[] { ".odin", ".bytes" };

        Type _serializationUtility;
        Type _dataFormat;
        MethodInfo _deserializeWeak;
        object _binaryFormat;
        object _jsonFormat;

        public bool IsAvailable()
        {
            _serializationUtility = FindType("Sirenix.Serialization.SerializationUtility");
            _dataFormat = FindType("Sirenix.Serialization.DataFormat");
            if (_serializationUtility == null || _dataFormat == null) return false;

            _deserializeWeak = _serializationUtility.GetMethod("DeserializeValueWeak",
                new[] { typeof(byte[]), _dataFormat });
            if (_deserializeWeak == null) return false;

            try
            {
                _binaryFormat = Enum.Parse(_dataFormat, "Binary");
                _jsonFormat = Enum.Parse(_dataFormat, "JSON");
            }
            catch { return false; }

            return true;
        }

        public string Parse(string filePath)
        {
            if (!File.Exists(filePath))
                return Error("File not found: " + filePath);

            byte[] bytes;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                bytes = new byte[fs.Length];
                fs.Read(bytes, 0, bytes.Length);
            }

            // Try Binary first, then JSON
            object deserialized = null;
            foreach (var fmt in new[] { _binaryFormat, _jsonFormat })
            {
                try
                {
                    deserialized = _deserializeWeak.Invoke(null, new[] { bytes, fmt });
                    if (deserialized != null) break;
                }
                catch { }
            }

            if (deserialized == null)
                return Error("Failed to deserialize — not a recognized Odin Binary or JSON format");

            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"tables\":[");

            var objType = deserialized.GetType();

            if (deserialized is IList list && list.Count > 0)
            {
                // Collection → rows
                var elemType = list[0]?.GetType() ?? typeof(object);
                var members = GetReadableMembers(elemType);
                sb.Append("{\"name\":\"").Append(Esc(elemType.Name)).Append("\",\"columns\":[");
                AppendColumns(sb, members);
                sb.Append("],\"rows\":[");

                bool firstRow = true;
                int count = 0;
                foreach (var item in list)
                {
                    if (count >= 5000) break;
                    if (!firstRow) sb.Append(",");
                    firstRow = false;
                    AppendRow(sb, item, members);
                    count++;
                }
                sb.Append("]}");
            }
            else if (deserialized is IDictionary dict)
            {
                // Dictionary → key/value rows
                sb.Append("{\"name\":\"root\",\"columns\":[");
                sb.Append("{\"name\":\"key\",\"type\":\"string\",\"nullable\":false},");
                sb.Append("{\"name\":\"value\",\"type\":\"string\",\"nullable\":true}");
                sb.Append("],\"rows\":[");
                bool firstRow = true;
                foreach (DictionaryEntry entry in dict)
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
            else
            {
                // Single object → one-row table
                var members = GetReadableMembers(objType);
                sb.Append("{\"name\":\"").Append(Esc(objType.Name)).Append("\",\"columns\":[");
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
                try { var t = asm.GetType(fullName, false); if (t != null) return t; }
                catch { }
            }
            return null;
        }

        static List<MemberInfo> GetReadableMembers(Type type)
        {
            var list = new List<MemberInfo>();
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                list.Add(f);
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.CanRead && p.GetIndexParameters().Length == 0) list.Add(p);
            return list;
        }

        static void AppendColumns(StringBuilder sb, List<MemberInfo> members)
        {
            for (int i = 0; i < members.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var m = members[i];
                var t = m is FieldInfo fi ? fi.FieldType : ((PropertyInfo)m).PropertyType;
                sb.Append("{\"name\":\"").Append(Esc(m.Name))
                  .Append("\",\"type\":\"").Append(Esc(TypeName(t)))
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
                try { val = m is FieldInfo fi ? fi.GetValue(obj) : ((PropertyInfo)m).GetValue(obj); }
                catch { }
                sb.Append("\"").Append(Esc(m.Name)).Append("\":");
                AppendJsonValue(sb, val);
            }
            sb.Append("}");
        }

        static void AppendJsonValue(StringBuilder sb, object val)
        {
            if (val == null) { sb.Append("null"); return; }
            if (val is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (val is int or long or short or byte or uint or ulong or ushort or sbyte)
            { sb.Append(val.ToString()); return; }
            if (val is float f)
            { sb.Append(f.ToString(System.Globalization.CultureInfo.InvariantCulture)); return; }
            if (val is double d)
            { sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture)); return; }
            if (val is decimal dec)
            { sb.Append(dec.ToString(System.Globalization.CultureInfo.InvariantCulture)); return; }
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
            return t.Name;
        }

        static string Error(string msg) =>
            $"{{\"ok\":false,\"error\":\"{Esc(msg)}\"}}";

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"")
              .Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") ?? "";
    }
}
