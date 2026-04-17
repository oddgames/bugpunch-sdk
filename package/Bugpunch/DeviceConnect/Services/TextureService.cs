using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Lists all loaded textures (Texture2D, RenderTexture, etc.) and generates
    /// JPEG thumbnails on demand. Used by the Remote IDE Textures panel.
    /// </summary>
    public class TextureService
    {
        /// <summary>
        /// Return JSON array of all loaded textures.
        /// Each entry: { id, name, type, width, height, format, memoryKB, filterMode, wrapMode, mipmapCount, isReadable }
        /// Optional query filter by name substring.
        /// </summary>
        public string ListTextures(string filter = null, string typeFilter = null)
        {
            var textures = Resources.FindObjectsOfTypeAll<Texture>();
            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;

            foreach (var tex in textures)
            {
                if (tex == null) continue;
                // Skip editor-only textures
                if (tex.hideFlags.HasFlag(HideFlags.HideAndDontSave)) continue;

                var name = tex.name;
                if (string.IsNullOrEmpty(name)) name = "(unnamed)";

                // Name filter
                if (!string.IsNullOrEmpty(filter) &&
                    name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // Type filter
                var typeName = GetTextureType(tex);
                if (!string.IsNullOrEmpty(typeFilter) &&
                    !typeName.Equals(typeFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!first) sb.Append(",");
                first = false;

                var w = tex.width;
                var h = tex.height;
                var format = GetTextureFormat(tex);
                var memKB = (UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex) + 512) / 1024;

                sb.Append("{");
                sb.Append($"\"id\":{tex.GetInstanceID()},");
                sb.Append($"\"name\":\"{Esc(name)}\",");
                sb.Append($"\"type\":\"{typeName}\",");
                sb.Append($"\"width\":{w},");
                sb.Append($"\"height\":{h},");
                sb.Append($"\"format\":\"{Esc(format)}\",");
                sb.Append($"\"memoryKB\":{memKB},");
                sb.Append($"\"filterMode\":\"{tex.filterMode}\",");
                sb.Append($"\"wrapMode\":\"{tex.wrapMode}\",");
                sb.Append($"\"mipmapCount\":{tex.mipmapCount},");
                sb.Append($"\"isReadable\":{(IsReadable(tex) ? "true" : "false")}");
                sb.Append("}");
            }

            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Render a texture thumbnail as JPEG bytes.
        /// Works for Texture2D (readable or not), RenderTexture, and CubeMap (front face).
        /// </summary>
        public byte[] GetThumbnail(int instanceId, int maxSize = 128, int quality = 75)
        {
            try
            {
                var tex = FindTexture(instanceId);
                if (tex == null) return null;

                // Determine source dimensions
                int srcW = tex.width, srcH = tex.height;
                if (srcW == 0 || srcH == 0) return null;

                // Calculate thumbnail dimensions maintaining aspect ratio
                float aspect = (float)srcW / srcH;
                int thumbW, thumbH;
                if (srcW >= srcH)
                {
                    thumbW = Mathf.Min(srcW, maxSize);
                    thumbH = Mathf.Max(1, Mathf.RoundToInt(thumbW / aspect));
                }
                else
                {
                    thumbH = Mathf.Min(srcH, maxSize);
                    thumbW = Mathf.Max(1, Mathf.RoundToInt(thumbH * aspect));
                }

                // Blit through a temporary RT — works for readable, non-readable, RT, cubemap
                var rt = RenderTexture.GetTemporary(thumbW, thumbH, 0, RenderTextureFormat.ARGB32);
                var prev = RenderTexture.active;

                if (tex is Cubemap cube)
                {
                    // For cubemaps, just show the +Z (front) face
                    var faceTex = new Texture2D(cube.width, cube.width, TextureFormat.RGBA32, false);
                    Graphics.CopyTexture(cube, 2, 0, faceTex, 0, 0); // face 2 = +Z
                    Graphics.Blit(faceTex, rt);
                    UnityEngine.Object.Destroy(faceTex);
                }
                else
                {
                    Graphics.Blit(tex, rt);
                }

                RenderTexture.active = rt;
                var thumb = new Texture2D(thumbW, thumbH, TextureFormat.RGB24, false);
                thumb.ReadPixels(new Rect(0, 0, thumbW, thumbH), 0, 0);
                thumb.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                var bytes = thumb.EncodeToJPG(quality);
                UnityEngine.Object.Destroy(thumb);
                return bytes;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch] Texture thumbnail failed (id={instanceId}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Render the full texture as JPEG bytes (optionally scaled).
        /// </summary>
        public byte[] GetFullTexture(int instanceId, float scale = 1f, int quality = 85)
        {
            try
            {
                var tex = FindTexture(instanceId);
                if (tex == null) return null;

                int w = Mathf.Max(1, Mathf.RoundToInt(tex.width * scale));
                int h = Mathf.Max(1, Mathf.RoundToInt(tex.height * scale));

                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                var prev = RenderTexture.active;

                if (tex is Cubemap cube)
                {
                    var faceTex = new Texture2D(cube.width, cube.width, TextureFormat.RGBA32, false);
                    Graphics.CopyTexture(cube, 2, 0, faceTex, 0, 0);
                    Graphics.Blit(faceTex, rt);
                    UnityEngine.Object.Destroy(faceTex);
                }
                else
                {
                    Graphics.Blit(tex, rt);
                }

                RenderTexture.active = rt;
                var output = new Texture2D(w, h, TextureFormat.RGB24, false);
                output.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                output.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                var bytes = output.EncodeToJPG(quality);
                UnityEngine.Object.Destroy(output);
                return bytes;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch] Full texture capture failed (id={instanceId}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get detailed info for a single texture as JSON.
        /// </summary>
        public string GetTextureInfo(int instanceId)
        {
            var tex = FindTexture(instanceId);
            if (tex == null) return "{\"error\":\"Texture not found\"}";

            var name = string.IsNullOrEmpty(tex.name) ? "(unnamed)" : tex.name;
            var memKB = (UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex) + 512) / 1024;
            var typeName = GetTextureType(tex);
            var format = GetTextureFormat(tex);

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"id\":{tex.GetInstanceID()},");
            sb.Append($"\"name\":\"{Esc(name)}\",");
            sb.Append($"\"type\":\"{typeName}\",");
            sb.Append($"\"width\":{tex.width},");
            sb.Append($"\"height\":{tex.height},");
            sb.Append($"\"format\":\"{Esc(format)}\",");
            sb.Append($"\"memoryKB\":{memKB},");
            sb.Append($"\"filterMode\":\"{tex.filterMode}\",");
            sb.Append($"\"wrapMode\":\"{tex.wrapMode}\",");
            sb.Append($"\"mipmapCount\":{tex.mipmapCount},");
            sb.Append($"\"anisoLevel\":{tex.anisoLevel},");
            sb.Append($"\"isReadable\":{(IsReadable(tex) ? "true" : "false")}");

            if (tex is RenderTexture rtex)
            {
                sb.Append($",\"depth\":{rtex.depth}");
                sb.Append($",\"antiAliasing\":{rtex.antiAliasing}");
                sb.Append($",\"volumeDepth\":{rtex.volumeDepth}");
                sb.Append($",\"useMipMap\":{(rtex.useMipMap ? "true" : "false")}");
            }

            sb.Append("}");
            return sb.ToString();
        }

        static Texture FindTexture(int instanceId)
        {
            var all = Resources.FindObjectsOfTypeAll<Texture>();
            foreach (var tex in all)
            {
                if (tex != null && tex.GetInstanceID() == instanceId)
                    return tex;
            }
            return null;
        }

        static string GetTextureType(Texture tex)
        {
            if (tex is RenderTexture) return "RenderTexture";
            if (tex is Texture2D) return "Texture2D";
            if (tex is Cubemap) return "Cubemap";
            if (tex is Texture2DArray) return "Texture2DArray";
            if (tex is Texture3D) return "Texture3D";
            return tex.GetType().Name;
        }

        static string GetTextureFormat(Texture tex)
        {
            if (tex is Texture2D t2d) return t2d.format.ToString();
            if (tex is RenderTexture rt) return rt.format.ToString();
            if (tex is Cubemap cube) return cube.format.ToString();
            return "Unknown";
        }

        static bool IsReadable(Texture tex)
        {
            if (tex is Texture2D t2d)
            {
                try { t2d.GetPixel(0, 0); return true; }
                catch { return false; }
            }
            return tex is RenderTexture;
        }

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";
    }
}
