using System;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Persistent deviceId for this client. Mobile defers to the native
    /// <c>BugpunchTunnel</c>'s persisted UUID — that's the id the server's
    /// tunnels map is keyed by, so any C# code that needs to refer to "this
    /// device" must agree with native or it'll point at nothing. Editor /
    /// Standalone keep the deterministic MD5 fallback so local dev isn't
    /// dependent on the native plugin.
    /// </summary>
    public static class DeviceIdentity
    {
        static string _cachedId;

        public static string GetDeviceId()
        {
            if (!string.IsNullOrEmpty(_cachedId)) return _cachedId;

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            var nativeId = BugpunchNative.TunnelDeviceId();
            if (!string.IsNullOrEmpty(nativeId))
            {
                _cachedId = nativeId;
                return _cachedId;
            }
            // Native tunnel not yet up — fall through to the deterministic
            // hash so callers don't get an empty id during early init.
#endif

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
