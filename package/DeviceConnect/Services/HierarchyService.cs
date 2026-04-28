using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ODDGames.Bugpunch.DeviceConnect
{
    public class HierarchyService
    {
        /// <summary>
        /// Get the full scene hierarchy as JSON
        /// </summary>
        public string GetHierarchy()
        {
            var sb = new StringBuilder();
            sb.Append("[");

            var scenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (scenes.Count > 0 || sb.Length > 1) sb.Append(",");
                    BuildNode(root.transform, sb);
                }
            }

            // Also include DontDestroyOnLoad objects
            var ddol = GetDontDestroyOnLoadObjects();
            foreach (var obj in ddol)
            {
                sb.Append(",");
                BuildNode(obj.transform, sb);
            }

            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Get loaded scenes as JSON
        /// </summary>
        public string GetScenes()
        {
            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (i > 0) sb.Append(",");
                sb.Append($"{{\"name\":\"{BugpunchJson.Esc(scene.name)}\",\"path\":\"{BugpunchJson.Esc(scene.path)}\",\"isLoaded\":{(scene.isLoaded ? "true" : "false")},\"rootCount\":{scene.rootCount}}}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        void BuildNode(Transform t, StringBuilder sb)
        {
            var go = t.gameObject;
            sb.Append("{");
            sb.Append($"\"recid\":{go.GetInstanceID()},");
            sb.Append($"\"name\":\"{BugpunchJson.Esc(go.name)}\",");
            sb.Append($"\"active\":{(go.activeSelf ? "true" : "false")},");
            sb.Append($"\"layer\":{go.layer},");
            sb.Append($"\"tag\":\"{BugpunchJson.Esc(go.tag)}\",");

            // Components summary
            var components = go.GetComponents<Component>();
            sb.Append("\"components\":[");
            for (int i = 0; i < components.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var c = components[i];
                if (c == null) { sb.Append("\"(Missing)\""); continue; }
                sb.Append($"\"{BugpunchJson.Esc(c.GetType().Name)}\"");
            }
            sb.Append("],");

            // Position
            var p = t.localPosition;
            sb.Append($"\"position\":{{\"x\":{p.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},\"y\":{p.y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},\"z\":{p.z.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}}},");

            // Children
            sb.Append("\"children\":[");
            for (int i = 0; i < t.childCount; i++)
            {
                if (i > 0) sb.Append(",");
                BuildNode(t.GetChild(i), sb);
            }
            sb.Append("]");

            sb.Append("}");
        }

        static GameObject[] GetDontDestroyOnLoadObjects()
        {
            // Trick: create a temp object, move to DDOL, get its scene, then destroy it
            try
            {
                var temp = new GameObject("__bugpunch_temp__");
                Object.DontDestroyOnLoad(temp);
                var ddolScene = temp.scene;
                Object.DestroyImmediate(temp);

                if (ddolScene.IsValid())
                    return ddolScene.GetRootGameObjects();
            }
            catch { }
            return new GameObject[0];
        }

        /// <summary>
        /// Get direct children of a GameObject by instance ID (lazy tree).
        /// Negative IDs represent scene roots (1-based negated).
        /// </summary>
        public string GetChildren(string instanceIdStr)
        {
            if (!int.TryParse(instanceIdStr, out var id))
                return "[]";

            var sb = new StringBuilder();
            sb.Append("[");

            if (id < 0)
            {
                // Negative IDs = scene roots (1-based negated)
                int sceneIdx = (-id) - 1;
                UnityEngine.SceneManagement.Scene scene;
                if (sceneIdx < SceneManager.sceneCount)
                    scene = SceneManager.GetSceneAt(sceneIdx);
                else
                {
                    // DontDestroyOnLoad scene
                    var ddolObjects = GetDontDestroyOnLoadObjects();
                    if (ddolObjects.Length > 0)
                        scene = ddolObjects[0].scene;
                    else
                        return "[]";
                }

                if (!scene.IsValid()) return "[]";

                var roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    AppendChildNode(sb, roots[i]);
                }
            }
            else
            {
                // Positive IDs = GameObject instance IDs
                var go = FastFindGameObject(id);
                if (go == null) return "[]";

                var t = go.transform;
                for (int i = 0; i < t.childCount; i++)
                {
                    if (i > 0) sb.Append(",");
                    AppendChildNode(sb, t.GetChild(i).gameObject);
                }
            }

            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Destroy a GameObject by instance ID. Returns success/error JSON.
        /// </summary>
        public string DeleteGameObject(string instanceIdStr)
        {
            if (!int.TryParse(instanceIdStr, out var id))
                return "{\"ok\":false,\"error\":\"Invalid instance ID\"}";

            var go = FastFindGameObject(id);

            if (go == null)
                return "{\"ok\":false,\"error\":\"GameObject not found\"}";

            Object.Destroy(go);
            return "{\"ok\":true}";
        }

        /// <summary>
        /// Header info for the editor-style strip at the top of the Inspector:
        /// active toggle, name, static flag, tag, layer.
        /// </summary>
        public string GetGameObject(string instanceIdStr)
        {
            if (!int.TryParse(instanceIdStr, out var id))
                return "{\"error\":\"Invalid instance ID\"}";

            var go = FastFindGameObject(id);
            if (go == null) return "{\"error\":\"GameObject not found\"}";

            var sb = new StringBuilder(256);
            sb.Append("{");
            sb.Append($"\"instanceId\":{go.GetInstanceID()},");
            sb.Append($"\"name\":\"{BugpunchJson.Esc(go.name)}\",");
            sb.Append($"\"active\":{(go.activeSelf ? "true" : "false")},");
            sb.Append($"\"activeInHierarchy\":{(go.activeInHierarchy ? "true" : "false")},");
            sb.Append($"\"isStatic\":{(go.isStatic ? "true" : "false")},");
            sb.Append($"\"tag\":\"{BugpunchJson.Esc(go.tag)}\",");
            sb.Append($"\"layer\":{go.layer},");
            sb.Append($"\"layerName\":\"{BugpunchJson.Esc(LayerMask.LayerToName(go.layer) ?? "")}\",");
            sb.Append($"\"scene\":\"{BugpunchJson.Esc(go.scene.name ?? "")}\"");
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Apply changes to GameObject header fields. Body fields are all
        /// optional: name, active, isStatic, tag, layer.
        /// </summary>
        public string ApplyGameObject(string instanceIdStr, string body)
        {
            if (!int.TryParse(instanceIdStr, out var id))
                return "{\"ok\":false,\"error\":\"Invalid instance ID\"}";

            var go = FastFindGameObject(id);
            if (go == null) return "{\"ok\":false,\"error\":\"GameObject not found\"}";

            try
            {
                var name = RequestRouter.JsonVal(body, "name");
                if (name != null) go.name = name;

                var activeRaw = RequestRouter.JsonVal(body, "active");
                if (activeRaw != null) go.SetActive(activeRaw == "true" || activeRaw == "True" || activeRaw == "1");

                var staticRaw = RequestRouter.JsonVal(body, "isStatic");
                if (staticRaw != null) go.isStatic = staticRaw == "true" || staticRaw == "True" || staticRaw == "1";

                var tag = RequestRouter.JsonVal(body, "tag");
                if (tag != null) go.tag = tag; // throws UnityException if tag is not registered

                var layerRaw = RequestRouter.JsonVal(body, "layer");
                if (layerRaw != null && int.TryParse(layerRaw, out var layer))
                    go.layer = Mathf.Clamp(layer, 0, 31);

                return "{\"ok\":true}";
            }
            catch (System.Exception ex)
            {
                return $"{{\"ok\":false,\"error\":\"{BugpunchJson.Esc(ex.Message)}\"}}";
            }
        }

        static void AppendChildNode(StringBuilder sb, GameObject go)
        {
            sb.Append("{");
            sb.Append($"\"id\":{go.GetInstanceID()},");
            sb.Append($"\"name\":\"{BugpunchJson.Esc(go.name)}\",");
            sb.Append($"\"hasChildren\":{(go.transform.childCount > 0 ? "true" : "false")},");

            sb.Append("\"components\":[");
            var components = go.GetComponents<Component>();
            bool first = true;
            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\"{BugpunchJson.Esc(c.GetType().Name)}\"");
            }
            sb.Append("]");

            sb.Append("}");
        }

        static MethodInfo _findMethod;
        static GameObject FastFindGameObject(int id)
        {
            // Use Unity's internal fast lookup (no allocation)
            if (_findMethod == null)
                _findMethod = typeof(Object).GetMethod("FindObjectFromInstanceID", BindingFlags.NonPublic | BindingFlags.Static);
            if (_findMethod != null)
            {
                try
                {
                    var obj = _findMethod.Invoke(null, new object[] { id }) as Object;
                    if (obj is GameObject go) return go;
                    if (obj is Component c) return c.gameObject;
                }
                catch { }
            }
            // Fallback
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (g.GetInstanceID() == id) return g;
            }
            return null;
        }
    }
}
