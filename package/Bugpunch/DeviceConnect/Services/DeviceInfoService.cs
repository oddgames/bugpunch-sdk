using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Returns a comprehensive JSON blob with every available Unity SystemInfo property
    /// and other runtime info about the connected device.
    /// </summary>
    public class DeviceInfoService
    {
        public string GetDeviceInfo()
        {
            var sb = new StringBuilder(4096);
            sb.Append("{");

            // Device
            Js(sb, "deviceName", SystemInfo.deviceName);
            Js(sb, "deviceModel", SystemInfo.deviceModel);
            Js(sb, "deviceType", SystemInfo.deviceType.ToString());
            Try(sb, "deviceUniqueIdentifier", () => SystemInfo.deviceUniqueIdentifier);

            // OS
            Js(sb, "operatingSystem", SystemInfo.operatingSystem);
            Js(sb, "operatingSystemFamily", SystemInfo.operatingSystemFamily.ToString());

            // CPU
            Js(sb, "processorType", SystemInfo.processorType);
            Ji(sb, "processorCount", SystemInfo.processorCount);
            Ji(sb, "processorFrequency", SystemInfo.processorFrequency);

            // Memory
            Ji(sb, "systemMemorySize", SystemInfo.systemMemorySize);

            // GPU
            Js(sb, "graphicsDeviceName", SystemInfo.graphicsDeviceName);
            Js(sb, "graphicsDeviceVendor", SystemInfo.graphicsDeviceVendor);
            Js(sb, "graphicsDeviceVersion", SystemInfo.graphicsDeviceVersion);
            Js(sb, "graphicsDeviceType", SystemInfo.graphicsDeviceType.ToString());
            Ji(sb, "graphicsDeviceID", SystemInfo.graphicsDeviceID);
            Ji(sb, "graphicsDeviceVendorID", SystemInfo.graphicsDeviceVendorID);
            Ji(sb, "graphicsMemorySize", SystemInfo.graphicsMemorySize);
            Jb(sb, "graphicsMultiThreaded", SystemInfo.graphicsMultiThreaded);
            Ji(sb, "graphicsShaderLevel", SystemInfo.graphicsShaderLevel);
            Jb(sb, "graphicsUVStartsAtTop", SystemInfo.graphicsUVStartsAtTop);
            Ji(sb, "maxTextureSize", SystemInfo.maxTextureSize);
            Ji(sb, "maxCubemapSize", SystemInfo.maxCubemapSize);
            Ji(sb, "maxComputeBufferInputsVertex", SystemInfo.maxComputeBufferInputsVertex);
            Js(sb, "npotSupport", SystemInfo.npotSupport.ToString());

            // Rendering
            Ji(sb, "supportedRenderTargetCount", SystemInfo.supportedRenderTargetCount);
            Jb(sb, "supportsInstancing", SystemInfo.supportsInstancing);
            Jb(sb, "supportsComputeShaders", SystemInfo.supportsComputeShaders);
            Jb(sb, "supportsGeometryShaders", SystemInfo.supportsGeometryShaders);
            Jb(sb, "supportsTessellationShaders", SystemInfo.supportsTessellationShaders);
            Jb(sb, "supportsRayTracing", SystemInfo.supportsRayTracing);
            Jb(sb, "supports2DArrayTextures", SystemInfo.supports2DArrayTextures);
            Jb(sb, "supports3DTextures", SystemInfo.supports3DTextures);
            Jb(sb, "supportsSparseTextures", SystemInfo.supportsSparseTextures);
            Jb(sb, "supportsMotionVectors", SystemInfo.supportsMotionVectors);
            Jb(sb, "supportsMultisampleAutoResolve", SystemInfo.supportsMultisampleAutoResolve);
            Jb(sb, "supportsAsyncCompute", SystemInfo.supportsAsyncCompute);
            Jb(sb, "supportsAsyncGPUReadback", SystemInfo.supportsAsyncGPUReadback);

            // Screen
            Ji(sb, "screenWidth", Screen.width);
            Ji(sb, "screenHeight", Screen.height);
            Jf(sb, "screenDpi", Screen.dpi);
            Try(sb, "screenCurrentResolution", () =>
            {
                var res = Screen.currentResolution;
                return $"{res.width}x{res.height}@{res.refreshRateRatio}";
            });
            Jb(sb, "screenFullScreen", Screen.fullScreen);
            Js(sb, "screenOrientation", Screen.orientation.ToString());

            // Battery
            Jf(sb, "batteryLevel", SystemInfo.batteryLevel);
            Js(sb, "batteryStatus", SystemInfo.batteryStatus.ToString());

            // Application
            Js(sb, "appVersion", Application.version);
            Js(sb, "appIdentifier", Application.identifier);
            Js(sb, "appProductName", Application.productName);
            Js(sb, "appCompanyName", Application.companyName);
            Js(sb, "appPlatform", Application.platform.ToString());
            Js(sb, "appUnityVersion", Application.unityVersion);
            Js(sb, "appSystemLanguage", Application.systemLanguage.ToString());
            Js(sb, "appInternetReachability", Application.internetReachability.ToString());
            Jb(sb, "appIsEditor", Application.isEditor);
            Jb(sb, "appIsFocused", Application.isFocused);
            Jb(sb, "appRunInBackground", Application.runInBackground);
            Ji(sb, "appTargetFrameRate", Application.targetFrameRate);
            Jb(sb, "appIsDebugBuild", Debug.isDebugBuild);

            // Quality
            try
            {
                Ji(sb, "qualityLevel", QualitySettings.GetQualityLevel());
                Js(sb, "qualityName", QualitySettings.names[QualitySettings.GetQualityLevel()]);
            }
            catch (Exception) { /* unavailable */ }
            Ji(sb, "vsyncCount", QualitySettings.vSyncCount);
            Ji(sb, "antiAliasing", QualitySettings.antiAliasing);
            Js(sb, "anisotropicFiltering", QualitySettings.anisotropicFiltering.ToString());
            Js(sb, "shadowQuality", QualitySettings.shadows.ToString());
            Js(sb, "shadowResolution", QualitySettings.shadowResolution.ToString());
            Try(sb, "textureQuality", () => QualitySettings.globalTextureMipmapLimit.ToString());

            // Paths
            Js(sb, "persistentDataPath", Application.persistentDataPath);
            Js(sb, "temporaryCachePath", Application.temporaryCachePath);
            Js(sb, "dataPath", Application.dataPath);
            Js(sb, "streamingAssetsPath", Application.streamingAssetsPath);
            Try(sb, "consoleLogPath", () => Application.consoleLogPath, last: true);

            // Remove trailing comma if present, then close
            if (sb.Length > 1 && sb[sb.Length - 1] == ',')
                sb.Length--;

            sb.Append("}");
            return sb.ToString();
        }

        // -- Helpers for type-safe JSON emission --

        void Js(StringBuilder sb, string key, string value, bool last = false)
        {
            sb.Append($"\"{key}\":\"{Esc(value ?? "")}\"{(last ? "" : ",")}");
        }

        void Ji(StringBuilder sb, string key, int value, bool last = false)
        {
            sb.Append($"\"{key}\":{value}{(last ? "" : ",")}");
        }

        void Jf(StringBuilder sb, string key, float value, bool last = false)
        {
            sb.Append(string.Format(CultureInfo.InvariantCulture, "\"{0}\":{1:G}{2}", key, value, last ? "" : ","));
        }

        void Jb(StringBuilder sb, string key, bool value, bool last = false)
        {
            sb.Append($"\"{key}\":{(value ? "true" : "false")}{(last ? "" : ",")}");
        }

        /// <summary>
        /// Try to get a string value from a delegate. Some SystemInfo properties throw on certain platforms.
        /// </summary>
        void Try(StringBuilder sb, string key, Func<string> getter, bool last = false)
        {
            try
            {
                var value = getter();
                Js(sb, key, value, last);
            }
            catch (Exception)
            {
                Js(sb, key, "N/A", last);
            }
        }

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";
    }
}
