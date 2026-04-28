using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ODDGames.Bugpunch.DeviceConnect.Database
{
    /// <summary>
    /// Database plugin for Siaqodb. Detected via reflection — only activates
    /// when <c>Sqo.Siaqodb</c> is present in the project.
    /// </summary>
    public class SiaqodbPlugin : DatabasePluginBase
    {
        public override string ProviderId => "sqo";
        public override string DisplayName => "Siaqodb";
        public override string[] Extensions => new[] { ".sqo" };

        Type _siaqodbType;
        MethodInfo _loadAllGeneric;
        MethodInfo _getAllTypes;

        public override bool IsAvailable()
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

        public override ParseResult Parse(string filePath)
        {
            // Siaqodb operates on a directory containing .sqo files.
            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return Fail("Directory not found: " + dir);

            object db = null;
            try
            {
                db = Activator.CreateInstance(_siaqodbType, dir);
                var storedTypes = _getAllTypes.Invoke(db, null) as IList;
                if (storedTypes == null || storedTypes.Count == 0)
                    return Ok();

                var tables = new List<TableData>(storedTypes.Count);
                foreach (Type storedType in storedTypes)
                {
                    var loadAll = _loadAllGeneric.MakeGenericMethod(storedType);
                    var rows = loadAll.Invoke(db, null) as IEnumerable;
                    tables.Add(TableFromObjects(storedType.Name, rows));
                }
                return Ok(tables);
            }
            finally
            {
                if (db is IDisposable d) d.Dispose();
            }
        }
    }
}
