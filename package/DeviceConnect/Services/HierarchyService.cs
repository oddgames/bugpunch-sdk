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
                sb.Append($"{{\"name\":\"{EscapeJson(scene.name)}\",\"path\":\"{EscapeJson(scene.path)}\",\"isLoaded\":{(scene.isLoaded ? "true" : "false")},\"rootCount\":{scene.rootCount}}}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        void BuildNode(Transform t, StringBuilder sb)
        {
            var go = t.gameObject;
            sb.Append("{");
            sb.Append($"\"recid\":{go.GetInstanceID()},");
            sb.Append($"\"name\":\"{EscapeJson(go.name)}\",");
            sb.Append($"\"active\":{(go.activeSelf ? "true" : "false")},");
            sb.Append($"\"layer\":{go.layer},");
            sb.Append($"\"tag\":\"{EscapeJson(go.tag)}\",");

            // Components summary
            var components = go.GetComponents<Component>();
            sb.Append("\"components\":[");
            for (int i = 0; i < components.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var c = components[i];
                if (c == null) { sb.Append("\"(Missing)\""); continue; }
                sb.Append($"\"{EscapeJson(c.GetType().Name)}\"");
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
                    var go = roots[i];
                    sb.Append($"{{\"id\":{go.GetInstanceID()},\"name\":\"{EscapeJson(go.name)}\",\"hasChildren\":{(go.transform.childCount > 0 ? "true" : "false")}}}");
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
                    var child = t.GetChild(i);
                    var childGo = child.gameObject;
                    sb.Append($"{{\"id\":{childGo.GetInstanceID()},\"name\":\"{EscapeJson(childGo.name)}\",\"hasChildren\":{(child.childCount > 0 ? "true" : "false")}}}");
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

        static string EscapeJson(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";
    }
}
