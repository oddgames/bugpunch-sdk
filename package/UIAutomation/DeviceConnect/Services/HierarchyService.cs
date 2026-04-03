using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ODDGames.UIAutomation.DeviceConnect
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
            sb.Append($"\"position\":{{\"x\":{t.localPosition.x:F3},\"y\":{t.localPosition.y:F3},\"z\":{t.localPosition.z:F3}}},");

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
                var temp = new GameObject("__odddev_temp__");
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
                    var t = roots[i].transform;
                    sb.Append($"{{\"id\":{t.GetInstanceID()},\"name\":\"{EscapeJson(t.name)}\",\"hasChildren\":{(t.childCount > 0 ? "true" : "false")}}}");
                }
            }
            else
            {
                // Positive IDs = GameObject instance IDs
                GameObject go = null;
                foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (g.GetInstanceID() == id) { go = g; break; }
                }
                if (go == null) return "[]";

                var t = go.transform;
                for (int i = 0; i < t.childCount; i++)
                {
                    if (i > 0) sb.Append(",");
                    var child = t.GetChild(i);
                    sb.Append($"{{\"id\":{child.GetInstanceID()},\"name\":\"{EscapeJson(child.name)}\",\"hasChildren\":{(child.childCount > 0 ? "true" : "false")}}}");
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

            GameObject go = null;
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (g.GetInstanceID() == id) { go = g; break; }
            }

            if (go == null)
                return "{\"ok\":false,\"error\":\"GameObject not found\"}";

            Object.Destroy(go);
            return "{\"ok\":true}";
        }

        static string EscapeJson(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";
    }
}
