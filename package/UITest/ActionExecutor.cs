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
    }
}
