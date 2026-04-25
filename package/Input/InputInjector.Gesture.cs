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
        /// Performs a two-finger rotation gesture on an element or screen center.
        /// </summary>
        public static async Task Rotate(GameObject element, float degrees, float duration = 0.5f, float fingerDistance = 0.05f)
        {
            Vector2 centerPos = element != null
                ? GetScreenPosition(element)
                : new Vector2(Screen.width / 2f, Screen.height / 2f);

            await InjectRotate(centerPos, degrees, duration, fingerDistance);
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
                UnityEngine.Profiling.Profiler.BeginSample("InputInjector.TwoFingerDrag.Frame");
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
                UnityEngine.Profiling.Profiler.EndSample();
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
