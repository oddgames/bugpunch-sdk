using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Builds the failure report shown to the developer when a Search times out.
    /// Includes hierarchy dump, visible text elements, and near-miss analysis.
    /// </summary>
    internal static class FailureReporter
    {
        /// <summary>
        /// Dumps the full active scene + DontDestroyOnLoad hierarchy as text and writes
        /// it to hierarchy.txt in the session folder. Returns the dumped content.
        /// </summary>
        internal static string DumpHierarchy(string sessionFolder, Action<string> logMessage)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== Scene Hierarchy: {SceneManager.GetActiveScene().name} ===");
            sb.AppendLine($"Frame: {Time.frameCount} | Time: {Time.time:F2}s");
            sb.AppendLine();

            var rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var root in rootObjects)
                DumpTransform(root.transform, sb, 0);

            // Also dump DontDestroyOnLoad objects
            var ddolObjects = GetDontDestroyOnLoadObjects();
            if (ddolObjects.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("=== DontDestroyOnLoad ===");
                foreach (var obj in ddolObjects)
                    DumpTransform(obj.transform, sb, 0);
            }

            var content = sb.ToString();

            try
            {
                var path = Path.Combine(sessionFolder, "hierarchy.txt");
                File.WriteAllText(path, content);
                logMessage?.Invoke("Hierarchy dumped to hierarchy.txt");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestReport] Failed to write hierarchy: {ex.Message}");
            }

            return content;
        }

        /// <summary>
        /// Builds the combined FAILURE_REPORT.txt content (assertion message + diagnostics).
        /// Caller writes the file to disk.
        /// </summary>
        internal static string BuildReportText(
            Search search,
            float searchTime,
            string sessionFolder,
            IReadOnlyList<string> screenshotPaths)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Element not found within {searchTime}s");
            sb.AppendLine();
            sb.AppendLine("--- DIAGNOSTIC REPORT ---");
            sb.AppendLine($"Search: {search}");
            sb.AppendLine($"Scene: {SceneManager.GetActiveScene().name} (frame {Time.frameCount})");
            sb.AppendLine($"Screen: {Screen.width}x{Screen.height}");
            sb.AppendLine();

            // Show text elements on screen (most useful for debugging)
            AppendVisibleTextElements(sb);

            // Show near-misses — objects that match some but not all conditions
            AppendNearMisses(sb, search);

            // Receiver info if applicable
            if (search.UsesReceiverFilter)
            {
                var receivers = search.Receivers;
                if (receivers != null && receivers.Count > 0)
                    sb.AppendLine($"Receivers at position: {string.Join(", ", receivers.Select(r => r.name))}");
                else
                    sb.AppendLine("No receivers found at element position");
                sb.AppendLine("Tip: Remove .RequiresReceiver() from the search to click without receiver validation");
                sb.AppendLine();
            }

            // Screenshot summary
            sb.AppendLine($"Screenshots captured: {screenshotPaths.Count} files");
            foreach (var path in screenshotPaths)
                sb.AppendLine($"  {Path.GetFileName(path)}");
            sb.AppendLine();

            sb.AppendLine($"Diagnostic folder: {sessionFolder}");

            return sb.ToString();
        }

        static void DumpTransform(Transform t, StringBuilder sb, int depth)
        {
            var indent = new string(' ', depth * 2);
            var activeFlag = t.gameObject.activeInHierarchy ? "" : " [INACTIVE]";
            var annotations = GetAnnotations(t);
            var annotationStr = annotations.Length > 0 ? " " + string.Join(" ", annotations) : "";

            sb.AppendLine($"{indent}{t.name}{activeFlag}{annotationStr}");

            // Recurse children
            for (int i = 0; i < t.childCount; i++)
                DumpTransform(t.GetChild(i), sb, depth + 1);
        }

        static void AppendVisibleTextElements(StringBuilder sb)
        {
            sb.AppendLine("Visible text elements on screen:");

            var screenRect = new Rect(0, 0, Screen.width, Screen.height);
            var textElements = UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var visibleTexts = new List<(string path, string text, Vector2 pos)>();

            foreach (var tmp in textElements)
            {
                if (string.IsNullOrEmpty(tmp.text)) continue;
                var bounds = InputInjector.GetScreenBounds(tmp.gameObject);
                if (bounds.width > 0 && bounds.height > 0 && bounds.Overlaps(screenRect))
                {
                    var path = Search.GetHierarchyPath(tmp.transform);
                    var pos = InputInjector.GetScreenPosition(tmp.gameObject);
                    visibleTexts.Add((path, tmp.text, pos));
                }
            }

            // Also check legacy Text
            var legacyTexts = UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var text in legacyTexts)
            {
                if (string.IsNullOrEmpty(text.text)) continue;
                var bounds = InputInjector.GetScreenBounds(text.gameObject);
                if (bounds.width > 0 && bounds.height > 0 && bounds.Overlaps(screenRect))
                {
                    var path = Search.GetHierarchyPath(text.transform);
                    var pos = InputInjector.GetScreenPosition(text.gameObject);
                    visibleTexts.Add((path, text.text, pos));
                }
            }

            if (visibleTexts.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var (path, text, pos) in visibleTexts.OrderBy(t => -t.pos.y).ThenBy(t => t.pos.x))
                    sb.AppendLine($"  {path} \"{Truncate(text, 60)}\" at ({pos.x:F0},{pos.y:F0})");
            }
            sb.AppendLine();
        }

        static void AppendNearMisses(StringBuilder sb, Search search)
        {
            var conditions = search.Conditions;
            var descriptions = search.DescriptionParts;
            if (conditions == null || conditions.Count == 0) return;

            sb.AppendLine("Near-misses (matched some conditions but not all):");

            var allObjects = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var nearMisses = new List<(GameObject go, int matched, int total, string failedConditions)>();

            foreach (var t in allObjects)
            {
                if (t == null) continue;
                var go = t.gameObject;

                int matched = 0;
                var failedDescs = new List<string>();

                for (int i = 0; i < conditions.Count; i++)
                {
                    try
                    {
                        if (conditions[i](go))
                            matched++;
                        else if (i < descriptions.Count)
                            failedDescs.Add(descriptions[i]);
                    }
                    catch
                    {
                        if (i < descriptions.Count)
                            failedDescs.Add(descriptions[i]);
                    }
                }

                // Near-miss: matched at least 1 condition but not all
                if (matched > 0 && matched < conditions.Count)
                {
                    var failedStr = string.Join(", ", failedDescs);
                    nearMisses.Add((go, matched, conditions.Count, failedStr));
                }
            }

            if (nearMisses.Count == 0)
            {
                sb.AppendLine("  (none — no objects matched any condition)");
            }
            else
            {
                // Show top 20 near-misses, sorted by most conditions matched
                foreach (var (go, matched, total, failed) in nearMisses
                    .OrderByDescending(n => n.matched)
                    .Take(20))
                {
                    var path = Search.GetHierarchyPath(go.transform);
                    var activeStr = go.activeInHierarchy ? "" : " [INACTIVE]";
                    sb.AppendLine($"  {path}{activeStr} — matched {matched}/{total}, failed: {failed}");
                }

                if (nearMisses.Count > 20)
                    sb.AppendLine($"  ... and {nearMisses.Count - 20} more");
            }
            sb.AppendLine();
        }

        static List<GameObject> GetDontDestroyOnLoadObjects()
        {
            var results = new List<GameObject>();
            try
            {
                // Find objects not in the active scene
                var activeScene = SceneManager.GetActiveScene();
                var allRoots = new List<GameObject>();
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene != activeScene && scene.isLoaded)
                        allRoots.AddRange(scene.GetRootGameObjects());
                }
                return allRoots;
            }
            catch
            {
                return results;
            }
        }

        static string[] GetAnnotations(Transform t)
        {
            var components = t.GetComponents<Component>();
            if (components.Length <= 1) return Array.Empty<string>(); // only Transform

            List<string> annotations = null;

            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;

                string annotation = c switch
                {
                    // UI components
                    TMP_Text tmp => $"[TMP] \"{Truncate(tmp.text, 50)}\"",
                    Text text => $"[Text] \"{Truncate(text.text, 50)}\"",
                    Image img when img.sprite != null => $"[Image] sprite={img.sprite.name}",
                    RawImage raw when raw.texture != null => $"[RawImage] texture={raw.texture.name}",
                    Button btn => btn.interactable ? "[Button]" : "[Button:disabled]",
                    Toggle tog => tog.interactable
                        ? $"[Toggle:{(tog.isOn ? "ON" : "OFF")}]"
                        : $"[Toggle:disabled:{(tog.isOn ? "ON" : "OFF")}]",
                    TMP_InputField inp => $"[InputField] \"{Truncate(inp.text, 30)}\"",
                    Slider sld => $"[Slider:{sld.value:F2}]",
                    TMP_Dropdown dd => $"[Dropdown:{dd.captionText?.text ?? "?"}]",
                    ScrollRect _ => "[ScrollRect]",
                    Canvas cvs => $"[Canvas:{cvs.renderMode}]",
                    CanvasGroup cg when !cg.interactable || cg.alpha < 1f =>
                        $"[CanvasGroup:alpha={cg.alpha:F2},interactable={cg.interactable}]",
                    // 3D / scene components
                    Camera cam => $"[Camera:{cam.clearFlags}]",
                    Light light => $"[Light:{light.type}]",
                    MeshRenderer mr => $"[MeshRenderer]",
                    SkinnedMeshRenderer smr => $"[SkinnedMesh]",
                    MeshFilter mf when mf.sharedMesh != null => $"[Mesh:{mf.sharedMesh.name}]",
                    Collider col => $"[{col.GetType().Name}]",
                    Rigidbody rb => rb.isKinematic ? "[Rigidbody:kinematic]" : "[Rigidbody]",
                    Animator anim => $"[Animator]",
                    AudioSource audio => audio.isPlaying ? "[AudioSource:playing]" : "[AudioSource]",
                    ParticleSystem ps => ps.isPlaying ? "[ParticleSystem:playing]" : "[ParticleSystem]",
                    _ => null
                };

                if (annotation != null)
                    (annotations ??= new List<string>()).Add(annotation);
            }

            return annotations?.ToArray() ?? Array.Empty<string>();
        }

        static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\n", "\\n").Replace("\r", "");
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }
    }
}
