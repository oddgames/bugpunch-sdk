using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Lists all loaded materials and renders preview thumbnails on a sphere.
    /// Thumbnails are rendered off-screen using a temporary camera + lit sphere.
    /// </summary>
    public class MaterialService : MonoBehaviour
    {
        // Reusable preview resources — created once, destroyed with the service
        Camera _previewCam;
        GameObject _previewSphere;
        Light _previewLight;
        Light _previewFillLight;
        GameObject _previewRoot; // parent container, positioned far away

        void OnDestroy()
        {
            if (_previewRoot != null) Destroy(_previewRoot);
        }

        /// <summary>
        /// Return JSON array of all loaded materials.
        /// </summary>
        public string ListMaterials()
        {
            var materials = Resources.FindObjectsOfTypeAll<Material>();
            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;
            foreach (var mat in materials)
            {
                if (mat == null) continue;
                // Skip internal/hidden materials
                if (mat.hideFlags != HideFlags.None) continue;
                var name = mat.name;
                if (string.IsNullOrEmpty(name)) continue;

                if (!first) sb.Append(",");
                first = false;

                var shader = mat.shader != null ? mat.shader.name : "";
                var instanceId = mat.GetInstanceID();

                // Get main texture name if any
                var texName = "";
                if (mat.HasProperty("_MainTex"))
                {
                    var tex = mat.mainTexture;
                    if (tex != null) texName = tex.name;
                }
                else if (mat.HasProperty("_BaseMap"))
                {
                    var tex = mat.GetTexture("_BaseMap");
                    if (tex != null) texName = tex.name;
                }

                var color = "";
                if (mat.HasProperty("_Color"))
                {
                    var c = mat.color;
                    color = $"#{ColorUtility.ToHtmlStringRGBA(c)}";
                }
                else if (mat.HasProperty("_BaseColor"))
                {
                    var c = mat.GetColor("_BaseColor");
                    color = $"#{ColorUtility.ToHtmlStringRGBA(c)}";
                }

                sb.Append("{");
                sb.Append($"\"id\":{instanceId}");
                sb.Append($",\"name\":\"{Esc(name)}\"");
                sb.Append($",\"shader\":\"{Esc(shader)}\"");
                if (!string.IsNullOrEmpty(texName))
                    sb.Append($",\"texture\":\"{Esc(texName)}\"");
                if (!string.IsNullOrEmpty(color))
                    sb.Append($",\"color\":\"{color}\"");
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Render a material on a sphere and return JPEG bytes.
        /// Must be called from a coroutine (needs rendering).
        /// </summary>
        public byte[] RenderThumbnail(int instanceId, int size = 128, int quality = 80)
        {
            var mat = FindMaterial(instanceId);
            if (mat == null) return null;

            EnsurePreviewScene();

            try
            {
                // Apply material to sphere
                _previewSphere.GetComponent<MeshRenderer>().sharedMaterial = mat;

                var rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 4;
                _previewCam.targetTexture = rt;
                _previewCam.Render();
                _previewCam.targetTexture = null;

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                var bytes = tex.EncodeToJPG(quality);
                Destroy(tex);
                return bytes;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch] Material thumbnail failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extract the main texture from a material and return it as PNG bytes.
        /// Returns null if the material has no readable texture.
        /// </summary>
        public byte[] GetTexture(int instanceId, string propertyName = null, int maxSize = 1024)
        {
            var mat = FindMaterial(instanceId);
            if (mat == null) return null;

            try
            {
                Texture tex = null;
                if (!string.IsNullOrEmpty(propertyName) && mat.HasProperty(propertyName))
                {
                    tex = mat.GetTexture(propertyName);
                }
                else
                {
                    // Try common texture properties
                    foreach (var prop in new[] { "_MainTex", "_BaseMap", "_BumpMap", "_NormalMap",
                                                  "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap",
                                                  "_DetailAlbedoMap", "_DetailNormalMap", "_ParallaxMap" })
                    {
                        if (mat.HasProperty(prop))
                        {
                            var t = mat.GetTexture(prop);
                            if (t != null) { tex = t; break; }
                        }
                    }
                }

                if (tex == null) return null;

                // Blit to a readable RenderTexture to handle non-readable textures
                var w = Mathf.Min(tex.width, maxSize);
                var h = Mathf.Min(tex.height, maxSize);
                if (tex.width > maxSize || tex.height > maxSize)
                {
                    float aspect = (float)tex.width / tex.height;
                    if (tex.width > tex.height) { w = maxSize; h = Mathf.Max(1, Mathf.RoundToInt(maxSize / aspect)); }
                    else { h = maxSize; w = Mathf.Max(1, Mathf.RoundToInt(maxSize * aspect)); }
                }

                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(tex, rt);

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                readable.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                var bytes = readable.EncodeToPNG();
                Destroy(readable);
                return bytes;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bugpunch] Material texture export failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// List all texture properties on a material.
        /// </summary>
        public string GetTextureProperties(int instanceId)
        {
            var mat = FindMaterial(instanceId);
            if (mat == null) return "[]";

            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;

            var shader = mat.shader;
            int propCount = shader.GetPropertyCount();
            for (int i = 0; i < propCount; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                var propName = shader.GetPropertyName(i);
                var tex = mat.GetTexture(propName);
                if (tex == null) continue;

                if (!first) sb.Append(",");
                first = false;

                var desc = shader.GetPropertyDescription(i);
                sb.Append("{");
                sb.Append($"\"property\":\"{Esc(propName)}\"");
                sb.Append($",\"label\":\"{Esc(desc)}\"");
                sb.Append($",\"textureName\":\"{Esc(tex.name)}\"");
                sb.Append($",\"width\":{tex.width}");
                sb.Append($",\"height\":{tex.height}");
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Preview scene setup
        // ------------------------------------------------------------------

        void EnsurePreviewScene()
        {
            if (_previewRoot != null) return;

            // Put everything far away so it doesn't interfere with the game
            var farPos = new Vector3(0, -10000, 0);

            _previewRoot = new GameObject("__BugpunchMaterialPreview__");
            _previewRoot.transform.position = farPos;
            _previewRoot.hideFlags = HideFlags.HideAndDontSave;

            // Sphere
            _previewSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _previewSphere.transform.SetParent(_previewRoot.transform, false);
            _previewSphere.transform.localPosition = Vector3.zero;
            _previewSphere.hideFlags = HideFlags.HideAndDontSave;
            // Remove collider — not needed for preview
            var col = _previewSphere.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Camera
            var camGo = new GameObject("PreviewCam");
            camGo.transform.SetParent(_previewRoot.transform, false);
            camGo.transform.localPosition = new Vector3(0, 0, -2.2f);
            camGo.transform.LookAt(_previewSphere.transform);
            camGo.hideFlags = HideFlags.HideAndDontSave;
            _previewCam = camGo.AddComponent<Camera>();
            _previewCam.enabled = false; // manual render only
            _previewCam.clearFlags = CameraClearFlags.SolidColor;
            _previewCam.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f);
            _previewCam.fieldOfView = 30f;
            _previewCam.nearClipPlane = 0.1f;
            _previewCam.farClipPlane = 10f;
            _previewCam.cullingMask = ~0; // render everything on this layer

            // Key light
            var lightGo = new GameObject("PreviewLight");
            lightGo.transform.SetParent(_previewRoot.transform, false);
            lightGo.transform.localPosition = new Vector3(1.5f, 2f, -1.5f);
            lightGo.transform.LookAt(_previewSphere.transform);
            lightGo.hideFlags = HideFlags.HideAndDontSave;
            _previewLight = lightGo.AddComponent<Light>();
            _previewLight.type = LightType.Directional;
            _previewLight.intensity = 1.0f;
            _previewLight.color = Color.white;

            // Fill light
            var fillGo = new GameObject("PreviewFillLight");
            fillGo.transform.SetParent(_previewRoot.transform, false);
            fillGo.transform.localPosition = new Vector3(-1f, -0.5f, -2f);
            fillGo.transform.LookAt(_previewSphere.transform);
            fillGo.hideFlags = HideFlags.HideAndDontSave;
            _previewFillLight = fillGo.AddComponent<Light>();
            _previewFillLight.type = LightType.Directional;
            _previewFillLight.intensity = 0.4f;
            _previewFillLight.color = new Color(0.8f, 0.85f, 1f);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        static Material FindMaterial(int instanceId)
        {
            var all = Resources.FindObjectsOfTypeAll<Material>();
            foreach (var m in all)
            {
                if (m != null && m.GetInstanceID() == instanceId) return m;
            }
            return null;
        }

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";
    }
}
