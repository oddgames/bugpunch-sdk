using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace ODDGames.Bugpunch.DeviceConnect
{
    /// <summary>
    /// Storyboard input capture. On every UI press (uGUI Selectable / IPointerClickHandler
    /// or UI Toolkit VisualElement) downscales the current frame to a max-540 long-side
    /// ARGB32 RenderTexture, async-reads the bytes back, and pushes them into the native
    /// storyboard ring with the press metadata (path / label / scene / x,y / screen dims).
    ///
    /// The native ring (10 slots) is the rescue path for crash screenshots — the signal
    /// handler dumps the newest slot as <c>screenshot_at_crash</c>, and the whole ring
    /// uploads as <c>storyboard_frames</c> on the next manifest enrich.
    ///
    /// Replaces the older 1 Hz timer-driven rolling buffer. Crashes that happen with no
    /// recent UI input fall back to the most recent press frame in the ring (which can be
    /// arbitrarily stale — accepted trade-off).
    ///
    /// Capture cost is dominated by AsyncGPUReadback bus bandwidth on the small RT
    /// (~660 KB at 540×304 ARGB32). No main-thread stall — readback completes 2-3 frames
    /// later. Capture is throttled to 200 ms so rapid taps can't blow the ring inside &lt;1 s.
    /// </summary>
    public class BugpunchInputCapture : MonoBehaviour
    {
        // Long-side cap for the captured frame. The same RT serves both the
        // storyboard ring (10 slots, browsable on the dashboard) and the
        // crash-screenshot rescue path (signal handler dumps the newest
        // slot as screenshot_at_crash). 540 was too small for the at-crash
        // case — phone-screen detail was unreadable. 1080 quadruples the
        // pixel count (~2.6 MB per slot, ~26 MB peak disk for the full
        // ring) but matches the long edge of a typical 1080p panel so the
        // crash screenshot is identifiable. Tunable per game via
        // BugpunchConfig in a follow-up.
        const int MAX_LONG_SIDE = 1080;
        const float CAPTURE_THROTTLE_S = 0.2f;
        const int MAX_PATH_LEN = 191;
        const int MAX_LABEL_LEN = 95;

        RenderTexture m_Rt;
        int m_RtW, m_RtH;
        float m_LastCaptureRealTime;
        readonly Queue<PendingFrame> m_PendingFrames = new();

        // UI Toolkit roots we've already wired a PointerDownEvent callback on. Re-checked
        // on a 1 Hz tick in case the game added new UIDocument instances at runtime.
        readonly HashSet<VisualElement> m_RegisteredRoots = new();
        float m_NextUIDocsScan;

        // Per-pointer "is a press in progress" tracking is not needed here — we react on
        // the down-edge only via wasPressedThisFrame, and the throttle covers double-fires.

        struct PendingFrame
        {
            public long tsMs;
            public string path;
            public string label;
            public string scene;
            public float x, y;
            public int screenW, screenH;
            public int w, h;
        }

        void OnDisable()
        {
            UnregisterUIToolkit();
            if (m_Rt != null)
            {
                m_Rt.Release();
                Destroy(m_Rt);
                m_Rt = null;
            }
            m_PendingFrames.Clear();
        }

        void Update()
        {
            // Detect presses regardless of EventSystem state. TryHandlePress
            // emits a "(world)" / "(no-eventsystem)" frame when there's no
            // raycaster to identify the UI target, so every tap still
            // produces a storyboard moment with a screenshot at the press
            // location.
#if ENABLE_INPUT_SYSTEM
            DetectInputSystemPresses();
#else
            DetectLegacyInputPresses();
#endif

            if (Time.unscaledTime >= m_NextUIDocsScan)
            {
                m_NextUIDocsScan = Time.unscaledTime + 1f;
                RegisterUIToolkit();
            }
        }

#if ENABLE_INPUT_SYSTEM
        void DetectInputSystemPresses()
        {
            var ts = Touchscreen.current;
            if (ts != null)
            {
                foreach (var touch in ts.touches)
                {
                    if (touch.press.wasPressedThisFrame)
                        TryHandlePress(touch.position.ReadValue());
                }
            }

            // Mouse / pen on desktop. Mouse extends Pointer, so the press control is on
            // .leftButton specifically (vs Pointer.press which Touchscreen uses).
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                TryHandlePress(mouse.position.ReadValue());

            var pen = Pen.current;
            if (pen != null && pen.tip.wasPressedThisFrame)
                TryHandlePress(pen.position.ReadValue());
        }
#else
        void DetectLegacyInputPresses()
        {
            if (Input.GetMouseButtonDown(0))
                TryHandlePress(Input.mousePosition);
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began) TryHandlePress(t.position);
            }
        }
#endif

        void TryHandlePress(Vector2 screenPos)
        {
            if (Time.unscaledTime - m_LastCaptureRealTime < CAPTURE_THROTTLE_S) return;

            // Raycast through all registered raycasters (GraphicRaycaster,
            // PhysicsRaycaster) when an EventSystem is available, so UI taps
            // get path + label metadata. Misses are still captured — every
            // press produces a storyboard moment regardless of whether a UI
            // element was hit, since "user tapped here" is itself useful
            // crash context. Background-only games (no Canvas with raycasters
            // at all) still get a screenshot per tap.
            string path;
            string label;
            string scene = SceneManager.GetActiveScene().name ?? "";

            var es = EventSystem.current;
            if (es != null)
            {
                var ped = new PointerEventData(es) { position = screenPos };
                var results = new List<RaycastResult>();
                es.RaycastAll(ped, results);
                if (results.Count > 0 && results[0].gameObject != null)
                {
                    var hitGo = results[0].gameObject;
                    path = BuildHierarchyPath(hitGo.transform);
                    label = ExtractUguiLabel(hitGo);
                }
                else
                {
                    path = "(world)";
                    label = $"tap@{Mathf.RoundToInt(screenPos.x)},{Mathf.RoundToInt(screenPos.y)}";
                }
            }
            else
            {
                path = "(no-eventsystem)";
                label = $"tap@{Mathf.RoundToInt(screenPos.x)},{Mathf.RoundToInt(screenPos.y)}";
            }

            EnqueueCapture(screenPos, path, label, scene);
        }

        void EnqueueCapture(Vector2 screenPos, string path, string label, string scene)
        {
            m_LastCaptureRealTime = Time.unscaledTime;
            StartCoroutine(CapturePressFrame(screenPos, path, label, scene));
        }

        IEnumerator CapturePressFrame(Vector2 screenPos, string path, string label, string scene)
        {
            // Wait for end-of-frame so the press's visual response (button highlight) is
            // baked into the captured frame — the storyboard moment then matches what the
            // user saw, not the pre-press state.
            yield return new WaitForEndOfFrame();

            EnsureRT();
            if (m_Rt == null) yield break;

            // Capture into a full-screen-sized intermediate RT first.
            // ScreenCapture.CaptureScreenshotIntoRenderTexture writes pixels
            // 1:1 — it does *not* downscale to fit the target. If we pointed
            // it straight at our 1080-px storage RT, we'd get a cropped
            // top-left rectangle of the screen instead of the full frame.
            // Blit then handles the downscale via the GPU at no extra round-
            // trip cost.
            var sw = Screen.width;
            var sh = Screen.height;
            if (sw <= 0 || sh <= 0) yield break;

            RenderTexture full = RenderTexture.GetTemporary(sw, sh, 0, RenderTextureFormat.ARGB32);
            bool captureOk = false;
            try
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
                Graphics.Blit(full, m_Rt);
                captureOk = true;
            }
            catch (Exception e)
            {
                BugpunchNative.ReportSdkError("BugpunchInputCapture.Capture", e);
            }
            RenderTexture.ReleaseTemporary(full);
            if (!captureOk) yield break;

            var meta = new PendingFrame
            {
                tsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                path = ClampLen(path, MAX_PATH_LEN),
                label = ClampLen(label, MAX_LABEL_LEN),
                scene = scene ?? "",
                x = screenPos.x,
                y = screenPos.y,
                screenW = Screen.width,
                screenH = Screen.height,
                w = m_RtW,
                h = m_RtH,
            };
            m_PendingFrames.Enqueue(meta);

            try { AsyncGPUReadback.Request(m_Rt, 0, OnReadbackComplete); }
            catch (Exception e)
            {
                m_PendingFrames.Dequeue();
                BugpunchNative.ReportSdkError("BugpunchInputCapture.Readback", e);
            }
        }

        void OnReadbackComplete(AsyncGPUReadbackRequest req)
        {
            if (m_PendingFrames.Count == 0) return;
            var pf = m_PendingFrames.Dequeue();

            // ScreenCapture's Y-axis is flipped on some platforms relative to the press
            // coordinates. We don't unflip server-side — we let the dashboard do the math
            // using the screenW/screenH the press was captured against.
            if (req.hasError) return;

            var data = req.GetData<byte>();
            if (data.Length != pf.w * pf.h * 4) return;

            // .ToArray() allocates a managed byte[] for JNI / P-Invoke marshaling. ~660 KB
            // per press at 540×304. Acceptable given the 200 ms throttle.
            byte[] bytes = data.ToArray();

            BugpunchNative.PushButtonPressFrame(
                pf.tsMs, pf.path, pf.label, pf.scene,
                pf.x, pf.y, pf.screenW, pf.screenH,
                pf.w, pf.h, bytes);
        }

        void EnsureRT()
        {
            int sw = Screen.width, sh = Screen.height;
            if (sw <= 0 || sh <= 0) return;

            int w, h;
            if (sw >= sh)
            {
                w = Math.Min(sw, MAX_LONG_SIDE);
                h = Math.Max(1, (int)((long)sh * w / sw));
            }
            else
            {
                h = Math.Min(sh, MAX_LONG_SIDE);
                w = Math.Max(1, (int)((long)sw * h / sh));
            }
            // Round to even for stride sanity.
            w = (w + 1) & ~1;
            h = (h + 1) & ~1;

            if (m_Rt != null && m_RtW == w && m_RtH == h) return;

            if (m_Rt != null)
            {
                m_Rt.Release();
                Destroy(m_Rt);
            }
            m_RtW = w;
            m_RtH = h;
            m_Rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "BugpunchInputCapture",
                useMipMap = false,
                autoGenerateMips = false,
            };
            m_Rt.Create();
        }

        // ── UI Toolkit wiring ───────────────────────────────────────────────

        void RegisterUIToolkit()
        {
            var docs = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            foreach (var d in docs)
            {
                var root = d != null ? d.rootVisualElement : null;
                if (root == null || m_RegisteredRoots.Contains(root)) continue;
                root.RegisterCallback<PointerDownEvent>(OnUITKPointerDown, TrickleDown.TrickleDown);
                m_RegisteredRoots.Add(root);
            }
        }

        void UnregisterUIToolkit()
        {
            foreach (var root in m_RegisteredRoots)
            {
                if (root != null)
                    root.UnregisterCallback<PointerDownEvent>(OnUITKPointerDown, TrickleDown.TrickleDown);
            }
            m_RegisteredRoots.Clear();
        }

        void OnUITKPointerDown(PointerDownEvent evt)
        {
            if (Time.unscaledTime - m_LastCaptureRealTime < CAPTURE_THROTTLE_S) return;

            var target = evt.target as VisualElement;
            if (target == null) return;

            // PointerDownEvent.position is in screen-space pixel coordinates with the
            // origin at the bottom-left, matching uGUI conventions — no conversion needed.
            Vector2 screenPos = new(evt.position.x, evt.position.y);

            string path = BuildVePath(target);
            string label = ExtractVELabel(target);
            string scene = SceneManager.GetActiveScene().name ?? "";

            EnqueueCapture(screenPos, path, label, scene);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        static string BuildHierarchyPath(Transform t)
        {
            if (t == null) return "";
            // Build with a fixed buffer to avoid string concat allocations on a hot path.
            var stack = ListPool<string>.Get();
            try
            {
                while (t != null) { stack.Add(t.name); t = t.parent; }
                var sb = new System.Text.StringBuilder(64);
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    if (sb.Length > 0) sb.Append('/');
                    sb.Append(stack[i]);
                }
                return sb.ToString();
            }
            finally { ListPool<string>.Release(stack); }
        }

        static string BuildVePath(VisualElement ve)
        {
            if (ve == null) return "";
            var stack = ListPool<string>.Get();
            try
            {
                while (ve != null)
                {
                    var name = !string.IsNullOrEmpty(ve.name) ? ve.name : ve.GetType().Name;
                    stack.Add(name);
                    ve = ve.parent;
                }
                var sb = new System.Text.StringBuilder(64);
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    if (sb.Length > 0) sb.Append('/');
                    sb.Append(stack[i]);
                }
                return sb.ToString();
            }
            finally { ListPool<string>.Release(stack); }
        }

        static string ExtractUguiLabel(GameObject go)
        {
            if (go == null) return "";

            // Type prefix — "Button: ...", "Toggle: ...", "Slider", etc. Falls back to
            // the GameObject name when there's no Selectable in the parent chain.
            string typeLabel = "";
            string text = "";

            var sel = go.GetComponentInParent<UnityEngine.UI.Selectable>();
            if (sel != null)
            {
                typeLabel = sel.GetType().Name;
                text = FindLabelText(sel.gameObject);
            }
            else
            {
                // Custom click handlers — IPointerClickHandler / IPointerDownHandler — get
                // the type name of the highest-priority handler the GameObject hosts.
                var handlers = go.GetComponents<IPointerClickHandler>();
                if (handlers != null && handlers.Length > 0 && handlers[0] is Component c)
                    typeLabel = c.GetType().Name;
                text = FindLabelText(go);
            }

            return ComposeLabel(typeLabel, text, go.name);
        }

        static string FindLabelText(GameObject go)
        {
            // TMP first (more common in modern projects), then legacy UI.Text.
            var tmp = go.GetComponentInChildren<TMPro.TMP_Text>(includeInactive: false);
            if (tmp != null && !string.IsNullOrEmpty(tmp.text)) return tmp.text;
            var legacy = go.GetComponentInChildren<UnityEngine.UI.Text>(includeInactive: false);
            if (legacy != null && !string.IsNullOrEmpty(legacy.text)) return legacy.text;
            return "";
        }

        static string ExtractVELabel(VisualElement ve)
        {
            if (ve == null) return "";

            // Walk up to a control type that's the equivalent of a Selectable:
            // Button, Toggle, Slider, etc. UIToolkit's controls live in a separate
            // namespace, so we rely on type-name conventions.
            VisualElement walker = ve;
            string typeLabel = "";
            string text = "";
            while (walker != null && string.IsNullOrEmpty(typeLabel))
            {
                var typeName = walker.GetType().Name;
                if (typeName == "Button" || typeName == "Toggle" || typeName == "Slider"
                    || typeName == "SliderInt" || typeName == "DropdownField"
                    || typeName == "RadioButton" || typeName == "RadioButtonGroup"
                    || typeName == "TextField" || typeName == "Foldout")
                {
                    typeLabel = typeName;
                    // UIToolkit's Button stores label in .text via a private setter on
                    // BaseField — accessible through reflection-free `.text` on Button.
                    var textProp = walker.GetType().GetProperty("text",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (textProp != null && textProp.PropertyType == typeof(string))
                    {
                        try { text = (string)textProp.GetValue(walker) ?? ""; } catch { }
                    }
                    break;
                }
                walker = walker.parent;
            }

            string fallbackName = !string.IsNullOrEmpty(ve.name) ? ve.name : ve.GetType().Name;
            return ComposeLabel(typeLabel, text, fallbackName);
        }

        static string ComposeLabel(string typeLabel, string text, string fallback)
        {
            string trimmed = (text ?? "").Trim();
            if (!string.IsNullOrEmpty(typeLabel) && !string.IsNullOrEmpty(trimmed))
                return $"{typeLabel}: {trimmed}";
            if (!string.IsNullOrEmpty(typeLabel))
                return typeLabel;
            return fallback ?? "";
        }

        static string ClampLen(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= maxLen ? s : s.Substring(s.Length - maxLen, maxLen);
        }

        // Tiny pool to avoid per-call List<string> allocations on the press-detection path.
        static class ListPool<T>
        {
            static readonly Stack<List<T>> s_Pool = new();
            public static List<T> Get() => s_Pool.Count > 0 ? s_Pool.Pop() : new List<T>(8);
            public static void Release(List<T> l) { l.Clear(); s_Pool.Push(l); }
        }
    }
}
