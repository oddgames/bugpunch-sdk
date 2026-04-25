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
        #region Scroll Actions

        /// <summary>
        /// Scrolls at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position to scroll at</param>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        public static async Task ScrollAt(Vector2 position, float delta)
        {
            await using (await RunAction($"ScrollAt(({position.x:F0},{position.y:F0}), delta={delta})"))
            {
                await InputInjector.InjectScroll(position, delta);
            }
        }

        /// <summary>
        /// Scrolls on a UI element matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when element is not found within searchTime</exception>
        public static async Task Scroll(Search search, float delta, float searchTime = -1f, int index = 0)
        {
            await using var action = await RunAction($"Scroll({search}, delta={delta})");

            var element = await search.Find(ResolveSearchTime(searchTime), index);

            if (element != null)
            {
                // Capture name before any async operations - element may be destroyed
                var elementName = element.name;
                var screenPos = InputInjector.GetScreenPosition(element);
                await InputInjector.InjectScroll(screenPos, delta);
                action.SetResult($"'{elementName}' at ({screenPos.x:F0},{screenPos.y:F0})");
                return;
            }

            action.Fail(BuildSearchFailureMessage(search, ResolveSearchTime(searchTime)));
        }

        /// <summary>
        /// Scrolls on a UI element matching the search query in a named direction.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="direction">Direction: "up", "down", "left", "right"</param>
        /// <param name="amount">Scroll amount (0-1 normalized)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when element is not found within searchTime</exception>
        public static async Task Scroll(Search search, string direction, float amount = 0.3f, float searchTime = -1f, int index = 0)
        {
            ValidateNormalized(amount, nameof(amount));
            await using var action = await RunAction($"Scroll({search}, {direction}, amount={amount})");

            var element = await search.Find(ResolveSearchTime(searchTime), index);

            if (element != null)
            {
                // Capture name before any async operations - element may be destroyed
                var elementName = element.name;
                await InputInjector.ScrollElement(element, direction, amount);
                action.SetResult($"'{elementName}'");
                return;
            }

            action.Fail(BuildSearchFailureMessage(search, ResolveSearchTime(searchTime)));
        }

        #endregion

        #region Slider/Scrollbar Actions

        /// <summary>
        /// Clicks on a slider at a specific position matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the Slider</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when slider is not found within searchTime</exception>
        public static async Task ClickSlider(Search search, float normalizedValue, float searchTime = -1f)
        {
            ValidateNormalized(normalizedValue, nameof(normalizedValue));
            await using var action = await RunAction($"ClickSlider({search}, {normalizedValue:F2})");

            var slider = await FindComponent<Slider>(search, ResolveSearchTime(searchTime));

            if (slider != null)
            {
                var clickPos = InputInjector.GetSliderClickPosition(slider, normalizedValue);
                await InputInjector.InjectPointerTap(clickPos);
                action.SetResult($"'{slider.name}'");
                return;
            }

            action.Fail($"Slider not found within {ResolveSearchTime(searchTime)}s");
        }

        /// <summary>
        /// Drags a slider from one value to another matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the Slider</param>
        /// <param name="fromValue">Starting value (0-1)</param>
        /// <param name="toValue">Ending value (0-1)</param>
        /// <param name="duration">Duration of the drag</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when slider is not found within searchTime</exception>
        public static async Task DragSlider(Search search, float fromValue, float toValue, float duration = 0.15f, float searchTime = -1f, float holdTime = 0.05f)
        {
            ValidateNormalized(fromValue, nameof(fromValue));
            ValidateNormalized(toValue, nameof(toValue));
            await using var action = await RunAction($"DragSlider({search}, {fromValue:F2} -> {toValue:F2})");

            var slider = await FindComponent<Slider>(search, ResolveSearchTime(searchTime));

            if (slider != null)
            {
                var startPos = InputInjector.GetSliderClickPosition(slider, fromValue);
                var endPos = InputInjector.GetSliderClickPosition(slider, toValue);
                await InputInjector.InjectPointerDrag(startPos, endPos, duration, holdTime);
                action.SetResult($"'{slider.name}'");
                return;
            }

            action.Fail($"Slider not found within {ResolveSearchTime(searchTime)}s");
        }

        /// <summary>
        /// Sets a slider to a specific value matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the Slider</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when slider is not found within searchTime</exception>
        public static async Task SetSlider(Search search, float normalizedValue, float searchTime = -1f)
        {
            ValidateNormalized(normalizedValue, nameof(normalizedValue));
            await using var action = await RunAction($"SetSlider({search}, {normalizedValue:F2})");

            var slider = await FindComponent<Slider>(search, ResolveSearchTime(searchTime));

            if (slider != null)
            {
                await InputInjector.SetSlider(slider, normalizedValue);
                action.SetResult($"'{slider.name}'");
                return;
            }

            action.Fail($"Slider not found within {ResolveSearchTime(searchTime)}s");
        }

        /// <summary>
        /// Sets a scrollbar to a specific value matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the Scrollbar</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when scrollbar is not found within searchTime</exception>
        public static async Task SetScrollbar(Search search, float normalizedValue, float searchTime = -1f)
        {
            ValidateNormalized(normalizedValue, nameof(normalizedValue));
            await using var action = await RunAction($"SetScrollbar({search}, {normalizedValue:F2})");

            var scrollbar = await FindComponent<Scrollbar>(search, ResolveSearchTime(searchTime));

            if (scrollbar != null)
            {
                await InputInjector.SetScrollbar(scrollbar, normalizedValue);
                action.SetResult($"'{scrollbar.name}'");
                return;
            }

            action.Fail($"Scrollbar not found within {ResolveSearchTime(searchTime)}s");
        }

        #endregion

        #region ScrollTo Operations

        /// <summary>
        /// Checks if a RectTransform is visible within the viewport of a ScrollRect.
        /// </summary>
        /// <param name="scrollRect">The ScrollRect containing the viewport</param>
        /// <param name="target">The RectTransform to check visibility for</param>
        /// <returns>True if the target is fully visible within the viewport</returns>
        public static bool IsVisibleInViewport(ScrollRect scrollRect, RectTransform target)
        {
            if (scrollRect == null || target == null)
                return false;

            var viewport = scrollRect.viewport != null ? scrollRect.viewport : scrollRect.GetComponent<RectTransform>();
            if (viewport == null)
                return false;

            // Get the world corners of the viewport
            var viewportCorners = new Vector3[4];
            viewport.GetWorldCorners(viewportCorners);
            var viewportRect = new Rect(
                viewportCorners[0].x,
                viewportCorners[0].y,
                viewportCorners[2].x - viewportCorners[0].x,
                viewportCorners[2].y - viewportCorners[0].y
            );

            // Get the world corners of the target
            var targetCorners = new Vector3[4];
            target.GetWorldCorners(targetCorners);
            var targetRect = new Rect(
                targetCorners[0].x,
                targetCorners[0].y,
                targetCorners[2].x - targetCorners[0].x,
                targetCorners[2].y - targetCorners[0].y
            );

            // Check if target is fully contained within viewport
            return viewportRect.Contains(new Vector2(targetRect.xMin, targetRect.yMin)) &&
                   viewportRect.Contains(new Vector2(targetRect.xMax, targetRect.yMax));
        }

        /// <summary>
        /// Gets the screen-space bounds of a ScrollRect's viewport.
        /// </summary>
        private static Rect GetViewportScreenBounds(ScrollRect scrollRect)
        {
            var viewport = scrollRect.viewport != null ? scrollRect.viewport : scrollRect.GetComponent<RectTransform>();
            var corners = new Vector3[4];
            viewport.GetWorldCorners(corners);

            // Convert world corners to screen space
            var canvas = scrollRect.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                // For ScreenSpaceOverlay, world coords are screen coords
                return new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
            }
            else if (canvas != null && canvas.worldCamera != null)
            {
                var cam = canvas.worldCamera;
                var min = cam.WorldToScreenPoint(corners[0]);
                var max = cam.WorldToScreenPoint(corners[2]);
                return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
            }
            else
            {
                var min = Camera.main.WorldToScreenPoint(corners[0]);
                var max = Camera.main.WorldToScreenPoint(corners[2]);
                return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
            }
        }

        /// <summary>
        /// Gets the world-space center of a RectTransform.
        /// </summary>
        private static Vector3 GetWorldCenter(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return (corners[0] + corners[2]) / 2f;
        }

        /// <summary>
        /// Scrolls a ScrollRect to make a target element visible using drag input injection.
        /// Automatically detects scroll direction based on target position relative to viewport.
        /// Supports horizontal, vertical, and diagonal scrolling for 2D scroll views.
        /// </summary>
        /// <param name="scrollViewSearch">Search query to find the ScrollRect</param>
        /// <param name="targetSearch">Search query to find the target element to scroll to</param>
        /// <param name="maxScrollAttempts">Maximum number of drag attempts</param>
        /// <param name="throwIfMissing">If true, throws exception when target not found</param>
        /// <param name="searchTime">Time to search for the scroll view</param>
        /// <returns>The GameObject of the target if found, null otherwise</returns>
        public static async Task<GameObject> ScrollTo(Search scrollViewSearch, Search targetSearch, int maxScrollAttempts = 20, bool throwIfMissing = true, float searchTime = -1f)
        {
            await using var action = await RunAction($"ScrollTo({scrollViewSearch}, {targetSearch})");
            var scrollRect = await Find<ScrollRect>(scrollViewSearch, true, searchTime);

            if (scrollRect == null)
            {
                if (throwIfMissing)
                    action.Fail("ScrollRect not found");
                action.Warn("ScrollRect not found");
                return null;
            }

            bool canScrollHorizontal = scrollRect.horizontal;
            bool canScrollVertical = scrollRect.vertical;

            LogDebug($"ScrollTo: found scroll view '{scrollRect.name}', horizontal={canScrollHorizontal}, vertical={canScrollVertical}");

            // First check if target is already visible
            var target = await targetSearch.Find(0.5f);
            if (target != null)
            {
                var targetRect = target.GetComponent<RectTransform>();
                if (targetRect != null && IsVisibleInViewport(scrollRect, targetRect))
                {
                    action.SetResult($"target '{target.name}' already visible");
                    return target;
                }
            }

            // Get viewport for position calculations
            var viewport = scrollRect.viewport != null ? scrollRect.viewport : scrollRect.GetComponent<RectTransform>();
            var viewportBounds = GetViewportScreenBounds(scrollRect);

            // Calculate drag positions
            float leftX = viewportBounds.x + viewportBounds.width * 0.1f;
            float rightX = viewportBounds.x + viewportBounds.width * 0.9f;
            float topY = viewportBounds.y + viewportBounds.height * 0.9f;
            float bottomY = viewportBounds.y + viewportBounds.height * 0.1f;
            float centerX = viewportBounds.x + viewportBounds.width / 2f;
            float centerY = viewportBounds.y + viewportBounds.height / 2f;

            // Determine initial scroll direction based on target position relative to viewport
            // Try to find target (even if not visible) to determine direction
            target = await targetSearch.Find(0.5f);
            int verticalDir = 1;  // 1 = scroll down (reveal below), -1 = scroll up (reveal above)
            int horizontalDir = 1; // 1 = scroll right (reveal right), -1 = scroll left (reveal left)

            if (target != null)
            {
                var targetRect = target.GetComponent<RectTransform>();
                if (targetRect != null)
                {
                    var viewportCenter = GetWorldCenter(viewport);
                    var targetCenter = GetWorldCenter(targetRect);

                    // Determine which direction to scroll based on target position
                    if (canScrollVertical)
                    {
                        verticalDir = targetCenter.y < viewportCenter.y ? 1 : -1; // Below viewport = scroll down
                    }
                    if (canScrollHorizontal)
                    {
                        horizontalDir = targetCenter.x > viewportCenter.x ? 1 : -1; // Right of viewport = scroll right
                    }
                    Log($"ScrollTo: target detected at ({targetCenter.x:F0}, {targetCenter.y:F0}), viewport center ({viewportCenter.x:F0}, {viewportCenter.y:F0}), scrolling vDir={verticalDir} hDir={horizontalDir}");
                }
            }

            int attempts = 0;
            bool verticalReversed = false;
            bool horizontalReversed = false;

            Log($"ScrollTo: viewport bounds ({viewportBounds.x:F0}, {viewportBounds.y:F0}, {viewportBounds.width:F0}x{viewportBounds.height:F0})");

            while (attempts < maxScrollAttempts)
            {
                // Check for target before each drag
                target = await targetSearch.Find(0.5f);
                if (target != null)
                {
                    var targetRect = target.GetComponent<RectTransform>();
                    if (targetRect != null && IsVisibleInViewport(scrollRect, targetRect))
                    {
                        action.SetResult($"found target '{target.name}' after {attempts} drags");
                        return target;
                    }
                }

                // Check boundaries and reverse if needed
                float vPos = scrollRect.verticalNormalizedPosition;
                float hPos = scrollRect.horizontalNormalizedPosition;

                if (canScrollVertical && !verticalReversed)
                {
                    if (verticalDir == 1 && vPos <= 0.01f) // Hit bottom while scrolling down
                    {
                        verticalDir = -1;
                        verticalReversed = true;
                        Log($"ScrollTo: hit bottom, reversing to scroll up");
                    }
                    else if (verticalDir == -1 && vPos >= 0.99f) // Hit top while scrolling up
                    {
                        verticalDir = 1;
                        verticalReversed = true;
                        Log($"ScrollTo: hit top, reversing to scroll down");
                    }
                }

                if (canScrollHorizontal && !horizontalReversed)
                {
                    if (horizontalDir == 1 && hPos >= 0.99f) // Hit right while scrolling right
                    {
                        horizontalDir = -1;
                        horizontalReversed = true;
                        Log($"ScrollTo: hit right edge, reversing to scroll left");
                    }
                    else if (horizontalDir == -1 && hPos <= 0.01f) // Hit left while scrolling left
                    {
                        horizontalDir = 1;
                        horizontalReversed = true;
                        Log($"ScrollTo: hit left edge, reversing to scroll right");
                    }
                }

                // If we've reversed both directions and hit boundaries again, we've searched everything
                if (verticalReversed && horizontalReversed)
                {
                    bool vAtBoundary = (verticalDir == 1 && vPos <= 0.01f) || (verticalDir == -1 && vPos >= 0.99f);
                    bool hAtBoundary = (horizontalDir == 1 && hPos >= 0.99f) || (horizontalDir == -1 && hPos <= 0.01f);

                    if ((!canScrollVertical || vAtBoundary) && (!canScrollHorizontal || hAtBoundary))
                    {
                        Log($"ScrollTo: searched entire scroll area, target not found");
                        break;
                    }
                }

                // Calculate drag direction
                Vector2 startPos, endPos;

                if (canScrollVertical && canScrollHorizontal)
                {
                    // Diagonal drag for 2D scroll views
                    float startX = horizontalDir == 1 ? rightX : leftX;
                    float endX = horizontalDir == 1 ? leftX : rightX;
                    float startY = verticalDir == 1 ? bottomY : topY;
                    float endY = verticalDir == 1 ? topY : bottomY;
                    startPos = new Vector2(startX, startY);
                    endPos = new Vector2(endX, endY);
                }
                else if (canScrollHorizontal)
                {
                    // Horizontal only
                    startPos = horizontalDir == 1 ? new Vector2(rightX, centerY) : new Vector2(leftX, centerY);
                    endPos = horizontalDir == 1 ? new Vector2(leftX, centerY) : new Vector2(rightX, centerY);
                }
                else
                {
                    // Vertical only (default)
                    startPos = verticalDir == 1 ? new Vector2(centerX, bottomY) : new Vector2(centerX, topY);
                    endPos = verticalDir == 1 ? new Vector2(centerX, topY) : new Vector2(centerX, bottomY);
                }

                LogDebug($"ScrollTo: drag {attempts + 1} from ({startPos.x:F0}, {startPos.y:F0}) to ({endPos.x:F0}, {endPos.y:F0}), vPos={vPos:F3}, hPos={hPos:F3}");
                await DragFromTo(startPos, endPos, duration: 0.15f, holdTime: 0.05f);
                await Async.DelayFrames(2);

                attempts++;
            }

            // Final check after all attempts
            target = await targetSearch.Find(0.5f);
            if (target != null)
            {
                var targetRect = target.GetComponent<RectTransform>();
                if (targetRect != null && IsVisibleInViewport(scrollRect, targetRect))
                {
                    action.SetResult($"found target '{target.name}' after {attempts} drags");
                    return target;
                }
            }

            if (throwIfMissing)
                action.Fail($"target not found after {attempts} drag attempts");

            action.Warn($"target not found after {attempts} drag attempts");
            return null;
        }

        /// <summary>
        /// Scrolls to an element and clicks on it.
        /// </summary>
        /// <param name="scrollViewSearch">Search query to find the ScrollRect</param>
        /// <param name="targetSearch">Search query to find the target element to scroll to and click</param>
        /// <param name="maxScrollAttempts">Maximum number of scroll increments to try</param>
        /// <param name="throwIfMissing">If true, throws exception when target not found</param>
        /// <param name="searchTime">Time to search for the scroll view</param>
        /// <returns>True if target was found and clicked, false otherwise</returns>
        public static async Task<bool> ScrollToAndClick(Search scrollViewSearch, Search targetSearch, int maxScrollAttempts = 20, bool throwIfMissing = true, float searchTime = 5)
        {
            await using var action = await RunAction($"ScrollToAndClick({scrollViewSearch}, {targetSearch})");
            var target = await ScrollTo(scrollViewSearch, targetSearch, maxScrollAttempts, throwIfMissing, searchTime);

            if (target == null)
            {
                action.Warn("target not found");
                return false;
            }

            var screenPos = InputInjector.GetScreenPosition(target);
            await ClickAtPosition(screenPos, target.name);
            action.SetResult($"clicked '{target.name}'");
            return true;
        }

        #endregion
    }
}
