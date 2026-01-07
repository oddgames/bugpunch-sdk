using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using TMPro;

namespace ODDGames.UITest
{
    public class UITestInputInterceptor : MonoBehaviour
    {
        public static UITestInputInterceptor Instance { get; private set; }

        // Delegate for custom component type detection (used by EzGUI module, etc.)
        public delegate string ComponentTypeDetector(GameObject go);
        private static List<ComponentTypeDetector> componentTypeDetectors = new();

        /// <summary>
        /// Register a custom component type detector.
        /// Called by extension modules (e.g., EzGUI) to detect their component types.
        /// </summary>
        public static void RegisterComponentTypeDetector(ComponentTypeDetector detector)
        {
            if (detector != null && !componentTypeDetectors.Contains(detector))
            {
                componentTypeDetectors.Add(detector);
            }
        }

        /// <summary>
        /// Unregister a component type detector.
        /// </summary>
        public static void UnregisterComponentTypeDetector(ComponentTypeDetector detector)
        {
            componentTypeDetectors.Remove(detector);
        }

        const float DRAG_THRESHOLD = 20f;
        const float HOLD_THRESHOLD = 0.5f;
        const float CLICK_MAX_DURATION = 0.3f;

        bool isPointerDown;
        Vector2 pointerDownPosition;
        float pointerDownTime;
        bool isDragging;
        Vector2 lastDragPosition;
        GameObject pointerDownTarget;
        List<RaycastResult> raycastResults = new();

        // Key press tracking
        HashSet<Key> keysDownThisFrame = new();
        HashSet<Key> keysDownLastFrame = new();

        void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnEnable()
        {
            EnhancedTouchSupport.Enable();
        }

        void OnDisable()
        {
            EnhancedTouchSupport.Disable();
        }

        void Update()
        {
            if (!UITestRecorder.Instance || !UITestRecorder.Instance.IsRecording) return;

            ProcessInput();
        }

        void ProcessInput()
        {
            // Use Input System EnhancedTouch for touch input
            var activeTouches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
            if (activeTouches.Count > 0)
            {
                ProcessTouch(activeTouches[0]);
            }
            else
            {
                ProcessMouse();
            }

            ProcessKeyboard();
        }

        void ProcessTouch(UnityEngine.InputSystem.EnhancedTouch.Touch touch)
        {
            switch (touch.phase)
            {
                case UnityEngine.InputSystem.TouchPhase.Began:
                    OnPointerDown(touch.screenPosition);
                    break;
                case UnityEngine.InputSystem.TouchPhase.Moved:
                case UnityEngine.InputSystem.TouchPhase.Stationary:
                    OnPointerMove(touch.screenPosition);
                    break;
                case UnityEngine.InputSystem.TouchPhase.Ended:
                case UnityEngine.InputSystem.TouchPhase.Canceled:
                    OnPointerUp(touch.screenPosition);
                    break;
            }
        }

        void ProcessMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 mousePos = mouse.position.ReadValue();

            if (mouse.leftButton.wasPressedThisFrame)
            {
                OnPointerDown(mousePos);
            }
            else if (mouse.leftButton.isPressed)
            {
                OnPointerMove(mousePos);
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                OnPointerUp(mousePos);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.1f)
            {
                OnScroll(mousePos, scroll);
            }
        }

        void ProcessKeyboard()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Swap the hashsets
            (keysDownThisFrame, keysDownLastFrame) = (keysDownLastFrame, keysDownThisFrame);
            keysDownThisFrame.Clear();

            // Check all keys we care about for recording
            foreach (var key in GetRecordableKeys())
            {
                var keyControl = keyboard[key];
                if (keyControl != null && keyControl.isPressed)
                {
                    keysDownThisFrame.Add(key);

                    // Only record on key down (not held)
                    if (!keysDownLastFrame.Contains(key))
                    {
                        RecordKeyPress(key);
                    }
                }
            }
        }

        static readonly Key[] recordableKeys = new Key[]
        {
            // Navigation keys
            Key.UpArrow, Key.DownArrow, Key.LeftArrow, Key.RightArrow,
            Key.Enter, Key.NumpadEnter, Key.Escape, Key.Tab, Key.Space,
            Key.Backspace, Key.Delete,
            // Letters (for text input detection)
            Key.A, Key.B, Key.C, Key.D, Key.E, Key.F, Key.G, Key.H, Key.I, Key.J,
            Key.K, Key.L, Key.M, Key.N, Key.O, Key.P, Key.Q, Key.R, Key.S, Key.T,
            Key.U, Key.V, Key.W, Key.X, Key.Y, Key.Z,
            // Numbers
            Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
            Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
            // Numpad
            Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4,
            Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9,
        };

        static IEnumerable<Key> GetRecordableKeys() => recordableKeys;

        void RecordKeyPress(Key key)
        {
            // Get the currently focused/selected object
            GameObject target = EventSystem.current?.currentSelectedGameObject;
            string targetName = target != null ? target.name : "";
            string targetPath = target != null ? GetHierarchyPath(target) : "";

            // Convert Input System Key to legacy KeyCode for the recorder
            KeyCode keyCode = KeyToKeyCode(key);

            UITestRecorder.Instance.RecordKeyPress(keyCode, targetName, targetPath);
        }

        static KeyCode KeyToKeyCode(Key key)
        {
            return key switch
            {
                // Navigation
                Key.UpArrow => KeyCode.UpArrow,
                Key.DownArrow => KeyCode.DownArrow,
                Key.LeftArrow => KeyCode.LeftArrow,
                Key.RightArrow => KeyCode.RightArrow,
                Key.Enter => KeyCode.Return,
                Key.NumpadEnter => KeyCode.KeypadEnter,
                Key.Escape => KeyCode.Escape,
                Key.Tab => KeyCode.Tab,
                Key.Space => KeyCode.Space,
                Key.Backspace => KeyCode.Backspace,
                Key.Delete => KeyCode.Delete,

                // Letters
                Key.A => KeyCode.A, Key.B => KeyCode.B, Key.C => KeyCode.C, Key.D => KeyCode.D,
                Key.E => KeyCode.E, Key.F => KeyCode.F, Key.G => KeyCode.G, Key.H => KeyCode.H,
                Key.I => KeyCode.I, Key.J => KeyCode.J, Key.K => KeyCode.K, Key.L => KeyCode.L,
                Key.M => KeyCode.M, Key.N => KeyCode.N, Key.O => KeyCode.O, Key.P => KeyCode.P,
                Key.Q => KeyCode.Q, Key.R => KeyCode.R, Key.S => KeyCode.S, Key.T => KeyCode.T,
                Key.U => KeyCode.U, Key.V => KeyCode.V, Key.W => KeyCode.W, Key.X => KeyCode.X,
                Key.Y => KeyCode.Y, Key.Z => KeyCode.Z,

                // Numbers
                Key.Digit0 => KeyCode.Alpha0, Key.Digit1 => KeyCode.Alpha1, Key.Digit2 => KeyCode.Alpha2,
                Key.Digit3 => KeyCode.Alpha3, Key.Digit4 => KeyCode.Alpha4, Key.Digit5 => KeyCode.Alpha5,
                Key.Digit6 => KeyCode.Alpha6, Key.Digit7 => KeyCode.Alpha7, Key.Digit8 => KeyCode.Alpha8,
                Key.Digit9 => KeyCode.Alpha9,

                // Numpad
                Key.Numpad0 => KeyCode.Keypad0, Key.Numpad1 => KeyCode.Keypad1, Key.Numpad2 => KeyCode.Keypad2,
                Key.Numpad3 => KeyCode.Keypad3, Key.Numpad4 => KeyCode.Keypad4, Key.Numpad5 => KeyCode.Keypad5,
                Key.Numpad6 => KeyCode.Keypad6, Key.Numpad7 => KeyCode.Keypad7, Key.Numpad8 => KeyCode.Keypad8,
                Key.Numpad9 => KeyCode.Keypad9,

                _ => KeyCode.None
            };
        }

        void OnPointerDown(Vector2 position)
        {
            isPointerDown = true;
            pointerDownPosition = position;
            lastDragPosition = position;
            pointerDownTime = Time.realtimeSinceStartup;
            isDragging = false;
            pointerDownTarget = GetUIElementAtPosition(position);
        }

        void OnPointerMove(Vector2 position)
        {
            if (!isPointerDown) return;

            float distance = Vector2.Distance(position, pointerDownPosition);
            if (distance > DRAG_THRESHOLD && !isDragging)
            {
                isDragging = true;
            }

            lastDragPosition = position;
        }

        void OnPointerUp(Vector2 position)
        {
            if (!isPointerDown) return;

            float duration = Time.realtimeSinceStartup - pointerDownTime;
            float distance = Vector2.Distance(position, pointerDownPosition);

            if (isDragging && distance > DRAG_THRESHOLD)
            {
                RecordDrag(pointerDownPosition, position, duration);
            }
            else if (duration >= HOLD_THRESHOLD)
            {
                RecordHold(position, duration);
            }
            else
            {
                RecordClick(position);
            }

            isPointerDown = false;
            isDragging = false;
            pointerDownTarget = null;
        }

        void OnScroll(Vector2 position, float delta)
        {
            var target = GetUIElementAtPosition(position);
            if (target == null) return;

            var (siblingIndex, siblingCount) = GetSiblingInfo(target);

            UITestRecorder.Instance.RecordScroll(
                target.name,
                GetHierarchyPath(target),
                GetComponentTypeName(target),
                GetParentName(target),
                position,
                delta,
                siblingIndex,
                siblingCount,
                GetGrandparentName(target)
            );
        }

        void RecordClick(Vector2 position)
        {
            var target = GetUIElementAtPosition(position);
            if (target == null) return;

            var textContent = GetTextContent(target);
            var (siblingIndex, siblingCount) = GetSiblingInfo(target);

            UITestRecorder.Instance.RecordClick(
                target.name,
                GetHierarchyPath(target),
                GetComponentTypeName(target),
                GetParentName(target),
                position,
                textContent,
                siblingIndex,
                siblingCount,
                GetGrandparentName(target)
            );
        }

        void RecordHold(Vector2 position, float duration)
        {
            var target = pointerDownTarget != null ? pointerDownTarget : GetUIElementAtPosition(position);
            if (target == null) return;

            var (siblingIndex, siblingCount) = GetSiblingInfo(target);

            UITestRecorder.Instance.RecordHold(
                target.name,
                GetHierarchyPath(target),
                GetComponentTypeName(target),
                GetParentName(target),
                position,
                duration,
                siblingIndex,
                siblingCount,
                GetGrandparentName(target)
            );
        }

        void RecordDrag(Vector2 startPosition, Vector2 endPosition, float duration)
        {
            var startTarget = pointerDownTarget != null ? pointerDownTarget : GetUIElementAtPosition(startPosition);

            string targetName = startTarget != null ? startTarget.name : "Screen";
            string targetPath = startTarget != null ? GetHierarchyPath(startTarget) : "";
            string targetType = startTarget != null ? GetComponentTypeName(startTarget) : "";
            string parentName = startTarget != null ? GetParentName(startTarget) : "";
            string grandparentName = startTarget != null ? GetGrandparentName(startTarget) : "";

            var scrollRect = FindScrollRectInParents(startTarget);
            string scrollRectName = scrollRect != null ? scrollRect.name : null;

            var (siblingIndex, siblingCount) = startTarget != null
                ? GetSiblingInfo(startTarget)
                : (0, 0);

            UITestRecorder.Instance.RecordDrag(
                targetName,
                targetPath,
                targetType,
                parentName,
                startPosition,
                endPosition,
                duration,
                scrollRectName,
                siblingIndex,
                siblingCount,
                grandparentName
            );
        }

        GameObject GetUIElementAtPosition(Vector2 screenPosition)
        {
            if (EventSystem.current == null) return null;

            var pointerData = new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };

            raycastResults.Clear();
            EventSystem.current.RaycastAll(pointerData, raycastResults);

            foreach (var result in raycastResults)
            {
                if (result.gameObject.activeInHierarchy)
                {
                    return result.gameObject;
                }
            }

            return null;
        }

        string GetHierarchyPath(GameObject go)
        {
            if (go == null) return "";

            var path = go.name;
            var parent = go.transform.parent;

            int depth = 0;
            while (parent != null && depth < 5)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
                depth++;
            }

            return path;
        }

        string GetComponentTypeName(GameObject go)
        {
            if (go == null) return "";

            // Check custom detectors first (e.g., EZ GUI types)
            foreach (var detector in componentTypeDetectors)
            {
                var result = detector(go);
                if (!string.IsNullOrEmpty(result))
                    return result;
            }

            // Standard Unity UI types
            if (go.GetComponent<Button>()) return "Button";
            if (go.GetComponent<Toggle>()) return "Toggle";
            if (go.GetComponent<Slider>()) return "Slider";
            if (go.GetComponent<InputField>()) return "InputField";
            if (go.GetComponent<TMP_InputField>()) return "TMP_InputField";
            if (go.GetComponent<ScrollRect>()) return "ScrollRect";
            if (go.GetComponent<Dropdown>()) return "Dropdown";
            if (go.GetComponent<TMP_Dropdown>()) return "TMP_Dropdown";
            if (go.GetComponent<Selectable>()) return "Selectable";
            if (go.GetComponent<Image>()) return "Image";
            if (go.GetComponent<RawImage>()) return "RawImage";

            return "GameObject";
        }

        string GetGrandparentName(GameObject go)
        {
            if (go == null) return "";
            var parent = go.transform.parent;
            if (parent == null) return "";
            var grandparent = parent.parent;
            if (grandparent == null) return "";
            return grandparent.name;
        }

        string GetParentName(GameObject go)
        {
            if (go == null || go.transform.parent == null) return "";
            return go.transform.parent.name;
        }

        string GetTextContent(GameObject go)
        {
            if (go == null) return "";

            var tmp = go.GetComponentInChildren<TMP_Text>();
            if (tmp != null && !string.IsNullOrEmpty(tmp.text))
            {
                return tmp.text.Trim();
            }

            var text = go.GetComponentInChildren<Text>();
            if (text != null && !string.IsNullOrEmpty(text.text))
            {
                return text.text.Trim();
            }

            return "";
        }

        (int index, int count) GetSiblingInfo(GameObject go)
        {
            if (go == null || go.transform.parent == null)
                return (0, 1);

            var parent = go.transform.parent;
            var sameName = new List<Transform>();

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (!child.gameObject.activeInHierarchy) continue;

                if (child.name == go.name)
                {
                    sameName.Add(child);
                }
            }

            int siblingIndex = 0;
            for (int i = 0; i < sameName.Count; i++)
            {
                if (sameName[i] == go.transform)
                {
                    siblingIndex = i;
                    break;
                }
            }

            return (siblingIndex, sameName.Count);
        }

        ScrollRect FindScrollRectInParents(GameObject go)
        {
            if (go == null) return null;

            var current = go.transform;
            while (current != null)
            {
                var scrollRect = current.GetComponent<ScrollRect>();
                if (scrollRect != null)
                    return scrollRect;
                current = current.parent;
            }

            return null;
        }
    }
}
