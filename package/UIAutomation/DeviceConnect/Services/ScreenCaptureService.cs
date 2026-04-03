using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ODDGames.UIAutomation.DeviceConnect
{
    public class ScreenCaptureService : MonoBehaviour
    {
        /// <summary>
        /// Capture the screen as JPEG bytes. Must be called after WaitForEndOfFrame.
        /// </summary>
        public byte[] CaptureScreen(float scale = 0.5f, int quality = 75)
        {
            try
            {
                var screenWidth = Screen.width;
                var screenHeight = Screen.height;
                var targetWidth = Mathf.Max(1, Mathf.RoundToInt(screenWidth * scale));
                var targetHeight = Mathf.Max(1, Mathf.RoundToInt(screenHeight * scale));

                // Read screen pixels
                var screenTex = new Texture2D(screenWidth, screenHeight, TextureFormat.RGB24, false);
                screenTex.ReadPixels(new Rect(0, 0, screenWidth, screenHeight), 0, 0);
                screenTex.Apply();

                // Scale down if needed
                Texture2D outputTex;
                if (scale < 1f)
                {
                    outputTex = ScaleTexture(screenTex, targetWidth, targetHeight);
                    Destroy(screenTex);
                }
                else
                {
                    outputTex = screenTex;
                }

                // Encode to JPEG
                var bytes = outputTex.EncodeToJPG(quality);
                Destroy(outputTex);

                return bytes;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OddDev] Screen capture failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get list of active cameras as JSON
        /// </summary>
        public string GetCameras()
        {
            var sb = new StringBuilder();
            sb.Append("[");
            var cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var cam = cameras[i];
                sb.Append($"{{\"name\":\"{EscapeJson(cam.name)}\",\"depth\":{cam.depth},\"enabled\":{(cam.enabled ? "true" : "false")},\"instanceId\":{cam.GetInstanceID()}}}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        static Texture2D ScaleTexture(Texture2D source, int targetWidth, int targetHeight)
        {
            var rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;

            Graphics.Blit(source, rt);

            var result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            return result;
        }

        static string EscapeJson(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
    }
}
