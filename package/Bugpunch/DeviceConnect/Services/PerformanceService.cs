using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace ODDGames.Bugpunch.DeviceConnect
{
    public class PerformanceService
    {
        /// <summary>
        /// Get current performance metrics as JSON
        /// </summary>
        public string GetMetrics()
        {
            var sb = new StringBuilder();
            sb.Append("{");

            // Frame timing
            var dt = Time.deltaTime;
            var fps = dt > 0f ? 1f / dt : 0f;
            sb.Append(string.Format(CultureInfo.InvariantCulture, "\"fps\":{0:F1},", fps));
            sb.Append(string.Format(CultureInfo.InvariantCulture, "\"frameTime\":{0:F2},", dt * 1000f));
            sb.Append($"\"frameCount\":{Time.frameCount},");

            // Memory (Profiler)
            long totalAlloc = 0, monoHeap = 0, monoUsed = 0;
            try
            {
                totalAlloc = Profiler.GetTotalAllocatedMemoryLong();
                monoHeap = Profiler.GetMonoHeapSizeLong();
                monoUsed = Profiler.GetMonoUsedSizeLong();
            }
            catch (Exception)
            {
                // Some platforms may not support profiler queries
            }

            sb.Append(string.Format(CultureInfo.InvariantCulture, "\"memory\":{0:F1},", totalAlloc / (1024f * 1024f)));
            sb.Append(string.Format(CultureInfo.InvariantCulture, "\"monoHeap\":{0:F1},", monoHeap / (1024f * 1024f)));
            sb.Append(string.Format(CultureInfo.InvariantCulture, "\"monoUsed\":{0:F1},", monoUsed / (1024f * 1024f)));

            // GC
            sb.Append($"\"gcCount\":{GC.CollectionCount(0)},");

            // Settings
            sb.Append($"\"targetFps\":{Application.targetFrameRate},");
            sb.Append($"\"vsync\":{QualitySettings.vSyncCount},");

            // Platform info
            sb.Append($"\"platform\":\"{Esc(Application.platform.ToString())}\",");
            sb.Append($"\"unityVersion\":\"{Esc(Application.unityVersion)}\",");
            sb.Append($"\"graphicsDevice\":\"{Esc(SystemInfo.graphicsDeviceName)}\",");
            sb.Append($"\"graphicsMemory\":{SystemInfo.graphicsMemorySize},");
            sb.Append($"\"systemMemory\":{SystemInfo.systemMemorySize},");
            sb.Append($"\"processorType\":\"{Esc(SystemInfo.processorType)}\",");
            sb.Append($"\"processorCount\":{SystemInfo.processorCount},");

            // Screen
            sb.Append($"\"screenRes\":\"{Screen.width}x{Screen.height}\",");

            // Quality
            string qualityName;
            try
            {
                qualityName = QualitySettings.names[QualitySettings.GetQualityLevel()];
            }
            catch (Exception)
            {
                qualityName = "Unknown";
            }
            sb.Append($"\"quality\":\"{Esc(qualityName)}\",");

            // Battery
            sb.Append(string.Format(CultureInfo.InvariantCulture, "\"batteryLevel\":{0:F2},", SystemInfo.batteryLevel));
            sb.Append($"\"batteryStatus\":\"{SystemInfo.batteryStatus}\"");

            sb.Append("}");
            return sb.ToString();
        }

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";
    }
}
