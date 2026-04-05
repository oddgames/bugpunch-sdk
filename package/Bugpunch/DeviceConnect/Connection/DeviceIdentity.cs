using System;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Generates a deterministic device ID that persists across reconnects and restarts.
    /// Editor: MD5(projectPath + machineName) — unique per project per machine.
    /// Mobile: MD5(deviceUniqueIdentifier) — unique per physical device.
    /// </summary>
    public static class DeviceIdentity
    {
        static string _cachedId;

        public static string GetDeviceId()
        {
            if (_cachedId != null) return _cachedId;

            string raw;
            if (Application.isEditor)
                raw = Application.dataPath + "|" + SystemInfo.deviceName;
            else
                raw = SystemInfo.deviceUniqueIdentifier;

            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(raw));
                _cachedId = new Guid(hash).ToString();
            }
            return _cachedId;
        }
    }
}
