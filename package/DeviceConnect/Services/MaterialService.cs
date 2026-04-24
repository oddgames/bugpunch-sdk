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
        /// Return JSON array of all loaded materials. Each entry includes names of
        /// all GameObjects that reference it and all texture names bound to any
        /// shader property — both exposed for dashboard-side search.
        /// </summary>
        public string ListMaterials()
        {
            // Build material→GameObject index from all scene renderers (sharedMaterials
            // shares the instanceId across renderers, so one lookup suffices per material).
            var gosByMat = new Dictionary<int, List<string>>();
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var go = renderer.gameObject;
                if (go == null || go.hideFlags != HideFlags.None) continue;
                var goName = go.name;
                if (string.IsNullOrEmpty(goName)) continue;
                foreach (var sm in renderer.sharedMaterials)
                {
                    if (sm == null) continue;
                    var id = sm.GetInstanceID();
                    if (!gosByMat.TryGetValue(id, out var list))
                    {
                        list = new List<string>();
                        gosByMat[id] = list;
                    }
                    if (!list.Contains(goName)) list.Add(goName);
                }
            }

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

                var shader = mat.shader;
                var shaderName = shader != null ? shader.name : "";
                var instanceId = mat.GetInstanceID();

                // Main texture (for legacy 'texture' field + "Main Tex" row in detail view).
                var mainTexName = "";
                if (mat.HasProperty("_MainTex"))
                {
                    var tex = mat.mainTexture;
                    if (tex != null) mainTexName = tex.name;
                }
                else if (mat.HasProperty("_BaseMap"))
                {
                    var tex = mat.GetTexture("_BaseMap");
                    if (tex != null) mainTexName = tex.name;
                }

                // All textures bound to any shader property — used for search.
                var texNames = new List<string>();
                if (shader != null)
                {
                    int propCount = shader.GetPropertyCount();
                    for (int i = 0; i < propCount; i++)
                    {
                        if (shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                        var tex = mat.GetTexture(shader.GetPropertyNameId(i));
                        if (tex == null) continue;
                        var tn = tex.name;
                        if (string.IsNullOrEmpty(tn)) continue;
                        if (!texNames.Contains(tn)) texNames.Add(tn);
                    }
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
                sb.Append($",\"shader\":\"{Esc(shaderName)}\"");
                if (!string.IsNullOrEmpty(mainTexName))
                    sb.Append($",\"texture\":\"{Esc(mainTexName)}\"");
                if (texNames.Count > 0)
                {
                    sb.Append(",\"textures\":[");
                    for (int i = 0; i < texNames.Count; i++)
                    {
                        if (i > 0) sb.Append(",");
                        sb.Append($"\"{Esc(texNames[i])}\"");
                    }
                    sb.Append("]");
                }
                if (gosByMat.TryGetValue(instanceId, out var gos) && gos.Count > 0)
                {
                    sb.Append(",\"gameObjects\":[");
                    for (int i = 0; i < gos.Count; i++)
                    {
                        if (i > 0) sb.Append(",");
                        sb.Append($"\"{Esc(gos[i])}\"");
                    }
                    sb.Append("]");
                }
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
                Debug.LogError($"[Bugpunch.MaterialService] Material thumbnail failed: {ex.Message}");
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
                Debug.LogError($"[Bugpunch.MaterialService] Material texture export failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Full property dump for the material drill-down inspector:
        /// every shader property with its live value (Color, Float, Range,
        /// Vector, Texture, Int) plus the shader's keyword list with each
        /// keyword's current enabled state.
        /// </summary>
        public string GetProperties(int instanceId)
        {
            var mat = FindMaterial(instanceId);
            if (mat == null) return "{\"error\":\"material not found\"}";

            var shader = mat.shader;
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"id\":{instanceId}");
            sb.Append($",\"name\":\"{Esc(mat.name)}\"");
            sb.Append($",\"shader\":\"{Esc(shader != null ? shader.name : "")}\"");
            sb.Append($",\"renderQueue\":{mat.renderQueue}");

            // Properties
            sb.Append(",\"properties\":[");
            if (shader != null)
            {
                int propCount = shader.GetPropertyCount();
                bool first = true;
                for (int i = 0; i < propCount; i++)
                {
                    var propName = shader.GetPropertyName(i);
                    var propType = shader.GetPropertyType(i);
                    var desc = shader.GetPropertyDescription(i);
                    var flags = shader.GetPropertyFlags(i);
                    // Skip HideInInspector-flagged unless hidden type already covered
                    bool hidden = (flags & ShaderPropertyFlags.HideInInspector) != 0;

                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("{");
                    sb.Append($"\"name\":\"{Esc(propName)}\"");
                    sb.Append($",\"label\":\"{Esc(desc)}\"");
                    sb.Append($",\"hidden\":{(hidden ? "true" : "false")}");

                    switch (propType)
                    {
                        case ShaderPropertyType.Color:
                        {
                            var c = mat.GetColor(propName);
                            sb.Append(",\"type\":\"Color\"");
                            sb.Append($",\"value\":{{\"r\":{c.r:F4},\"g\":{c.g:F4},\"b\":{c.b:F4},\"a\":{c.a:F4}}}");
                            break;
                        }
                        case ShaderPropertyType.Float:
                        {
                            var v = mat.GetFloat(propName);
                            sb.Append(",\"type\":\"Float\"");
                            sb.Append($",\"value\":{v.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)}");
                            break;
                        }
                        case ShaderPropertyType.Range:
                        {
                            var v = mat.GetFloat(propName);
                            var min = shader.GetPropertyRangeLimits(i).x;
                            var max = shader.GetPropertyRangeLimits(i).y;
                            sb.Append(",\"type\":\"Range\"");
                            sb.Append($",\"value\":{v.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)}");
                            sb.Append($",\"min\":{min.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)}");
                            sb.Append($",\"max\":{max.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)}");
                            break;
                        }
                        case ShaderPropertyType.Vector:
                        {
                            var v = mat.GetVector(propName);
                            sb.Append(",\"type\":\"Vector\"");
                            sb.Append($",\"value\":{{\"x\":{v.x:F4},\"y\":{v.y:F4},\"z\":{v.z:F4},\"w\":{v.w:F4}}}");
                            break;
                        }
                        case ShaderPropertyType.Texture:
                        {
                            var tex = mat.GetTexture(propName);
                            var scale = mat.GetTextureScale(propName);
                            var offset = mat.GetTextureOffset(propName);
                            sb.Append(",\"type\":\"Texture\"");
                            if (tex != null)
                            {
                                sb.Append(",\"value\":{");
                                sb.Append($"\"id\":{tex.GetInstanceID()}");
                                sb.Append($",\"name\":\"{Esc(tex.name)}\"");
                                sb.Append($",\"width\":{tex.width}");
                                sb.Append($",\"height\":{tex.height}");
                                sb.Append("}");
                            }
                            else
                            {
                                sb.Append(",\"value\":null");
                            }
                            sb.Append($",\"scale\":{{\"x\":{scale.x:F4},\"y\":{scale.y:F4}}}");
                            sb.Append($",\"offset\":{{\"x\":{offset.x:F4},\"y\":{offset.y:F4}}}");
                            break;
                        }
#if UNITY_2021_1_OR_NEWER
                        case ShaderPropertyType.Int:
                        {
                            var v = mat.GetInt(propName);
                            sb.Append(",\"type\":\"Int\"");
                            sb.Append($",\"value\":{v}");
                            break;
                        }
#endif
                    }
                    sb.Append("}");
                }
            }
            sb.Append("]");

            // Keywords — union of shader-declared and currently-enabled keywords,
            // each tagged with its current enabled state on this material.
            sb.Append(",\"keywords\":[");
            {
                var enabled = new System.Collections.Generic.HashSet<string>(mat.shaderKeywords ?? Array.Empty<string>());
                var all = new System.Collections.Generic.List<(string name, bool declared)>();
                var seen = new System.Collections.Generic.HashSet<string>();
#if UNITY_2021_2_OR_NEWER
                if (shader != null)
                {
                    try
                    {
                        foreach (var kw in shader.keywordSpace.keywordNames)
                        {
                            if (string.IsNullOrEmpty(kw) || !seen.Add(kw)) continue;
                            all.Add((kw, true));
                        }
                    }
                    catch { /* some shaders throw — fall through to runtime-enabled list */ }
                }
#endif
                foreach (var kw in enabled)
                {
                    if (string.IsNullOrEmpty(kw) || !seen.Add(kw)) continue;
                    all.Add((kw, false));
                }
                all.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

                bool first = true;
                foreach (var (kw, declared) in all)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("{");
                    sb.Append($"\"name\":\"{Esc(kw)}\"");
                    sb.Append($",\"enabled\":{(enabled.Contains(kw) ? "true" : "false")}");
                    sb.Append($",\"declared\":{(declared ? "true" : "false")}");
                    sb.Append("}");
                }
            }
            sb.Append("]");

            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Write a single shader property on the material.
        /// `type` is one of Color / Float / Range / Vector / Int / Texture.
        /// For Texture, `textureId` is the instance ID of a loaded texture (0 = clear).
        /// </summary>
        public string SetProperty(int instanceId, string propName, string type, string body)
        {
            var mat = FindMaterial(instanceId);
            if (mat == null) return "{\"ok\":false,\"error\":\"material not found\"}";
            if (string.IsNullOrEmpty(propName)) return "{\"ok\":false,\"error\":\"missing property name\"}";
            if (!mat.HasProperty(propName)) return "{\"ok\":false,\"error\":\"shader has no property " + Esc(propName) + "\"}";

            try
            {
                switch (type)
                {
                    case "Color":
                    {
                        var c = new Color(
                            ParseFloat(RequestRouter.JsonVal(body, "r")),
                            ParseFloat(RequestRouter.JsonVal(body, "g")),
                            ParseFloat(RequestRouter.JsonVal(body, "b")),
                            ParseFloat(RequestRouter.JsonVal(body, "a"), 1f));
                        mat.SetColor(propName, c);
                        break;
                    }
                    case "Float":
                    case "Range":
                    {
                        var v = ParseFloat(RequestRouter.JsonVal(body, "value"));
                        mat.SetFloat(propName, v);
                        break;
                    }
                    case "Int":
                    {
                        var v = ParseInt(RequestRouter.JsonVal(body, "value"));
#if UNITY_2021_1_OR_NEWER
                        mat.SetInteger(propName, v);
#else
                        mat.SetInt(propName, v);
#endif
                        break;
                    }
                    case "Vector":
                    {
                        var v = new Vector4(
                            ParseFloat(RequestRouter.JsonVal(body, "x")),
                            ParseFloat(RequestRouter.JsonVal(body, "y")),
                            ParseFloat(RequestRouter.JsonVal(body, "z")),
                            ParseFloat(RequestRouter.JsonVal(body, "w")));
                        mat.SetVector(propName, v);
                        break;
                    }
                    case "Texture":
                    {
                        var texIdStr = RequestRouter.JsonVal(body, "textureId");
                        var texId = ParseInt(texIdStr);
                        if (texId == 0)
                        {
                            mat.SetTexture(propName, null);
                        }
                        else
                        {
                            var tex = FindTexture(texId);
                            if (tex == null) return "{\"ok\":false,\"error\":\"texture not found\"}";
                            mat.SetTexture(propName, tex);
                        }
                        // Optional tiling / offset
                        var sxStr = RequestRouter.JsonVal(body, "scaleX");
                        var syStr = RequestRouter.JsonVal(body, "scaleY");
                        var oxStr = RequestRouter.JsonVal(body, "offsetX");
                        var oyStr = RequestRouter.JsonVal(body, "offsetY");
                        if (!string.IsNullOrEmpty(sxStr) || !string.IsNullOrEmpty(syStr))
                        {
                            var cur = mat.GetTextureScale(propName);
                            mat.SetTextureScale(propName, new Vector2(
                                string.IsNullOrEmpty(sxStr) ? cur.x : ParseFloat(sxStr),
                                string.IsNullOrEmpty(syStr) ? cur.y : ParseFloat(syStr)));
                        }
                        if (!string.IsNullOrEmpty(oxStr) || !string.IsNullOrEmpty(oyStr))
                        {
                            var cur = mat.GetTextureOffset(propName);
                            mat.SetTextureOffset(propName, new Vector2(
                                string.IsNullOrEmpty(oxStr) ? cur.x : ParseFloat(oxStr),
                                string.IsNullOrEmpty(oyStr) ? cur.y : ParseFloat(oyStr)));
                        }
                        break;
                    }
                    default:
                        return "{\"ok\":false,\"error\":\"unsupported type " + Esc(type) + "\"}";
                }
                return "{\"ok\":true}";
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + Esc(ex.Message) + "\"}";
            }
        }

        /// <summary>
        /// Enable or disable a shader keyword on the material.
        /// </summary>
        public string SetKeyword(int instanceId, string keyword, bool enabled)
        {
            var mat = FindMaterial(instanceId);
            if (mat == null) return "{\"ok\":false,\"error\":\"material not found\"}";
            if (string.IsNullOrEmpty(keyword)) return "{\"ok\":false,\"error\":\"missing keyword\"}";
            try
            {
                if (enabled) mat.EnableKeyword(keyword);
                else mat.DisableKeyword(keyword);
                return "{\"ok\":true}";
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + Esc(ex.Message) + "\"}";
            }
        }

        /// <summary>
        /// Set the material's render queue (-1 = from shader).
        /// </summary>
        public string SetRenderQueue(int instanceId, int queue)
        {
            var mat = FindMaterial(instanceId);
            if (mat == null) return "{\"ok\":false,\"error\":\"material not found\"}";
            mat.renderQueue = queue;
            return "{\"ok\":true}";
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

        static Texture FindTexture(int instanceId)
        {
            var all = Resources.FindObjectsOfTypeAll<Texture>();
            foreach (var t in all)
            {
                if (t != null && t.GetInstanceID() == instanceId) return t;
            }
            return null;
        }

        static float ParseFloat(string s, float fallback = 0f)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            return float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        static int ParseInt(string s, int fallback = 0)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            return int.TryParse(s, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        static string Esc(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";
    }
}
