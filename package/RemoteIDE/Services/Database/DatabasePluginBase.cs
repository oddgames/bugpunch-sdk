using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace ODDGames.Bugpunch.RemoteIDE.Database
{
    /// <summary>
    /// Convenience base for <see cref="IDatabasePlugin"/> implementations.
    /// Provides shared helpers — type lookup, member reflection, table
    /// building — so a new plugin only has to detect its library, hand
    /// objects back, and let the base build the wire format.
    ///
    /// Minimal example:
    /// <code>
    /// public class MyPlugin : DatabasePluginBase
    /// {
    ///     public override string ProviderId   => "mine";
    ///     public override string DisplayName  => "My Format";
    ///     public override string[] Extensions => new[] { ".mine" };
    ///     public override bool IsAvailable()  => FindType("My.Lib.Reader") != null;
    ///     public override ParseResult Parse(string filePath)
    ///     {
    ///         var rows = MyLib.Read(filePath);
    ///         return Ok(TableFromObjects("Rows", rows));
    ///     }
    /// }
    /// </code>
    /// </summary>
    public abstract class DatabasePluginBase : IDatabasePlugin
    {
        public abstract string ProviderId { get; }
        public abstract string DisplayName { get; }
        public abstract string[] Extensions { get; }
        public abstract bool IsAvailable();
        public abstract ParseResult Parse(string filePath);

        /// <summary>Default per-table row cap. Plugins can override by passing
        /// an explicit <c>maxRows</c> to <see cref="TableFromObjects"/>.</summary>
        protected const int DefaultMaxRows = 5000;

        // -----------------------------------------------------------------
        // Result builders
        // -----------------------------------------------------------------

        protected static ParseResult Ok(params TableData[] tables)
            => new ParseResult { ok = true, tables = new List<TableData>(tables) };

        protected static ParseResult Ok(List<TableData> tables)
            => new ParseResult { ok = true, tables = tables };

        protected static ParseResult Fail(string message)
            => new ParseResult { ok = false, error = message };

        // -----------------------------------------------------------------
        // Object → table helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Reflect over <paramref name="rows"/> using the first non-null
        /// element to derive columns, then materialize each row as a
        /// dictionary keyed by member name. The resulting <see cref="TableData"/>
        /// is ready to drop into <see cref="Ok"/>.
        /// </summary>
        protected static TableData TableFromObjects(string name, IEnumerable rows, int maxRows = DefaultMaxRows)
        {
            var table = new TableData
            {
                name = name,
                columns = new List<ColumnData>(),
                rows = new List<Dictionary<string, object>>(),
            };
            if (rows == null) return table;

            List<MemberInfo> members = null;
            int count = 0;
            foreach (var obj in rows)
            {
                if (count >= maxRows) break;
                if (obj == null) continue;

                if (members == null)
                {
                    members = GetReadableMembers(obj.GetType());
                    foreach (var m in members) table.columns.Add(ColumnFor(m));
                }
                table.rows.Add(RowFromObject(obj, members));
                count++;
            }
            return table;
        }

        /// <summary>Build a single-row table from one object (e.g. an Odin
        /// file whose root is a POCO).</summary>
        protected static TableData TableFromObject(string name, object obj)
        {
            var members = obj == null ? new List<MemberInfo>() : GetReadableMembers(obj.GetType());
            var table = new TableData
            {
                name = name,
                columns = new List<ColumnData>(),
                rows = new List<Dictionary<string, object>>(),
            };
            foreach (var m in members) table.columns.Add(ColumnFor(m));
            if (obj != null) table.rows.Add(RowFromObject(obj, members));
            return table;
        }

        /// <summary>Build a key/value table from a dictionary.</summary>
        protected static TableData TableFromDictionary(string name, IDictionary dict)
        {
            var table = new TableData
            {
                name = name,
                columns = new List<ColumnData>
                {
                    new ColumnData { name = "key",   type = "string", nullable = false },
                    new ColumnData { name = "value", type = "string", nullable = true  },
                },
                rows = new List<Dictionary<string, object>>(),
            };
            if (dict == null) return table;
            foreach (DictionaryEntry e in dict)
            {
                table.rows.Add(new Dictionary<string, object>
                {
                    { "key",   e.Key?.ToString() },
                    { "value", e.Value },
                });
            }
            return table;
        }

        // -----------------------------------------------------------------
        // Reflection helpers (shared with all plugins)
        // -----------------------------------------------------------------

        protected static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { var t = asm.GetType(fullName, false); if (t != null) return t; }
                catch { }
            }
            return null;
        }

        protected static List<MemberInfo> GetReadableMembers(Type type)
        {
            var list = new List<MemberInfo>();
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                list.Add(f);
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.CanRead && p.GetIndexParameters().Length == 0) list.Add(p);
            return list;
        }

        static ColumnData ColumnFor(MemberInfo m)
        {
            var t = m is FieldInfo fi ? fi.FieldType : ((PropertyInfo)m).PropertyType;
            return new ColumnData { name = m.Name, type = TypeName(t), nullable = true };
        }

        static Dictionary<string, object> RowFromObject(object obj, List<MemberInfo> members)
        {
            var row = new Dictionary<string, object>(members.Count);
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                object val = null;
                try { val = m is FieldInfo fi ? fi.GetValue(obj) : ((PropertyInfo)m).GetValue(obj); }
                catch { }
                row[m.Name] = val;
            }
            return row;
        }

        static string TypeName(Type t)
        {
            if (t == typeof(int))    return "int";
            if (t == typeof(long))   return "long";
            if (t == typeof(float))  return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(bool))   return "bool";
            if (t == typeof(string)) return "string";
            return t.Name;
        }
    }
}
