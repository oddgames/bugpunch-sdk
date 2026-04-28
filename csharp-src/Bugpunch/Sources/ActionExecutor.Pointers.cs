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
        #region Click Actions

        /// <summary>
        /// Clicks on a UI element matching the search query.
        /// Searches for a matching element and clicks on its screen position.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element to click when multiple match (0-based)</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when element is not found or not clickable within searchTime</exception>
        public static async Task Click(Search search, float searchTime = -1f, int index = 0)
        {
            await using var action = await RunAction($"Click({search})");

            var element = await search.Find(ResolveSearchTime(searchTime), index);

            if (element != null)
            {
                var elementName = element.name;
                var screenPos = InputInjector.GetScreenPosition(element);

                if (IsScreenPositionClickable(screenPos))
                {
                    await InputInjector.InjectPointerTap(screenPos);
                    action.SetResult($"'{elementName}' at ({screenPos.x:F0},{screenPos.y:F0})");
                    return;
                }

                action.Fail($"Element '{elementName}' found but off-screen at ({screenPos.x:F0},{screenPos.y:F0})");
                return;
            }

            action.Fail(BuildSearchFailureMessage(search, ResolveSearchTime(searchTime)));
        }

        /// <summary>
        /// Private helper to click at a screen position. Used internally when we already have a position.
        /// </summary>
        private static async Task ClickAtPosition(Vector2 screenPos, string logName = null)
        {
            if (logName != null)
                Log($"Click '{logName}' at ({screenPos.x:F0},{screenPos.y:F0})");
            await InputInjector.InjectPointerTap(screenPos);
        }

        /// <summary>
        /// Clicks at a screen position specified by percentage.
        /// </summary>
        /// <param name="normalizedPosition">Screen position as percentage (0-1 for both x and y)</param>
        /// <param name="requireReceivers">Whether to log receiver information (default false)</param>
        public static async Task ClickAt(Vector2 normalizedPosition, bool requireReceivers = false)
        {
            ValidateNormalized(normalizedPosition.x, nameof(normalizedPosition) + ".x");
            ValidateNormalized(normalizedPosition.y, nameof(normalizedPosition) + ".y");
            var screenPosition = new Vector2(Screen.width * normalizedPosition.x, Screen.height * normalizedPosition.y);
            await using var action = await RunAction($"ClickAt(({screenPosition.x:F0},{screenPosition.y:F0}))");
            await InputInjector.InjectPointerTap(screenPosition);
            if (requireReceivers)
            {
                var receivers = InputInjector.GetReceiversAtPosition(screenPosition,
                    typeof(IPointerClickHandler), typeof(IPointerUpHandler));
                action.SetResult($"receivers: {FormatReceivers(receivers)}");
            }
        }

        /// <summary>
        /// Clicks at a screen position specified by percentage.
        /// </summary>
        /// <param name="xPercent">Horizontal position as percentage (0-1)</param>
        /// <param name="yPercent">Vertical position as percentage (0-1)</param>
        /// <param name="requireReceivers">Whether to log receiver information (default false)</param>
        public static async Task ClickAt(float xPercent, float yPercent, bool requireReceivers = false)
        {
            ValidateNormalized(xPercent, nameof(xPercent));
            ValidateNormalized(yPercent, nameof(yPercent));
            await ClickAt(new Vector2(xPercent, yPercent), requireReceivers);
        }

        /// <summary>
        /// Double-clicks on a UI element matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when element is not found or not clickable within searchTime</exception>
        public static async Task DoubleClick(Search search, float searchTime = -1f, int index = 0)
        {
            await using var action = await RunAction($"DoubleClick({search})");

            var element = await search.Find(ResolveSearchTime(searchTime), index);

            if (element != null)
            {
                var elementName = element.name;
                var screenPos = InputInjector.GetScreenPosition(element);

                if (IsScreenPositionClickable(screenPos))
                {
                    await InputInjector.InjectPointerDoubleTap(screenPos);
                    action.SetResult($"'{elementName}' at ({screenPos.x:F0},{screenPos.y:F0})");
                    return;
                }

                action.Fail($"Element '{elementName}' found but off-screen at ({screenPos.x:F0},{screenPos.y:F0})");
                return;
            }

            action.Fail(BuildSearchFailureMessage(search, ResolveSearchTime(searchTime)));
        }

        /// <summary>
        /// Double-clicks at a specific screen position.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to double-click</param>
        /// <param name="requireReceivers">Whether to log receiver information (default false)</param>
        public static async Task DoubleClickAt(Vector2 screenPosition, bool requireReceivers = false)
        {
            await using var action = await RunAction($"DoubleClickAt(({screenPosition.x:F0},{screenPosition.y:F0}))");
            await InputInjector.InjectPointerDoubleTap(screenPosition);
            if (requireReceivers)
            {
                var receivers = InputInjector.GetReceiversAtPosition(screenPosition,
                    typeof(IPointerClickHandler), typeof(IPointerUpHandler));
                action.SetResult($"receivers: {FormatReceivers(receivers)}");
            }
        }

        /// <summary>
        /// Triple-clicks on a UI element matching the search query.
        /// Performs three rapid clicks in succession.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when element is not found or not clickable within searchTime</exception>
        public static async Task TripleClick(Search search, float searchTime = -1f, int index = 0)
        {
            await using var action = await RunAction($"TripleClick({search})");

            var element = await search.Find(ResolveSearchTime(searchTime), index);

            if (element != null)
            {
                var elementName = element.name;
                var screenPos = InputInjector.GetScreenPosition(element);

                if (IsScreenPositionClickable(screenPos))
                {
                    await InputInjector.InjectPointerTripleTap(screenPos);
                    action.SetResult($"'{elementName}' at ({screenPos.x:F0},{screenPos.y:F0})");
                    return;
                }

                action.Fail($"Element '{elementName}' found but off-screen at ({screenPos.x:F0},{screenPos.y:F0})");
                return;
            }

            action.Fail(BuildSearchFailureMessage(search, ResolveSearchTime(searchTime)));
        }

        /// <summary>
        /// Triple-clicks at a specific screen position.
        /// Performs three rapid clicks in succession.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to triple-click</param>
        /// <param name="requireReceivers">Whether to log receiver information (default false)</param>
        public static async Task TripleClickAt(Vector2 screenPosition, bool requireReceivers = false)
        {
            await using var action = await RunAction($"TripleClickAt(({screenPosition.x:F0},{screenPosition.y:F0}))");
            await InputInjector.InjectPointerTripleTap(screenPosition);
            if (requireReceivers)
            {
                var receivers = InputInjector.GetReceiversAtPosition(screenPosition,
                    typeof(IPointerClickHandler), typeof(IPointerUpHandler));
                action.SetResult($"receivers: {FormatReceivers(receivers)}");
            }
        }

        /// <summary>
        /// Holds/long-presses on a UI element matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="seconds">Duration of the hold in seconds</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when element is not found within searchTime</exception>
        public static async Task Hold(Search search, float seconds, float searchTime = -1f, int index = 0)
        {
            await using var action = await RunAction($"Hold({search}, {seconds}s)");

            var element = await search.Find(ResolveSearchTime(searchTime), index);

            if (element != null)
            {
                // Capture name before any async operations - element may be destroyed by hold handler
                var elementName = element.name;
                var screenPos = InputInjector.GetScreenPosition(element);
                await InputInjector.InjectPointerHold(screenPos, seconds);
                action.SetResult($"'{elementName}' at ({screenPos.x:F0},{screenPos.y:F0})");
                return;
            }

            action.Fail(BuildSearchFailureMessage(search, ResolveSearchTime(searchTime)));
        }

        /// <summary>
        /// Holds/long-presses at a specific screen position.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to hold</param>
        /// <param name="seconds">Duration of the hold in seconds</param>
        /// <param name="requireReceivers">Whether to log receiver information (default false)</param>
        public static async Task HoldAt(Vector2 screenPosition, float seconds, bool requireReceivers = false)
        {
            await using var action = await RunAction($"HoldAt(({screenPosition.x:F0},{screenPosition.y:F0}), {seconds}s)");
            await InputInjector.InjectPointerHold(screenPosition, seconds);
            if (requireReceivers)
            {
                var receivers = InputInjector.GetReceiversAtPosition(screenPosition,
                    typeof(IPointerDownHandler), typeof(IPointerUpHandler));
                action.SetResult($"receivers: {FormatReceivers(receivers)}");
            }
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
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        /// <param name="button">Which mouse button to use for dragging</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when element is not found within searchTime</exception>
        public static async Task Drag(Search search, Vector2 direction, float duration = 0.15f, float searchTime = -1f, int index = 0, float holdTime = 0.05f, PointerButton button = PointerButton.Left)
        {
            await using var action = await RunAction($"Drag({search}, direction=({direction.x:F0},{direction.y:F0}))");

            var element = await search.Find(ResolveSearchTime(searchTime), index);

            if (element != null)
            {
                // Capture name before any async operations - element may be destroyed
                var elementName = element.name;
                var screenPos = InputInjector.GetScreenPosition(element);
                var endPos = screenPos + direction;
                await InputInjector.InjectPointerDrag(screenPos, endPos, duration, holdTime, button);
                action.SetResult($"'{elementName}' from ({screenPos.x:F0},{screenPos.y:F0}) by ({direction.x:F0},{direction.y:F0})");
                return;
            }

            action.Fail(BuildSearchFailureMessage(search, ResolveSearchTime(searchTime)));
        }

        /// <summary>
        /// Drags from a source element matching search query to a target element matching another search query.
        /// </summary>
        /// <param name="fromSearch">The search query to find the source element</param>
        /// <param name="toSearch">The search query to find the target element</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        /// <param name="searchTime">Maximum time to search for elements</param>
        /// <param name="holdTime">Time to hold at start position before dragging (for elements requiring hold-to-drag)</param>
        /// <param name="button">Which mouse button to use for dragging</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when source or target element is not found within searchTime</exception>
        public static async Task DragTo(Search fromSearch, Search toSearch, float duration = 0.15f, float searchTime = -1f, float holdTime = 0.05f, PointerButton button = PointerButton.Left)
        {
            await using var action = await RunAction($"DragTo({fromSearch} -> {toSearch})");

            var fromElement = await fromSearch.Find(ResolveSearchTime(searchTime));
            if (fromElement == null)
                action.Fail($"Source element not found within {ResolveSearchTime(searchTime)}s");

            var toElement = await toSearch.Find(ResolveSearchTime(searchTime));
            if (toElement == null)
                action.Fail($"Target element not found within {ResolveSearchTime(searchTime)}s");

            var fromPos = InputInjector.GetScreenPosition(fromElement);
            var toPos = InputInjector.GetScreenPosition(toElement);
            await InputInjector.InjectPointerDrag(fromPos, toPos, duration, holdTime, button);
            action.SetResult($"'{fromElement.name}' to '{toElement.name}'");
        }

        /// <summary>
        /// Drags between two screen positions.
        /// </summary>
        /// <param name="startPosition">Start screen position</param>
        /// <param name="endPosition">End screen position</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        /// <param name="button">Which mouse button to use for dragging</param>
        /// <param name="requireReceivers">Whether to log receiver information (default false)</param>
        public static async Task DragFromTo(Vector2 startPosition, Vector2 endPosition, float duration = 0.15f, float holdTime = 0.05f, PointerButton button = PointerButton.Left, bool requireReceivers = false)
        {
            await using var action = await RunAction($"DragFromTo(({startPosition.x:F0},{startPosition.y:F0}) -> ({endPosition.x:F0},{endPosition.y:F0}))");
            await InputInjector.InjectPointerDrag(startPosition, endPosition, duration, holdTime, button);
            if (requireReceivers)
            {
                var receivers = InputInjector.GetReceiversAtPosition(startPosition,
                    typeof(IDragHandler), typeof(IBeginDragHandler));
                action.SetResult($"receivers: {FormatReceivers(receivers)}");
            }
        }

        /// <summary>
        /// Performs a drag gesture from the screen center in a direction.
        /// </summary>
        /// <param name="direction">The direction to drag (pixel offset)</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        /// <param name="requireReceivers">Whether to log receiver information (default false)</param>
        public static async Task Drag(Vector2 direction, float duration = 0.15f, float holdTime = 0.05f, bool requireReceivers = false)
        {
            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var endPos = center + direction;
            await using var action = await RunAction($"Drag(direction=({direction.x:F0},{direction.y:F0})) from center");
            await InputInjector.InjectPointerDrag(center, endPos, duration, holdTime);
            if (requireReceivers)
            {
                var receivers = InputInjector.GetReceiversAtPosition(center,
                    typeof(IDragHandler), typeof(IBeginDragHandler));
                action.SetResult($"receivers: {FormatReceivers(receivers)}");
            }
        }

        #endregion
    }
}
