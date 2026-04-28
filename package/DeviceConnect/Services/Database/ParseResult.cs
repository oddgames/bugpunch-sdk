using System.Collections.Generic;

namespace ODDGames.Bugpunch.DeviceConnect.Database
{
    /// <summary>
    /// Standard wire format for the database viewer. Plugins build one of
    /// these and the registry takes care of JSON serialization. Field names
    /// match the JSON keys the dashboard expects (<c>ok</c>, <c>tables</c>,
    /// <c>columns</c>, <c>rows</c>) — do not rename without updating the
    /// server-side <c>DeviceProxyHandle</c>.
    /// </summary>
    public class ParseResult
    {
        public bool ok;
        public string error;
        public List<TableData> tables;
    }

    public class TableData
    {
        public string name;
        public List<ColumnData> columns;
        public List<Dictionary<string, object>> rows;
    }

    public class ColumnData
    {
        public string name;
        public string type;
        public bool nullable = true;
    }
}
