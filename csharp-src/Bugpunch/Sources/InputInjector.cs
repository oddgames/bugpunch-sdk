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

namespace ODDGames.Bugpunch
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
    internal static partial class InputInjector
    {
        private static List<InputDevice> _disabledDevices = new List<InputDevice>();

        private static Mouse _virtualMouse;
        private static Keyboard _virtualKeyboard;
        private static Touchscreen _virtualTouchscreen;
        // Store only the individual values we change (not the entire InputSettings SO)
        // to avoid ScriptableObject lifecycle issues during play mode exit.
        private static InputSettings.BackgroundBehavior _savedBackgroundBehavior;
        private static InputSettings.EditorInputBehaviorInPlayMode _savedEditorInputBehavior;
        private static bool _settingsSaved;

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
            _settingsSaved = false;

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

            // Snapshot only the values we're about to change (not the full InputSettings SO,
            // which causes ScriptableObject lifecycle issues during play mode exit).
            _savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            _savedEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            _settingsSaved = true;

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

            // Restore only the values we changed — avoids replacing the entire InputSettings
            // ScriptableObject, which can trigger native assertion failures from update ticks.
            if (_settingsSaved)
            {
                try
                {
                    InputSystem.settings.backgroundBehavior = _savedBackgroundBehavior;
                    InputSystem.settings.editorInputBehaviorInPlayMode = _savedEditorInputBehavior;
                }
                catch { }
                _settingsSaved = false;
            }

            try { EnableHardwareInput(); } catch { _disabledDevices.Clear(); }
            try { CleanupVirtualDevices(); }
            catch
            {
                _virtualMouse = null;
                _virtualKeyboard = null;
                _virtualTouchscreen = null;
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

            // During ExitingPlayMode the InputSystem is being torn down — ANY InputSystem
            // API call (settings, devices, etc.) can trigger native assertion failures from
            // update ticks that fire mid-transition. Don't touch anything.
            // Just clear our references; domain reload via OnDomainReload() handles full cleanup.
            _disabledDevices.Clear();
            _virtualMouse = null;
            _virtualKeyboard = null;
            _virtualTouchscreen = null;
            _settingsSaved = false;
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
        public static bool DebugMode { get; set; }

        static void LogDebug(string message)
        {
            if (DebugMode)
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

        #endregion
    }
}
