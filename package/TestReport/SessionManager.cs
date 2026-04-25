using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Capture lifecycle helpers for TestReport: screenshot queueing,
    /// hierarchy snapshot building, sanitation utilities.
    /// State that must persist between calls (screenshot queue, capturer ref) lives here.
    /// </summary>
    internal static class SessionManager
    {
        // Queue of screenshot file paths waiting for end-of-frame capture.
        // Shared between SessionManager (enqueue) and the ScreenshotCapturer MonoBehaviour (drain).
        internal static readonly Queue<string> PendingScreenshots = new();
        internal static readonly List<Task> PendingFileWrites = new();
        internal static ScreenshotCapturer Capturer;

        // Reusable buffers to avoid per-capture allocations
        static readonly List<Component> _componentBuffer = new();
        static readonly Dictionary<Transform, HierarchyNode> _nodeMap = new();

        // -------- Screenshot capture --------

        /// <summary>
        /// Queues a screenshot for end-of-frame capture. The actual ReadPixels call
        /// happens in the ScreenshotCapturer coroutine when the back buffer is ready.
        /// </summary>
        internal static void QueueScreenshot(string fullPath)
        {
            PendingScreenshots.Enqueue(fullPath);
            EnsureScreenshotCapturer();
        }

        static void EnsureScreenshotCapturer()
        {
            if (Capturer != null) return;
            var go = new GameObject("[TestReport.ScreenshotCapturer]")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            UnityEngine.Object.DontDestroyOnLoad(go);
            Capturer = go.AddComponent<ScreenshotCapturer>();
        }

        /// <summary>
        /// Synchronously drains any pending screenshots — called on EndSession to ensure
        /// nothing is lost if the coroutine hasn't run yet.
        /// </summary>
        internal static void FlushPendingScreenshots()
        {
            while (PendingScreenshots.Count > 0)
            {
                var path = PendingScreenshots.Dequeue();
                try
                {
                    var texture = ScreenCapture.CaptureScreenshotAsTexture();
                    if (texture != null)
                    {
                        var pngBytes = texture.EncodeToPNG();
                        UnityEngine.Object.Destroy(texture);
                        PendingFileWrites.Add(Task.Run(() =>
                        {
                            try { File.WriteAllBytes(path, pngBytes); }
                            catch { }
                        }));
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[TestReport] Screenshot flush failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Hidden MonoBehaviour that captures screenshots at WaitForEndOfFrame.
        /// Captures once per frame — all queued paths from that frame share the same pixels.
        /// </summary>
        internal class ScreenshotCapturer : MonoBehaviour
        {
            System.Collections.IEnumerator Start()
            {
                while (true)
                {
                    yield return new WaitForEndOfFrame();
                    ProcessQueue();
                }
            }

            void ProcessQueue()
            {
                if (PendingScreenshots.Count == 0) return;
                try
                {
                    var texture = ScreenCapture.CaptureScreenshotAsTexture();
                    if (texture == null) return;
                    var pngBytes = texture.EncodeToPNG();
                    UnityEngine.Object.Destroy(texture);

                    while (PendingScreenshots.Count > 0)
                    {
                        var path = PendingScreenshots.Dequeue();
                        var pathCopy = path; // capture for closure
                        PendingFileWrites.Add(Task.Run(() =>
                        {
                            try { File.WriteAllBytes(pathCopy, pngBytes); }
                            catch { }
                        }));
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[TestReport] Screenshot capture failed: {ex.Message}");
                    PendingScreenshots.Clear();
                }
            }

            void OnDestroy()
            {
                Capturer = null;
            }
        }

        // -------- Hierarchy snapshot --------

        /// <summary>
        /// Builds a HierarchySnapshot via a single FindObjectsByType scan, assembles the
        /// nested tree grouped by scene, and returns it ready for serialization.
        /// </summary>
        internal static HierarchySnapshot BuildSnapshot(bool detailed = false, int maxDepth = -1)
        {
            var snapshot = new HierarchySnapshot
            {
                screenWidth = Screen.width,
                screenHeight = Screen.height
            };

            BuildHierarchy(snapshot, detailed, maxDepth);
            return snapshot;
        }

        /// <summary>
        /// Builds a nested hierarchy tree from ALL GameObjects, grouped by scene.
        /// Single native FindObjectsByType call, then assembles parent-child relationships.
        /// </summary>
        static void BuildHierarchy(HierarchySnapshot snapshot, bool detailed, int maxDepth)
        {
            _nodeMap.Clear();

            // Single native call — gets ALL Transforms across all scenes + DontDestroyOnLoad
            var allTransforms = UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            // Pass 1: Create a node for every Transform (skip HideInHierarchy objects)
            var corners = new Vector3[4];
            var mainCam = Camera.main;

            // Store camera VP matrix for client-side 3D→screen projection
            if (mainCam != null)
            {
                var vp = mainCam.projectionMatrix * mainCam.worldToCameraMatrix;
                snapshot.cameraMatrix = new float[16];
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++)
                        snapshot.cameraMatrix[r * 4 + c] = vp[r, c];
            }

            for (int i = 0; i < allTransforms.Length; i++)
            {
                var t = allTransforms[i];
                if (t == null) continue;

                var go = t.gameObject;
                if ((go.hideFlags & HideFlags.HideInHierarchy) != 0) continue;

                var node = new HierarchyNode
                {
                    name = t.name,
                    active = go.activeInHierarchy,
                    instanceId = go.GetInstanceID(),
                    childCount = t.childCount,
                    siblingIndex = t.GetSiblingIndex()
                };

                // Extract text content if present (cheap single check)
                if (go.TryGetComponent<TMP_Text>(out var tmp))
                    node.text = tmp.text;
                else if (go.TryGetComponent<Text>(out var legacyText))
                    node.text = legacyText.text;

                // Screen-space bounds for UI elements (RectTransform only)
                if (t is RectTransform rt)
                {
                    rt.GetWorldCorners(corners);
                    var canvas = go.GetComponentInParent<Canvas>();
                    Camera cam = null;
                    if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                        cam = canvas.worldCamera != null ? canvas.worldCamera : mainCam;

                    float minX = float.MaxValue, maxX = float.MinValue;
                    float minY = float.MaxValue, maxY = float.MinValue;
                    for (int c = 0; c < 4; c++)
                    {
                        Vector2 sp = cam != null
                            ? RectTransformUtility.WorldToScreenPoint(cam, corners[c])
                            : (Vector2)corners[c];
                        if (sp.x < minX) minX = sp.x;
                        if (sp.x > maxX) maxX = sp.x;
                        if (sp.y < minY) minY = sp.y;
                        if (sp.y > maxY) maxY = sp.y;
                    }
                    node.x = minX;
                    node.y = minY;
                    node.w = maxX - minX;
                    node.h = maxY - minY;

                    // Depth for UI: negative sort order so higher sort order = lower depth = in front
                    if (canvas != null)
                        node.depth = -(canvas.sortingOrder * 10000f + GetHierarchyOrder(t));
                }
                // World-space bounds for 3D objects — projected client-side using cameraMatrix
                else if (go.TryGetComponent<Renderer>(out var renderer) && renderer.isVisible)
                {
                    var b = renderer.bounds;
                    node.worldBounds = new[] { b.center.x, b.center.y, b.center.z, b.extents.x, b.extents.y, b.extents.z };
                }

                _nodeMap[t] = node;
            }

            // Pass 2: Assemble parent-child relationships, collect roots per scene
            var sceneRoots = new Dictionary<string, List<HierarchyNode>>();
            for (int i = 0; i < allTransforms.Length; i++)
            {
                var t = allTransforms[i];
                if (t == null || !_nodeMap.TryGetValue(t, out var node)) continue;

                if (t.parent != null && _nodeMap.TryGetValue(t.parent, out var parentNode))
                {
                    if (maxDepth >= 0)
                    {
                        int depth = 0;
                        var check = t.parent;
                        while (check != null)
                        {
                            depth++;
                            check = check.parent;
                        }
                        if (depth > maxDepth) continue;
                    }
                    parentNode.children.Add(node);
                }
                else
                {
                    // Root object — group by scene name
                    var scene = t.gameObject.scene;
                    var sceneName = scene.IsValid() ? scene.name : "DontDestroyOnLoad";
                    if (!sceneRoots.TryGetValue(sceneName, out var list))
                    {
                        list = new List<HierarchyNode>();
                        sceneRoots[sceneName] = list;
                    }
                    list.Add(node);
                }
            }

            // Pass 3: Create scene header nodes and add as top-level roots
            foreach (var kvp in sceneRoots)
            {
                var sceneNode = new HierarchyNode
                {
                    name = kvp.Key,
                    active = true,
                    isScene = true,
                    childCount = kvp.Value.Count,
                    children = kvp.Value
                };
                sceneNode.children.Sort((a, b) => a.siblingIndex.CompareTo(b.siblingIndex));
                snapshot.roots.Add(sceneNode);
            }

            // Sort scenes: loaded scenes first, DontDestroyOnLoad last
            snapshot.roots.Sort((a, b) =>
            {
                if (a.name == "DontDestroyOnLoad") return 1;
                if (b.name == "DontDestroyOnLoad") return -1;
                return string.Compare(a.name, b.name, StringComparison.Ordinal);
            });

            // Sort all children recursively by sibling index
            foreach (var sceneNode in snapshot.roots)
                SortChildrenRecursive(sceneNode.children);

            _nodeMap.Clear();
        }

        /// <summary>
        /// Computes a stable rendering order for a UI element within its canvas.
        /// </summary>
        static float GetHierarchyOrder(Transform t)
        {
            int depth = 0;
            var current = t.parent;
            while (current != null) { depth++; current = current.parent; }
            return depth * 1000f + t.GetSiblingIndex();
        }

        static void SortChildrenRecursive(List<HierarchyNode> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var children = nodes[i].children;
                if (children.Count > 1)
                    children.Sort((a, b) => a.siblingIndex.CompareTo(b.siblingIndex));
                if (children.Count > 0)
                    SortChildrenRecursive(children);
            }
        }

        // -------- Component property serialization (for detailed snapshots) --------

        internal static readonly JsonSerializerSettings ComponentJsonSettings = new()
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            MaxDepth = 2,
            Error = (_, args) => args.ErrorContext.Handled = true,
            Formatting = Formatting.None,
            ContractResolver = new ComponentContractResolver()
        };

        /// <summary>
        /// Custom contract resolver that only serializes value-type and string properties
        /// on Unity components — skips object references to avoid circular refs.
        /// </summary>
        class ComponentContractResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
        {
            protected override IList<Newtonsoft.Json.Serialization.JsonProperty> CreateProperties(
                Type type, MemberSerialization memberSerialization)
            {
                var allProps = base.CreateProperties(type, memberSerialization);
                var filtered = new List<Newtonsoft.Json.Serialization.JsonProperty>();

                for (int i = 0; i < allProps.Count; i++)
                {
                    var p = allProps[i];
                    var pt = p.PropertyType;
                    if (pt == null) continue;

                    if (pt.IsPrimitive || pt.IsEnum || pt == typeof(string)
                        || pt == typeof(Vector2) || pt == typeof(Vector3) || pt == typeof(Vector4)
                        || pt == typeof(Color) || pt == typeof(Color32)
                        || pt == typeof(Rect) || pt == typeof(Bounds)
                        || pt == typeof(Quaternion)
                        || pt == typeof(Vector2Int) || pt == typeof(Vector3Int))
                    {
                        var prop = type.GetProperty(p.PropertyName,
                            BindingFlags.Public | BindingFlags.Instance);
                        if (prop != null && Attribute.IsDefined(prop, typeof(ObsoleteAttribute)))
                            continue;

                        filtered.Add(p);
                    }
                }

                return filtered;
            }
        }

        // -------- Filename utilities --------

        internal static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }
    }
}
