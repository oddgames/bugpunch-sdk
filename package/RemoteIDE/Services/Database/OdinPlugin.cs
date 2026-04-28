using System;
using System.Collections;
using System.IO;
using System.Reflection;

namespace ODDGames.Bugpunch.RemoteIDE.Database
{
    /// <summary>
    /// Database plugin for Odin Serializer. Detected via reflection — only
    /// activates when <c>Sirenix.Serialization</c> is present in the project.
    /// </summary>
    public class OdinPlugin : DatabasePluginBase
    {
        public override string ProviderId => "odin";
        public override string DisplayName => "Odin Serializer";
        public override string[] Extensions => new[] { ".odin", ".bytes" };

        Type _serializationUtility;
        Type _dataFormat;
        MethodInfo _deserializeWeak;
        object _binaryFormat;
        object _jsonFormat;

        public override bool IsAvailable()
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

        public override ParseResult Parse(string filePath)
        {
            if (!File.Exists(filePath))
                return Fail("File not found: " + filePath);

            byte[] bytes;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                bytes = new byte[fs.Length];
                fs.Read(bytes, 0, bytes.Length);
            }

            // Try Binary first, then JSON — same logic as Odin's own code path.
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
                return Fail("Failed to deserialize — not a recognized Odin Binary or JSON format");

            var rootName = deserialized.GetType().Name;

            if (deserialized is IList list && list.Count > 0)
                return Ok(TableFromObjects(list[0]?.GetType().Name ?? rootName, list));

            if (deserialized is IDictionary dict)
                return Ok(TableFromDictionary("root", dict));

            return Ok(TableFromObject(rootName, deserialized));
        }
    }
}
