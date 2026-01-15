using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ODDGames.UITest
{
    /// <summary>
    /// Shared action execution logic used by UITestBehaviour, VisualTestRunner, and AIActionExecutor.
    /// All methods take resolved GameObjects (no searching) and execute the action via InputInjector.
    /// This ensures consistent behavior across all test execution paths.
    /// </summary>
    public static class ActionExecutor
    {
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
                    Debug.Log($"[ActionExecutor] Click({search}) -> '{target.name}' at ({screenPos.x:F0}, {screenPos.y:F0})");
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
            Debug.Log($"[ActionExecutor] Click at ({screenPos.x:F0}, {screenPos.y:F0}) on '{target.name}'");
            await InputInjector.InjectPointerTap(screenPos);
        }

        /// <summary>
        /// Clicks at a specific screen position.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to click</param>
        public static async UniTask ClickAtAsync(Vector2 screenPosition)
        {
            Debug.Log($"[ActionExecutor] Click at ({screenPosition.x:F0}, {screenPosition.y:F0})");
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
                    Debug.Log($"[ActionExecutor] DoubleClick({search}) -> '{target.name}' at ({screenPos.x:F0}, {screenPos.y:F0})");
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
            Debug.Log($"[ActionExecutor] DoubleClick at ({screenPos.x:F0}, {screenPos.y:F0}) on '{target.name}'");
            await InputInjector.InjectPointerDoubleTap(screenPos);
        }

        /// <summary>
        /// Double-clicks at a specific screen position.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to double-click</param>
        public static async UniTask DoubleClickAtAsync(Vector2 screenPosition)
        {
            Debug.Log($"[ActionExecutor] DoubleClick at ({screenPosition.x:F0}, {screenPosition.y:F0})");
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
                    Debug.Log($"[ActionExecutor] TripleClick({search}) -> '{target.name}' at ({screenPos.x:F0}, {screenPos.y:F0})");
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
            Debug.Log($"[ActionExecutor] TripleClick at ({screenPos.x:F0}, {screenPos.y:F0}) on '{target.name}'");
            await InputInjector.InjectPointerTripleTap(screenPos);
        }

        /// <summary>
        /// Triple-clicks at a specific screen position.
        /// Performs three rapid clicks in succession.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to triple-click</param>
        public static async UniTask TripleClickAtAsync(Vector2 screenPosition)
        {
            Debug.Log($"[ActionExecutor] TripleClick at ({screenPosition.x:F0}, {screenPosition.y:F0})");
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
                    Debug.Log($"[ActionExecutor] Hold({search}) -> '{target.name}' at ({screenPos.x:F0}, {screenPos.y:F0}) for {seconds}s");
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
            Debug.Log($"[ActionExecutor] Hold at ({screenPos.x:F0}, {screenPos.y:F0}) on '{target.name}' for {seconds}s");
            await InputInjector.InjectPointerHold(screenPos, seconds);
        }

        /// <summary>
        /// Holds/long-presses at a specific screen position.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to hold</param>
        /// <param name="seconds">Duration of the hold in seconds</param>
        public static async UniTask HoldAtAsync(Vector2 screenPosition, float seconds)
        {
            Debug.Log($"[ActionExecutor] Hold at ({screenPosition.x:F0}, {screenPosition.y:F0}) for {seconds}s");
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
                    Debug.Log($"[ActionExecutor] Type({search}) -> '{target.name}' text=\"{text}\" (clear={clearFirst}, enter={pressEnter})");
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

            Debug.Log($"[ActionExecutor] Type \"{text}\" into '{inputField.name}' (clear={clearFirst}, enter={pressEnter})");
            await InputInjector.TypeIntoField(inputField, text, clearFirst, pressEnter);
        }

        /// <summary>
        /// Types text without targeting a specific input field (assumes something is focused).
        /// </summary>
        /// <param name="text">The text to type</param>
        public static async UniTask TypeTextAsync(string text)
        {
            Debug.Log($"[ActionExecutor] TypeText \"{text}\"");
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
        public static async UniTask<bool> DragAsync(Search search, Vector2 direction, float duration = 0.5f, float searchTime = 10f, int index = 0)
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
                    Debug.Log($"[ActionExecutor] Drag({search}) -> '{target.name}' from ({screenPos.x:F0}, {screenPos.y:F0}) by ({direction.x:F0}, {direction.y:F0}) over {duration}s");
                    await InputInjector.InjectPointerDrag(screenPos, endPos, duration);
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
        /// <returns>True if both elements were found and drag completed, false otherwise</returns>
        public static async UniTask<bool> DragToAsync(Search fromSearch, Search toSearch, float duration = 0.5f, float searchTime = 10f)
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
                    Debug.Log($"[ActionExecutor] DragTo({fromSearch}) -> '{fromTarget.name}' to ({toSearch}) -> '{toTarget.name}' over {duration}s");
                    await InputInjector.InjectPointerDrag(fromPos, toPos, duration);
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
        public static async UniTask DragAsync(GameObject source, Vector2 direction, float duration = 0.5f)
        {
            if (source == null)
                throw new System.ArgumentNullException(nameof(source), "Drag source cannot be null");

            var startPos = InputInjector.GetScreenPosition(source);
            var endPos = startPos + direction;
            Debug.Log($"[ActionExecutor] Drag from ({startPos.x:F0}, {startPos.y:F0}) by ({direction.x:F0}, {direction.y:F0}) over {duration}s");
            await InputInjector.InjectPointerDrag(startPos, endPos, duration);
        }

        /// <summary>
        /// Drags from a source GameObject in a named direction.
        /// </summary>
        /// <param name="source">The GameObject to drag from</param>
        /// <param name="direction">Direction name: "up", "down", "left", "right"</param>
        /// <param name="normalizedDistance">Distance as fraction of screen height (0-1)</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        public static async UniTask DragAsync(GameObject source, string direction, float normalizedDistance = 0.2f, float duration = 0.5f)
        {
            if (source == null)
                throw new System.ArgumentNullException(nameof(source), "Drag source cannot be null");

            var offset = InputInjector.GetDirectionOffset(direction, normalizedDistance);
            await DragAsync(source, offset, duration);
        }

        /// <summary>
        /// Drags from a source GameObject to a target GameObject.
        /// </summary>
        /// <param name="source">The GameObject to drag from</param>
        /// <param name="target">The GameObject to drag to</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        public static async UniTask DragToAsync(GameObject source, GameObject target, float duration = 0.5f)
        {
            if (source == null)
                throw new System.ArgumentNullException(nameof(source), "Drag source cannot be null");
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Drag target cannot be null");

            var startPos = InputInjector.GetScreenPosition(source);
            var endPos = InputInjector.GetScreenPosition(target);
            Debug.Log($"[ActionExecutor] Drag from '{source.name}' to '{target.name}' over {duration}s");
            await InputInjector.InjectPointerDrag(startPos, endPos, duration);
        }

        /// <summary>
        /// Drags from a source GameObject to a target screen position.
        /// </summary>
        /// <param name="source">The GameObject to drag from</param>
        /// <param name="targetPosition">The screen position to drag to</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        public static async UniTask DragToAsync(GameObject source, Vector2 targetPosition, float duration = 0.5f)
        {
            if (source == null)
                throw new System.ArgumentNullException(nameof(source), "Drag source cannot be null");

            var startPos = InputInjector.GetScreenPosition(source);
            Debug.Log($"[ActionExecutor] Drag from '{source.name}' to ({targetPosition.x:F0}, {targetPosition.y:F0}) over {duration}s");
            await InputInjector.InjectPointerDrag(startPos, targetPosition, duration);
        }

        /// <summary>
        /// Drags between two screen positions.
        /// </summary>
        /// <param name="startPosition">Start screen position</param>
        /// <param name="endPosition">End screen position</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        public static async UniTask DragFromToAsync(Vector2 startPosition, Vector2 endPosition, float duration = 0.5f)
        {
            Debug.Log($"[ActionExecutor] Drag from ({startPosition.x:F0}, {startPosition.y:F0}) to ({endPosition.x:F0}, {endPosition.y:F0}) over {duration}s");
            await InputInjector.InjectPointerDrag(startPosition, endPosition, duration);
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
            Debug.Log($"[ActionExecutor] Scroll on '{target.name}' delta={delta}");
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

            Debug.Log($"[ActionExecutor] Scroll '{target.name}' {direction} amount={amount}");
            await InputInjector.ScrollElement(target, direction, amount);
        }

        /// <summary>
        /// Scrolls at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position to scroll at</param>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        public static async UniTask ScrollAtAsync(Vector2 position, float delta)
        {
            Debug.Log($"[ActionExecutor] Scroll at ({position.x:F0}, {position.y:F0}) delta={delta}");
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
                    Debug.Log($"[ActionExecutor] Scroll({search}) -> '{target.name}' at ({screenPos.x:F0}, {screenPos.y:F0}) delta={delta}");
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
                    Debug.Log($"[ActionExecutor] Scroll({search}) -> '{target.name}' {direction} amount={amount}");
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

            Debug.Log($"[ActionExecutor] SetSlider '{slider.name}' to {normalizedValue:F2}");
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

            Debug.Log($"[ActionExecutor] SetScrollbar '{scrollbar.name}' to {normalizedValue:F2}");
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
            Debug.Log($"[ActionExecutor] ClickSlider '{slider.name}' at {normalizedValue:F2} position ({clickPos.x:F0}, {clickPos.y:F0})");
            await InputInjector.InjectPointerTap(clickPos);
        }

        /// <summary>
        /// Drags a slider from one value to another.
        /// </summary>
        /// <param name="slider">The Slider component</param>
        /// <param name="fromValue">Starting value (0-1)</param>
        /// <param name="toValue">Ending value (0-1)</param>
        /// <param name="duration">Duration of the drag</param>
        public static async UniTask DragSliderAsync(Slider slider, float fromValue, float toValue, float duration = 0.3f)
        {
            if (slider == null)
                throw new System.ArgumentNullException(nameof(slider), "Slider cannot be null");

            var startPos = InputInjector.GetSliderClickPosition(slider, fromValue);
            var endPos = InputInjector.GetSliderClickPosition(slider, toValue);
            Debug.Log($"[ActionExecutor] DragSlider '{slider.name}' from {fromValue:F2} to {toValue:F2}");
            await InputInjector.InjectPointerDrag(startPos, endPos, duration);
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
                        Debug.Log($"[ActionExecutor] ClickSlider({search}) -> '{go.name}' value={normalizedValue:F2}");
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
        public static async UniTask<bool> DragSliderAsync(Search search, float fromValue, float toValue, float duration = 0.3f, float searchTime = 10f)
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
                        Debug.Log($"[ActionExecutor] DragSlider({search}) -> '{go.name}' from {fromValue:F2} to {toValue:F2}");
                        await DragSliderAsync(slider, fromValue, toValue, duration);
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
                        Debug.Log($"[ActionExecutor] SetSlider({search}) -> '{go.name}' value={normalizedValue:F2}");
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
                        Debug.Log($"[ActionExecutor] SetScrollbar({search}) -> '{go.name}' value={normalizedValue:F2}");
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
        public static async UniTask SwipeAsync(GameObject target, string direction, float normalizedDistance = 0.2f, float duration = 0.3f)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Swipe target cannot be null");

            Debug.Log($"[ActionExecutor] Swipe on '{target.name}' {direction} distance={normalizedDistance} duration={duration}s");
            await InputInjector.Swipe(target, direction, normalizedDistance, duration);
        }

        /// <summary>
        /// Performs a swipe gesture at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position to start the swipe</param>
        /// <param name="direction">Direction: "up", "down", "left", "right"</param>
        /// <param name="normalizedDistance">Distance as fraction of screen (0-1)</param>
        /// <param name="duration">Duration of the swipe</param>
        public static async UniTask SwipeAtAsync(Vector2 position, string direction, float normalizedDistance = 0.2f, float duration = 0.3f)
        {
            var offset = InputInjector.GetDirectionOffset(direction, normalizedDistance);
            var endPos = position + offset;
            Debug.Log($"[ActionExecutor] Swipe at ({position.x:F0}, {position.y:F0}) {direction}");
            await InputInjector.InjectMouseDrag(position, endPos, duration);
        }

        /// <summary>
        /// Performs a pinch gesture on a target GameObject.
        /// </summary>
        /// <param name="target">The GameObject to pinch on</param>
        /// <param name="scale">Scale factor (less than 1 = zoom out, greater than 1 = zoom in)</param>
        /// <param name="duration">Duration of the pinch</param>
        public static async UniTask PinchAsync(GameObject target, float scale, float duration = 0.5f)
        {
            Debug.Log($"[ActionExecutor] Pinch on '{target?.name ?? "screen center"}' scale={scale} duration={duration}s");
            await InputInjector.Pinch(target, scale, duration);
        }

        /// <summary>
        /// Performs a pinch gesture at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position for the pinch center</param>
        /// <param name="scale">Scale factor</param>
        /// <param name="duration">Duration of the pinch</param>
        public static async UniTask PinchAtAsync(Vector2 position, float scale, float duration = 0.5f)
        {
            Debug.Log($"[ActionExecutor] Pinch at ({position.x:F0}, {position.y:F0}) scale={scale} duration={duration}s");
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
            Debug.Log($"[ActionExecutor] Pinch at ({position.x:F0}, {position.y:F0}) scale={scale} duration={duration}s fingerDistance={fingerDistancePixels}px");
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
        public static async UniTask TwoFingerSwipeAsync(GameObject target, string direction, float normalizedDistance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {
            Debug.Log($"[ActionExecutor] TwoFingerSwipe on '{target?.name ?? "screen center"}' {direction}");
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
        public static async UniTask TwoFingerSwipeAtAsync(Vector2 position, string direction, float normalizedDistance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {
            Debug.Log($"[ActionExecutor] TwoFingerSwipe at ({position.x:F0}, {position.y:F0}) {direction}");
            await InputInjector.InjectTwoFingerSwipe(position, direction, normalizedDistance, duration, fingerSpacing);
        }

        /// <summary>
        /// Performs a rotation gesture on a target GameObject.
        /// </summary>
        /// <param name="target">The GameObject to rotate on</param>
        /// <param name="degrees">Rotation angle (positive = clockwise, negative = counter-clockwise)</param>
        /// <param name="duration">Duration of the rotation</param>
        /// <param name="fingerDistance">Normalized distance from center for fingers (0-1)</param>
        public static async UniTask RotateAsync(GameObject target, float degrees, float duration = 0.5f, float fingerDistance = 0.05f)
        {
            Debug.Log($"[ActionExecutor] Rotate on '{target?.name ?? "screen center"}' degrees={degrees} duration={duration}s");
            await InputInjector.Rotate(target, degrees, duration, fingerDistance);
        }

        /// <summary>
        /// Performs a rotation gesture at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position for the rotation center</param>
        /// <param name="degrees">Rotation angle</param>
        /// <param name="duration">Duration of the rotation</param>
        /// <param name="fingerDistance">Normalized distance from center for fingers (0-1)</param>
        public static async UniTask RotateAtAsync(Vector2 position, float degrees, float duration = 0.5f, float fingerDistance = 0.05f)
        {
            Debug.Log($"[ActionExecutor] Rotate at ({position.x:F0}, {position.y:F0}) degrees={degrees} duration={duration}s");
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
            Debug.Log($"[ActionExecutor] Rotate at ({position.x:F0}, {position.y:F0}) degrees={degrees} duration={duration}s radius={radiusPixels}px");
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
            Debug.Log($"[ActionExecutor] PressKey {key}");
            await InputInjector.PressKey(key);
        }

        /// <summary>
        /// Holds a keyboard key for a duration.
        /// </summary>
        /// <param name="key">The key to hold</param>
        /// <param name="duration">How long to hold in seconds</param>
        public static async UniTask HoldKeyAsync(Key key, float duration)
        {
            Debug.Log($"[ActionExecutor] HoldKey {key} for {duration}s");
            await InputInjector.HoldKey(key, duration);
        }

        /// <summary>
        /// Holds multiple keyboard keys simultaneously.
        /// </summary>
        /// <param name="keys">The keys to hold</param>
        /// <param name="duration">How long to hold in seconds</param>
        public static async UniTask HoldKeysAsync(Key[] keys, float duration)
        {
            Debug.Log($"[ActionExecutor] HoldKeys [{string.Join(", ", keys)}] for {duration}s");
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
                        Debug.Log($"[ActionExecutor] ClickDropdown({search}) -> '{go.name}' index={optionIndex}");
                        await ClickDropdownItemAsync(legacyDropdown.gameObject, legacyDropdown.template, optionIndex);
                        return true;
                    }

                    // Try TMP_Dropdown
                    var tmpDropdown = go.GetComponent<TMPro.TMP_Dropdown>();
                    if (tmpDropdown != null)
                    {
                        Debug.Log($"[ActionExecutor] ClickDropdown({search}) -> '{go.name}' index={optionIndex}");
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
                            Debug.Log($"[ActionExecutor] ClickDropdown({search}) -> '{go.name}' label=\"{optionLabel}\" (index={optionIndex})");
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
                            Debug.Log($"[ActionExecutor] ClickDropdown({search}) -> '{go.name}' label=\"{optionLabel}\" (index={optionIndex})");
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
                    Debug.Log($"[ActionExecutor] ClickDropdown selecting option {optionIndex}: '{targetToggle.name}'");
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
                    Debug.Log($"[ActionExecutor] ClickAny({search}) -> '{target.name}' at ({screenPos.x:F0}, {screenPos.y:F0}) (from {results.Count} matches)");
                    await ClickAsync(target);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        #endregion
    }
}
