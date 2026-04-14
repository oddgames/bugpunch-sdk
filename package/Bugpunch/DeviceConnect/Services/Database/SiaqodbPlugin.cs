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
    /// Database plugin for Siaqodb. Detected via reflection — only activates
    /// when the Sqo.Siaqodb type is present in the project.
    /// </summary>
    public class SiaqodbPlugin : IDatabasePlugin
    {
        public string ProviderId => "sqo";
        public string DisplayName => "Siaqodb";
        public string[] Extensions => new[] { ".sqo" };

        Type _siaqodbType;
        MethodInfo _loadAllGeneric;
        MethodInfo _getAllTypes;

        public bool IsAvailable()
        {
            _siaqodbType = FindType("Sqo.Siaqodb");
            if (_siaqodbType == null) return false;

            _getAllTypes = _siaqodbType.GetMethod("GetAllTypes",
                BindingFlags.Public | BindingFlags.Instance);
            _loadAllGeneric = _siaqodbType.GetMethod("LoadAll",
                BindingFlags.Public | BindingFlags.Instance,
                null, Type.EmptyTypes, null);

            return _getAllTypes != null && _loadAllGeneric != null;
        }

        public string Parse(string filePath)
        {
            // Siaqodb operates on a directory containing .sqo files
            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return Error("Directory not found: " + dir);

            object db = null;
            try
            {
                db = Activator.CreateInstance(_siaqodbType, dir);

                var storedTypes = _getAllTypes.Invoke(db, null) as IList;
                if (storedTypes == null || storedTypes.Count == 0)
                    return "{\"ok\":true,\"tables\":[]}";

                var sb = new StringBuilder();
                sb.Append("{\"ok\":true,\"tables\":[");

                bool firstTable = true;
                foreach (Type storedType in storedTypes)
                {
                    if (!firstTable) sb.Append(",");
                    firstTable = false;

                    var members = GetReadableMembers(storedType);
                    sb.Append("{\"name\":\"").Append(Esc(storedType.Name)).Append("\",\"columns\":[");
                    AppendColumns(sb, members);
                    sb.Append("],\"rows\":[");

                    try
                    {
                        var loadAll = _loadAllGeneric.MakeGenericMethod(storedType);
                        var objects = loadAll.Invoke(db, null) as IEnumerable;
                        bool firstRow = true;
                        int count = 0;

                        if (objects != null)
                        {
                            foreach (var obj in objects)
                            {
                                if (count >= 5000) break;
                                if (!firstRow) sb.Append(",");
                                firstRow = false;
                                AppendRow(sb, obj, members);
                                count++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Bugpunch] Siaqodb: failed to load {storedType.Name}: {ex.Message}");
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
