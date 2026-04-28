using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ODDGames.Bugpunch.RemoteIDE
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

                // Capture into an explicit RT instead of ReadPixels-on-backbuffer.
                // The old path raced with the WebRTC render loop: when the
                // streamer had its 960x540 RT bound as active, a ReadPixels
                // sized for the full screen spilled outside that RT and Unity
                // spammed "attempting to ReadPixels outside of RenderTexture
                // bounds!". Going through Blit is also faster (GPU-only) and
                // handles the Y-flip from CaptureScreenshotIntoRenderTexture.
                var screenRT = RenderTexture.GetTemporary(screenWidth, screenHeight, 0);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(screenRT);

                var targetRT = RenderTexture.GetTemporary(targetWidth, targetHeight, 0);
                Graphics.Blit(screenRT, targetRT, new Vector2(1, -1), new Vector2(0, 1));

                var prevActive = RenderTexture.active;
                RenderTexture.active = targetRT;
                var outputTex = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
                outputTex.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                outputTex.Apply();
                RenderTexture.active = prevActive;

                RenderTexture.ReleaseTemporary(screenRT);
                RenderTexture.ReleaseTemporary(targetRT);

                var bytes = outputTex.EncodeToJPG(quality);
                Destroy(outputTex);

                return bytes;
            }
            catch (Exception ex)
            {
                BugpunchNative.ReportSdkError("ScreenCaptureService.CaptureScreen", ex);
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
                sb.Append($"{{\"name\":\"{BugpunchJson.Esc(cam.name)}\",\"depth\":{cam.depth},\"enabled\":{(cam.enabled ? "true" : "false")},\"instanceId\":{cam.GetInstanceID()}}}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Capture from a specific camera by instance ID. Does not require WaitForEndOfFrame.
        /// </summary>
        public byte[] CaptureFromCamera(int instanceId, float scale = 0.5f, int quality = 75)
        {
            try
            {
                Camera cam = null;
                foreach (var c in Camera.allCameras)
                {
                    if (c.GetInstanceID() == instanceId) { cam = c; break; }
                }
                if (cam == null) return null;

                var w = Mathf.Max(1, Mathf.RoundToInt(cam.pixelWidth * scale));
                var h = Mathf.Max(1, Mathf.RoundToInt(cam.pixelHeight * scale));

                var rt = RenderTexture.GetTemporary(w, h, 24);
                var prev = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prev;

                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);

                var bytes = tex.EncodeToJPG(quality);
                Destroy(tex);
                return bytes;
            }
            catch (Exception ex)
            {
                BugpunchNative.ReportSdkError("ScreenCaptureService.CaptureFromCamera", ex);
                return null;
            }
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
    }
}
