using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ODDGames.UITest
{
    /// <summary>
    /// Wrapper for navigating object properties via reflection.
    /// Supports fluent chaining and iteration over arrays/lists.
    /// </summary>
    public class StaticPath : IEnumerable<StaticPath>
    {
        private readonly object _value;

        public StaticPath(object value) => _value = value;

        /// <summary>
        /// Gets a typed value from this object or navigates a sub-path.
        /// </summary>
        public T GetValue<T>(string subPath = null)
        {
            var value = string.IsNullOrEmpty(subPath) ? _value : NavigatePath(_value, subPath);

            if (value == null)
            {
                if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                    return default;
                return default;
            }

            if (value is T typedValue)
                return typedValue;

            try { return (T)Convert.ChangeType(value, typeof(T)); }
            catch { return default; }
        }

        /// <summary>
        /// Navigates to a property/field and returns a new StaticPath.
        /// </summary>
        public StaticPath Property(string name)
        {
            var value = NavigatePath(_value, name);
            return new StaticPath(value);
        }

        /// <summary>
        /// Iterates over array/list elements, yielding StaticPath for each.
        /// </summary>
        public IEnumerator<StaticPath> GetEnumerator()
        {
            if (_value == null)
                yield break;

            if (_value is IEnumerable enumerable && !(_value is string))
            {
                foreach (var item in enumerable)
                {
                    if (item != null)
                        yield return new StaticPath(item);
                }
            }
            else
            {
                yield return this;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private static object NavigatePath(object current, string path)
        {
            if (current == null || string.IsNullOrEmpty(path))
                return current;

            var parts = path.Split('.');
            foreach (var part in parts)
            {
                if (current == null) return null;

                var type = current.GetType();
                var prop = type.GetProperty(part, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    current = prop.GetValue(current);
                    continue;
                }

                var field = type.GetField(part, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    current = field.GetValue(current);
                    continue;
                }

                return null;
            }
            return current;
        }

        /// <summary>The raw wrapped value.</summary>
        public object Value => _value;

        /// <summary>Gets the value as string, or calls ToString() if not a string.</summary>
        public string StringValue => _value as string ?? _value?.ToString();

        /// <summary>Gets the value as bool. Returns false if not a bool.</summary>
        public bool BoolValue => _value is bool b && b;

        /// <summary>Gets the value as float. Returns 0 if not convertible.</summary>
        public float FloatValue
        {
            get
            {
                if (_value is float f) return f;
                if (_value is double d) return (float)d;
                if (_value is int i) return i;
                if (float.TryParse(_value?.ToString(), out var result)) return result;
                return 0f;
            }
        }

        /// <summary>Gets the value as int. Returns 0 if not convertible.</summary>
        public int IntValue
        {
            get
            {
                if (_value is int i) return i;
                if (_value is float f) return (int)f;
                if (_value is double d) return (int)d;
                if (int.TryParse(_value?.ToString(), out var result)) return result;
                return 0;
            }
        }

        /// <summary>Gets the GameObject if value is a Component or GameObject.</summary>
        public GameObject GameObject
        {
            get
            {
                if (_value is GameObject go) return go;
                if (_value is Component comp) return comp.gameObject;
                return null;
            }
        }
    }

    /// <summary>
    /// Shared action execution logic used by UITestBehaviour, VisualTestRunner, and AIActionExecutor.
    /// All methods take resolved GameObjects (no searching) and execute the action via InputInjector.
    /// This ensures consistent behavior across all test execution paths.
    /// </summary>
    public static class ActionExecutor
    {
        static void Log(string message) => Debug.Log($"[UIAUTOMATOR] {message}");

        static void LogDebug(string message)
        {
            if (UITestBehaviour.DebugMode)
                Debug.Log($"[UIAUTOMATOR:DEBUG] {message}");
        }

        #region Click Actions

        /// <summary>
        /// Clicks on a UI element matching the search query.
        /// Searches for a matching element and clicks on its screen position.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element to click when multiple match (0-based)</param>
        /// <returns>True if element was found and clicked, false otherwise</returns>
        public static async UniTask<bool> ClickAsync(Search search, float searchTime = 10f, int index = 0)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                {
                    var target = results[index];
                    var screenPos = InputInjector.GetScreenPosition(target);
                    Log($"Click({search}) -> '{target.name}' at ({screenPos.x:F0},{screenPos.y:F0})");
                    await InputInjector.InjectPointerTap(screenPos);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Clicks on a target GameObject.
        /// </summary>
        /// <param name="target">The GameObject to click on</param>
        public static async UniTask ClickAsync(GameObject target)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Click target cannot be null");

            var screenPos = InputInjector.GetScreenPosition(target);
            Log($"Click '{target.name}' at ({screenPos.x:F0},{screenPos.y:F0})");
            await InputInjector.InjectPointerTap(screenPos);
        }

        /// <summary>
        /// Clicks at a specific screen position.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to click</param>
        public static async UniTask ClickAtAsync(Vector2 screenPosition)
        {
            Log($"Click at ({screenPosition.x:F0}, {screenPosition.y:F0})");
            await InputInjector.InjectPointerTap(screenPosition);
        }

        /// <summary>
        /// Double-clicks on a UI element matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <returns>True if element was found and double-clicked, false otherwise</returns>
        public static async UniTask<bool> DoubleClickAsync(Search search, float searchTime = 10f, int index = 0)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                {
                    var target = results[index];
                    var screenPos = InputInjector.GetScreenPosition(target);
                    Log($"DoubleClick({search}) -> '{target.name}' at ({screenPos.x:F0}, {screenPos.y:F0})");
                    await InputInjector.InjectPointerDoubleTap(screenPos);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Double-clicks on a target GameObject.
        /// </summary>
        /// <param name="target">The GameObject to double-click on</param>
        public static async UniTask DoubleClickAsync(GameObject target)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "DoubleClick target cannot be null");

            var screenPos = InputInjector.GetScreenPosition(target);
            Log($"DoubleClick at ({screenPos.x:F0}, {screenPos.y:F0}) on '{target.name}'");
            await InputInjector.InjectPointerDoubleTap(screenPos);
        }

        /// <summary>
        /// Double-clicks at a specific screen position.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to double-click</param>
        public static async UniTask DoubleClickAtAsync(Vector2 screenPosition)
        {
            Log($"DoubleClick at ({screenPosition.x:F0}, {screenPosition.y:F0})");
            await InputInjector.InjectPointerDoubleTap(screenPosition);
        }

        /// <summary>
        /// Triple-clicks on a UI element matching the search query.
        /// Performs three rapid clicks in succession.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <returns>True if element was found and triple-clicked, false otherwise</returns>
        public static async UniTask<bool> TripleClickAsync(Search search, float searchTime = 10f, int index = 0)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                {
                    var target = results[index];
                    var screenPos = InputInjector.GetScreenPosition(target);
                    Log($"TripleClick({search}) -> '{target.name}' at ({screenPos.x:F0}, {screenPos.y:F0})");
                    await InputInjector.InjectPointerTripleTap(screenPos);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Triple-clicks on a target GameObject.
        /// Performs three rapid clicks in succession.
        /// </summary>
        /// <param name="target">The GameObject to triple-click on</param>
        public static async UniTask TripleClickAsync(GameObject target)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "TripleClick target cannot be null");

            var screenPos = InputInjector.GetScreenPosition(target);
            Log($"TripleClick at ({screenPos.x:F0}, {screenPos.y:F0}) on '{target.name}'");
            await InputInjector.InjectPointerTripleTap(screenPos);
        }

        /// <summary>
        /// Triple-clicks at a specific screen position.
        /// Performs three rapid clicks in succession.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to triple-click</param>
        public static async UniTask TripleClickAtAsync(Vector2 screenPosition)
        {
            Log($"TripleClick at ({screenPosition.x:F0}, {screenPosition.y:F0})");
            await InputInjector.InjectPointerTripleTap(screenPosition);
        }

        /// <summary>
        /// Holds/long-presses on a UI element matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="seconds">Duration of the hold in seconds</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <returns>True if element was found and held, false otherwise</returns>
        public static async UniTask<bool> HoldAsync(Search search, float seconds, float searchTime = 10f, int index = 0)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                {
                    var target = results[index];
                    var screenPos = InputInjector.GetScreenPosition(target);
                    Log($"Hold({search}) -> '{target.name}' at ({screenPos.x:F0}, {screenPos.y:F0}) for {seconds}s");
                    await InputInjector.InjectPointerHold(screenPos, seconds);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Holds/long-presses on a target GameObject.
        /// </summary>
        /// <param name="target">The GameObject to hold on</param>
        /// <param name="seconds">Duration of the hold in seconds</param>
        public static async UniTask HoldAsync(GameObject target, float seconds)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Hold target cannot be null");

            var screenPos = InputInjector.GetScreenPosition(target);
            Log($"Hold at ({screenPos.x:F0}, {screenPos.y:F0}) on '{target.name}' for {seconds}s");
            await InputInjector.InjectPointerHold(screenPos, seconds);
        }

        /// <summary>
        /// Holds/long-presses at a specific screen position.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to hold</param>
        /// <param name="seconds">Duration of the hold in seconds</param>
        public static async UniTask HoldAtAsync(Vector2 screenPosition, float seconds)
        {
            Log($"Hold at ({screenPosition.x:F0}, {screenPosition.y:F0}) for {seconds}s");
            await InputInjector.InjectPointerHold(screenPosition, seconds);
        }

        #endregion

        #region Text Input

        /// <summary>
        /// Types text into an input field matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the input field</param>
        /// <param name="text">The text to type</param>
        /// <param name="clearFirst">Whether to clear existing text first</param>
        /// <param name="pressEnter">Whether to press Enter after typing</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <returns>True if input field was found and text was typed, false otherwise</returns>
        public static async UniTask<bool> TypeAsync(Search search, string text, bool clearFirst = true, bool pressEnter = false, float searchTime = 10f, int index = 0)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                {
                    var target = results[index];
                    Log($"Type({search}) -> '{target.name}' text=\"{text}\" (clear={clearFirst}, enter={pressEnter})");
                    await InputInjector.TypeIntoField(target, text, clearFirst, pressEnter);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Types text into an input field.
        /// </summary>
        /// <param name="inputField">The input field GameObject (TMP_InputField or InputField)</param>
        /// <param name="text">The text to type</param>
        /// <param name="clearFirst">Whether to clear existing text first</param>
        /// <param name="pressEnter">Whether to press Enter after typing</param>
        public static async UniTask TypeAsync(GameObject inputField, string text, bool clearFirst = true, bool pressEnter = false)
        {
            if (inputField == null)
                throw new System.ArgumentNullException(nameof(inputField), "Type target cannot be null");

            Log($"Type \"{text}\" into '{inputField.name}' (clear={clearFirst}, enter={pressEnter})");
            await InputInjector.TypeIntoField(inputField, text, clearFirst, pressEnter);
        }

        /// <summary>
        /// Types text without targeting a specific input field (assumes something is focused).
        /// </summary>
        /// <param name="text">The text to type</param>
        public static async UniTask TypeTextAsync(string text)
        {
            Log($"TypeText \"{text}\"");
            await InputInjector.TypeText(text);
        }

        #endregion

        #region Drag Actions

        /// <summary>
        /// Drags from a source element matching search query in a direction.
        /// </summary>
        /// <param name="search">The search query to find the source element</param>
        /// <param name="direction">The direction to drag (pixel offset)</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <returns>True if element was found and dragged, false otherwise</returns>
        public static async UniTask<bool> DragAsync(Search search, Vector2 direction, float duration = 1.0f, float searchTime = 10f, int index = 0, float holdTime = 0.5f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                {
                    var target = results[index];
                    var screenPos = InputInjector.GetScreenPosition(target);
                    var endPos = screenPos + direction;
                    Log($"Drag({search}) -> '{target.name}' from ({screenPos.x:F0}, {screenPos.y:F0}) by ({direction.x:F0}, {direction.y:F0}) over {duration}s (hold={holdTime}s)");
                    await InputInjector.InjectPointerDrag(screenPos, endPos, duration, holdTime);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Drags from a source element matching search query to a target element matching another search query.
        /// </summary>
        /// <param name="fromSearch">The search query to find the source element</param>
        /// <param name="toSearch">The search query to find the target element</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        /// <param name="searchTime">Maximum time to search for elements</param>
        /// <param name="holdTime">Time to hold at start position before dragging (for elements requiring hold-to-drag)</param>
        /// <returns>True if both elements were found and drag completed, false otherwise</returns>
        public static async UniTask<bool> DragToAsync(Search fromSearch, Search toSearch, float duration = 1.0f, float searchTime = 10f, float holdTime = 0.5f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var fromResults = fromSearch.FindAll();
                var toResults = toSearch.FindAll();
                if (fromResults.Count > 0 && toResults.Count > 0)
                {
                    var fromTarget = fromResults[0];
                    var toTarget = toResults[0];
                    var fromPos = InputInjector.GetScreenPosition(fromTarget);
                    var toPos = InputInjector.GetScreenPosition(toTarget);
                    Log($"DragTo({fromSearch}) -> '{fromTarget.name}' to ({toSearch}) -> '{toTarget.name}' over {duration}s (hold={holdTime}s)");
                    await InputInjector.InjectPointerDrag(fromPos, toPos, duration, holdTime);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Drags from a source GameObject in a direction.
        /// </summary>
        /// <param name="source">The GameObject to drag from</param>
        /// <param name="direction">The direction to drag (pixel offset)</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        public static async UniTask DragAsync(GameObject source, Vector2 direction, float duration = 1.0f, float holdTime = 0.5f)
        {
            if (source == null)
                throw new System.ArgumentNullException(nameof(source), "Drag source cannot be null");

            var startPos = InputInjector.GetScreenPosition(source);
            var endPos = startPos + direction;
            Log($"Drag from ({startPos.x:F0}, {startPos.y:F0}) by ({direction.x:F0}, {direction.y:F0}) over {duration}s (hold={holdTime}s)");
            await InputInjector.InjectPointerDrag(startPos, endPos, duration, holdTime);
        }

        /// <summary>
        /// Drags from a source GameObject in a named direction.
        /// </summary>
        /// <param name="source">The GameObject to drag from</param>
        /// <param name="direction">Direction name: "up", "down", "left", "right"</param>
        /// <param name="normalizedDistance">Distance as fraction of screen height (0-1)</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        public static async UniTask DragAsync(GameObject source, string direction, float normalizedDistance = 0.2f, float duration = 1.0f, float holdTime = 0.5f)
        {
            if (source == null)
                throw new System.ArgumentNullException(nameof(source), "Drag source cannot be null");

            var offset = InputInjector.GetDirectionOffset(direction, normalizedDistance);
            await DragAsync(source, offset, duration, holdTime);
        }

        /// <summary>
        /// Drags from a source GameObject to a target GameObject.
        /// </summary>
        /// <param name="source">The GameObject to drag from</param>
        /// <param name="target">The GameObject to drag to</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        public static async UniTask DragToAsync(GameObject source, GameObject target, float duration = 1.0f, float holdTime = 0.5f)
        {
            if (source == null)
                throw new System.ArgumentNullException(nameof(source), "Drag source cannot be null");
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Drag target cannot be null");

            var startPos = InputInjector.GetScreenPosition(source);
            var endPos = InputInjector.GetScreenPosition(target);
            Log($"Drag from '{source.name}' to '{target.name}' over {duration}s (hold={holdTime}s)");
            await InputInjector.InjectPointerDrag(startPos, endPos, duration, holdTime);
        }

        /// <summary>
        /// Drags from a source GameObject to a target screen position.
        /// </summary>
        /// <param name="source">The GameObject to drag from</param>
        /// <param name="targetPosition">The screen position to drag to</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        public static async UniTask DragToAsync(GameObject source, Vector2 targetPosition, float duration = 1.0f, float holdTime = 0.5f)
        {
            if (source == null)
                throw new System.ArgumentNullException(nameof(source), "Drag source cannot be null");

            var startPos = InputInjector.GetScreenPosition(source);
            Log($"Drag from '{source.name}' to ({targetPosition.x:F0}, {targetPosition.y:F0}) over {duration}s (hold={holdTime}s)");
            await InputInjector.InjectPointerDrag(startPos, targetPosition, duration, holdTime);
        }

        /// <summary>
        /// Drags between two screen positions.
        /// </summary>
        /// <param name="startPosition">Start screen position</param>
        /// <param name="endPosition">End screen position</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        public static async UniTask DragFromToAsync(Vector2 startPosition, Vector2 endPosition, float duration = 1.0f, float holdTime = 0.5f)
        {
            Log($"Drag from ({startPosition.x:F0}, {startPosition.y:F0}) to ({endPosition.x:F0}, {endPosition.y:F0}) over {duration}s (hold={holdTime}s)");
            await InputInjector.InjectPointerDrag(startPosition, endPosition, duration, holdTime);
        }

        #endregion

        #region Scroll Actions

        /// <summary>
        /// Scrolls on a target GameObject.
        /// </summary>
        /// <param name="target">The GameObject to scroll on</param>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        public static async UniTask ScrollAsync(GameObject target, float delta)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Scroll target cannot be null");

            var screenPos = InputInjector.GetScreenPosition(target);
            Log($"Scroll on '{target.name}' delta={delta}");
            await InputInjector.InjectScroll(screenPos, delta);
        }

        /// <summary>
        /// Scrolls on a target GameObject in a named direction.
        /// </summary>
        /// <param name="target">The GameObject to scroll on</param>
        /// <param name="direction">Direction: "up", "down", "left", "right"</param>
        /// <param name="amount">Scroll amount (0-1 normalized)</param>
        public static async UniTask ScrollAsync(GameObject target, string direction, float amount = 0.3f)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Scroll target cannot be null");

            Log($"Scroll '{target.name}' {direction} amount={amount}");
            await InputInjector.ScrollElement(target, direction, amount);
        }

        /// <summary>
        /// Scrolls at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position to scroll at</param>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        public static async UniTask ScrollAtAsync(Vector2 position, float delta)
        {
            Log($"Scroll at ({position.x:F0}, {position.y:F0}) delta={delta}");
            await InputInjector.InjectScroll(position, delta);
        }

        /// <summary>
        /// Scrolls on a UI element matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <returns>True if element was found and scrolled, false otherwise</returns>
        public static async UniTask<bool> ScrollAsync(Search search, float delta, float searchTime = 10f, int index = 0)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                {
                    var target = results[index];
                    var screenPos = InputInjector.GetScreenPosition(target);
                    Log($"Scroll({search}) -> '{target.name}' at ({screenPos.x:F0}, {screenPos.y:F0}) delta={delta}");
                    await InputInjector.InjectScroll(screenPos, delta);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Scrolls on a UI element matching the search query in a named direction.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="direction">Direction: "up", "down", "left", "right"</param>
        /// <param name="amount">Scroll amount (0-1 normalized)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <returns>True if element was found and scrolled, false otherwise</returns>
        public static async UniTask<bool> ScrollAsync(Search search, string direction, float amount = 0.3f, float searchTime = 10f, int index = 0)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                {
                    var target = results[index];
                    Log($"Scroll({search}) -> '{target.name}' {direction} amount={amount}");
                    await InputInjector.ScrollElement(target, direction, amount);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        #endregion

        #region Slider/Scrollbar Actions

        /// <summary>
        /// Sets a slider to a specific value by clicking.
        /// </summary>
        /// <param name="slider">The Slider component</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        public static async UniTask SetSliderAsync(Slider slider, float normalizedValue)
        {
            if (slider == null)
                throw new System.ArgumentNullException(nameof(slider), "Slider cannot be null");

            Log($"SetSlider '{slider.name}' to {normalizedValue:F2}");
            await InputInjector.SetSlider(slider, normalizedValue);
        }

        /// <summary>
        /// Sets a slider to a specific value by clicking (finds Slider on GameObject).
        /// </summary>
        /// <param name="target">GameObject with Slider component</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        public static async UniTask SetSliderAsync(GameObject target, float normalizedValue)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Slider target cannot be null");

            var slider = target.GetComponent<Slider>();
            if (slider == null)
                throw new System.InvalidOperationException($"GameObject '{target.name}' does not have a Slider component");

            await SetSliderAsync(slider, normalizedValue);
        }

        /// <summary>
        /// Sets a scrollbar to a specific value by clicking.
        /// </summary>
        /// <param name="scrollbar">The Scrollbar component</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        public static async UniTask SetScrollbarAsync(Scrollbar scrollbar, float normalizedValue)
        {
            if (scrollbar == null)
                throw new System.ArgumentNullException(nameof(scrollbar), "Scrollbar cannot be null");

            Log($"SetScrollbar '{scrollbar.name}' to {normalizedValue:F2}");
            await InputInjector.SetScrollbar(scrollbar, normalizedValue);
        }

        /// <summary>
        /// Sets a scrollbar to a specific value by clicking (finds Scrollbar on GameObject).
        /// </summary>
        /// <param name="target">GameObject with Scrollbar component</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        public static async UniTask SetScrollbarAsync(GameObject target, float normalizedValue)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Scrollbar target cannot be null");

            var scrollbar = target.GetComponent<Scrollbar>();
            if (scrollbar == null)
                throw new System.InvalidOperationException($"GameObject '{target.name}' does not have a Scrollbar component");

            await SetScrollbarAsync(scrollbar, normalizedValue);
        }

        /// <summary>
        /// Clicks on a slider at a specific position to set its value.
        /// </summary>
        /// <param name="slider">The Slider component</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        public static async UniTask ClickSliderAsync(Slider slider, float normalizedValue)
        {
            if (slider == null)
                throw new System.ArgumentNullException(nameof(slider), "Slider cannot be null");

            var clickPos = InputInjector.GetSliderClickPosition(slider, normalizedValue);
            Log($"ClickSlider '{slider.name}' at {normalizedValue:F2} position ({clickPos.x:F0}, {clickPos.y:F0})");
            await InputInjector.InjectPointerTap(clickPos);
        }

        /// <summary>
        /// Drags a slider from one value to another.
        /// </summary>
        /// <param name="slider">The Slider component</param>
        /// <param name="fromValue">Starting value (0-1)</param>
        /// <param name="toValue">Ending value (0-1)</param>
        /// <param name="duration">Duration of the drag</param>
        public static async UniTask DragSliderAsync(Slider slider, float fromValue, float toValue, float duration = 1.0f, float holdTime = 0.5f)
        {
            if (slider == null)
                throw new System.ArgumentNullException(nameof(slider), "Slider cannot be null");

            var startPos = InputInjector.GetSliderClickPosition(slider, fromValue);
            var endPos = InputInjector.GetSliderClickPosition(slider, toValue);
            Log($"DragSlider '{slider.name}' from {fromValue:F2} to {toValue:F2} (hold={holdTime}s)");
            await InputInjector.InjectPointerDrag(startPos, endPos, duration, holdTime);
        }

        /// <summary>
        /// Clicks on a slider at a specific position matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the Slider</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <returns>True if slider was found and clicked, false otherwise</returns>
        public static async UniTask<bool> ClickSliderAsync(Search search, float normalizedValue, float searchTime = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                foreach (var go in results)
                {
                    var slider = go.GetComponent<Slider>();
                    if (slider != null)
                    {
                        Log($"ClickSlider({search}) -> '{go.name}' value={normalizedValue:F2}");
                        await ClickSliderAsync(slider, normalizedValue);
                        return true;
                    }
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Drags a slider from one value to another matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the Slider</param>
        /// <param name="fromValue">Starting value (0-1)</param>
        /// <param name="toValue">Ending value (0-1)</param>
        /// <param name="duration">Duration of the drag</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <returns>True if slider was found and dragged, false otherwise</returns>
        public static async UniTask<bool> DragSliderAsync(Search search, float fromValue, float toValue, float duration = 1.0f, float searchTime = 10f, float holdTime = 0.5f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                foreach (var go in results)
                {
                    var slider = go.GetComponent<Slider>();
                    if (slider != null)
                    {
                        Log($"DragSlider({search}) -> '{go.name}' from {fromValue:F2} to {toValue:F2}");
                        await DragSliderAsync(slider, fromValue, toValue, duration, holdTime);
                        return true;
                    }
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Sets a slider to a specific value matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the Slider</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <returns>True if slider was found and set, false otherwise</returns>
        public static async UniTask<bool> SetSliderAsync(Search search, float normalizedValue, float searchTime = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                foreach (var go in results)
                {
                    var slider = go.GetComponent<Slider>();
                    if (slider != null)
                    {
                        Log($"SetSlider({search}) -> '{go.name}' value={normalizedValue:F2}");
                        await SetSliderAsync(slider, normalizedValue);
                        return true;
                    }
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Sets a scrollbar to a specific value matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the Scrollbar</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <returns>True if scrollbar was found and set, false otherwise</returns>
        public static async UniTask<bool> SetScrollbarAsync(Search search, float normalizedValue, float searchTime = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                foreach (var go in results)
                {
                    var scrollbar = go.GetComponent<Scrollbar>();
                    if (scrollbar != null)
                    {
                        Log($"SetScrollbar({search}) -> '{go.name}' value={normalizedValue:F2}");
                        await SetScrollbarAsync(scrollbar, normalizedValue);
                        return true;
                    }
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        #endregion

        #region Gesture Actions

        /// <summary>
        /// Performs a swipe gesture on a target GameObject.
        /// </summary>
        /// <param name="target">The GameObject to swipe on</param>
        /// <param name="direction">Direction: "up", "down", "left", "right"</param>
        /// <param name="normalizedDistance">Distance as fraction of screen (0-1)</param>
        /// <param name="duration">Duration of the swipe</param>
        public static async UniTask SwipeAsync(GameObject target, string direction, float normalizedDistance = 0.2f, float duration = 1.0f)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Swipe target cannot be null");

            Log($"Swipe on '{target.name}' {direction} distance={normalizedDistance} duration={duration}s");
            await InputInjector.Swipe(target, direction, normalizedDistance, duration);
        }

        /// <summary>
        /// Performs a swipe gesture at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position to start the swipe</param>
        /// <param name="direction">Direction: "up", "down", "left", "right"</param>
        /// <param name="normalizedDistance">Distance as fraction of screen (0-1)</param>
        /// <param name="duration">Duration of the swipe</param>
        public static async UniTask SwipeAtAsync(Vector2 position, string direction, float normalizedDistance = 0.2f, float duration = 1.0f)
        {
            var offset = InputInjector.GetDirectionOffset(direction, normalizedDistance);
            var endPos = position + offset;
            Log($"Swipe at ({position.x:F0}, {position.y:F0}) {direction}");
            await InputInjector.InjectMouseDrag(position, endPos, duration);
        }

        /// <summary>
        /// Performs a pinch gesture on a target GameObject.
        /// </summary>
        /// <param name="target">The GameObject to pinch on</param>
        /// <param name="scale">Scale factor (less than 1 = zoom out, greater than 1 = zoom in)</param>
        /// <param name="duration">Duration of the pinch</param>
        public static async UniTask PinchAsync(GameObject target, float scale, float duration = 1.0f)
        {
            Log($"Pinch on '{target?.name ?? "screen center"}' scale={scale} duration={duration}s");
            await InputInjector.Pinch(target, scale, duration);
        }

        /// <summary>
        /// Performs a pinch gesture at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position for the pinch center</param>
        /// <param name="scale">Scale factor</param>
        /// <param name="duration">Duration of the pinch</param>
        public static async UniTask PinchAtAsync(Vector2 position, float scale, float duration = 1.0f)
        {
            Log($"Pinch at ({position.x:F0}, {position.y:F0}) scale={scale} duration={duration}s");
            await InputInjector.InjectPinch(position, scale, duration);
        }

        /// <summary>
        /// Performs a pinch gesture at a specific screen position with custom finger distance.
        /// </summary>
        /// <param name="position">Screen position for the pinch center</param>
        /// <param name="scale">Scale factor</param>
        /// <param name="duration">Duration of the pinch</param>
        /// <param name="fingerDistancePixels">Initial distance of each finger from center in pixels</param>
        public static async UniTask PinchAtAsync(Vector2 position, float scale, float duration, float fingerDistancePixels)
        {
            Log($"Pinch at ({position.x:F0}, {position.y:F0}) scale={scale} duration={duration}s fingerDistance={fingerDistancePixels}px");
            await InputInjector.InjectPinch(position, scale, duration, fingerDistancePixels);
        }

        /// <summary>
        /// Performs a two-finger swipe gesture on a target GameObject.
        /// </summary>
        /// <param name="target">The GameObject to swipe on</param>
        /// <param name="direction">Direction: "up", "down", "left", "right"</param>
        /// <param name="normalizedDistance">Distance as fraction of screen (0-1)</param>
        /// <param name="duration">Duration of the swipe</param>
        /// <param name="fingerSpacing">Normalized spacing between fingers (0-1)</param>
        public static async UniTask TwoFingerSwipeAsync(GameObject target, string direction, float normalizedDistance = 0.2f, float duration = 1.0f, float fingerSpacing = 0.03f)
        {
            Log($"TwoFingerSwipe on '{target?.name ?? "screen center"}' {direction}");
            await InputInjector.TwoFingerSwipe(target, direction, normalizedDistance, duration, fingerSpacing);
        }

        /// <summary>
        /// Performs a two-finger swipe gesture at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position for the swipe center</param>
        /// <param name="direction">Direction: "up", "down", "left", "right"</param>
        /// <param name="normalizedDistance">Distance as fraction of screen (0-1)</param>
        /// <param name="duration">Duration of the swipe</param>
        /// <param name="fingerSpacing">Normalized spacing between fingers (0-1)</param>
        public static async UniTask TwoFingerSwipeAtAsync(Vector2 position, string direction, float normalizedDistance = 0.2f, float duration = 1.0f, float fingerSpacing = 0.03f)
        {
            Log($"TwoFingerSwipe at ({position.x:F0}, {position.y:F0}) {direction}");
            await InputInjector.InjectTwoFingerSwipe(position, direction, normalizedDistance, duration, fingerSpacing);
        }

        /// <summary>
        /// Performs a rotation gesture on a target GameObject.
        /// </summary>
        /// <param name="target">The GameObject to rotate on</param>
        /// <param name="degrees">Rotation angle (positive = clockwise, negative = counter-clockwise)</param>
        /// <param name="duration">Duration of the rotation</param>
        /// <param name="fingerDistance">Normalized distance from center for fingers (0-1)</param>
        public static async UniTask RotateAsync(GameObject target, float degrees, float duration = 1.0f, float fingerDistance = 0.05f)
        {
            Log($"Rotate on '{target?.name ?? "screen center"}' degrees={degrees} duration={duration}s");
            await InputInjector.Rotate(target, degrees, duration, fingerDistance);
        }

        /// <summary>
        /// Performs a rotation gesture at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position for the rotation center</param>
        /// <param name="degrees">Rotation angle</param>
        /// <param name="duration">Duration of the rotation</param>
        /// <param name="fingerDistance">Normalized distance from center for fingers (0-1)</param>
        public static async UniTask RotateAtAsync(Vector2 position, float degrees, float duration = 1.0f, float fingerDistance = 0.05f)
        {
            Log($"Rotate at ({position.x:F0}, {position.y:F0}) degrees={degrees} duration={duration}s");
            await InputInjector.InjectRotate(position, degrees, duration, fingerDistance);
        }

        /// <summary>
        /// Performs a rotation gesture at a specific screen position with pixel-based radius.
        /// </summary>
        /// <param name="position">Screen position for the rotation center</param>
        /// <param name="degrees">Rotation angle</param>
        /// <param name="duration">Duration of the rotation</param>
        /// <param name="radiusPixels">Distance from center in pixels for finger positions</param>
        public static async UniTask RotateAtPixelsAsync(Vector2 position, float degrees, float duration, float radiusPixels)
        {
            Log($"Rotate at ({position.x:F0}, {position.y:F0}) degrees={degrees} duration={duration}s radius={radiusPixels}px");
            await InputInjector.InjectRotatePixels(position, degrees, duration, radiusPixels);
        }

        #endregion

        #region Keyboard Actions

        /// <summary>
        /// Presses a keyboard key.
        /// </summary>
        /// <param name="key">The key to press</param>
        public static async UniTask PressKeyAsync(Key key)
        {
            Log($"PressKey {key}");
            await InputInjector.PressKey(key);
        }

        /// <summary>
        /// Holds a keyboard key for a duration.
        /// </summary>
        /// <param name="key">The key to hold</param>
        /// <param name="duration">How long to hold in seconds</param>
        public static async UniTask HoldKeyAsync(Key key, float duration)
        {
            Log($"HoldKey {key} for {duration}s");
            await InputInjector.HoldKey(key, duration);
        }

        /// <summary>
        /// Holds multiple keyboard keys simultaneously.
        /// </summary>
        /// <param name="keys">The keys to hold</param>
        /// <param name="duration">How long to hold in seconds</param>
        public static async UniTask HoldKeysAsync(Key[] keys, float duration)
        {
            Log($"HoldKeys [{string.Join(", ", keys)}] for {duration}s");
            await InputInjector.HoldKeys(keys, duration);
        }

        #endregion

        #region Dropdown Actions

        /// <summary>
        /// Selects a dropdown option by index using realistic click interactions.
        /// </summary>
        /// <param name="search">The search query to find the Dropdown or TMP_Dropdown</param>
        /// <param name="optionIndex">Index of the option to select (0-based)</param>
        /// <param name="searchTime">Maximum time to search for the dropdown</param>
        /// <returns>True if dropdown was found and option selected, false otherwise</returns>
        public static async UniTask<bool> ClickDropdownAsync(Search search, int optionIndex, float searchTime = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                foreach (var go in results)
                {
                    // Try legacy Dropdown
                    var legacyDropdown = go.GetComponent<Dropdown>();
                    if (legacyDropdown != null)
                    {
                        Log($"ClickDropdown({search}) -> '{go.name}' index={optionIndex}");
                        await ClickDropdownItemAsync(legacyDropdown.gameObject, legacyDropdown.template, optionIndex);
                        return true;
                    }

                    // Try TMP_Dropdown
                    var tmpDropdown = go.GetComponent<TMPro.TMP_Dropdown>();
                    if (tmpDropdown != null)
                    {
                        Log($"ClickDropdown({search}) -> '{go.name}' index={optionIndex}");
                        await ClickDropdownItemAsync(tmpDropdown.gameObject, tmpDropdown.template, optionIndex);
                        return true;
                    }
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Selects a dropdown option by label text using realistic click interactions.
        /// </summary>
        /// <param name="search">The search query to find the Dropdown or TMP_Dropdown</param>
        /// <param name="optionLabel">The text label of the option to select</param>
        /// <param name="searchTime">Maximum time to search for the dropdown</param>
        /// <returns>True if dropdown was found and option selected, false otherwise</returns>
        public static async UniTask<bool> ClickDropdownAsync(Search search, string optionLabel, float searchTime = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                foreach (var go in results)
                {
                    // Try legacy Dropdown
                    var legacyDropdown = go.GetComponent<Dropdown>();
                    if (legacyDropdown != null)
                    {
                        int optionIndex = legacyDropdown.options.FindIndex(o => o.text == optionLabel);
                        if (optionIndex >= 0)
                        {
                            Log($"ClickDropdown({search}) -> '{go.name}' label=\"{optionLabel}\" (index={optionIndex})");
                            await ClickDropdownItemAsync(legacyDropdown.gameObject, legacyDropdown.template, optionIndex);
                            return true;
                        }
                    }

                    // Try TMP_Dropdown
                    var tmpDropdown = go.GetComponent<TMPro.TMP_Dropdown>();
                    if (tmpDropdown != null)
                    {
                        int optionIndex = tmpDropdown.options.FindIndex(o => o.text == optionLabel);
                        if (optionIndex >= 0)
                        {
                            Log($"ClickDropdown({search}) -> '{go.name}' label=\"{optionLabel}\" (index={optionIndex})");
                            await ClickDropdownItemAsync(tmpDropdown.gameObject, tmpDropdown.template, optionIndex);
                            return true;
                        }
                    }
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Internal method to click a dropdown item after the dropdown has been found.
        /// </summary>
        private static async UniTask ClickDropdownItemAsync(GameObject dropdownGO, RectTransform template, int optionIndex)
        {
            // Capture existing toggles before opening dropdown
            var existingToggles = new System.Collections.Generic.HashSet<Toggle>(
                UnityEngine.Object.FindObjectsByType<Toggle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));

            // Click the dropdown to open it
            await ClickAsync(dropdownGO);

            // Wait for new toggles to appear (the dropdown items)
            Toggle[] newToggles = null;
            float waitTime = 0f;
            const float maxWaitTime = 0.5f;

            while (waitTime < maxWaitTime)
            {
                await UniTask.DelayFrame(1);

                var allToggles = UnityEngine.Object.FindObjectsByType<Toggle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

                // Find toggles that are new (created by opening the dropdown) and not part of the template
                newToggles = allToggles
                    .Where(t => !existingToggles.Contains(t))
                    .Where(t => t.gameObject.activeInHierarchy)
                    .Where(t => template == null || (!t.transform.IsChildOf(template) && t.transform != template))
                    .OrderBy(t => t.transform.GetSiblingIndex())
                    .ToArray();

                if (newToggles.Length > optionIndex)
                {
                    var targetToggle = newToggles[optionIndex];
                    Log($"ClickDropdown selecting option {optionIndex}: '{targetToggle.name}'");
                    await ClickAsync(targetToggle.gameObject);
                    return;
                }

                await UniTask.Delay(50, true);
                waitTime += 0.05f;
            }

            Debug.LogWarning($"[ActionExecutor] ClickDropdown - Item at index {optionIndex} not found (found {newToggles?.Length ?? 0} new toggles)");
        }

        #endregion

        #region Utility Actions

        /// <summary>
        /// Clicks any one of the elements matching the search query.
        /// </summary>
        /// <param name="search">The search query to find elements</param>
        /// <param name="searchTime">Maximum time to search for elements</param>
        /// <returns>True if any element was found and clicked, false otherwise</returns>
        public static async UniTask<bool> ClickAnyAsync(Search search, float searchTime = 10f)
        {
            float startTime = Time.realtimeSinceStartup;
            var rnd = new System.Random((int)System.DateTime.Now.Millisecond);

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > 0)
                {
                    // Randomly select one
                    var target = results.OrderBy(_ => rnd.Next()).First();
                    var screenPos = InputInjector.GetScreenPosition(target);
                    Log($"ClickAny({search}) -> '{target.name}' at ({screenPos.x:F0}, {screenPos.y:F0}) (from {results.Count} matches)");
                    await ClickAsync(target);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        #endregion

        #region Search-Based Assertions

        /// <summary>
        /// Asserts that an element matching the search exists.
        /// </summary>
        /// <param name="search">The search query</param>
        /// <param name="message">Optional custom failure message</param>
        /// <exception cref="InvalidOperationException">If element not found</exception>
        public static void AssertExists(Search search, string message = null)
        {
            var result = search.FindFirst();
            if (result == null)
            {
                var msg = message ?? $"Assert failed: element not found";
                throw new InvalidOperationException($"{msg} - Search: {search}");
            }
            Log($"Assert passed: {search} exists (found '{result.name}')");
        }

        /// <summary>
        /// Asserts that no element matching the search exists.
        /// </summary>
        /// <param name="search">The search query</param>
        /// <param name="message">Optional custom failure message</param>
        /// <exception cref="InvalidOperationException">If element is found</exception>
        public static void AssertNotExists(Search search, string message = null)
        {
            var result = search.FindFirst();
            if (result != null)
            {
                var msg = message ?? $"Assert failed: element should not exist";
                throw new InvalidOperationException($"{msg} - Search: {search}, Found: '{result.name}'");
            }
            Log($"Assert passed: {search} does not exist");
        }

        /// <summary>
        /// Asserts that the text content of an element matches expected value.
        /// Works with TMP_Text and legacy Text components.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="expected">Expected text content</param>
        /// <param name="message">Optional custom failure message</param>
        public static void AssertText(Search search, string expected, string message = null)
        {
            var go = search.FindFirst();
            if (go == null)
                throw new InvalidOperationException($"Assert failed: element not found - Search: {search}");

            string actual = null;
            var tmp = go.GetComponent<TMPro.TMP_Text>();
            if (tmp != null)
                actual = tmp.text;
            else
            {
                var legacy = go.GetComponent<UnityEngine.UI.Text>();
                if (legacy != null)
                    actual = legacy.text;
            }

            if (actual == null)
                throw new InvalidOperationException($"Assert failed: '{go.name}' has no text component");

            if (actual != expected)
            {
                var msg = message ?? $"Assert failed: text mismatch";
                throw new InvalidOperationException($"{msg} - Expected: \"{expected}\", Actual: \"{actual}\"");
            }
            Log($"Assert passed: {search} text == \"{expected}\"");
        }

        /// <summary>
        /// Asserts that a toggle is in the expected state.
        /// </summary>
        /// <param name="search">The search query to find the toggle</param>
        /// <param name="expectedOn">Expected isOn state</param>
        /// <param name="message">Optional custom failure message</param>
        public static void AssertToggle(Search search, bool expectedOn, string message = null)
        {
            var go = search.FindFirst();
            if (go == null)
                throw new InvalidOperationException($"Assert failed: element not found - Search: {search}");

            var toggle = go.GetComponent<UnityEngine.UI.Toggle>();
            if (toggle == null)
                throw new InvalidOperationException($"Assert failed: '{go.name}' is not a Toggle");

            if (toggle.isOn != expectedOn)
            {
                var msg = message ?? $"Assert failed: toggle state mismatch";
                throw new InvalidOperationException($"{msg} - Expected: {expectedOn}, Actual: {toggle.isOn}");
            }
            Log($"Assert passed: {search} isOn == {expectedOn}");
        }

        /// <summary>
        /// Asserts that a slider value is within expected range.
        /// </summary>
        /// <param name="search">The search query to find the slider</param>
        /// <param name="expected">Expected value</param>
        /// <param name="tolerance">Allowed tolerance (default 0.01)</param>
        /// <param name="message">Optional custom failure message</param>
        public static void AssertSlider(Search search, float expected, float tolerance = 0.01f, string message = null)
        {
            var go = search.FindFirst();
            if (go == null)
                throw new InvalidOperationException($"Assert failed: element not found - Search: {search}");

            var slider = go.GetComponent<Slider>();
            if (slider == null)
                throw new InvalidOperationException($"Assert failed: '{go.name}' is not a Slider");

            if (Mathf.Abs(slider.value - expected) > tolerance)
            {
                var msg = message ?? $"Assert failed: slider value mismatch";
                throw new InvalidOperationException($"{msg} - Expected: {expected} (±{tolerance}), Actual: {slider.value}");
            }
            Log($"Assert passed: {search} value ≈ {expected}");
        }

        /// <summary>
        /// Asserts that an element is interactable.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="expectedInteractable">Expected interactable state</param>
        /// <param name="message">Optional custom failure message</param>
        public static void AssertInteractable(Search search, bool expectedInteractable = true, string message = null)
        {
            var go = search.FindFirst();
            if (go == null)
                throw new InvalidOperationException($"Assert failed: element not found - Search: {search}");

            var selectable = go.GetComponent<UnityEngine.UI.Selectable>();
            if (selectable == null)
                throw new InvalidOperationException($"Assert failed: '{go.name}' is not a Selectable");

            if (selectable.interactable != expectedInteractable)
            {
                var msg = message ?? $"Assert failed: interactable state mismatch";
                throw new InvalidOperationException($"{msg} - Expected: {expectedInteractable}, Actual: {selectable.interactable}");
            }
            Log($"Assert passed: {search} interactable == {expectedInteractable}");
        }

        /// <summary>
        /// Asserts that a static path resolves to a truthy value.
        /// </summary>
        /// <param name="path">Dot-separated path to the value (e.g., "GameManager.Instance.IsReady")</param>
        /// <param name="message">Optional custom failure message</param>
        public static void Assert(string path, string message = null)
        {
            var value = ResolveStaticPath(path);
            if (!IsTruthy(value))
            {
                var msg = message ?? $"Assert failed: {path}";
                throw new InvalidOperationException($"{msg} - Expected truthy value but got: {value ?? "null"}");
            }
            Log($"Assert passed: {path} is truthy ({value})");
        }

        /// <summary>
        /// Asserts that a static path equals an expected value.
        /// </summary>
        /// <typeparam name="T">Type of the value</typeparam>
        /// <param name="path">Dot-separated path to the value</param>
        /// <param name="expected">Expected value</param>
        /// <param name="message">Optional custom failure message</param>
        public static void Assert<T>(string path, T expected, string message = null)
        {
            var value = ResolveStaticPath(path);
            T actual;

            if (value is T typedValue)
                actual = typedValue;
            else if (value != null)
            {
                try { actual = (T)Convert.ChangeType(value, typeof(T)); }
                catch { throw new InvalidOperationException($"Assert failed: {path} - Cannot convert {value.GetType().Name} to {typeof(T).Name}"); }
            }
            else if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                throw new InvalidOperationException($"Assert failed: {path} - Path resolved to null but expected {typeof(T).Name}");
            else
                actual = default;

            if (!Equals(actual, expected))
            {
                var msg = message ?? $"Assert failed: {path}";
                throw new InvalidOperationException($"{msg} - Expected: {expected}, Actual: {actual}");
            }
            Log($"Assert passed: {path} == {expected}");
        }

        /// <summary>
        /// Asserts that a numeric value at a static path is greater than expected.
        /// </summary>
        /// <param name="path">Dot-separated path to the value</param>
        /// <param name="greaterThan">Value that the path must be greater than</param>
        /// <param name="message">Optional custom failure message</param>
        public static void AssertGreater(string path, double greaterThan, string message = null)
        {
            var value = ResolveStaticPath(path);
            double actual;
            try { actual = Convert.ToDouble(value); }
            catch { throw new InvalidOperationException($"Assert failed: {path} - Cannot convert {value?.GetType().Name ?? "null"} to number"); }

            if (actual <= greaterThan)
            {
                var msg = message ?? $"Assert failed: {path}";
                throw new InvalidOperationException($"{msg} - Expected > {greaterThan}, Actual: {actual}");
            }
            Log($"Assert passed: {path} ({actual}) > {greaterThan}");
        }

        /// <summary>
        /// Asserts that a numeric value at a static path is less than expected.
        /// </summary>
        /// <param name="path">Dot-separated path to the value</param>
        /// <param name="lessThan">Value that the path must be less than</param>
        /// <param name="message">Optional custom failure message</param>
        public static void AssertLess(string path, double lessThan, string message = null)
        {
            var value = ResolveStaticPath(path);
            double actual;
            try { actual = Convert.ToDouble(value); }
            catch { throw new InvalidOperationException($"Assert failed: {path} - Cannot convert {value?.GetType().Name ?? "null"} to number"); }

            if (actual >= lessThan)
            {
                var msg = message ?? $"Assert failed: {path}";
                throw new InvalidOperationException($"{msg} - Expected < {lessThan}, Actual: {actual}");
            }
            Log($"Assert passed: {path} ({actual}) < {lessThan}");
        }

        /// <summary>
        /// Asserts that a Search (static path or UI element) resolves to a truthy value.
        /// </summary>
        /// <param name="search">Search to check (e.g., Search.Static("GameManager.IsReady"))</param>
        /// <param name="message">Optional custom failure message</param>
        public static void Assert(Search search, string message = null)
        {
            if (!IsTruthy(search?.Value))
            {
                var msg = message ?? "Assert failed: Search";
                throw new InvalidOperationException($"{msg} - Expected truthy value but got: {search?.Value ?? "null"}");
            }
            Log($"Assert passed: Search is truthy ({search.Value})");
        }

        /// <summary>
        /// Asserts that a Search (static path or UI element) equals an expected value.
        /// </summary>
        /// <typeparam name="T">Type of the value</typeparam>
        /// <param name="search">Search to check</param>
        /// <param name="expected">Expected value</param>
        /// <param name="message">Optional custom failure message</param>
        public static void Assert<T>(Search search, T expected, string message = null)
        {
            var value = search?.Value;
            T actual;

            if (value is T typedValue)
                actual = typedValue;
            else if (value != null)
            {
                try { actual = (T)Convert.ChangeType(value, typeof(T)); }
                catch { throw new InvalidOperationException($"Assert failed: Search - Cannot convert {value.GetType().Name} to {typeof(T).Name}"); }
            }
            else if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                throw new InvalidOperationException($"Assert failed: Search - Value is null but expected {typeof(T).Name}");
            else
                actual = default;

            if (!Equals(actual, expected))
            {
                var msg = message ?? "Assert failed: Search";
                throw new InvalidOperationException($"{msg} - Expected: {expected}, Actual: {actual}");
            }
            Log($"Assert passed: Search == {expected}");
        }

        /// <summary>
        /// Asserts that a Search numeric value is greater than expected.
        /// </summary>
        public static void AssertGreater(Search search, double greaterThan, string message = null)
        {
            var value = search?.Value;
            double actual;
            try { actual = Convert.ToDouble(value); }
            catch { throw new InvalidOperationException($"Assert failed: Search - Cannot convert {value?.GetType().Name ?? "null"} to number"); }

            if (actual <= greaterThan)
            {
                var msg = message ?? "Assert failed: Search";
                throw new InvalidOperationException($"{msg} - Expected > {greaterThan}, Actual: {actual}");
            }
            Log($"Assert passed: Search ({actual}) > {greaterThan}");
        }

        /// <summary>
        /// Asserts that a Search numeric value is less than expected.
        /// </summary>
        public static void AssertLess(Search search, double lessThan, string message = null)
        {
            var value = search?.Value;
            double actual;
            try { actual = Convert.ToDouble(value); }
            catch { throw new InvalidOperationException($"Assert failed: Search - Cannot convert {value?.GetType().Name ?? "null"} to number"); }

            if (actual >= lessThan)
            {
                var msg = message ?? "Assert failed: Search";
                throw new InvalidOperationException($"{msg} - Expected < {lessThan}, Actual: {actual}");
            }
            Log($"Assert passed: Search ({actual}) < {lessThan}");
        }

        #endregion

        #region GetValue Methods

        /// <summary>
        /// Gets a value from a UI element found by the search.
        /// Supports: string (text), bool (toggle), float (slider/scrollbar), int, and arrays.
        /// </summary>
        /// <typeparam name="T">Type of value to get: string, bool, float, int, or T[]</typeparam>
        /// <param name="search">The search query to find the element</param>
        /// <returns>The value from the element</returns>
        /// <example>
        /// var text = GetValue&lt;string&gt;(Name("ScoreLabel"));
        /// var isOn = GetValue&lt;bool&gt;(Name("SoundToggle"));
        /// var volume = GetValue&lt;float&gt;(Name("VolumeSlider"));
        /// var items = GetValue&lt;string[]&gt;(Name("Dropdown"));
        /// </example>
        public static T GetValue<T>(Search search)
        {
            var go = search.FindFirst();
            if (go == null)
                throw new InvalidOperationException($"GetValue failed: element not found - Search: {search}");

            var type = typeof(T);

            // String - get text content
            if (type == typeof(string))
            {
                var tmp = go.GetComponent<TMPro.TMP_Text>();
                if (tmp != null)
                    return (T)(object)tmp.text;

                var legacy = go.GetComponent<UnityEngine.UI.Text>();
                if (legacy != null)
                    return (T)(object)legacy.text;

                var inputField = go.GetComponent<TMPro.TMP_InputField>();
                if (inputField != null)
                    return (T)(object)inputField.text;

                var legacyInput = go.GetComponent<UnityEngine.UI.InputField>();
                if (legacyInput != null)
                    return (T)(object)legacyInput.text;

                throw new InvalidOperationException($"GetValue<string> failed: '{go.name}' has no text component");
            }

            // Bool - get toggle state
            if (type == typeof(bool))
            {
                var toggle = go.GetComponent<UnityEngine.UI.Toggle>();
                if (toggle != null)
                    return (T)(object)toggle.isOn;

                throw new InvalidOperationException($"GetValue<bool> failed: '{go.name}' is not a Toggle");
            }

            // Float - get slider/scrollbar value
            if (type == typeof(float))
            {
                var slider = go.GetComponent<Slider>();
                if (slider != null)
                    return (T)(object)slider.value;

                var scrollbar = go.GetComponent<UnityEngine.UI.Scrollbar>();
                if (scrollbar != null)
                    return (T)(object)scrollbar.value;

                throw new InvalidOperationException($"GetValue<float> failed: '{go.name}' is not a Slider or Scrollbar");
            }

            // Int - get slider value as int, dropdown index, or text as int
            if (type == typeof(int))
            {
                var slider = go.GetComponent<Slider>();
                if (slider != null)
                    return (T)(object)(int)slider.value;

                var dropdown = go.GetComponent<UnityEngine.UI.Dropdown>();
                if (dropdown != null)
                    return (T)(object)dropdown.value;

                var tmpDropdown = go.GetComponent<TMPro.TMP_Dropdown>();
                if (tmpDropdown != null)
                    return (T)(object)tmpDropdown.value;

                // Try parsing text as int
                var tmp = go.GetComponent<TMPro.TMP_Text>();
                if (tmp != null && int.TryParse(tmp.text, out var intVal))
                    return (T)(object)intVal;

                var legacy = go.GetComponent<UnityEngine.UI.Text>();
                if (legacy != null && int.TryParse(legacy.text, out intVal))
                    return (T)(object)intVal;

                throw new InvalidOperationException($"GetValue<int> failed: '{go.name}' is not a Slider, Dropdown, or text with int value");
            }

            // String array - get dropdown options
            if (type == typeof(string[]))
            {
                var dropdown = go.GetComponent<UnityEngine.UI.Dropdown>();
                if (dropdown != null)
                    return (T)(object)dropdown.options.Select(o => o.text).ToArray();

                var tmpDropdown = go.GetComponent<TMPro.TMP_Dropdown>();
                if (tmpDropdown != null)
                    return (T)(object)tmpDropdown.options.Select(o => o.text).ToArray();

                throw new InvalidOperationException($"GetValue<string[]> failed: '{go.name}' is not a Dropdown");
            }

            throw new InvalidOperationException($"GetValue<{type.Name}> failed: unsupported type. Use string, bool, float, int, or string[]");
        }

        /// <summary>
        /// Gets a value from a static path using reflection.
        /// Resolves dot-separated paths like "GameManager.Instance.Score" or "Player.Health".
        /// </summary>
        /// <typeparam name="T">Type of value to get</typeparam>
        /// <param name="path">Dot-separated path to the value (e.g., "GameManager.Instance.Score")</param>
        /// <returns>The value at the path</returns>
        /// <example>
        /// var score = GetValue&lt;int&gt;("GameManager.Instance.Score");
        /// var name = GetValue&lt;string&gt;("Player.Instance.Name");
        /// var isReady = GetValue&lt;bool&gt;("GameManager.Instance.IsReady");
        /// var items = GetValue&lt;string[]&gt;("Inventory.Instance.ItemNames");
        /// </example>
        public static T GetValue<T>(string path)
        {
            var value = ResolveStaticPath(path);

            if (value == null)
            {
                if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                    throw new InvalidOperationException($"GetValue<{typeof(T).Name}> failed: {path} resolved to null");
                return default;
            }

            if (value is T typedValue)
                return typedValue;

            // Try conversion
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                throw new InvalidOperationException($"GetValue<{typeof(T).Name}> failed: cannot convert {value.GetType().Name} to {typeof(T).Name}");
            }
        }

        /// <summary>
        /// Creates a StaticPath from a dot-separated static path.
        /// Returns an iterable wrapper that can navigate properties and iterate over arrays/lists.
        /// </summary>
        /// <param name="path">Dot-separated path (e.g., "GameManager.Instance.AllPlayers")</param>
        /// <returns>StaticPath wrapper that can be iterated or navigated</returns>
        /// <example>
        /// // Iterate over array
        /// foreach (var truck in Static("Generator.Trucks.AllTrucks"))
        /// {
        ///     var health = truck.GetValue&lt;float&gt;("DamageController.Health");
        ///     var name = truck.GetValue&lt;string&gt;("Name");
        /// }
        ///
        /// // Navigate properties
        /// var player = Static("GameManager.Instance.Player");
        /// var score = player.GetValue&lt;int&gt;("Score");
        ///
        /// // Chain navigation
        /// var damage = Static("Generator.Trucks.PlayerTruck").Property("DamageController").GetValue&lt;float&gt;("Health");
        /// </example>
        public static StaticPath Static(string path)
        {
            var value = ResolveStaticPath(path);
            return new StaticPath(value);
        }

        #endregion

        #region Wait Methods

        /// <summary>
        /// Waits until an element matching the search exists.
        /// </summary>
        /// <param name="search">The search query</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <returns>True if element found, false if timeout</returns>
        public static async UniTask<bool> WaitFor(Search search, float timeout = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                var result = search.FindFirst();
                if (result != null)
                {
                    Log($"WaitFor({search}) found '{result.name}'");
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            LogDebug($"WaitFor({search}) timed out after {timeout}s");
            return false;
        }

        /// <summary>
        /// Waits until an element's text matches the expected value.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="expectedText">Expected text content</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <returns>True if text matched, false if timeout</returns>
        public static async UniTask<bool> WaitFor(Search search, string expectedText, float timeout = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                var go = search.FindFirst();
                if (go != null)
                {
                    string actual = null;
                    var tmp = go.GetComponent<TMPro.TMP_Text>();
                    if (tmp != null)
                        actual = tmp.text;
                    else
                    {
                        var legacy = go.GetComponent<UnityEngine.UI.Text>();
                        if (legacy != null)
                            actual = legacy.text;
                    }

                    if (actual == expectedText)
                    {
                        Log($"WaitFor({search}, \"{expectedText}\") satisfied");
                        return true;
                    }
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Waits until a toggle is in the expected state.
        /// </summary>
        /// <param name="search">The search query to find the toggle</param>
        /// <param name="expectedOn">Expected isOn state</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <returns>True if state matched, false if timeout</returns>
        public static async UniTask<bool> WaitFor(Search search, bool expectedOn, float timeout = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                var go = search.FindFirst();
                if (go != null)
                {
                    var toggle = go.GetComponent<UnityEngine.UI.Toggle>();
                    if (toggle != null && toggle.isOn == expectedOn)
                    {
                        Log($"WaitFor({search}, {expectedOn}) satisfied");
                        return true;
                    }
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Waits until no element matching the search exists.
        /// </summary>
        /// <param name="search">The search query</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <returns>True if element gone, false if timeout</returns>
        public static async UniTask<bool> WaitForNot(Search search, float timeout = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                var result = search.FindFirst();
                if (result == null)
                {
                    Log($"WaitForNot({search}) satisfied");
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            LogDebug($"WaitForNot({search}) timed out after {timeout}s");
            return false;
        }

        /// <summary>
        /// Waits until a static path resolves to a truthy value.
        /// </summary>
        /// <param name="path">Dot-separated path to the value (e.g., "GameManager.Instance.IsReady")</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <returns>True if truthy, false if timeout</returns>
        public static async UniTask<bool> WaitFor(string path, float timeout = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                try
                {
                    var value = ResolveStaticPath(path);
                    if (IsTruthy(value))
                    {
                        Log($"WaitFor({path}) satisfied");
                        return true;
                    }
                }
                catch
                {
                    // Path might not be resolvable yet, keep trying
                }

                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Waits until a static path equals an expected value.
        /// </summary>
        /// <typeparam name="T">Type of the value</typeparam>
        /// <param name="path">Dot-separated path to the value</param>
        /// <param name="expected">Expected value to wait for</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <returns>True if value matched, false if timeout</returns>
        public static async UniTask<bool> WaitFor<T>(string path, T expected, float timeout = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                try
                {
                    var value = ResolveStaticPath(path);
                    if (value is T typedValue && Equals(typedValue, expected))
                    {
                        Log($"WaitFor({path}, {expected}) satisfied");
                        return true;
                    }
                    // Try conversion
                    if (value != null)
                    {
                        try
                        {
                            var converted = (T)Convert.ChangeType(value, typeof(T));
                            if (Equals(converted, expected))
                            {
                                Log($"WaitFor({path}, {expected}) satisfied");
                                return true;
                            }
                        }
                        catch { }
                    }
                }
                catch
                {
                    // Path might not be resolvable yet, keep trying
                }

                await UniTask.Delay(100, true);
            }

            LogDebug($"WaitFor({path}, {expected}) timed out after {timeout}s");
            return false;
        }

        /// <summary>
        /// Resolves a dot-separated path to a value using reflection.
        /// Searches all loaded assemblies for matching types.
        /// Public entry point for Search.Static().
        /// </summary>
        public static object ResolveStaticPathPublic(string path) => ResolveStaticPath(path);

        private static object ResolveStaticPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or empty", nameof(path));

            var parts = path.Split('.');
            if (parts.Length < 2)
                throw new ArgumentException($"Path must have at least type and member: {path}", nameof(path));

            // Try progressively longer type names to find the base type
            Type type = null;
            int memberStartIndex = 0;

            for (int i = 1; i <= parts.Length; i++)
            {
                var typeName = string.Join(".", parts, 0, i);
                type = FindTypeInAllAssemblies(typeName);
                if (type != null)
                {
                    memberStartIndex = i;
                    break;
                }
            }

            if (type == null)
            {
                // Try to find types ending with the first part of the path
                var firstPart = parts[0];
                var matches = FindTypesEndingWith(firstPart);

                if (matches.Count == 1)
                {
                    // Exactly one match - use it automatically
                    type = matches[0];
                    memberStartIndex = 1;
                }
                else
                {
                    var hint = matches.Count > 1
                        ? $" Multiple types found: {string.Join(", ", matches.Select(t => t.FullName).Take(5))}. Use full namespace to disambiguate."
                        : " Make sure to include the full namespace (e.g., 'MyNamespace.MyClass.Property').";
                    throw new InvalidOperationException($"Could not find type '{firstPart}' in path: {path}.{hint}");
                }
            }

            if (memberStartIndex >= parts.Length)
                throw new InvalidOperationException($"Path '{path}' resolves to a type with no members specified");

            // Navigate through members
            object current = null;
            Type currentType = type;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

            for (int i = memberStartIndex; i < parts.Length; i++)
            {
                var memberName = parts[i];

                // Try property first
                var prop = currentType.GetProperty(memberName, flags);
                if (prop != null)
                {
                    current = prop.GetValue(current);
                    if (current == null && i < parts.Length - 1)
                        throw new InvalidOperationException($"Null reference at '{memberName}' in path: {path}");
                    currentType = current?.GetType() ?? prop.PropertyType;
                    continue;
                }

                // Try field
                var field = currentType.GetField(memberName, flags);
                if (field != null)
                {
                    current = field.GetValue(current);
                    if (current == null && i < parts.Length - 1)
                        throw new InvalidOperationException($"Null reference at '{memberName}' in path: {path}");
                    currentType = current?.GetType() ?? field.FieldType;
                    continue;
                }

                throw new InvalidOperationException($"Member '{memberName}' not found on type '{currentType.Name}' in path: {path}");
            }

            return current;
        }

        // Cache for type lookups - maps short name to resolved Type
        private static readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();

        /// <summary>
        /// Searches all loaded assemblies for a type by name. Results are cached.
        /// </summary>
        private static Type FindTypeInAllAssemblies(string typeName)
        {
            if (_typeCache.TryGetValue(typeName, out var cached))
                return cached;

            // Check common Unity types first for performance
            Type result = typeName switch
            {
                "GameObject" => typeof(GameObject),
                "Debug" => typeof(Debug),
                "PlayerPrefs" => typeof(PlayerPrefs),
                "Time" => typeof(Time),
                "Application" => typeof(Application),
                "Screen" => typeof(Screen),
                "Input" => typeof(Input),
                "Resources" => typeof(Resources),
                _ => null
            };

            if (result == null)
            {
                // Search all loaded assemblies
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        result = assembly.GetType(typeName);
                        if (result != null)
                            break;
                    }
                    catch
                    {
                        // Some assemblies may throw on GetType, skip them
                    }
                }
            }

            if (result != null)
                _typeCache[typeName] = result;

            return result;
        }

        /// <summary>
        /// Finds all types whose name ends with the given suffix (e.g., "TestTrucks" finds "MyNamespace.TestTrucks").
        /// Results are cached if exactly one match is found.
        /// </summary>
        private static List<Type> FindTypesEndingWith(string typeName)
        {
            // Check cache first - if we have an exact match, return it
            if (_typeCache.TryGetValue(typeName, out var cached))
                return new List<Type> { cached };

            var matches = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.Name == typeName)
                        {
                            matches.Add(type);
                        }
                    }
                }
                catch
                {
                    // Some assemblies may throw, skip them
                }
            }

            // Cache if exactly one match
            if (matches.Count == 1)
                _typeCache[typeName] = matches[0];

            return matches;
        }

        /// <summary>
        /// Determines if a value is "truthy" (not null, not zero, not false, not empty string).
        /// </summary>
        private static bool IsTruthy(object value)
        {
            if (value == null)
                return false;
            if (value is bool b)
                return b;
            if (value is int i)
                return i != 0;
            if (value is float f)
                return f != 0f;
            if (value is double d)
                return d != 0.0;
            if (value is string s)
                return !string.IsNullOrEmpty(s);
            if (value is long l)
                return l != 0;
            return true; // Non-null object is truthy
        }

        #endregion
    }
}
