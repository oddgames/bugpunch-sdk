using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

using Debug = UnityEngine.Debug;

namespace ODDGames.UIAutomation
{
    /// <summary>
    /// Async helpers for Unity frame-based operations.
    /// </summary>
    public static class Async
    {
        private static SynchronizationContext _unitySyncContext;

        /// <summary>
        /// Captures the Unity main thread synchronization context.
        /// Call this from a MonoBehaviour Awake/Start or RuntimeInitializeOnLoadMethod.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CaptureContext()
        {
            _unitySyncContext = SynchronizationContext.Current;
        }

        /// <summary>
        /// Ensures execution continues on the Unity main thread and waits for a frame.
        /// Use this after completing an action to ensure the EventSystem has processed events.
        /// </summary>
        public static async Task ToMainThread()
        {
            // If we're already on main thread with Unity's context, just yield a frame
            if (SynchronizationContext.Current == _unitySyncContext && _unitySyncContext != null)
            {
                await DelayFrames(1);
                return;
            }

            // Otherwise, marshal back to main thread
            var tcs = new TaskCompletionSource<bool>();
            if (_unitySyncContext != null)
            {
                _unitySyncContext.Post(_ =>
                {
                    tcs.SetResult(true);
                }, null);
                await tcs.Task;
                await DelayFrames(1);
            }
            else
            {
                // Fallback if context wasn't captured (shouldn't happen in normal use)
                await DelayFrames(1);
            }
        }

        /// <summary>
        /// Waits for the specified number of frames.
        /// </summary>
        public static async Task DelayFrames(int frameCount = 1, CancellationToken ct = default)
        {
            int target = Time.frameCount + frameCount;
            while (Time.frameCount < target)
            {
                ct.ThrowIfCancellationRequested();

                await Task.Yield();
            }
        }

        /// <summary>
        /// Waits for at least the specified number of frames AND at least the specified time.
        /// Use this for timing-critical operations like multi-click detection where both
        /// frame processing and real-time thresholds matter.
        /// Uses Stopwatch for accurate wall-clock timing independent of Unity's TimeScale.
        /// </summary>
        /// <param name="minFrames">Minimum number of frames to wait</param>
        /// <param name="minSeconds">Minimum time to wait in seconds</param>
        /// <param name="ct">Cancellation token</param>
        public static async Task Delay(int minFrames, float minSeconds, CancellationToken ct = default)
        {
            int targetFrame = Time.frameCount + minFrames;
            long startTicks = Stopwatch.GetTimestamp();
            long targetTicks = startTicks + (long)(minSeconds * Stopwatch.Frequency);

            while (Time.frameCount < targetFrame || Stopwatch.GetTimestamp() < targetTicks)
            {
                ct.ThrowIfCancellationRequested();

                await Task.Yield();
            }
        }

    }

    /// <summary>
    /// Extension methods for Task.
    /// </summary>
    public static class TaskExtensions
    {
        /// <summary>
        /// Fire and forget a task, logging any exceptions.
        /// </summary>
        public static void Forget(this Task task)
        {
            if (task == null) return;
            task.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                    Debug.LogException(t.Exception.InnerException ?? t.Exception);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// Fire and forget a task with a result.
        /// </summary>
        public static void Forget<T>(this Task<T> task)
        {
            if (task == null) return;
            task.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                    Debug.LogException(t.Exception.InnerException ?? t.Exception);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }


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
        public static Keys Press(params Key[] keys) => new Keys().AddStep(keys, 0f);

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
        public Keys ThenPress(params Key[] keys) => AddStep(keys, 0f);

        Keys AddStep(Key[] keys, float duration, bool isPress = true)
        {
            _steps.Add(new KeyStep(keys, duration, isPress));
            return this;
        }

        /// <summary>
        /// Execute the key sequence.
        /// </summary>
        public async Task Execute()
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
        public System.Runtime.CompilerServices.TaskAwaiter GetAwaiter() => Execute().GetAwaiter();

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
    /// Specifies which mouse button to use for drag operations.
    /// </summary>
    public enum PointerButton
    {
        /// <summary>Left mouse button (default for most drag operations)</summary>
        Left,
        /// <summary>Right mouse button (for context menus, camera rotation, etc.)</summary>
        Right,
        /// <summary>Middle mouse button (for panning, special actions)</summary>
        Middle
    }

    /// <summary>
    /// Internal utility class for injecting input events using Unity's Input System.
    /// Used by ActionExecutor and UIAutomationTestFixture. Call Setup()/TearDown() to manage lifecycle.
    /// </summary>
    internal static class InputInjector
    {
        private static List<InputDevice> _disabledDevices = new List<InputDevice>();

        private static Mouse _virtualMouse;
        private static Keyboard _virtualKeyboard;
        private static Touchscreen _virtualTouchscreen;
        private static InputSettings _savedSettings;

        /// <summary>
        /// Full cleanup on domain reload: re-enable hardware, remove virtual devices, restore settings.
        /// Handles the case where a test crashed or play mode exited without TearDown running.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnDomainReload()
        {
#if UNITY_EDITOR
            // Unsubscribe stale play mode hook (survives domain reload as static delegate)
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif

            // Re-enable any hardware devices that may still be disabled
            // (stale _disabledDevices list is lost on reload, so scan all devices)
            foreach (var device in InputSystem.devices)
            {
                if (!device.enabled && !device.name.StartsWith("UIAutomation_"))
                {
                    InputSystem.EnableDevice(device);
                }
            }

            // Clear stale static references (they may point to invalid devices)
            _virtualMouse = null;
            _virtualKeyboard = null;
            _virtualTouchscreen = null;
            _disabledDevices = new List<InputDevice>();
            _savedSettings = null;

            // Remove any orphaned virtual devices from previous runs
            CleanupOrphanedVirtualDevices();
        }

        /// <summary>
        /// Configures input for UI automation testing. Call once before tests begin.
        /// Matches Unity's InputTestFixture.Setup() pattern:
        /// - Configures input settings for background/editor behavior
        /// - Creates virtual devices
        /// - Disables hardware input
        /// </summary>
        public static void Setup()
        {
            // Snapshot settings so TearDown() can restore them exactly
            _savedSettings = ScriptableObject.Instantiate(InputSystem.settings);

            // Ensure InputSystemUIInputModule processes events even when Game View is unfocused
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

            // Route all device input to Game View during play mode — matches InputTestFixture behavior
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            // Clean up any orphaned virtual devices before creating new ones
            CleanupOrphanedVirtualDevices();

            // Create virtual devices so InputAction bindings like <Mouse>/position
            // resolve to our virtual device — no .current race, no Editor windows
            // consuming injected events.
            _virtualMouse = InputSystem.AddDevice<Mouse>("UIAutomation_Mouse");
            _virtualKeyboard = InputSystem.AddDevice<Keyboard>("UIAutomation_Keyboard");
            _virtualTouchscreen = InputSystem.AddDevice<Touchscreen>("UIAutomation_Touchscreen");

            // Disable hardware so bindings can only resolve to our virtual devices
            _disabledDevices.Clear();
            foreach (var device in InputSystem.devices)
            {
                if (device.name.StartsWith("UIAutomation_")) continue;
                if (device.enabled)
                {
                    _disabledDevices.Add(device);
                    InputSystem.DisableDevice(device);
                }
            }

            // Safety: re-enable hardware input when app quits (in case test crashed)
            Application.quitting += OnApplicationQuitting;

#if UNITY_EDITOR
            // Safety: restore input when exiting play mode (in case TearDown didn't run)
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        /// <summary>
        /// Restores input state after testing. Call once after tests complete.
        /// Matches Unity's InputTestFixture.TearDown() pattern.
        /// </summary>
        public static void TearDown()
        {
            Application.quitting -= OnApplicationQuitting;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif

            EnableHardwareInput();
            CleanupVirtualDevices();

            // Restore original InputSystem.settings (backgroundBehavior, editorInputBehaviorInPlayMode, etc.)
            if (_savedSettings != null)
            {
                InputSystem.settings = _savedSettings;
                _savedSettings = null;
            }
        }

        private static void OnApplicationQuitting()
        {
            if (_disabledDevices.Count > 0)
            {
                Debug.Log("[InputInjector] Application quitting - restoring hardware input devices");
                EnableHardwareInput();
            }
        }

#if UNITY_EDITOR
        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state != UnityEditor.PlayModeStateChange.ExitingPlayMode) return;

            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

            // Only restore if we have state to restore (Setup was called in this session)
            if (_disabledDevices.Count == 0 && _virtualMouse == null && _savedSettings == null) return;

            Debug.Log("[InputInjector] Exiting play mode - restoring input state");
            EnableHardwareInput();
            CleanupVirtualDevices();

            if (_savedSettings != null)
            {
                InputSystem.settings = _savedSettings;
                _savedSettings = null;
            }
        }
#endif

        /// <summary>
        /// Gets a working mouse device, creating a virtual one if necessary.
        /// </summary>
        private static Mouse GetMouse()
        {
            // Always use the virtual mouse for injection. The system mouse's events can be
            // consumed by Editor windows (Inspector scroll, Console, etc.) before reaching
            // the Game View, causing injected scroll and other input to silently fail.
            if (_virtualMouse == null || !_virtualMouse.added)
            {
                _virtualMouse = InputSystem.AddDevice<Mouse>("UIAutomation_Mouse");
            }
            return _virtualMouse;
        }

        /// <summary>
        /// Gets a working keyboard device, creating a virtual one if necessary.
        /// </summary>
        private static Keyboard GetKeyboard()
        {
            // Always use the virtual keyboard for injection consistency
            if (_virtualKeyboard == null || !_virtualKeyboard.added)
            {
                _virtualKeyboard = InputSystem.AddDevice<Keyboard>("UIAutomation_Keyboard");
            }
            return _virtualKeyboard;
        }

        /// <summary>
        /// Gets a working touchscreen device, creating a virtual one if necessary.
        /// </summary>
        private static Touchscreen GetTouchscreen()
        {
            // Always use the virtual touchscreen for injection consistency
            if (_virtualTouchscreen == null || !_virtualTouchscreen.added)
            {
                _virtualTouchscreen = InputSystem.AddDevice<Touchscreen>("UIAutomation_Touchscreen");
            }
            return _virtualTouchscreen;
        }

        private static void CleanupVirtualDevices()
        {
            if (_virtualMouse != null && _virtualMouse.added)
            {
                InputSystem.RemoveDevice(_virtualMouse);
                _virtualMouse = null;
            }
            if (_virtualKeyboard != null && _virtualKeyboard.added)
            {
                InputSystem.RemoveDevice(_virtualKeyboard);
                _virtualKeyboard = null;
            }
            if (_virtualTouchscreen != null && _virtualTouchscreen.added)
            {
                InputSystem.RemoveDevice(_virtualTouchscreen);
                _virtualTouchscreen = null;
            }
        }

        /// <summary>
        /// Removes any orphaned virtual devices that may have accumulated from previous test runs.
        /// This handles the case where domain reload loses our static references but devices persist.
        /// </summary>
        private static void CleanupOrphanedVirtualDevices()
        {
            var devicesToRemove = new List<InputDevice>();
            foreach (var device in InputSystem.devices)
            {
                // Find devices with names like UIAutomation_Mouse, UIAutomation_Mouse1, UIAutomation_Keyboard2, etc.
                if (device.name.StartsWith("UIAutomation_Mouse") ||
                    device.name.StartsWith("UIAutomation_Keyboard") ||
                    device.name.StartsWith("UIAutomation_Touchscreen"))
                {
                    // Skip if it's our current tracked device
                    if (device == _virtualMouse || device == _virtualKeyboard || device == _virtualTouchscreen)
                        continue;

                    devicesToRemove.Add(device);
                }
            }

            foreach (var device in devicesToRemove)
            {
                Debug.Log($"[InputInjector] Removing orphaned virtual device: {device.name}");
                InputSystem.RemoveDevice(device);
            }
        }

        private static void EnableHardwareInput()
        {
            foreach (var device in _disabledDevices)
            {
                if (device != null && !device.enabled)
                {
                    InputSystem.EnableDevice(device);
                    Debug.Log($"[InputInjector] Re-enabled hardware device: {device.name}");
                }
            }
            _disabledDevices.Clear();
        }

        /// <summary>
        /// Logs a debug message only when ActionExecutor.DebugMode is enabled.
        /// </summary>
        static void LogDebug(string message)
        {
            if (ActionExecutor.DebugMode)
                Debug.Log($"[InputInjector] {message}");
        }
        /// <summary>
        /// Clamps a screen position to stay at least 1 pixel inside the screen edges.
        /// Prevents injected input from going off-screen during drags, scrolls, and gestures.
        /// </summary>
        static Vector2 ClampToScreen(Vector2 pos)
        {
            pos.x = Mathf.Clamp(pos.x, 1f, Screen.width - 2f);
            pos.y = Mathf.Clamp(pos.y, 1f, Screen.height - 2f);
            return pos;
        }

        /// <summary>
        /// Gets the screen position of a GameObject (works with both UI and world-space objects).
        /// </summary>
        public static Vector2 GetScreenPosition(GameObject go) => UIUtility.GetScreenPosition(go);

        /// <summary>
        /// Gets the screen-space bounding box of a GameObject.
        /// </summary>
        public static Rect GetScreenBounds(GameObject go) => UIUtility.GetScreenBounds(go);

        /// <summary>
        /// Gets a screen position on the GameObject that is not occluded by other UI elements.
        /// Currently returns center position - occlusion detection is planned for future versions.
        /// </summary>
        public static Vector2 GetClearClickPosition(GameObject go, int gridSize = 5) => GetScreenPosition(go);

        /// <summary>
        /// Gets all raycast hits at the given screen position using EventSystem.RaycastAll.
        /// </summary>
        public static List<RaycastResult> GetHitsAtPosition(Vector2 screenPosition) => UIUtility.GetHitsAtPosition(screenPosition);

        /// <summary>
        /// Gets all GameObjects at position that have any of the specified handler interface types.
        /// </summary>
        public static List<GameObject> GetReceiversAtPosition(Vector2 screenPosition, params Type[] handlerTypes) => UIUtility.GetReceiversAtPosition(screenPosition, handlerTypes);

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
        public static async Task SetSlider(Slider slider, float normalizedValue)
        {
            var clickPos = GetSliderClickPosition(slider, normalizedValue);
            await InjectPointerTap(clickPos);
        }

        /// <summary>
        /// Sets a scrollbar to a specific normalized value (0-1) via click.
        /// </summary>
        public static async Task SetScrollbar(Scrollbar scrollbar, float normalizedValue)
        {
            var clickPos = GetScrollbarClickPosition(scrollbar, normalizedValue);
            await InjectPointerTap(clickPos);
        }

        /// <summary>
        /// Clears an input field's text content.
        /// Note: Uses direct text manipulation as a workaround because Unity's IMGUI keyboard
        /// shortcuts (Ctrl+A, Backspace) cannot be injected via the new Input System.
        /// </summary>
        public static async Task ClearInputField(GameObject inputFieldGO)
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
                await Async.DelayFrames(2);

                // Clear the text directly - workaround for IMGUI keyboard shortcut limitation
                // Then re-activate the field since setting text can deactivate it
                tmpInput.text = "";
                tmpInput.ActivateInputField();
                tmpInput.MoveTextEnd(false);
                await Async.DelayFrames(2);
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
                await Async.DelayFrames(2);

                // Clear the text directly - workaround for IMGUI keyboard shortcut limitation
                // Then re-activate the field since setting text can deactivate it
                legacyInput.text = "";
                legacyInput.ActivateInputField();
                legacyInput.MoveTextEnd(false);
                await Async.DelayFrames(2);
            }
        }

        /// <summary>
        /// Presses a key while holding a modifier key (e.g., Ctrl+A, Shift+Tab).
        /// This simulates the real sequence: modifier down, key down, key up, modifier up.
        /// </summary>
        public static async Task PressKeyWithModifier(Key modifier, Key key)
        {


            var keyboard = GetKeyboard();
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] PressKeyWithModifier - No keyboard device found");
                return;
            }

            // Modifier down
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(modifier));
            await Async.DelayFrames(2);

            // Key down (with modifier held)
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(modifier, key));
            await Async.DelayFrames(2);

            // Key up (modifier still held)
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(modifier));
            await Async.DelayFrames(2);

            // Modifier up
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Types text into an input field.
        ///
        /// Note: TMP_InputField uses IMGUI's Event.PopEvent() for keyboard input, not the Input System's
        /// Keyboard.onTextInput. InputSystem.QueueTextEvent() does not work with TMP_InputField.
        /// This method uses direct text manipulation as the only reliable cross-platform approach.
        /// See: https://discussions.unity.com/t/code-to-fix-tmp_inputfield-to-support-new-inputsystem/774250
        /// </summary>
        public static async Task TypeIntoField(GameObject inputFieldGO, string text, bool clearFirst = true, bool pressEnter = false)
        {
            var screenPos = GetScreenPosition(inputFieldGO);
            LogDebug($"TypeIntoField '{inputFieldGO.name}' at ({screenPos.x:F0},{screenPos.y:F0}) text='{text}'");
            InputVisualizer.RecordText(text ?? "", screenPos);

            // Click to focus the field
            await InjectPointerTap(screenPos);
            await Async.DelayFrames(2);

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
                await Async.DelayFrames(2);
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
                    await Async.DelayFrames(2);
                }
                else
                {
                    Debug.LogWarning($"[InputInjector] TypeIntoField - No TMP_InputField or InputField found on '{inputFieldGO.name}'");
                }
            }

            // Press Enter if requested
            if (pressEnter)
            {
                await Async.DelayFrames(2);
                await PressKey(Key.Enter);
            }
        }

        /// <summary>
        /// Performs a swipe gesture on an element.
        /// </summary>
        public static async Task Swipe(GameObject element, string direction, float normalizedDistance = 0.2f, float duration = 0.3f)
        {

            var startPos = GetScreenPosition(element);
            var offset = GetDirectionOffset(direction, normalizedDistance);
            var endPos = startPos + offset;

            await InjectMouseDrag(startPos, endPos, duration);
        }

        /// <summary>
        /// Performs a scroll action on a scrollable element.
        /// </summary>
        public static async Task ScrollElement(GameObject scrollableElement, string direction, float amount = 0.3f)
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
        public static async Task Pinch(GameObject element, float scale, float duration = 0.5f)
        {
            Vector2 center = element != null
                ? GetScreenPosition(element)
                : new Vector2(Screen.width / 2f, Screen.height / 2f);

            await InjectPinch(center, scale, duration);
        }

        /// <summary>
        /// Performs a two-finger swipe gesture on an element or screen center.
        /// </summary>
        public static async Task TwoFingerSwipe(GameObject element, string direction, float normalizedDistance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {
            Vector2 centerPos = element != null
                ? GetScreenPosition(element)
                : new Vector2(Screen.width / 2f, Screen.height / 2f);

            var offset = GetDirectionOffset(direction, normalizedDistance);

            // Calculate finger positions (clamped by InjectTwoFingerDrag)
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
        public static async Task InjectTwoFingerSwipe(Vector2 centerPosition, string direction, float normalizedDistance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {


            var offset = GetDirectionOffset(direction, normalizedDistance);

            // Calculate finger positions (clamped by InjectTwoFingerDrag)
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
        public static async Task Rotate(GameObject element, float degrees, float duration = 0.5f, float fingerDistance = 0.05f)
        {
            Vector2 centerPos = element != null
                ? GetScreenPosition(element)
                : new Vector2(Screen.width / 2f, Screen.height / 2f);

            await InjectRotate(centerPos, degrees, duration, fingerDistance);
        }

        #endregion


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
        /// Events are queued and processed by Unity's natural update cycle to ensure
        /// all scripts (including those checking wasPressedThisFrame) see each state change.
        /// </summary>
        public static async Task InjectPointerTap(Vector2 screenPosition)
        {
            LogDebug($"InjectPointerTap at ({screenPosition.x:F0},{screenPosition.y:F0})");
            InputVisualizer.RecordClick(screenPosition, 1);
            InputVisualizer.RecordCursorPosition(screenPosition, !ShouldUseTouchInput());

            if (ShouldUseTouchInput())
            {
                LogDebug("Using touch input");
                await InjectTouchTap(screenPosition);
                return;
            }

            var mouse = GetMouse();
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] Click - No mouse device found, cannot inject click");
                return;
            }

            LogDebug($"Using mouse input, device={mouse.deviceId}");

            // Move mouse to position with clean state (no buttons pressed, zero delta)
            // Using full StateEvent ensures no stale button state causes camera jumps
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, posPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, posPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            await Async.DelayFrames(2);

            // Press — processed by Unity's automatic update, visible to all scripts
            using (StateEvent.From(mouse, out var downPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, downPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, downPtr);
                mouse.leftButton.WriteValueIntoEvent(1f, downPtr);
                InputSystem.QueueEvent(downPtr);
            }
            await Async.DelayFrames(4);

            // Release — realistic gap between press and release (~67ms at 60fps)
            using (StateEvent.From(mouse, out var upPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, upPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, upPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, upPtr);
                InputSystem.QueueEvent(upPtr);
            }
            LogDebug("InjectPointerTap complete");
            InputVisualizer.RecordCursorEnd();
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Injects a double click/tap at the specified screen position.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// </summary>
        public static async Task InjectPointerDoubleTap(Vector2 screenPosition)
        {
            LogDebug($"InjectPointerDoubleTap at ({screenPosition.x:F0},{screenPosition.y:F0})");
            InputVisualizer.RecordClick(screenPosition, 2);
            InputVisualizer.RecordCursorPosition(screenPosition, !ShouldUseTouchInput());


            if (ShouldUseTouchInput())
            {
                LogDebug("Using touch input");
                await InjectTouchDoubleTap(screenPosition);
                return;
            }

            var mouse = GetMouse();
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] DoubleClick - No mouse device found");
                return;
            }

            LogDebug($"Using mouse input, device={mouse.deviceId}");

            // Move mouse to position with clean state (no buttons, zero delta)
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, posPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, posPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            await Async.DelayFrames(2);

            // First click — press
            using (StateEvent.From(mouse, out var d1))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, d1);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, d1);
                mouse.leftButton.WriteValueIntoEvent(1f, d1);
                InputSystem.QueueEvent(d1);
            }
            await Async.DelayFrames(4);

            // First click — release
            using (StateEvent.From(mouse, out var u1))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, u1);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, u1);
                mouse.leftButton.WriteValueIntoEvent(0f, u1);
                InputSystem.QueueEvent(u1);
            }
            await Async.DelayFrames(2);

            // Inter-click gap — must be within double-click speed threshold (~300ms)
            await Async.Delay(2, 0.05f);

            // Second click — press
            using (StateEvent.From(mouse, out var d2))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, d2);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, d2);
                mouse.leftButton.WriteValueIntoEvent(1f, d2);
                InputSystem.QueueEvent(d2);
            }
            await Async.DelayFrames(4);

            // Second click — release
            using (StateEvent.From(mouse, out var u2))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, u2);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, u2);
                mouse.leftButton.WriteValueIntoEvent(0f, u2);
                InputSystem.QueueEvent(u2);
            }
            LogDebug("InjectPointerDoubleTap complete");
            InputVisualizer.RecordCursorEnd();
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Injects a double-tap touch gesture.
        /// </summary>
        public static async Task InjectTouchDoubleTap(Vector2 screenPosition)
        {
            await InjectTouchTap(screenPosition);
            await Async.Delay(2, 0.05f);
            await InjectTouchTap(screenPosition);
        }

        /// <summary>
        /// Injects a triple click/tap at the specified screen position.
        /// Triple-click is commonly used to select all text in input fields.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// </summary>
        public static async Task InjectPointerTripleTap(Vector2 screenPosition)
        {
            LogDebug($"InjectPointerTripleTap at ({screenPosition.x:F0},{screenPosition.y:F0})");
            InputVisualizer.RecordClick(screenPosition, 3);
            InputVisualizer.RecordCursorPosition(screenPosition, !ShouldUseTouchInput());


            if (ShouldUseTouchInput())
            {
                LogDebug("Using touch input");
                await InjectTouchTripleTap(screenPosition);
                return;
            }

            var mouse = GetMouse();
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] TripleClick - No mouse device found");
                return;
            }

            LogDebug($"Using mouse input, device={mouse.deviceId}");

            // Move mouse to position with clean state (no buttons, zero delta)
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, posPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, posPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            await Async.DelayFrames(2);

            // Three clicks with inter-click gaps within multi-click speed threshold
            for (int click = 0; click < 3; click++)
            {
                if (click > 0)
                    await Async.Delay(2, 0.05f);

                using (StateEvent.From(mouse, out var down))
                {
                    mouse.position.WriteValueIntoEvent(screenPosition, down);
                    mouse.delta.WriteValueIntoEvent(Vector2.zero, down);
                    mouse.leftButton.WriteValueIntoEvent(1f, down);
                    InputSystem.QueueEvent(down);
                }
                await Async.DelayFrames(4);

                using (StateEvent.From(mouse, out var up))
                {
                    mouse.position.WriteValueIntoEvent(screenPosition, up);
                    mouse.delta.WriteValueIntoEvent(Vector2.zero, up);
                    mouse.leftButton.WriteValueIntoEvent(0f, up);
                    InputSystem.QueueEvent(up);
                }
                await Async.DelayFrames(2);
            }

            LogDebug("InjectPointerTripleTap complete");
            InputVisualizer.RecordCursorEnd();
        }

        /// <summary>
        /// Injects a triple-tap touch gesture.
        /// </summary>
        public static async Task InjectTouchTripleTap(Vector2 screenPosition)
        {
            for (int i = 0; i < 3; i++)
            {
                if (i > 0) await Async.Delay(2, 0.05f);
                await InjectTouchTap(screenPosition);
            }
        }

        /// <summary>
        /// Injects a single-finger tap gesture using the Input System.
        /// Used on mobile platforms (iOS/Android).
        /// </summary>
        public static async Task InjectTouchTap(Vector2 screenPosition)
        {
            var touchscreen = GetTouchscreen();
            if (touchscreen == null)
            {
                Debug.LogWarning("[InputInjector] TouchTap - Could not create touchscreen device");
                return;
            }

            const int touchId = 1;
            InputVisualizer.RecordClick(screenPosition, 1, 0);
            InputVisualizer.RecordCursorPosition(screenPosition, false, 0, true);

            // Touch began — using TouchState struct (matches Unity's InputTestFixture.SetTouch pattern)
            InputSystem.QueueStateEvent(touchscreen, new TouchState
            {
                touchId = touchId,
                position = screenPosition,
                delta = Vector2.zero,
                phase = UnityEngine.InputSystem.TouchPhase.Began,
                pressure = 1f,
            });
            await Async.DelayFrames(4);

            // Touch ended
            InputSystem.QueueStateEvent(touchscreen, new TouchState
            {
                touchId = touchId,
                position = screenPosition,
                delta = Vector2.zero,
                phase = UnityEngine.InputSystem.TouchPhase.Ended,
                pressure = 0f,
            });
            InputVisualizer.RecordCursorEnd();
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Injects a drag gesture using the appropriate input method for the platform.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// </summary>
        /// <param name="startPos">Start position in screen coordinates</param>
        /// <param name="endPos">End position in screen coordinates</param>
        /// <param name="duration">Duration of the drag movement (minimum 1 second)</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        /// <param name="button">Which mouse button to use for dragging (ignored on touch devices)</param>
        public static async Task InjectPointerDrag(Vector2 startPos, Vector2 endPos, float duration, float holdTime = 0.05f, PointerButton button = PointerButton.Left)
        {
            InputVisualizer.RecordDragStart(startPos);
            InputVisualizer.RecordCursorPosition(startPos, !ShouldUseTouchInput());


            if (ShouldUseTouchInput())
            {
                await InjectTouchDrag(startPos, endPos, duration, holdTime);
                InputVisualizer.RecordDragEnd(endPos);
                InputVisualizer.RecordCursorEnd();
                return;
            }

            await InjectMouseDrag(startPos, endPos, duration, holdTime, button);
            InputVisualizer.RecordDragEnd(endPos);
            InputVisualizer.RecordCursorEnd();
        }

        /// <summary>
        /// Injects a mouse drag from start to end position using the Input System.
        /// Uses frame-based yields to ensure Unity processes events each frame (matching touch behavior).
        /// </summary>
        /// <param name="startPos">Start position in screen coordinates</param>
        /// <param name="endPos">End position in screen coordinates</param>
        /// <param name="duration">Duration of the drag movement</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        /// <param name="button">Which mouse button to use for dragging</param>
        internal static async Task InjectMouseDrag(Vector2 startPos, Vector2 endPos, float duration, float holdTime = 0.05f, PointerButton button = PointerButton.Left)
        {

            var mouse = GetMouse();
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] MouseDrag - No mouse device found, cannot inject drag");
                return;
            }

            // Get the appropriate button control based on the enum
            var buttonControl = button switch
            {
                PointerButton.Right => mouse.rightButton,
                PointerButton.Middle => mouse.middleButton,
                _ => mouse.leftButton
            };

            LogDebug($"MouseDrag start=({startPos.x:F0},{startPos.y:F0}) end=({endPos.x:F0},{endPos.y:F0}) duration={duration}s hold={holdTime}s button={button}");

            // Wait for any previous input state to settle before starting new drag
            await Async.DelayFrames(2);

            startPos = ClampToScreen(startPos);
            endPos = ClampToScreen(endPos);
            Vector2 previousPos = startPos;

            // Move mouse to start position — processed next frame
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(startPos, posPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            await Async.DelayFrames(2);

            // Mouse button down at start
            using (StateEvent.From(mouse, out var downPtr))
            {
                mouse.position.WriteValueIntoEvent(startPos, downPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, downPtr);
                buttonControl.WriteValueIntoEvent(1f, downPtr);
                InputSystem.QueueEvent(downPtr);
            }
            await Async.DelayFrames(2); // Allow PointerDown to register

            LogDebug($"MouseDrag mouse down at ({startPos.x:F0},{startPos.y:F0})");

            // Hold at start position before dragging (wall-clock based using Stopwatch)
            if (holdTime > 0)
            {
                long holdEndTicks = Stopwatch.GetTimestamp() + (long)(holdTime * Stopwatch.Frequency);
                while (Stopwatch.GetTimestamp() < holdEndTicks)
                {
                    using (StateEvent.From(mouse, out var holdPtr))
                    {
                        mouse.position.WriteValueIntoEvent(startPos, holdPtr);
                        mouse.delta.WriteValueIntoEvent(Vector2.zero, holdPtr);
                        buttonControl.WriteValueIntoEvent(1f, holdPtr);
                        InputSystem.QueueEvent(holdPtr);
                    }
                    await Async.DelayFrames(1);
                }
                LogDebug($"MouseDrag held for {holdTime}s");
            }
            else
            {
                // Even with no hold, send one event at start position to register the initial press
                // This ensures listeners can capture lastMousePosition before movement begins
                using (StateEvent.From(mouse, out var initPtr))
                {
                    mouse.position.WriteValueIntoEvent(startPos, initPtr);
                    mouse.delta.WriteValueIntoEvent(Vector2.zero, initPtr);
                    buttonControl.WriteValueIntoEvent(1f, initPtr);
                    InputSystem.QueueEvent(initPtr);
                }
                await Async.DelayFrames(1);
            }

            // Interpolate mouse position over duration
            // Use frame-based interpolation with minimum frames for realistic drag speed
            const int minFrames = 10; // ~167ms at 60fps — realistic minimum drag duration
            int frameCount = 0;
            long dragStartTicks = Stopwatch.GetTimestamp();
            long durationTicks = (long)(duration * Stopwatch.Frequency);

            while (frameCount < minFrames || Stopwatch.GetTimestamp() < dragStartTicks + durationTicks)
            {
                frameCount++;
                // Use frame count for interpolation to ensure smooth progression regardless of timing
                float t = Mathf.Clamp01((float)frameCount / minFrames);
                long elapsed = Stopwatch.GetTimestamp() - dragStartTicks;
                if (duration > 0 && elapsed < durationTicks)
                {
                    // If still within duration, use time-based interpolation for smoother motion
                    t = Mathf.Max(t, (float)elapsed / durationTicks);
                }
                t = Mathf.Clamp01(t);

                Vector2 currentPos = ClampToScreen(Vector2.Lerp(startPos, endPos, t));
                Vector2 delta = currentPos - previousPos;

                using (StateEvent.From(mouse, out var movePtr))
                {
                    mouse.position.WriteValueIntoEvent(currentPos, movePtr);
                    mouse.delta.WriteValueIntoEvent(delta, movePtr);
                    buttonControl.WriteValueIntoEvent(1f, movePtr);
                    InputSystem.QueueEvent(movePtr);
                }

                // Update cursor visualization with trail
                InputVisualizer.RecordCursorPosition(currentPos, isMouse: true, fingerIndex: 0, isPressed: true);

                previousPos = currentPos;
                await Async.DelayFrames(1);

                // Exit early if we've reached the end position and minimum frames
                if (frameCount >= minFrames && t >= 1f) break;
            }

            // Ensure we reach the final position
            using (StateEvent.From(mouse, out var finalPtr))
            {
                mouse.position.WriteValueIntoEvent(endPos, finalPtr);
                mouse.delta.WriteValueIntoEvent(endPos - previousPos, finalPtr);
                buttonControl.WriteValueIntoEvent(1f, finalPtr);
                InputSystem.QueueEvent(finalPtr);
            }
            await Async.DelayFrames(1);

            // Mouse button up at end
            using (StateEvent.From(mouse, out var upPtr))
            {
                mouse.position.WriteValueIntoEvent(endPos, upPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, upPtr);
                buttonControl.WriteValueIntoEvent(0f, upPtr);
                InputSystem.QueueEvent(upPtr);
            }

            LogDebug($"MouseDrag mouse up at ({endPos.x:F0},{endPos.y:F0})");

            // Allow UI to fully process drag end and pointer up before next action
            await Async.DelayFrames(4);
        }

        /// <summary>
        /// Injects a single-finger touch drag gesture using the Input System.
        /// Used on mobile platforms (iOS/Android).
        /// </summary>
        /// <param name="startPos">Start position in screen coordinates</param>
        /// <param name="endPos">End position in screen coordinates</param>
        /// <param name="duration">Duration of the drag movement</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        internal static async Task InjectTouchDrag(Vector2 startPos, Vector2 endPos, float duration, float holdTime = 0.05f)
        {

            var touchscreen = GetTouchscreen();
            if (touchscreen == null)
            {
                Debug.LogWarning("[InputInjector] TouchDrag - Could not create touchscreen device");
                return;
            }

            // Wait for any previous input state to settle before starting new drag
            await Async.DelayFrames(2);

            const int touchId = 1;
            startPos = ClampToScreen(startPos);
            endPos = ClampToScreen(endPos);
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
            await Async.DelayFrames(1);

            // Hold at start position before dragging (wall-clock based using Stopwatch)
            if (holdTime > 0)
            {
                long holdEndTicks = Stopwatch.GetTimestamp() + (long)(holdTime * Stopwatch.Frequency);
                while (Stopwatch.GetTimestamp() < holdEndTicks)
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
                    await Async.DelayFrames(1);
                }
            }

            // Touch moved - frame-based with minimum frames for realistic drag speed
            const int minFrames = 10; // ~167ms at 60fps — realistic minimum drag duration
            int frameCount = 0;
            long dragStartTicks = Stopwatch.GetTimestamp();
            long durationTicks = (long)(duration * Stopwatch.Frequency);

            while (frameCount < minFrames || Stopwatch.GetTimestamp() < dragStartTicks + durationTicks)
            {
                frameCount++;
                float t = Mathf.Clamp01((float)frameCount / minFrames);
                long elapsed = Stopwatch.GetTimestamp() - dragStartTicks;
                if (duration > 0 && elapsed < durationTicks)
                {
                    t = Mathf.Max(t, (float)elapsed / durationTicks);
                }
                t = Mathf.Clamp01(t);

                Vector2 currentPos = ClampToScreen(Vector2.Lerp(startPos, endPos, t));
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

                // Update cursor visualization with trail
                InputVisualizer.RecordCursorPosition(currentPos, isMouse: false, fingerIndex: 0, isPressed: true);

                previousPos = currentPos;
                await Async.DelayFrames(1);

                if (frameCount >= minFrames && t >= 1f) break;
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
            await Async.DelayFrames(1);

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
            // Allow UI to fully process touch end before next action
            await Async.DelayFrames(4);
        }

        /// <summary>
        /// Injects a scroll event at the specified position.
        /// </summary>
        /// <param name="position">Screen position to scroll at</param>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        public static async Task InjectScroll(Vector2 position, float delta)
        {
            await InjectScroll(position, new Vector2(0, delta));
        }

        /// <summary>
        /// Injects a scroll event at the specified position with a Vector2 delta.
        /// </summary>
        /// <param name="position">Screen position to scroll at</param>
        /// <param name="scrollDelta">Scroll delta vector (for horizontal and vertical scrolling)</param>
        public static async Task InjectScroll(Vector2 position, Vector2 scrollDelta)
        {
            InputVisualizer.RecordScroll(position, scrollDelta);


            var mouse = GetMouse();
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] Scroll - No mouse device found");
                return;
            }

            // Move mouse to position first so EventSystem establishes pointerEnter
            // on the element under the cursor. Without this, scroll events are dropped
            // because InputSystemUIInputModule requires a valid pointerEnter target.
            // Use full StateEvent to ensure clean state (zero delta, no buttons)
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(position, posPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, posPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            // Wait for Unity's automatic update to process position, then
            // InputSystemUIInputModule.Process() establishes pointerEnter
            await Async.DelayFrames(2);

            // Inject scroll using DeltaStateEvent on the scroll control.
            // CRITICAL: Do NOT call InputSystem.Update() after queuing — let the player
            // loop process it. Manual Update() would apply the scroll state, but then
            // Mouse.OnNextUpdate() resets scroll to zero at the start of the NEXT frame's
            // InputSystem.Update() — BEFORE InputSystemUIInputModule.Process() reads it.
            // By only queuing, the event is processed in the next frame's natural
            // InputSystem.Update() → Process() sequence, where scroll is still non-zero.
            using (DeltaStateEvent.From(mouse.scroll, out var scrollPtr))
            {
                mouse.scroll.WriteValueIntoEvent(scrollDelta, scrollPtr);
                InputSystem.QueueEvent(scrollPtr);
            }
            // Let the player loop process: InputSystem.Update() applies scroll,
            // then EventSystem.Update() → Process() reads it before next reset.
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Types text character by character using InputSystem.QueueTextEvent.
        /// This triggers Keyboard.onTextInput callback for any subscribers.
        ///
        /// Note: TMP_InputField does NOT use Keyboard.onTextInput - it uses IMGUI Event.PopEvent().
        /// For TMP_InputField, use TypeIntoField() instead which handles this limitation.
        /// </summary>
        public static async Task TypeText(string text)
        {

            LogDebug($"TypeText '{text}' ({text?.Length ?? 0} chars)");

            var keyboard = GetKeyboard();
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
                await Async.DelayFrames(1);
            }

            LogDebug("TypeText complete");
        }

        /// <summary>
        /// Selects all text in the currently focused input field using Ctrl+A.
        /// </summary>
        public static async Task SelectAllText()
        {

            var keyboard = GetKeyboard();
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] SelectAllText - No keyboard device found");
                return;
            }

            // Press Ctrl+A
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl, Key.A));
            await Async.DelayFrames(3);

            // Release keys
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Deletes all text in the currently focused input field using Ctrl+A followed by Backspace.
        /// </summary>
        public static async Task DeleteAllText()
        {
            await SelectAllText();
            await PressKey(Key.Backspace);
        }

        /// <summary>
        /// Presses and releases a keyboard key.
        /// </summary>
        public static async Task PressKey(Key key)
        {
            InputVisualizer.RecordKeyPress(key.ToString());


            var keyboard = GetKeyboard();
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] PressKey - No keyboard device found");
                return;
            }

            // Key down
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            await Async.DelayFrames(3);

            // Key up
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Holds a keyboard key for the specified duration.
        /// </summary>
        /// <param name="key">The key to hold</param>
        /// <param name="duration">How long to hold the key in seconds</param>
        public static async Task HoldKey(Key key, float duration)
        {
            InputVisualizer.RecordKeyHoldStart(key.ToString());


            var keyboard = GetKeyboard();
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] HoldKey - No keyboard device found");
                InputVisualizer.RecordKeyHoldEnd();
                return;
            }

            var keyState = new KeyboardState(key);

            // Key down
            InputSystem.QueueStateEvent(keyboard, keyState);
            await Async.DelayFrames(1);

            // Hold for duration - re-queue key state each frame so input system registers continuous hold
            long holdEndTicks = Stopwatch.GetTimestamp() + (long)(duration * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() < holdEndTicks)
            {
                InputSystem.QueueStateEvent(keyboard, keyState);
                await Async.DelayFrames(1);
            }

            // Key up
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputVisualizer.RecordKeyHoldEnd();
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Holds multiple keyboard keys simultaneously for the specified duration.
        /// </summary>
        /// <param name="keys">The keys to hold together</param>
        /// <param name="duration">How long to hold the keys in seconds</param>
        public static async Task HoldKeys(Key[] keys, float duration)
        {
            if (keys == null || keys.Length == 0)
                return;

            string keyNames = string.Join("+", System.Array.ConvertAll(keys, k => k.ToString()));
            InputVisualizer.RecordKeyHoldStart(keyNames);


            var keyboard = GetKeyboard();
            if (keyboard == null)
            {
                Debug.LogWarning("[InputInjector] HoldKeys - No keyboard device found");
                InputVisualizer.RecordKeyHoldEnd();
                return;
            }

            var keyState = new KeyboardState(keys);

            // Keys down (all at once)
            InputSystem.QueueStateEvent(keyboard, keyState);
            await Async.DelayFrames(1);

            // Hold for duration - re-queue key state each frame so input system registers continuous hold
            long holdEndTicks = Stopwatch.GetTimestamp() + (long)(duration * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() < holdEndTicks)
            {
                InputSystem.QueueStateEvent(keyboard, keyState);
                await Async.DelayFrames(1);
            }

            // Keys up
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputVisualizer.RecordKeyHoldEnd();
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Injects a hold/long-press at the specified screen position.
        /// Uses touch on mobile (iOS/Android), mouse on desktop.
        /// </summary>
        public static async Task InjectPointerHold(Vector2 screenPosition, float holdSeconds)
        {
            InputVisualizer.RecordHoldStart(screenPosition);


            if (ShouldUseTouchInput())
            {
                await InjectTouchHold(screenPosition, holdSeconds);
                return;
            }

            var mouse = GetMouse();
            if (mouse == null)
            {
                Debug.LogWarning("[InputInjector] Hold - No mouse device found, cannot inject hold");
                return;
            }

            // Move mouse to position with clean state (no buttons, zero delta)
            using (StateEvent.From(mouse, out var posPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, posPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, posPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, posPtr);
                InputSystem.QueueEvent(posPtr);
            }
            await Async.DelayFrames(2);

            // Mouse button down
            using (StateEvent.From(mouse, out var downPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, downPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, downPtr);
                mouse.leftButton.WriteValueIntoEvent(1f, downPtr);
                InputSystem.QueueEvent(downPtr);
            }
            await Async.DelayFrames(1);

            // Hold for specified duration (wall-clock based)
            long holdEndTicks = Stopwatch.GetTimestamp() + (long)(holdSeconds * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() < holdEndTicks)
            {
                await Async.DelayFrames(1);
            }

            // Mouse button up
            using (StateEvent.From(mouse, out var upPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, upPtr);
                mouse.delta.WriteValueIntoEvent(Vector2.zero, upPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, upPtr);
                InputSystem.QueueEvent(upPtr);
            }
            InputVisualizer.RecordHoldEnd();
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Injects a touch hold/long-press gesture using the Input System.
        /// Used on mobile platforms (iOS/Android).
        /// </summary>
        public static async Task InjectTouchHold(Vector2 screenPosition, float holdSeconds)
        {

            var touchscreen = GetTouchscreen();
            if (touchscreen == null)
            {
                Debug.LogWarning("[InputInjector] TouchHold - Could not create touchscreen device");
                return;
            }

            const int touchId = 1; // Touch IDs must be non-zero
            InputVisualizer.RecordHoldStart(screenPosition);

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
            await Async.DelayFrames(1);

            // Hold for specified duration (touch stays in Stationary phase, wall-clock based)
            long holdEndTicks = Stopwatch.GetTimestamp() + (long)(holdSeconds * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() < holdEndTicks)
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
                await Async.DelayFrames(1);
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
            InputVisualizer.RecordHoldEnd();
            await Async.DelayFrames(2);
        }

        /// <summary>
        /// Injects a pinch gesture for zooming.
        /// </summary>
        /// <param name="centerPosition">Center point of the pinch</param>
        /// <param name="scale">Scale factor: less than 1 = pinch in (zoom out), greater than 1 = pinch out (zoom in)</param>
        /// <param name="duration">Duration of the pinch gesture</param>
        public static async Task InjectPinch(Vector2 centerPosition, float scale, float duration)
        {
            const float startOffset = 100f; // Initial distance from center
            InputVisualizer.RecordPinchStart(centerPosition, startOffset * 2f);


            var touchscreen = GetTouchscreen();
            if (touchscreen == null)
            {
                Debug.LogWarning("[InputInjector] Pinch - Could not create touchscreen device");
                return;
            }

            // Calculate start and end offsets based on scale
            float endOffset = startOffset * scale;

            // Two touch points that move symmetrically
            Vector2 startTouch1 = ClampToScreen(centerPosition + new Vector2(-startOffset, 0));
            Vector2 startTouch2 = ClampToScreen(centerPosition + new Vector2(startOffset, 0));
            Vector2 endTouch1 = ClampToScreen(centerPosition + new Vector2(-endOffset, 0));
            Vector2 endTouch2 = ClampToScreen(centerPosition + new Vector2(endOffset, 0));

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
            await Async.DelayFrames(1);

            // Interpolate pinch movement
            for (int i = 1; i < totalFrames; i++)
            {
                float t = (float)i / totalFrames;
                Vector2 currentTouch1 = ClampToScreen(Vector2.Lerp(startTouch1, endTouch1, t));
                Vector2 currentTouch2 = ClampToScreen(Vector2.Lerp(startTouch2, endTouch2, t));

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
                InputVisualizer.RecordPinchUpdate(currentTouch1, currentTouch2);
                await Async.DelayFrames(1);
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
            InputVisualizer.RecordPinchEnd(endOffset * 2f);
            await Async.DelayFrames(1);
        }

        /// <summary>
        /// Injects a pinch gesture for zooming with custom finger distance.
        /// </summary>
        /// <param name="centerPosition">Center point of the pinch</param>
        /// <param name="scale">Scale factor: less than 1 = pinch in (zoom out), greater than 1 = pinch out (zoom in)</param>
        /// <param name="duration">Duration of the pinch gesture</param>
        /// <param name="fingerDistancePixels">Initial distance of each finger from center in pixels</param>
        public static async Task InjectPinch(Vector2 centerPosition, float scale, float duration, float fingerDistancePixels)
        {
            InputVisualizer.RecordPinchStart(centerPosition, fingerDistancePixels * 2f);

            var touchscreen = GetTouchscreen();
            if (touchscreen == null)
            {
                Debug.LogWarning("[InputInjector] Pinch - Could not create touchscreen device");
                return;
            }

            // Calculate start and end offsets based on scale and finger distance
            float startOffset = fingerDistancePixels;
            float endOffset = startOffset * scale;

            // Two touch points that move symmetrically
            Vector2 startTouch1 = ClampToScreen(centerPosition + new Vector2(-startOffset, 0));
            Vector2 startTouch2 = ClampToScreen(centerPosition + new Vector2(startOffset, 0));
            Vector2 endTouch1 = ClampToScreen(centerPosition + new Vector2(-endOffset, 0));
            Vector2 endTouch2 = ClampToScreen(centerPosition + new Vector2(endOffset, 0));

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
            await Async.DelayFrames(1);

            // Interpolate pinch movement
            for (int i = 1; i < totalFrames; i++)
            {
                float t = (float)i / totalFrames;
                Vector2 currentTouch1 = ClampToScreen(Vector2.Lerp(startTouch1, endTouch1, t));
                Vector2 currentTouch2 = ClampToScreen(Vector2.Lerp(startTouch2, endTouch2, t));

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
                InputVisualizer.RecordPinchUpdate(currentTouch1, currentTouch2);
                await Async.DelayFrames(1);
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
            InputVisualizer.RecordPinchEnd(endOffset * 2f);
            await Async.DelayFrames(1);
        }

        /// <summary>
        /// Simulates a two-finger drag gesture (both fingers moving in parallel).
        /// </summary>
        public static async Task InjectTwoFingerDrag(Vector2 start1, Vector2 end1, Vector2 start2, Vector2 end2, float duration)
        {
            InputVisualizer.RecordTwoFingerStart(start1, start2);

            var touchscreen = GetTouchscreen();
            if (touchscreen == null)
            {
                Debug.LogWarning("[InputInjector] TwoFingerDrag - Could not create touchscreen device");
                return;
            }

            start1 = ClampToScreen(start1);
            start2 = ClampToScreen(start2);
            end1 = ClampToScreen(end1);
            end2 = ClampToScreen(end2);

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
            await Async.DelayFrames(1);

            // Interpolate movement
            for (int i = 1; i < totalFrames; i++)
            {
                float t = (float)i / totalFrames;
                Vector2 current1 = ClampToScreen(Vector2.Lerp(start1, end1, t));
                Vector2 current2 = ClampToScreen(Vector2.Lerp(start2, end2, t));

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
                InputVisualizer.RecordTwoFingerUpdate(current1, current2);
                await Async.DelayFrames(1);
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
            InputVisualizer.RecordTwoFingerEnd(end1, end2);
            await Async.DelayFrames(1);
        }

        /// <summary>
        /// Simulates a two-finger rotation gesture.
        /// </summary>
        /// <param name="centerPosition">Center point of the rotation.</param>
        /// <param name="degrees">Rotation angle (positive = clockwise, negative = counter-clockwise).</param>
        /// <param name="duration">Duration of the gesture in seconds.</param>
        /// <param name="fingerDistance">Normalized distance from center (0-1) for finger positions.</param>
        public static async Task InjectRotate(Vector2 centerPosition, float degrees, float duration, float fingerDistance = 0.05f)
        {
            // Calculate the radius based on screen size and finger distance
            float radiusPixels = fingerDistance * Mathf.Min(Screen.width, Screen.height);
            InputVisualizer.RecordRotateStart(centerPosition, radiusPixels);


            await InjectRotatePixels(centerPosition, degrees, duration, radiusPixels);
            InputVisualizer.RecordRotateEnd(degrees);
        }

        /// <summary>
        /// Simulates a two-finger rotation gesture with pixel-based radius.
        /// </summary>
        /// <param name="centerPosition">Center point of the rotation.</param>
        /// <param name="degrees">Rotation angle (positive = clockwise, negative = counter-clockwise).</param>
        /// <param name="duration">Duration of the gesture in seconds.</param>
        /// <param name="radiusPixels">Distance from center in pixels for finger positions.</param>
        public static async Task InjectRotatePixels(Vector2 centerPosition, float degrees, float duration, float radiusPixels)
        {

            var touchscreen = GetTouchscreen();
            if (touchscreen == null)
            {
                Debug.LogWarning("[InputInjector] Rotate - Could not create touchscreen device");
                return;
            }

            float radius = radiusPixels;
            float radians = degrees * Mathf.Deg2Rad;

            // Start positions (fingers on opposite sides horizontally)
            Vector2 startTouch1 = ClampToScreen(centerPosition + new Vector2(-radius, 0));
            Vector2 startTouch2 = ClampToScreen(centerPosition + new Vector2(radius, 0));

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
            await Async.DelayFrames(1);

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

                Vector2 currentTouch1 = ClampToScreen(centerPosition + offset1);
                Vector2 currentTouch2 = ClampToScreen(centerPosition + offset2);

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
                InputVisualizer.RecordRotateUpdate(currentTouch1, currentTouch2);
                await Async.DelayFrames(1);
            }

            // Calculate final positions
            Vector2 endOffset1 = new Vector2(-radius * Mathf.Cos(radians), -radius * Mathf.Sin(radians));
            Vector2 endOffset2 = new Vector2(radius * Mathf.Cos(radians), radius * Mathf.Sin(radians));
            Vector2 endTouch1 = ClampToScreen(centerPosition + endOffset1);
            Vector2 endTouch2 = ClampToScreen(centerPosition + endOffset2);

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
            await Async.DelayFrames(1);
        }
    }
}
