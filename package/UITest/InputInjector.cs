using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace ODDGames.UITest
{
    /// <summary>
    /// Fluent builder for creating key sequences with hold durations.
    /// Supports both simultaneous (Together) and sequential (Then) key combinations.
    ///
    /// Examples:
    ///   // Walk forward for 2 seconds
    ///   await Keys.Hold(Key.W, 2f);
    ///
    ///   // Walk forward-left for 2 seconds (diagonal movement)
    ///   await Keys.Hold(Key.W, Key.A).For(2f);
    ///
    ///   // Sprint forward: hold Shift+W for 3 seconds
    ///   await Keys.Hold(Key.LeftShift, Key.W).For(3f);
    ///
    ///   // Complex sequence: W for 1s, then A for 0.5s, then W+D together for 2s
    ///   await Keys.Hold(Key.W).For(1f).Then(Key.A).For(0.5f).Then(Key.W, Key.D).For(2f);
    ///
    ///   // Tap then hold: press Space, then hold W for 2s
    ///   await Keys.Press(Key.Space).Then(Key.W).For(2f);
    /// </summary>
    public class Keys
    {
        readonly List<KeyStep> _steps = new();

        Keys() { }

        /// <summary>
        /// Start a sequence by holding one or more keys.
        /// Call For() to specify the hold duration.
        /// </summary>
        public static Keys Hold(params Key[] keys) => new Keys().AddStep(keys, 0f, false);

        /// <summary>
        /// Start a sequence by pressing (tap) one or more keys.
        /// </summary>
        public static Keys Press(params Key[] keys) => new Keys().AddStep(keys, 0f, true);

        /// <summary>
        /// Set the duration for the current step.
        /// </summary>
        public Keys For(float seconds)
        {
            if (_steps.Count > 0)
                _steps[_steps.Count - 1] = _steps[_steps.Count - 1].WithDuration(seconds);
            return this;
        }

        /// <summary>
        /// Chain another key hold after the current step.
        /// </summary>
        public Keys Then(params Key[] keys) => AddStep(keys, 0f, false);

        /// <summary>
        /// Chain a key press (tap) after the current step.
        /// </summary>
        public Keys ThenPress(params Key[] keys) => AddStep(keys, 0f, true);

        Keys AddStep(Key[] keys, float duration, bool isPress)
        {
            _steps.Add(new KeyStep(keys, duration, isPress));
            return this;
        }

        /// <summary>
        /// Execute the key sequence.
        /// </summary>
        public async UniTask Execute()
        {
            foreach (var step in _steps)
            {
                if (step.IsPress || step.Duration <= 0f)
                {
                    // Quick press for each key
                    foreach (var key in step.Keys)
                        await InputInjector.PressKey(key);
                }
                else if (step.Keys.Length == 1)
                {
                    await InputInjector.HoldKey(step.Keys[0], step.Duration);
                }
                else
                {
                    await InputInjector.HoldKeys(step.Keys, step.Duration);
                }
            }
        }

        /// <summary>
        /// Gets the awaiter for direct await support on Keys builder.
        /// </summary>
        public Cysharp.Threading.Tasks.UniTask.Awaiter GetAwaiter() => Execute().GetAwaiter();

        readonly struct KeyStep
        {
            public readonly Key[] Keys;
            public readonly float Duration;
            public readonly bool IsPress;

            public KeyStep(Key[] keys, float duration, bool isPress)
            {
                Keys = keys;
                Duration = duration;
                IsPress = isPress;
            }

            public KeyStep WithDuration(float duration) => new KeyStep(Keys, duration, IsPress);
        }
    }

    /// <summary>
    /// Public utility class for injecting input events using Unity's Input System.
    /// Used by UITestBehaviour and AI testing systems.
    /// </summary>
    public static class InputInjector
    {
        /// <summary>
        /// Logs a debug message only when UITestBehaviour.DebugMode is enabled.
        /// </summary>
        static void LogDebug(string message)
        {
            if (UITestBehaviour.DebugMode)
                Debug.Log($"[InputInjector] {message}");
        }
        /// <summary>
        /// Gets the screen position of a GameObject (works with both UI and world-space objects).
        /// </summary>
        public static Vector2 GetScreenPosition(GameObject go)
        {
            if (go == null) return Vector2.zero;

            if (go.TryGetComponent<RectTransform>(out var rt))
            {
                // UI element - get center of rect in screen space
                Vector3[] corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                Vector3 center = (corners[0] + corners[2]) / 2f;

                // Find the canvas to determine if it's screen space or world space
                var canvas = go.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    return center;
                }
                else
                {
                    // World space or camera space canvas
                    Camera cam = canvas?.worldCamera ?? Camera.main;
                    return cam != null ? RectTransformUtility.WorldToScreenPoint(cam, center) : (Vector2)center;
                }
            }

            // Try Renderer bounds (3D objects)
            if (go.TryGetComponent<Renderer>(out var renderer))
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    return cam.WorldToScreenPoint(renderer.bounds.center);
                }
            }

            // Fallback to transform position
            {
                Camera cam = Camera.main;
                return cam != null ? (Vector2)cam.WorldToScreenPoint(go.transform.position) : Vector2.zero;
            }
        }

        /// <summary>
        /// Gets the screen-space bounding box of a GameObject.
        /// </summary>
        public static Rect GetScreenBounds(GameObject go)
        {
            if (go == null) return Rect.zero;

            if (go.TryGetComponent<RectTransform>(out var rt))
            {
                Vector3[] corners = new Vector3[4];
                rt.GetWorldCorners(corners);

                var canvas = go.GetComponentInParent<Canvas>();
                Camera cam = null;
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    cam = canvas.worldCamera ?? Camera.main;
                }

                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;

                for (int i = 0; i < 4; i++)
                {
                    Vector2 screenPos;
                    if (cam != null)
                        screenPos = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                    else
                        screenPos = corners[i];

                    minX = Mathf.Min(minX, screenPos.x);
                    maxX = Mathf.Max(maxX, screenPos.x);
                    minY = Mathf.Min(minY, screenPos.y);
                    maxY = Mathf.Max(maxY, screenPos.y);
                }

                return new Rect(minX, minY, maxX - minX, maxY - minY);
            }

            // Try Renderer bounds (3D objects)
            if (go.TryGetComponent<Renderer>(out var renderer))
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    var bounds = renderer.bounds;
                    Vector2 screenMin = cam.WorldToScreenPoint(bounds.min);
                    Vector2 screenMax = cam.WorldToScreenPoint(bounds.max);

                    return new Rect(
                        Mathf.Min(screenMin.x, screenMax.x),
                        Mathf.Min(screenMin.y, screenMax.y),
                        Mathf.Abs(screenMax.x - screenMin.x),
                        Mathf.Abs(screenMax.y - screenMin.y)
                    );
                }
            }

            return Rect.zero;
        }

        #region High-Level Action Helpers
        // These are the SHARED action implementations that all execution paths should use.
        // This ensures consistent behavior whether executed via AI, Visual Builder, or Code.

        /// <summary>
        /// Calculates the screen position to click on a slider to set it to the target value.
        /// </summary>
        /// <param name="slider">The slider component</param>
        /// <param name="normalizedValue">Target value from 0 to 1</param>
        /// <returns>Screen position to click</returns>
        public static Vector2 GetSliderClickPosition(Slider slider, float normalizedValue)
        {
            var bounds = GetScreenBounds(slider.gameObject);

            return slider.direction switch
            {
                Slider.Direction.LeftToRight => new Vector2(
                    bounds.x + bounds.width * normalizedValue,
                    bounds.y + bounds.height * 0.5f),
                Slider.Direction.RightToLeft => new Vector2(
                    bounds.x + bounds.width * (1f - normalizedValue),
                    bounds.y + bounds.height * 0.5f),
                Slider.Direction.BottomToTop => new Vector2(
                    bounds.x + bounds.width * 0.5f,
                    bounds.y + bounds.height * normalizedValue),
                Slider.Direction.TopToBottom => new Vector2(
                    bounds.x + bounds.width * 0.5f,
                    bounds.y + bounds.height * (1f - normalizedValue)),
                _ => bounds.center
            };
        }

        /// <summary>
        /// Calculates the screen position to click on a scrollbar to set it to the target value.
        /// </summary>
        /// <param name="scrollbar">The scrollbar component</param>
        /// <param name="normalizedValue">Target value from 0 to 1</param>
        /// <returns>Screen position to click</returns>
        public static Vector2 GetScrollbarClickPosition(Scrollbar scrollbar, float normalizedValue)
        {
            var bounds = GetScreenBounds(scrollbar.gameObject);

            return scrollbar.direction switch
            {
                Scrollbar.Direction.LeftToRight => new Vector2(
                    bounds.x + bounds.width * normalizedValue,
                    bounds.y + bounds.height * 0.5f),
                Scrollbar.Direction.RightToLeft => new Vector2(
                    bounds.x + bounds.width * (1f - normalizedValue),
                    bounds.y + bounds.height * 0.5f),
                Scrollbar.Direction.BottomToTop => new Vector2(
                    bounds.x + bounds.width * 0.5f,
                    bounds.y + bounds.height * normalizedValue),
                Scrollbar.Direction.TopToBottom => new Vector2(
                    bounds.x + bounds.width * 0.5f,
                    bounds.y + bounds.height * (1f - normalizedValue)),
                _ => bounds.center
            };
        }

        /// <summary>
        /// Calculates a directional offset vector based on direction string and distance.
        /// Uses screen height for consistent distance scaling.
        /// </summary>
        /// <param name="direction">Direction: "up", "down", "left", "right"</param>
        /// <param name="normalizedDistance">Distance as fraction of screen (0-1)</param>
        /// <returns>Pixel offset vector</returns>
        public static Vector2 GetDirectionOffset(string direction, float normalizedDistance)
        {
            float pixelDistance = normalizedDistance * Screen.height;

            return direction?.ToLowerInvariant() switch
            {
                "up" => new Vector2(0, pixelDistance),
                "down" => new Vector2(0, -pixelDistance),
                "left" => new Vector2(-pixelDistance, 0),
                "right" => new Vector2(pixelDistance, 0),
                _ => Vector2.zero
            };
        }

        /// <summary>
        /// Sets a slider to a specific normalized value (0-1) via click.
        /// </summary>
        public static async UniTask SetSlider(Slider slider, float normalizedValue)
        {
            var clickPos = GetSliderClickPosition(slider, normalizedValue);
            await InjectPointerTap(clickPos);
        }

        /// <summary>
        /// Sets a scrollbar to a specific normalized value (0-1) via click.
        /// </summary>
        public static async UniTask SetScrollbar(Scrollbar scrollbar, float normalizedValue)
        {
            var clickPos = GetScrollbarClickPosition(scrollbar, normalizedValue);
            await InjectPointerTap(clickPos);
        }

        /// <summary>
        /// Clears an input field's text content.
        /// Note: Uses direct text manipulation as a workaround because Unity's IMGUI keyboard
        /// shortcuts (Ctrl+A, Backspace) cannot be injected via the new Input System.
        /// </summary>
        public static async UniTask ClearInputField(GameObject inputFieldGO)
        {
            // Check if there's text to clear
            var tmpInput = inputFieldGO.GetComponent<TMP_InputField>();
            if (tmpInput != null)
            {
                if (string.IsNullOrEmpty(tmpInput.text))
                    return;

                // Click to focus the field first
                var screenPos = GetScreenPosition(inputFieldGO);
                await InjectPointerTap(screenPos);
                await UniTask.DelayFrame(2);

                // Clear the text directly - workaround for IMGUI keyboard shortcut limitation
                // Then re-activate the field since setting text can deactivate it
                tmpInput.text = "";
                tmpInput.ActivateInputField();
                tmpInput.MoveTextEnd(false);
                await UniTask.DelayFrame(2);
                return;
            }

            var legacyInput = inputFieldGO.GetComponent<InputField>();
            if (legacyInput != null)
            {
                if (string.IsNullOrEmpty(legacyInput.text))
                    return;

                // Click to focus the field first
                var screenPos = GetScreenPosition(inputFieldGO);
                await InjectPointerTap(screenPos);
                await UniTask.DelayFrame(2);

                // Clear the text directly - workaround for IMGUI keyboard shortcut limitation
                // Then re-activate the field since setting text can deactivate it
                legacyInput.text = "";
                legacyInput.ActivateInputField();
                legacyInput.MoveTextEnd(false);
                await UniTask.DelayFrame(2);
            }
        }

        /// <summary>
        /// Presses a key while holding a modifier key (e.g., Ctrl+A, Shift+Tab).
        /// This simulates the real sequence: modifier down, key down, key up, modifier up.
        /// </summary>
        public static async UniTask PressKeyWithModifier(Key modifier, Key key)
        {
            await EnsureGameViewFocusAsync();

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] PressKeyWithModifier - No keyboard device found");
                return;
            }

            // Step 1: Press modifier key down
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(modifier));
            InputSystem.Update();
            await UniTask.Yield();
            await UniTask.DelayFrame(2); // Give time for EventSystem to process

            // Step 2: Press the main key while modifier is held
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(modifier, key));
            InputSystem.Update();
            await UniTask.Yield();
            await UniTask.DelayFrame(2);

            // Step 3: Release the main key (modifier still held)
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(modifier));
            InputSystem.Update();
            await UniTask.Yield();

            // Step 4: Release modifier
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            await UniTask.Yield();
        }

        /// <summary>
        /// Types text into an input field.
        ///
        /// Note: TMP_InputField uses IMGUI's Event.PopEvent() for keyboard input, not the Input System's
        /// Keyboard.onTextInput. InputSystem.QueueTextEvent() does not work with TMP_InputField.
        /// This method uses direct text manipulation as the only reliable cross-platform approach.
        /// See: https://discussions.unity.com/t/code-to-fix-tmp_inputfield-to-support-new-inputsystem/774250
        /// </summary>
        public static async UniTask TypeIntoField(GameObject inputFieldGO, string text, bool clearFirst = true, bool pressEnter = false)
        {
            var screenPos = GetScreenPosition(inputFieldGO);
            LogDebug($"TypeIntoField '{inputFieldGO.name}' at ({screenPos.x:F0},{screenPos.y:F0}) text='{text}'");

            // Click to focus the field
            await InjectPointerTap(screenPos);
            await UniTask.DelayFrame(2);

            // TMP_InputField uses IMGUI Event.PopEvent() for keyboard input, not Input System.
            // InputSystem.QueueTextEvent() does NOT work with TMP_InputField.
            // We must use direct text manipulation.
            var tmpInput = inputFieldGO.GetComponent<TMP_InputField>();
            if (tmpInput != null)
            {
                if (clearFirst)
                    tmpInput.text = text ?? "";
                else
                    tmpInput.text += text ?? "";

                // Re-activate and move cursor to end
                tmpInput.ActivateInputField();
                tmpInput.MoveTextEnd(false);
                await UniTask.DelayFrame(2);
            }
            else
            {
                var legacyInput = inputFieldGO.GetComponent<InputField>();
                if (legacyInput != null)
                {
                    if (clearFirst)
                        legacyInput.text = text ?? "";
                    else
                        legacyInput.text += text ?? "";

                    // Re-activate and move cursor to end
                    legacyInput.ActivateInputField();
                    legacyInput.MoveTextEnd(false);
                    await UniTask.DelayFrame(2);
                }
                else
                {
                    Debug.LogWarning($"[InputInjector] TypeIntoField - No TMP_InputField or InputField found on '{inputFieldGO.name}'");
                }
            }

            // Press Enter if requested
            if (pressEnter)
            {
                await UniTask.DelayFrame(2);
                await PressKey(Key.Enter);
            }
        }

        /// <summary>
        /// Performs a swipe gesture on an element.
        /// </summary>
        public static async UniTask Swipe(GameObject element, string direction, float normalizedDistance = 0.2f, float duration = 0.3f)
        {
            var startPos = GetScreenPosition(element);
            var offset = GetDirectionOffset(direction, normalizedDistance);
            var endPos = startPos + offset;

            await InjectMouseDrag(startPos, endPos, duration);
        }

        /// <summary>
        /// Performs a scroll action on a scrollable element.
        /// </summary>
        public static async UniTask ScrollElement(GameObject scrollableElement, string direction, float amount = 0.3f)
        {
            var center = GetScreenPosition(scrollableElement);

            // Convert direction and amount to scroll delta
            // Positive Y = scroll up (content moves down), Negative Y = scroll down
            float scrollDelta = amount * 500f; // Consistent scroll multiplier

            Vector2 delta = direction?.ToLowerInvariant() switch
            {
                "up" => new Vector2(0, scrollDelta),
                "down" => new Vector2(0, -scrollDelta),
                "left" => new Vector2(-scrollDelta, 0),
                "right" => new Vector2(scrollDelta, 0),
                _ => Vector2.zero
            };

            await InjectScroll(center, delta);
        }

        /// <summary>
        /// Performs a pinch gesture on an element or screen center.
        /// Scale > 1 zooms in, scale less than 1 zooms out.
        /// </summary>
        public static async UniTask Pinch(GameObject element, float scale, float duration = 0.5f)
        {
            Vector2 center = element != null
                ? GetScreenPosition(element)
                : new Vector2(Screen.width / 2f, Screen.height / 2f);

            await InjectPinch(center, scale, duration);
        }

        /// <summary>
        /// Performs a two-finger swipe gesture on an element or screen center.
        /// </summary>
        public static async UniTask TwoFingerSwipe(GameObject element, string direction, float normalizedDistance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {
            Vector2 centerPos = element != null
                ? GetScreenPosition(element)
                : new Vector2(Screen.width / 2f, Screen.height / 2f);

            var offset = GetDirectionOffset(direction, normalizedDistance);

            // Calculate finger positions
            var spacing = fingerSpacing * Screen.height / 2f;
            var finger1Start = centerPos + new Vector2(-spacing, 0);
            var finger2Start = centerPos + new Vector2(spacing, 0);
            var finger1End = finger1Start + offset;
            var finger2End = finger2Start + offset;

            await InjectTwoFingerDrag(finger1Start, finger1End, finger2Start, finger2End, duration);
        }

        /// <summary>
        /// Performs a two-finger swipe gesture at a specific screen position.
        /// </summary>
        /// <param name="centerPosition">Center screen position for the swipe.</param>
        /// <param name="direction">Direction: "up", "down", "left", "right".</param>
        /// <param name="normalizedDistance">Distance as fraction of screen (0-1).</param>
        /// <param name="duration">Duration of the swipe in seconds.</param>
        /// <param name="fingerSpacing">Normalized spacing between fingers (0-1).</param>
        public static async UniTask InjectTwoFingerSwipe(Vector2 centerPosition, string direction, float normalizedDistance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {
            var offset = GetDirectionOffset(direction, normalizedDistance);

            // Calculate finger positions
            var spacing = fingerSpacing * Screen.height / 2f;
            var finger1Start = centerPosition + new Vector2(-spacing, 0);
            var finger2Start = centerPosition + new Vector2(spacing, 0);
            var finger1End = finger1Start + offset;
            var finger2End = finger2Start + offset;

            await InjectTwoFingerDrag(finger1Start, finger1End, finger2Start, finger2End, duration);
        }

        /// <summary>
        /// Performs a two-finger rotation gesture on an element or screen center.
        /// </summary>
        public static async UniTask Rotate(GameObject element, float degrees, float duration = 0.5f, float fingerDistance = 0.05f)
        {
            Vector2 centerPos = element != null
                ? GetScreenPosition(element)
                : new Vector2(Screen.width / 2f, Screen.height / 2f);

            await InjectRotate(centerPos, degrees, duration, fingerDistance);
        }

        #endregion

        /// <summary>
        /// Ensures the Game view has focus in the Editor so input events are received.
        /// Does nothing at runtime or in builds.
        /// </summary>
        public static async UniTask EnsureGameViewFocusAsync()
        {
#if UNITY_EDITOR
            var gameViewType = System.Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameViewType != null)
            {
                var gameView = UnityEditor.EditorWindow.GetWindow(gameViewType, false, null, false);
                if (gameView != null)
                {
                    gameView.Focus();
                    // Wait for focus to take effect
                    await UniTask.Yield();
                    await UniTask.Yield();
                }
            }
#else
            await UniTask.CompletedTask;
#endif
        }

        /// <summary>
        /// Returns true if we should use touch input instead of mouse.
        /// On mobile platforms or when no mouse is available but touchscreen is.
        /// </summary>
        public static bool ShouldUseTouchInput()
        {
#if UNITY_IOS || UNITY_ANDROID
            return true;
#else
            // On desktop, use mouse if available, otherwise fall back to touch
            return Mouse.current == null && Touchscreen.current != null;
#endif
        }

        /// <summary>
        /// Injects a click/tap at the specified screen position.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// </summary>
        public static async UniTask InjectPointerTap(Vector2 screenPosition)
        {
            LogDebug($"InjectPointerTap at ({screenPosition.x:F0},{screenPosition.y:F0})");
            await EnsureGameViewFocusAsync();

            if (ShouldUseTouchInput())
            {
                LogDebug("Using touch input");
                await InjectTouchTap(screenPosition);
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] Click - No mouse device found, cannot inject click");
                return;
            }

            LogDebug($"Using mouse input, device={mouse.deviceId}");

            // Use MouseState struct for complete state control
            var mouseState = new MouseState { position = screenPosition, delta = Vector2.zero };

            // Move mouse to position
            InputSystem.QueueStateEvent(mouse, mouseState);
            InputSystem.Update(); // Force event processing
            await UniTask.Yield();

            // Mouse button down
            mouseState = mouseState.WithButton(MouseButton.Left, true);
            InputSystem.QueueStateEvent(mouse, mouseState);
            InputSystem.Update(); // Force event processing
            await UniTask.Yield();

            // Mouse button up
            mouseState = mouseState.WithButton(MouseButton.Left, false);
            InputSystem.QueueStateEvent(mouse, mouseState);
            InputSystem.Update(); // Force event processing
            LogDebug("InjectPointerTap complete");
            await UniTask.Yield();
        }

        /// <summary>
        /// Injects a double click/tap at the specified screen position.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// </summary>
        public static async UniTask InjectPointerDoubleTap(Vector2 screenPosition)
        {
            await EnsureGameViewFocusAsync();

            if (ShouldUseTouchInput())
            {
                await InjectTouchDoubleTap(screenPosition);
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] DoubleClick - No mouse device found");
                return;
            }

            var mouseState = new MouseState { position = screenPosition, delta = Vector2.zero };

            // First click
            InputSystem.QueueStateEvent(mouse, mouseState);
            InputSystem.Update();
            await UniTask.Yield();

            mouseState = mouseState.WithButton(MouseButton.Left, true);
            InputSystem.QueueStateEvent(mouse, mouseState);
            InputSystem.Update();
            await UniTask.Yield();

            mouseState = mouseState.WithButton(MouseButton.Left, false);
            InputSystem.QueueStateEvent(mouse, mouseState);
            InputSystem.Update();
            await UniTask.Yield();

            // Brief delay between clicks - needs to be long enough for Unity's Button to reset
            // but short enough to be recognized as a double-click by the system
            await UniTask.Delay(100);

            // Re-set position before second click to ensure it's registered after the delay
            mouseState = new MouseState { position = screenPosition, delta = Vector2.zero };
            InputSystem.QueueStateEvent(mouse, mouseState);
            InputSystem.Update();
            await UniTask.Yield();

            // Second click
            mouseState = mouseState.WithButton(MouseButton.Left, true);
            InputSystem.QueueStateEvent(mouse, mouseState);
            InputSystem.Update();
            await UniTask.Yield();

            mouseState = mouseState.WithButton(MouseButton.Left, false);
            InputSystem.QueueStateEvent(mouse, mouseState);
            InputSystem.Update();
            await UniTask.Yield();
        }

        /// <summary>
        /// Injects a double-tap touch gesture.
        /// </summary>
        public static async UniTask InjectTouchDoubleTap(Vector2 screenPosition)
        {
            // First tap
            await InjectTouchTap(screenPosition);

            // Brief delay between taps - needs to be long enough for Unity's Button to reset
            await UniTask.Delay(100);

            // Second tap
            await InjectTouchTap(screenPosition);
        }

        /// <summary>
        /// Injects a triple click/tap at the specified screen position.
        /// Triple-click is commonly used to select all text in input fields.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// </summary>
        public static async UniTask InjectPointerTripleTap(Vector2 screenPosition)
        {
            await EnsureGameViewFocusAsync();

            if (ShouldUseTouchInput())
            {
                await InjectTouchTripleTap(screenPosition);
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] TripleClick - No mouse device found");
                return;
            }

            // Three clicks in quick succession
            for (int i = 0; i < 3; i++)
            {
                var mouseState = new MouseState { position = screenPosition, delta = Vector2.zero };

                // Move to position
                InputSystem.QueueStateEvent(mouse, mouseState);
                InputSystem.Update();
                await UniTask.Yield();

                // Mouse down
                mouseState = mouseState.WithButton(MouseButton.Left, true);
                InputSystem.QueueStateEvent(mouse, mouseState);
                InputSystem.Update();
                await UniTask.Yield();

                // Mouse up
                mouseState = mouseState.WithButton(MouseButton.Left, false);
                InputSystem.QueueStateEvent(mouse, mouseState);
                InputSystem.Update();
                await UniTask.Yield();

                // Brief delay between clicks (short enough to be recognized as multi-click)
                if (i < 2)
                    await UniTask.Delay(50);
            }
        }

        /// <summary>
        /// Injects a triple-tap touch gesture.
        /// </summary>
        public static async UniTask InjectTouchTripleTap(Vector2 screenPosition)
        {
            for (int i = 0; i < 3; i++)
            {
                await InjectTouchTap(screenPosition);
                if (i < 2)
                    await UniTask.Delay(50);
            }
        }

        /// <summary>
        /// Injects a single-finger tap gesture using the Input System.
        /// Used on mobile platforms (iOS/Android).
        /// </summary>
        public static async UniTask InjectTouchTap(Vector2 screenPosition)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                touchscreen = InputSystem.AddDevice<Touchscreen>();
                if (touchscreen == null)
                {
                    Debug.LogWarning("[InputInjector] TouchTap - Could not create touchscreen device");
                    return;
                }
            }

            const int touchId = 1; // Touch IDs must be non-zero

            // Touch began
            using (StateEvent.From(touchscreen, out var beginPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, beginPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(screenPosition, beginPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, beginPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, beginPtr);
                InputSystem.QueueEvent(beginPtr);
            }
            InputSystem.Update(); // Force event processing

            await UniTask.Yield();

            // Touch ended (tap is just began + ended at same position)
            using (StateEvent.From(touchscreen, out var endPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, endPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(screenPosition, endPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, endPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(0f, endPtr);
                InputSystem.QueueEvent(endPtr);
            }
            InputSystem.Update(); // Force event processing

            await UniTask.Yield();
        }

        /// <summary>
        /// Injects a drag gesture using the appropriate input method for the platform.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// </summary>
        /// <param name="startPos">Start position in screen coordinates</param>
        /// <param name="endPos">End position in screen coordinates</param>
        /// <param name="duration">Duration of the drag movement (minimum 1 second)</param>
        /// <param name="holdTime">Time to hold at start position before dragging (default 0.5s)</param>
        public static async UniTask InjectPointerDrag(Vector2 startPos, Vector2 endPos, float duration, float holdTime = 0.5f)
        {
            await EnsureGameViewFocusAsync();

            if (ShouldUseTouchInput())
            {
                await InjectTouchDrag(startPos, endPos, duration, holdTime);
                return;
            }

            await InjectMouseDrag(startPos, endPos, duration, holdTime);
        }

        /// <summary>
        /// Injects a mouse drag from start to end position using the Input System.
        /// Uses frame-based yields to ensure Unity processes events each frame (matching touch behavior).
        /// </summary>
        /// <param name="startPos">Start position in screen coordinates</param>
        /// <param name="endPos">End position in screen coordinates</param>
        /// <param name="duration">Duration of the drag movement</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        public static async UniTask InjectMouseDrag(Vector2 startPos, Vector2 endPos, float duration, float holdTime = 0.5f)
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] MouseDrag - No mouse device found, cannot inject drag");
                return;
            }

            LogDebug($"MouseDrag start=({startPos.x:F0},{startPos.y:F0}) end=({endPos.x:F0},{endPos.y:F0}) duration={duration}s hold={holdTime}s");

            Vector2 previousPos = startPos;

            // Move mouse to start position
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(startPos, posPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            InputSystem.Update(); // Force event processing
            await UniTask.Yield();

            // Mouse button down at start
            using (StateEvent.From(mouse, out var downPtr))
            {
                mouse.position.WriteValueIntoEvent(startPos, downPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, downPtr);
                mouse.leftButton.WriteValueIntoEvent(1f, downPtr);
                InputSystem.QueueEvent(downPtr);
            }
            InputSystem.Update(); // Force event processing
            await UniTask.Yield(); // Allow PointerDown to register

            LogDebug($"MouseDrag mouse down at ({startPos.x:F0},{startPos.y:F0})");

            // Hold at start position before dragging (real-time based)
            if (holdTime > 0)
            {
                float holdEndTime = Time.realtimeSinceStartup + holdTime;
                while (Time.realtimeSinceStartup < holdEndTime)
                {
                    using (StateEvent.From(mouse, out var holdPtr))
                    {
                        mouse.position.WriteValueIntoEvent(startPos, holdPtr);
                        mouse.delta.WriteValueIntoEvent(Vector2.zero, holdPtr);
                        mouse.leftButton.WriteValueIntoEvent(1f, holdPtr);
                        InputSystem.QueueEvent(holdPtr);
                    }
                    InputSystem.Update();
                    await UniTask.Yield();
                }
                LogDebug($"MouseDrag held for {holdTime}s");
            }

            // Interpolate mouse position over duration (real-time based)
            float dragStartTime = Time.realtimeSinceStartup;
            float dragEndTime = dragStartTime + duration;
            while (Time.realtimeSinceStartup < dragEndTime)
            {
                float t = Mathf.Clamp01((Time.realtimeSinceStartup - dragStartTime) / duration);
                Vector2 currentPos = Vector2.Lerp(startPos, endPos, t);
                Vector2 delta = currentPos - previousPos;

                using (StateEvent.From(mouse, out var movePtr))
                {
                    mouse.position.WriteValueIntoEvent(currentPos, movePtr);
                    mouse.delta.WriteValueIntoEvent(delta, movePtr);
                    mouse.leftButton.WriteValueIntoEvent(1f, movePtr);
                    InputSystem.QueueEvent(movePtr);
                }
                InputSystem.Update(); // Force event processing each frame

                previousPos = currentPos;
                await UniTask.Yield(); // Frame-based to ensure event processing
            }

            // Ensure we reach the final position
            using (StateEvent.From(mouse, out var finalPtr))
            {
                mouse.position.WriteValueIntoEvent(endPos, finalPtr);
                mouse.delta.WriteValueIntoEvent(endPos - previousPos, finalPtr);
                mouse.leftButton.WriteValueIntoEvent(1f, finalPtr);
                InputSystem.QueueEvent(finalPtr);
            }
            InputSystem.Update();

            // Mouse button up at end
            using (StateEvent.From(mouse, out var upPtr))
            {
                mouse.position.WriteValueIntoEvent(endPos, upPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, upPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, upPtr);
                InputSystem.QueueEvent(upPtr);
            }
            InputSystem.Update(); // Force event processing

            LogDebug($"MouseDrag mouse up at ({endPos.x:F0},{endPos.y:F0})");

            // Allow UI to process the drag end event
            await UniTask.Yield();
            await UniTask.Yield();
        }

        /// <summary>
        /// Injects a single-finger touch drag gesture using the Input System.
        /// Used on mobile platforms (iOS/Android).
        /// </summary>
        /// <param name="startPos">Start position in screen coordinates</param>
        /// <param name="endPos">End position in screen coordinates</param>
        /// <param name="duration">Duration of the drag movement</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        public static async UniTask InjectTouchDrag(Vector2 startPos, Vector2 endPos, float duration, float holdTime = 0.5f)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                touchscreen = InputSystem.AddDevice<Touchscreen>();
                if (touchscreen == null)
                {
                    Debug.LogWarning("[InputInjector] TouchDrag - Could not create touchscreen device");
                    return;
                }
            }

            const int touchId = 1;
            Vector2 previousPos = startPos;

            // Touch began
            using (StateEvent.From(touchscreen, out var beginPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, beginPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(startPos, beginPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, beginPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, beginPtr);
                InputSystem.QueueEvent(beginPtr);
            }
            InputSystem.Update();
            await UniTask.Yield();

            // Hold at start position before dragging (real-time based, send Stationary events)
            if (holdTime > 0)
            {
                float holdEndTime = Time.realtimeSinceStartup + holdTime;
                while (Time.realtimeSinceStartup < holdEndTime)
                {
                    using (StateEvent.From(touchscreen, out var holdPtr))
                    {
                        touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, holdPtr);
                        touchscreen.touches[0].position.WriteValueIntoEvent(startPos, holdPtr);
                        touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, holdPtr);
                        touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Stationary, holdPtr);
                        touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, holdPtr);
                        InputSystem.QueueEvent(holdPtr);
                    }
                    InputSystem.Update();
                    await UniTask.Yield();
                }
            }

            // Touch moved (real-time based interpolation)
            float dragStartTime = Time.realtimeSinceStartup;
            float dragEndTime = dragStartTime + duration;
            while (Time.realtimeSinceStartup < dragEndTime)
            {
                float t = Mathf.Clamp01((Time.realtimeSinceStartup - dragStartTime) / duration);
                Vector2 currentPos = Vector2.Lerp(startPos, endPos, t);
                Vector2 delta = currentPos - previousPos;

                using (StateEvent.From(touchscreen, out var movePtr))
                {
                    touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, movePtr);
                    touchscreen.touches[0].position.WriteValueIntoEvent(currentPos, movePtr);
                    touchscreen.touches[0].delta.WriteValueIntoEvent(delta, movePtr);
                    touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);
                    touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, movePtr);
                    InputSystem.QueueEvent(movePtr);
                }
                InputSystem.Update();
                previousPos = currentPos;
                await UniTask.Yield();
            }

            // Ensure we reach the final position
            using (StateEvent.From(touchscreen, out var finalPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, finalPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(endPos, finalPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(endPos - previousPos, finalPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, finalPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, finalPtr);
                InputSystem.QueueEvent(finalPtr);
            }
            InputSystem.Update();

            // Touch ended
            using (StateEvent.From(touchscreen, out var endPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, endPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(endPos, endPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(endPos - previousPos, endPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(0f, endPtr);
                InputSystem.QueueEvent(endPtr);
            }
            InputSystem.Update();
            await UniTask.Yield();
        }

        /// <summary>
        /// Injects a scroll event at the specified position.
        /// </summary>
        /// <param name="position">Screen position to scroll at</param>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        public static async UniTask InjectScroll(Vector2 position, float delta)
        {
            await EnsureGameViewFocusAsync();

            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] Scroll - No mouse device found");
                return;
            }

            // Move mouse to position first
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(position, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            InputSystem.Update();
            await UniTask.Yield();

            // Send scroll event (120 is standard scroll delta unit)
            using (StateEvent.From(mouse, out var scrollPtr))
            {
                mouse.position.WriteValueIntoEvent(position, scrollPtr);
                mouse.scroll.WriteValueIntoEvent(new Vector2(0, delta * 120), scrollPtr);
                InputSystem.QueueEvent(scrollPtr);
            }
            InputSystem.Update();
            await UniTask.Yield();
        }

        /// <summary>
        /// Injects a scroll event at the specified position with a Vector2 delta.
        /// </summary>
        /// <param name="position">Screen position to scroll at</param>
        /// <param name="scrollDelta">Scroll delta vector (for horizontal and vertical scrolling)</param>
        public static async UniTask InjectScroll(Vector2 position, Vector2 scrollDelta)
        {
            await EnsureGameViewFocusAsync();

            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] Scroll - No mouse device found");
                return;
            }

            // Move to position first
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(position, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            InputSystem.Update();
            await UniTask.Yield();

            // Send scroll event
            using (StateEvent.From(mouse, out var scrollPtr))
            {
                mouse.position.WriteValueIntoEvent(position, scrollPtr);
                mouse.scroll.WriteValueIntoEvent(scrollDelta, scrollPtr);
                InputSystem.QueueEvent(scrollPtr);
            }
            InputSystem.Update();
            await UniTask.Yield();
        }

        /// <summary>
        /// Types text character by character using InputSystem.QueueTextEvent.
        /// This triggers Keyboard.onTextInput callback for any subscribers.
        ///
        /// Note: TMP_InputField does NOT use Keyboard.onTextInput - it uses IMGUI Event.PopEvent().
        /// For TMP_InputField, use TypeIntoField() instead which handles this limitation.
        /// </summary>
        public static async UniTask TypeText(string text)
        {
            LogDebug($"TypeText '{text}' ({text?.Length ?? 0} chars)");

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] TypeText - No keyboard device found");
                return;
            }

            foreach (char c in text)
            {
                if (!Application.isPlaying) break;

                // Queue text event - this is how Unity's InputTestFixture does it
                // See: https://github.com/Unity-Technologies/InputSystem/blob/develop/Assets/Tests/InputSystem/CoreTests_Devices.cs
                InputSystem.QueueTextEvent(keyboard, c);
                InputSystem.Update();
                await UniTask.Yield();
            }

            LogDebug("TypeText complete");
        }

        /// <summary>
        /// Selects all text in the currently focused input field using Ctrl+A.
        /// </summary>
        public static async UniTask SelectAllText()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] SelectAllText - No keyboard device found");
                return;
            }

            // Press Ctrl+A
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl, Key.A));
            InputSystem.Update();
            await UniTask.DelayFrame(2);

            // Release keys
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            await UniTask.Yield();
        }

        /// <summary>
        /// Deletes all text in the currently focused input field using Ctrl+A followed by Backspace.
        /// </summary>
        public static async UniTask DeleteAllText()
        {
            await SelectAllText();
            await PressKey(Key.Backspace);
        }

        /// <summary>
        /// Presses and releases a keyboard key.
        /// </summary>
        public static async UniTask PressKey(Key key)
        {
            await EnsureGameViewFocusAsync();

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] PressKey - No keyboard device found");
                return;
            }

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            InputSystem.Update();
            await UniTask.Yield();

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            await UniTask.Yield();
        }

        /// <summary>
        /// Holds a keyboard key for the specified duration.
        /// </summary>
        /// <param name="key">The key to hold</param>
        /// <param name="duration">How long to hold the key in seconds</param>
        public static async UniTask HoldKey(Key key, float duration)
        {
            await EnsureGameViewFocusAsync();

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] HoldKey - No keyboard device found");
                return;
            }

            var keyState = new KeyboardState(key);

            // Key down
            InputSystem.QueueStateEvent(keyboard, keyState);
            InputSystem.Update();
            await UniTask.Yield();

            // Hold for duration - re-queue key state each frame so input system registers continuous hold
            float elapsed = 0f;
            while (elapsed < duration)
            {
                InputSystem.QueueStateEvent(keyboard, keyState);
                InputSystem.Update();
                await UniTask.Yield();
                elapsed += Time.deltaTime;
            }

            // Key up
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            await UniTask.Yield();
        }

        /// <summary>
        /// Holds multiple keyboard keys simultaneously for the specified duration.
        /// </summary>
        /// <param name="keys">The keys to hold together</param>
        /// <param name="duration">How long to hold the keys in seconds</param>
        public static async UniTask HoldKeys(Key[] keys, float duration)
        {
            if (keys == null || keys.Length == 0)
                return;

            await EnsureGameViewFocusAsync();

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] HoldKeys - No keyboard device found");
                return;
            }

            var keyState = new KeyboardState(keys);

            // Keys down (all at once)
            InputSystem.QueueStateEvent(keyboard, keyState);
            InputSystem.Update();
            await UniTask.Yield();

            // Hold for duration - re-queue key state each frame so input system registers continuous hold
            float elapsed = 0f;
            while (elapsed < duration)
            {
                InputSystem.QueueStateEvent(keyboard, keyState);
                InputSystem.Update();
                await UniTask.Yield();
                elapsed += Time.deltaTime;
            }

            // Keys up
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            await UniTask.Yield();
        }

        /// <summary>
        /// Injects a hold/long-press at the specified screen position.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// </summary>
        public static async UniTask InjectPointerHold(Vector2 screenPosition, float holdSeconds)
        {
            await EnsureGameViewFocusAsync();

            if (ShouldUseTouchInput())
            {
                await InjectTouchHold(screenPosition, holdSeconds);
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] Hold - No mouse device found, cannot inject hold");
                return;
            }

            // Use MouseState struct for complete state control
            var mouseState = new MouseState { position = screenPosition, delta = Vector2.zero };

            // Move mouse to position
            InputSystem.QueueStateEvent(mouse, mouseState);
            InputSystem.Update();
            await UniTask.Yield();

            // Mouse button down
            mouseState = mouseState.WithButton(MouseButton.Left, true);
            InputSystem.QueueStateEvent(mouse, mouseState);
            InputSystem.Update();
            await UniTask.Yield();

            // Hold for specified duration - re-queue state each frame
            float elapsed = 0f;
            while (elapsed < holdSeconds)
            {
                InputSystem.QueueStateEvent(mouse, mouseState);
                InputSystem.Update();
                await UniTask.Yield();
                elapsed += Time.deltaTime;
            }

            // Mouse button up
            mouseState = mouseState.WithButton(MouseButton.Left, false);
            InputSystem.QueueStateEvent(mouse, mouseState);
            InputSystem.Update();
            await UniTask.Yield();
        }

        /// <summary>
        /// Injects a touch hold/long-press gesture using the Input System.
        /// Used on mobile platforms (iOS/Android).
        /// </summary>
        public static async UniTask InjectTouchHold(Vector2 screenPosition, float holdSeconds)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                touchscreen = InputSystem.AddDevice<Touchscreen>();
                if (touchscreen == null)
                {
                    Debug.LogWarning("[InputInjector] TouchHold - Could not create touchscreen device");
                    return;
                }
            }

            const int touchId = 1; // Touch IDs must be non-zero

            // Touch began
            using (StateEvent.From(touchscreen, out var beginPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, beginPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(screenPosition, beginPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, beginPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, beginPtr);
                InputSystem.QueueEvent(beginPtr);
            }
            InputSystem.Update();
            await UniTask.Yield();

            // Hold for specified duration (touch stays in Stationary phase)
            float elapsed = 0f;
            while (elapsed < holdSeconds)
            {
                using (StateEvent.From(touchscreen, out var stationaryPtr))
                {
                    touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, stationaryPtr);
                    touchscreen.touches[0].position.WriteValueIntoEvent(screenPosition, stationaryPtr);
                    touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, stationaryPtr);
                    touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Stationary, stationaryPtr);
                    touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, stationaryPtr);
                    InputSystem.QueueEvent(stationaryPtr);
                }
                InputSystem.Update();
                await UniTask.Yield();
                elapsed += Time.deltaTime;
            }

            // Touch ended
            using (StateEvent.From(touchscreen, out var endPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId, endPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(screenPosition, endPtr);
                touchscreen.touches[0].delta.WriteValueIntoEvent(Vector2.zero, endPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(0f, endPtr);
                InputSystem.QueueEvent(endPtr);
            }
            InputSystem.Update();
            await UniTask.Yield();
        }

        /// <summary>
        /// Injects a pinch gesture for zooming.
        /// </summary>
        /// <param name="centerPosition">Center point of the pinch</param>
        /// <param name="scale">Scale factor: less than 1 = pinch in (zoom out), greater than 1 = pinch out (zoom in)</param>
        /// <param name="duration">Duration of the pinch gesture</param>
        public static async UniTask InjectPinch(Vector2 centerPosition, float scale, float duration)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                touchscreen = InputSystem.AddDevice<Touchscreen>();
                if (touchscreen == null)
                {
                    Debug.LogWarning("[InputInjector] Pinch - Could not create touchscreen device");
                    return;
                }
            }

            // Calculate start and end offsets based on scale
            float startOffset = 100f; // Initial distance from center
            float endOffset = startOffset * scale;

            // Two touch points that move symmetrically
            Vector2 startTouch1 = centerPosition + new Vector2(-startOffset, 0);
            Vector2 startTouch2 = centerPosition + new Vector2(startOffset, 0);
            Vector2 endTouch1 = centerPosition + new Vector2(-endOffset, 0);
            Vector2 endTouch2 = centerPosition + new Vector2(endOffset, 0);

            const int touchId1 = 1;
            const int touchId2 = 2;
            int totalFrames = Mathf.Max(5, Mathf.RoundToInt(duration * 60));

            // Both touches begin
            using (StateEvent.From(touchscreen, out var beginPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId1, beginPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(startTouch1, beginPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, beginPtr);

                touchscreen.touches[1].touchId.WriteValueIntoEvent(touchId2, beginPtr);
                touchscreen.touches[1].position.WriteValueIntoEvent(startTouch2, beginPtr);
                touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[1].pressure.WriteValueIntoEvent(1f, beginPtr);

                InputSystem.QueueEvent(beginPtr);
            }
            InputSystem.Update();
            await UniTask.Yield();

            // Interpolate pinch movement
            for (int i = 1; i < totalFrames; i++)
            {
                float t = (float)i / totalFrames;
                Vector2 currentTouch1 = Vector2.Lerp(startTouch1, endTouch1, t);
                Vector2 currentTouch2 = Vector2.Lerp(startTouch2, endTouch2, t);

                using (StateEvent.From(touchscreen, out var movePtr))
                {
                    touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId1, movePtr);
                    touchscreen.touches[0].position.WriteValueIntoEvent(currentTouch1, movePtr);
                    touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);
                    touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, movePtr);

                    touchscreen.touches[1].touchId.WriteValueIntoEvent(touchId2, movePtr);
                    touchscreen.touches[1].position.WriteValueIntoEvent(currentTouch2, movePtr);
                    touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);
                    touchscreen.touches[1].pressure.WriteValueIntoEvent(1f, movePtr);

                    InputSystem.QueueEvent(movePtr);
                }
                InputSystem.Update();
                await UniTask.Yield();
            }

            // Both touches end
            using (StateEvent.From(touchscreen, out var endPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId1, endPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(endTouch1, endPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(0f, endPtr);

                touchscreen.touches[1].touchId.WriteValueIntoEvent(touchId2, endPtr);
                touchscreen.touches[1].position.WriteValueIntoEvent(endTouch2, endPtr);
                touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[1].pressure.WriteValueIntoEvent(0f, endPtr);

                InputSystem.QueueEvent(endPtr);
            }
            InputSystem.Update();
            await UniTask.Yield();
        }

        /// <summary>
        /// Injects a pinch gesture for zooming with custom finger distance.
        /// </summary>
        /// <param name="centerPosition">Center point of the pinch</param>
        /// <param name="scale">Scale factor: less than 1 = pinch in (zoom out), greater than 1 = pinch out (zoom in)</param>
        /// <param name="duration">Duration of the pinch gesture</param>
        /// <param name="fingerDistancePixels">Initial distance of each finger from center in pixels</param>
        public static async UniTask InjectPinch(Vector2 centerPosition, float scale, float duration, float fingerDistancePixels)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                touchscreen = InputSystem.AddDevice<Touchscreen>();
                if (touchscreen == null)
                {
                    Debug.LogWarning("[InputInjector] Pinch - Could not create touchscreen device");
                    return;
                }
            }

            // Calculate start and end offsets based on scale and finger distance
            float startOffset = fingerDistancePixels;
            float endOffset = startOffset * scale;

            // Two touch points that move symmetrically
            Vector2 startTouch1 = centerPosition + new Vector2(-startOffset, 0);
            Vector2 startTouch2 = centerPosition + new Vector2(startOffset, 0);
            Vector2 endTouch1 = centerPosition + new Vector2(-endOffset, 0);
            Vector2 endTouch2 = centerPosition + new Vector2(endOffset, 0);

            const int touchId1 = 1;
            const int touchId2 = 2;
            int totalFrames = Mathf.Max(5, Mathf.RoundToInt(duration * 60));

            // Both touches begin
            using (StateEvent.From(touchscreen, out var beginPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId1, beginPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(startTouch1, beginPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, beginPtr);

                touchscreen.touches[1].touchId.WriteValueIntoEvent(touchId2, beginPtr);
                touchscreen.touches[1].position.WriteValueIntoEvent(startTouch2, beginPtr);
                touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[1].pressure.WriteValueIntoEvent(1f, beginPtr);

                InputSystem.QueueEvent(beginPtr);
            }
            InputSystem.Update();
            await UniTask.Yield();

            // Interpolate pinch movement
            for (int i = 1; i < totalFrames; i++)
            {
                float t = (float)i / totalFrames;
                Vector2 currentTouch1 = Vector2.Lerp(startTouch1, endTouch1, t);
                Vector2 currentTouch2 = Vector2.Lerp(startTouch2, endTouch2, t);

                using (StateEvent.From(touchscreen, out var movePtr))
                {
                    touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId1, movePtr);
                    touchscreen.touches[0].position.WriteValueIntoEvent(currentTouch1, movePtr);
                    touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);
                    touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, movePtr);

                    touchscreen.touches[1].touchId.WriteValueIntoEvent(touchId2, movePtr);
                    touchscreen.touches[1].position.WriteValueIntoEvent(currentTouch2, movePtr);
                    touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);
                    touchscreen.touches[1].pressure.WriteValueIntoEvent(1f, movePtr);

                    InputSystem.QueueEvent(movePtr);
                }
                InputSystem.Update();
                await UniTask.Yield();
            }

            // Both touches end
            using (StateEvent.From(touchscreen, out var endPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId1, endPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(endTouch1, endPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(0f, endPtr);

                touchscreen.touches[1].touchId.WriteValueIntoEvent(touchId2, endPtr);
                touchscreen.touches[1].position.WriteValueIntoEvent(endTouch2, endPtr);
                touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[1].pressure.WriteValueIntoEvent(0f, endPtr);

                InputSystem.QueueEvent(endPtr);
            }
            InputSystem.Update();
            await UniTask.Yield();
        }

        /// <summary>
        /// Simulates a two-finger drag gesture (both fingers moving in parallel).
        /// </summary>
        public static async UniTask InjectTwoFingerDrag(Vector2 start1, Vector2 end1, Vector2 start2, Vector2 end2, float duration)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                touchscreen = InputSystem.AddDevice<Touchscreen>();
                if (touchscreen == null)
                {
                    Debug.LogWarning("[InputInjector] TwoFingerDrag - Could not create touchscreen device");
                    return;
                }
            }

            const int touchId1 = 1;
            const int touchId2 = 2;
            int totalFrames = Mathf.Max(5, Mathf.RoundToInt(duration * 60));

            // Both touches begin
            using (StateEvent.From(touchscreen, out var beginPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId1, beginPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(start1, beginPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, beginPtr);

                touchscreen.touches[1].touchId.WriteValueIntoEvent(touchId2, beginPtr);
                touchscreen.touches[1].position.WriteValueIntoEvent(start2, beginPtr);
                touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[1].pressure.WriteValueIntoEvent(1f, beginPtr);

                InputSystem.QueueEvent(beginPtr);
            }
            InputSystem.Update();
            await UniTask.Yield();

            // Interpolate movement
            for (int i = 1; i < totalFrames; i++)
            {
                float t = (float)i / totalFrames;
                Vector2 current1 = Vector2.Lerp(start1, end1, t);
                Vector2 current2 = Vector2.Lerp(start2, end2, t);

                using (StateEvent.From(touchscreen, out var movePtr))
                {
                    touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId1, movePtr);
                    touchscreen.touches[0].position.WriteValueIntoEvent(current1, movePtr);
                    touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);
                    touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, movePtr);

                    touchscreen.touches[1].touchId.WriteValueIntoEvent(touchId2, movePtr);
                    touchscreen.touches[1].position.WriteValueIntoEvent(current2, movePtr);
                    touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);
                    touchscreen.touches[1].pressure.WriteValueIntoEvent(1f, movePtr);

                    InputSystem.QueueEvent(movePtr);
                }
                InputSystem.Update();
                await UniTask.Yield();
            }

            // Both touches end
            using (StateEvent.From(touchscreen, out var endPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId1, endPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(end1, endPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(0f, endPtr);

                touchscreen.touches[1].touchId.WriteValueIntoEvent(touchId2, endPtr);
                touchscreen.touches[1].position.WriteValueIntoEvent(end2, endPtr);
                touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[1].pressure.WriteValueIntoEvent(0f, endPtr);

                InputSystem.QueueEvent(endPtr);
            }
            InputSystem.Update();
            await UniTask.Yield();
        }

        /// <summary>
        /// Simulates a two-finger rotation gesture.
        /// </summary>
        /// <param name="centerPosition">Center point of the rotation.</param>
        /// <param name="degrees">Rotation angle (positive = clockwise, negative = counter-clockwise).</param>
        /// <param name="duration">Duration of the gesture in seconds.</param>
        /// <param name="fingerDistance">Normalized distance from center (0-1) for finger positions.</param>
        public static async UniTask InjectRotate(Vector2 centerPosition, float degrees, float duration, float fingerDistance = 0.05f)
        {
            // Calculate the radius based on screen size and finger distance
            float radiusPixels = fingerDistance * Mathf.Min(Screen.width, Screen.height);
            await InjectRotatePixels(centerPosition, degrees, duration, radiusPixels);
        }

        /// <summary>
        /// Simulates a two-finger rotation gesture with pixel-based radius.
        /// </summary>
        /// <param name="centerPosition">Center point of the rotation.</param>
        /// <param name="degrees">Rotation angle (positive = clockwise, negative = counter-clockwise).</param>
        /// <param name="duration">Duration of the gesture in seconds.</param>
        /// <param name="radiusPixels">Distance from center in pixels for finger positions.</param>
        public static async UniTask InjectRotatePixels(Vector2 centerPosition, float degrees, float duration, float radiusPixels)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                touchscreen = InputSystem.AddDevice<Touchscreen>();
                if (touchscreen == null)
                {
                    Debug.LogWarning("[InputInjector] Rotate - Could not create touchscreen device");
                    return;
                }
            }

            float radius = radiusPixels;
            float radians = degrees * Mathf.Deg2Rad;

            // Start positions (fingers on opposite sides horizontally)
            Vector2 startTouch1 = centerPosition + new Vector2(-radius, 0);
            Vector2 startTouch2 = centerPosition + new Vector2(radius, 0);

            const int touchId1 = 1;
            const int touchId2 = 2;
            int totalFrames = Mathf.Max(5, Mathf.RoundToInt(duration * 60));

            // Both touches begin
            using (StateEvent.From(touchscreen, out var beginPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId1, beginPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(startTouch1, beginPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, beginPtr);

                touchscreen.touches[1].touchId.WriteValueIntoEvent(touchId2, beginPtr);
                touchscreen.touches[1].position.WriteValueIntoEvent(startTouch2, beginPtr);
                touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginPtr);
                touchscreen.touches[1].pressure.WriteValueIntoEvent(1f, beginPtr);

                InputSystem.QueueEvent(beginPtr);
            }
            InputSystem.Update();
            await UniTask.Yield();

            // Rotate touches around the center
            for (int i = 1; i <= totalFrames; i++)
            {
                float t = (float)i / totalFrames;
                float currentAngle = radians * t;

                // Calculate rotated positions
                Vector2 offset1 = new Vector2(
                    -radius * Mathf.Cos(currentAngle) - 0 * Mathf.Sin(currentAngle),
                    -radius * Mathf.Sin(currentAngle) + 0 * Mathf.Cos(currentAngle)
                );
                Vector2 offset2 = new Vector2(
                    radius * Mathf.Cos(currentAngle) - 0 * Mathf.Sin(currentAngle),
                    radius * Mathf.Sin(currentAngle) + 0 * Mathf.Cos(currentAngle)
                );

                Vector2 currentTouch1 = centerPosition + offset1;
                Vector2 currentTouch2 = centerPosition + offset2;

                using (StateEvent.From(touchscreen, out var movePtr))
                {
                    touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId1, movePtr);
                    touchscreen.touches[0].position.WriteValueIntoEvent(currentTouch1, movePtr);
                    touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);
                    touchscreen.touches[0].pressure.WriteValueIntoEvent(1f, movePtr);

                    touchscreen.touches[1].touchId.WriteValueIntoEvent(touchId2, movePtr);
                    touchscreen.touches[1].position.WriteValueIntoEvent(currentTouch2, movePtr);
                    touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, movePtr);
                    touchscreen.touches[1].pressure.WriteValueIntoEvent(1f, movePtr);

                    InputSystem.QueueEvent(movePtr);
                }
                InputSystem.Update();
                await UniTask.Yield();
            }

            // Calculate final positions
            Vector2 endOffset1 = new Vector2(-radius * Mathf.Cos(radians), -radius * Mathf.Sin(radians));
            Vector2 endOffset2 = new Vector2(radius * Mathf.Cos(radians), radius * Mathf.Sin(radians));
            Vector2 endTouch1 = centerPosition + endOffset1;
            Vector2 endTouch2 = centerPosition + endOffset2;

            // Both touches end
            using (StateEvent.From(touchscreen, out var endPtr))
            {
                touchscreen.touches[0].touchId.WriteValueIntoEvent(touchId1, endPtr);
                touchscreen.touches[0].position.WriteValueIntoEvent(endTouch1, endPtr);
                touchscreen.touches[0].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[0].pressure.WriteValueIntoEvent(0f, endPtr);

                touchscreen.touches[1].touchId.WriteValueIntoEvent(touchId2, endPtr);
                touchscreen.touches[1].position.WriteValueIntoEvent(endTouch2, endPtr);
                touchscreen.touches[1].phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endPtr);
                touchscreen.touches[1].pressure.WriteValueIntoEvent(0f, endPtr);

                InputSystem.QueueEvent(endPtr);
            }
            InputSystem.Update();
            await UniTask.Yield();
        }
    }
}
