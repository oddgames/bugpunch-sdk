using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

using TMPro;
using UnityEngine;
using Debug = UnityEngine.Debug;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ODDGames.Bugpunch
{
    public static partial class ActionExecutor
    {
        #region Gesture Actions

        /// <summary>
        /// Performs a swipe gesture at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position to start the swipe</param>
        /// <param name="direction">Direction: "up", "down", "left", "right"</param>
        /// <param name="normalizedDistance">Distance as fraction of screen (0-1)</param>
        /// <param name="duration">Duration of the swipe</param>
        /// <summary>
        /// Internal swipe implementation - no logging as callers handle that.
        /// </summary>
        private static async Task SwipeAtInternal(Vector2 position, string direction, float normalizedDistance, float duration)
        {
            var offset = InputInjector.GetDirectionOffset(direction, normalizedDistance);
            var endPos = position + offset;
            // Swipes should have no hold time - they're quick drag motions
            // Use InjectPointerDrag which handles focus and platform detection
            await InputInjector.InjectPointerDrag(position, endPos, duration, holdTime: 0f);
        }

        /// <summary>
        /// Performs a swipe gesture at a specific screen position (pixels).
        /// </summary>
        public static async Task SwipeAt(Vector2 position, string direction, float normalizedDistance = 0.2f, float duration = 0.15f)
        {
            ValidateNormalized(normalizedDistance, nameof(normalizedDistance));
            await using (await RunAction($"SwipeAt(({position.x:F0},{position.y:F0}), {direction})"))
            {
                await SwipeAtInternal(position, direction, normalizedDistance, duration);
            }
        }

        /// <summary>
        /// Performs a pinch gesture at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position for the pinch center</param>
        /// <param name="scale">Scale factor</param>
        /// <param name="duration">Duration of the pinch</param>
        public static async Task PinchAt(Vector2 position, float scale, float duration = 0.15f)
        {
            await using (await RunAction($"PinchAt(({position.x:F0},{position.y:F0}), scale={scale})"))
            {
                await InputInjector.InjectPinch(position, scale, duration);
            }
        }

        /// <summary>
        /// Performs a pinch gesture at a specific screen position with custom finger distance.
        /// </summary>
        /// <param name="position">Screen position for the pinch center</param>
        /// <param name="scale">Scale factor</param>
        /// <param name="duration">Duration of the pinch</param>
        /// <param name="fingerDistancePixels">Initial distance of each finger from center in pixels</param>
        public static async Task PinchAt(Vector2 position, float scale, float duration, float fingerDistancePixels)
        {
            await using (await RunAction($"PinchAt(({position.x:F0},{position.y:F0}), scale={scale}, fingerDistance={fingerDistancePixels}px)"))
            {
                await InputInjector.InjectPinch(position, scale, duration, fingerDistancePixels);
            }
        }

        /// <summary>
        /// Performs a pinch gesture at the screen center.
        /// </summary>
        /// <param name="scale">Scale factor (less than 1 = zoom out, greater than 1 = zoom in)</param>
        /// <param name="duration">Duration of the pinch</param>
        public static async Task Pinch(float scale, float duration = 0.15f)
        {
            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await using (await RunAction($"Pinch(scale={scale}) at center"))
            {
                await InputInjector.InjectPinch(center, scale, duration);
            }
        }

        /// <summary>
        /// Performs a pinch gesture at a screen position specified by percentage.
        /// </summary>
        /// <param name="xPercent">Horizontal position as percentage (0-1)</param>
        /// <param name="yPercent">Vertical position as percentage (0-1)</param>
        /// <param name="scale">Scale factor</param>
        /// <param name="duration">Duration of the pinch</param>
        public static async Task PinchAt(float xPercent, float yPercent, float scale, float duration = 0.15f)
        {
            ValidateNormalized(xPercent, nameof(xPercent));
            ValidateNormalized(yPercent, nameof(yPercent));
            var position = new Vector2(Screen.width * xPercent, Screen.height * yPercent);
            await PinchAt(position, scale, duration);
        }

        /// <summary>
        /// Performs a two-finger swipe gesture at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position for the swipe center</param>
        /// <param name="direction">Direction: "up", "down", "left", "right"</param>
        /// <param name="normalizedDistance">Distance as fraction of screen (0-1)</param>
        /// <param name="duration">Duration of the swipe</param>
        /// <param name="fingerSpacing">Normalized spacing between fingers (0-1)</param>
        public static async Task TwoFingerSwipeAt(Vector2 position, string direction, float normalizedDistance = 0.2f, float duration = 0.15f, float fingerSpacing = 0.03f)
        {
            ValidateNormalized(normalizedDistance, nameof(normalizedDistance));
            ValidateNormalized(fingerSpacing, nameof(fingerSpacing));
            await using (await RunAction($"TwoFingerSwipeAt(({position.x:F0},{position.y:F0}), {direction})"))
            {
                await InputInjector.InjectTwoFingerSwipe(position, direction, normalizedDistance, duration, fingerSpacing);
            }
        }

        /// <summary>
        /// Performs a two-finger swipe gesture at the screen center.
        /// </summary>
        /// <param name="direction">Swipe direction</param>
        /// <param name="normalizedDistance">Distance as fraction of screen (0-1)</param>
        /// <param name="duration">Duration of the swipe</param>
        /// <param name="fingerSpacing">Normalized spacing between fingers (0-1)</param>
        public static async Task TwoFingerSwipe(SwipeDirection direction, float normalizedDistance = 0.2f, float duration = 0.15f, float fingerSpacing = 0.03f)
        {
            ValidateNormalized(normalizedDistance, nameof(normalizedDistance));
            ValidateNormalized(fingerSpacing, nameof(fingerSpacing));
            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await using (await RunAction($"TwoFingerSwipe({direction}) at center"))
            {
                await InputInjector.InjectTwoFingerSwipe(center, direction.ToString().ToLower(), normalizedDistance, duration, fingerSpacing);
            }
        }

        /// <summary>
        /// Performs a two-finger swipe gesture at a screen position specified by percentage.
        /// </summary>
        /// <param name="xPercent">Horizontal position as percentage (0-1)</param>
        /// <param name="yPercent">Vertical position as percentage (0-1)</param>
        /// <param name="direction">Swipe direction</param>
        /// <param name="normalizedDistance">Distance as fraction of screen (0-1)</param>
        /// <param name="duration">Duration of the swipe</param>
        /// <param name="fingerSpacing">Normalized spacing between fingers (0-1)</param>
        public static async Task TwoFingerSwipeAt(float xPercent, float yPercent, SwipeDirection direction, float normalizedDistance = 0.2f, float duration = 0.15f, float fingerSpacing = 0.03f)
        {
            ValidateNormalized(xPercent, nameof(xPercent));
            ValidateNormalized(yPercent, nameof(yPercent));
            ValidateNormalized(normalizedDistance, nameof(normalizedDistance));
            ValidateNormalized(fingerSpacing, nameof(fingerSpacing));
            var position = new Vector2(Screen.width * xPercent, Screen.height * yPercent);
            await TwoFingerSwipeAt(position, direction.ToString().ToLower(), normalizedDistance, duration, fingerSpacing);
        }

        /// <summary>
        /// Performs a rotation gesture at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position for the rotation center</param>
        /// <param name="degrees">Rotation angle</param>
        /// <param name="duration">Duration of the rotation</param>
        /// <param name="fingerDistance">Normalized distance from center for fingers (0-1)</param>
        public static async Task RotateAt(Vector2 position, float degrees, float duration = 0.15f, float fingerDistance = 0.05f)
        {
            ValidateNormalized(fingerDistance, nameof(fingerDistance));
            await using (await RunAction($"RotateAt(({position.x:F0},{position.y:F0}), {degrees}°, {duration}s)"))
            {
                await InputInjector.InjectRotate(position, degrees, duration, fingerDistance);
            }
        }

        /// <summary>
        /// Performs a rotation gesture at a screen position specified by percentage.
        /// </summary>
        /// <param name="xPercent">Horizontal position as percentage (0-1)</param>
        /// <param name="yPercent">Vertical position as percentage (0-1)</param>
        /// <param name="degrees">Rotation angle</param>
        /// <param name="duration">Duration of the rotation</param>
        /// <param name="fingerDistance">Normalized distance from center for fingers (0-1)</param>
        public static async Task RotateAt(float xPercent, float yPercent, float degrees, float duration = 0.15f, float fingerDistance = 0.05f)
        {
            ValidateNormalized(xPercent, nameof(xPercent));
            ValidateNormalized(yPercent, nameof(yPercent));
            ValidateNormalized(fingerDistance, nameof(fingerDistance));
            var position = new Vector2(Screen.width * xPercent, Screen.height * yPercent);
            await RotateAt(position, degrees, duration, fingerDistance);
        }

        /// <summary>
        /// Performs a rotation gesture at a specific screen position with pixel-based radius.
        /// </summary>
        /// <param name="position">Screen position for the rotation center</param>
        /// <param name="degrees">Rotation angle</param>
        /// <param name="duration">Duration of the rotation</param>
        /// <param name="radiusPixels">Distance from center in pixels for finger positions</param>
        public static async Task RotateAtPixels(Vector2 position, float degrees, float duration, float radiusPixels)
        {
            await using (await RunAction($"RotateAtPixels(({position.x:F0},{position.y:F0}), {degrees}°, radius={radiusPixels}px)"))
            {
                await InputInjector.InjectRotatePixels(position, degrees, duration, radiusPixels);
            }
        }

        /// <summary>
        /// Performs a rotation gesture at the screen center.
        /// </summary>
        /// <param name="degrees">Rotation angle (positive = clockwise, negative = counter-clockwise)</param>
        /// <param name="duration">Duration of the rotation</param>
        /// <param name="fingerDistance">Normalized distance from center for fingers (0-1)</param>
        public static async Task Rotate(float degrees, float duration = 0.15f, float fingerDistance = 0.05f)
        {
            ValidateNormalized(fingerDistance, nameof(fingerDistance));
            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await using (await RunAction($"Rotate({degrees}°) at center"))
            {
                await InputInjector.InjectRotate(center, degrees, duration, fingerDistance);
            }
        }

        #endregion

        #region Swipe with Direction Enum

        /// <summary>
        /// Performs a swipe gesture on an element.
        /// </summary>
        public static async Task Swipe(Search search, SwipeDirection direction, float distance = 0.2f, float duration = 0.15f, bool throwIfMissing = true, float searchTime = 10)
        {
            await using var action = await RunAction($"Swipe({search}, {direction})");

            var element = await Find<RectTransform>(search, throwIfMissing, searchTime);
            if (element == null)
            {
                action.Warn("Element not found");
                return;
            }

            // Capture name before any async operations - element may be destroyed
            var elementName = element.name;
            await InputInjector.Swipe(element.gameObject, direction.ToString().ToLower(), distance, duration);
            action.SetResult($"'{elementName}'");
        }

        /// <summary>
        /// Performs a swipe gesture at screen center.
        /// </summary>
        public static async Task Swipe(SwipeDirection direction, float distance = 0.2f, float duration = 0.15f)
        {
            await using (await RunAction($"Swipe({direction}) at center"))
            {
                var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
                await SwipeAtInternal(center, direction.ToString().ToLower(), distance, duration);
            }
        }

        /// <summary>
        /// Performs a swipe gesture at a specific screen position.
        /// </summary>
        public static async Task SwipeAt(float xPercent, float yPercent, SwipeDirection direction, float distance = 0.2f, float duration = 0.15f)
        {
            ValidateNormalized(xPercent, nameof(xPercent));
            ValidateNormalized(yPercent, nameof(yPercent));
            ValidateNormalized(distance, nameof(distance));
            await using (await RunAction($"SwipeAt({xPercent:P0}, {yPercent:P0}, {direction})"))
            {
                var startPos = new Vector2(Screen.width * xPercent, Screen.height * yPercent);
                await SwipeAtInternal(startPos, direction.ToString().ToLower(), distance, duration);
            }
        }

        /// <summary>
        /// Performs a pinch gesture on an element.
        /// </summary>
        public static async Task Pinch(Search search, float scale, float duration = 0.15f, bool throwIfMissing = true, float searchTime = 10)
        {
            await using var action = await RunAction($"Pinch({search}, scale={scale})");

            var element = await Find<RectTransform>(search, throwIfMissing, searchTime);
            if (element == null)
            {
                action.Warn("Element not found");
                return;
            }

            // Capture name before any async operations - element may be destroyed
            var elementName = element.name;
            var screenPos = InputInjector.GetScreenPosition(element.gameObject);
            await InputInjector.InjectPinch(screenPos, scale, duration);
            action.SetResult($"'{elementName}'");
        }

        /// <summary>
        /// Performs a two-finger swipe gesture on an element.
        /// </summary>
        public static async Task TwoFingerSwipe(Search search, string direction, float distance = 0.2f, float duration = 0.15f, float fingerSpacing = 0.03f, bool throwIfMissing = true, float searchTime = 10)
        {
            ValidateNormalized(distance, nameof(distance));
            ValidateNormalized(fingerSpacing, nameof(fingerSpacing));
            await using var action = await RunAction($"TwoFingerSwipe({search}, {direction})");

            var element = await Find<RectTransform>(search, throwIfMissing, searchTime);
            if (element == null)
            {
                action.Warn("Element not found");
                return;
            }

            // Capture name before any async operations - element may be destroyed
            var elementName = element.name;
            var screenPos = InputInjector.GetScreenPosition(element.gameObject);
            await InputInjector.InjectTwoFingerSwipe(screenPos, direction, distance, duration, fingerSpacing);
            action.SetResult($"'{elementName}'");
        }

        /// <summary>
        /// Performs a rotation gesture on an element.
        /// </summary>
        public static async Task Rotate(Search search, float degrees, float duration = 0.15f, float fingerDistance = 0.05f, bool throwIfMissing = true, float searchTime = 10)
        {
            ValidateNormalized(fingerDistance, nameof(fingerDistance));
            await using var action = await RunAction($"Rotate({search}, {degrees}°)");

            var element = await Find<RectTransform>(search, throwIfMissing, searchTime);
            if (element == null)
            {
                action.Warn("Element not found");
                return;
            }

            // Capture name before any async operations - element may be destroyed
            var elementName = element.name;
            var screenPos = InputInjector.GetScreenPosition(element.gameObject);
            await InputInjector.InjectRotate(screenPos, degrees, duration, fingerDistance);
            action.SetResult($"'{elementName}'");
        }

        #endregion
    }
}
