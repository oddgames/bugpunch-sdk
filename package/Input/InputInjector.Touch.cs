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
    internal static partial class InputInjector
    {
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
        /// Injects a double-tap touch gesture.
        /// </summary>
        public static async Task InjectTouchDoubleTap(Vector2 screenPosition)
        {
            await InjectTouchTap(screenPosition);
            await Async.Delay(2, 0.05f);
            await InjectTouchTap(screenPosition);
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
    }
}
