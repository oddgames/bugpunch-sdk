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
        #region Dropdown Helpers

        /// <summary>
        /// Finds a Dropdown by option label.
        /// </summary>
        internal static async Task<(GameObject element, RectTransform template, int optionIndex)> FindDropdownByLabel(Search search, string optionLabel, float timeout)
        {
            var elements = await search.FindAll(timeout);
            foreach (var go in elements)
            {
                var legacy = go.GetComponent<Dropdown>();
                if (legacy != null)
                {
                    int idx = legacy.options.FindIndex(o => o.text == optionLabel);
                    if (idx >= 0)
                        return (go, legacy.template, idx);
                }

                var tmp = go.GetComponent<TMPro.TMP_Dropdown>();
                if (tmp != null)
                {
                    int idx = tmp.options.FindIndex(o => o.text == optionLabel);
                    if (idx >= 0)
                        return (go, tmp.template, idx);
                }
            }

            return (null, null, -1);
        }

        /// <summary>
        /// Finds a Dropdown (legacy or TMP).
        /// </summary>
        internal static async Task<(GameObject element, RectTransform template)> FindDropdown(Search search, float timeout)
        {
            var elements = await search.FindAll(timeout);
            foreach (var go in elements)
            {
                var legacy = go.GetComponent<Dropdown>();
                if (legacy != null)
                    return (go, legacy.template);

                var tmp = go.GetComponent<TMPro.TMP_Dropdown>();
                if (tmp != null)
                    return (go, tmp.template);
            }

            return (null, null);
        }

        #endregion

        #region Dropdown Actions

        /// <summary>
        /// Selects a dropdown option by index using realistic click interactions.
        /// </summary>
        /// <param name="search">The search query to find the Dropdown or TMP_Dropdown</param>
        /// <param name="optionIndex">Index of the option to select (0-based)</param>
        /// <param name="searchTime">Maximum time to search for the dropdown</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when dropdown is not found within searchTime</exception>
        public static async Task ClickDropdown(Search search, int optionIndex, float searchTime = -1f)
        {
            await using var action = await RunAction($"ClickDropdown({search}, index={optionIndex})");

            var (element, template) = await FindDropdown(search, searchTime);

            if (element != null)
            {
                // Capture name before any async operations - element may be destroyed
                var elementName = element.name;
                await ClickDropdownItem(element, template, optionIndex);
                action.SetResult($"'{elementName}' index={optionIndex}");
                return;
            }

            action.Fail($"Dropdown not found within {ResolveSearchTime(searchTime)}s");
        }

        /// <summary>
        /// Selects a dropdown option by label text using realistic click interactions.
        /// </summary>
        /// <param name="search">The search query to find the Dropdown or TMP_Dropdown</param>
        /// <param name="optionLabel">The text label of the option to select</param>
        /// <param name="searchTime">Maximum time to search for the dropdown</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when dropdown or option is not found within searchTime</exception>
        public static async Task ClickDropdown(Search search, string optionLabel, float searchTime = -1f)
        {
            await using var action = await RunAction($"ClickDropdown({search}, label=\"{optionLabel}\")");

            var (element, template, optionIndex) = await FindDropdownByLabel(search, optionLabel, searchTime);

            if (element != null && optionIndex >= 0)
            {
                // Capture name before any async operations - element may be destroyed
                var elementName = element.name;
                await ClickDropdownItem(element, template, optionIndex);
                action.SetResult($"'{elementName}' label=\"{optionLabel}\" (index={optionIndex})");
                return;
            }

            action.Fail($"Dropdown or option '{optionLabel}' not found within {ResolveSearchTime(searchTime)}s");
        }

        /// <summary>
        /// Internal method to click a dropdown item after the dropdown has been found.
        /// </summary>
        private static async Task ClickDropdownItem(GameObject dropdownGO, RectTransform template, int optionIndex)
        {
            // Capture existing toggles before opening dropdown
            var existingToggles = new System.Collections.Generic.HashSet<Toggle>(
                UnityEngine.Object.FindObjectsByType<Toggle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));

            // Click the dropdown to open it
            var dropdownPos = InputInjector.GetScreenPosition(dropdownGO);
            await ClickAtPosition(dropdownPos, dropdownGO.name);

            // Wait for new toggles to appear (the dropdown items)
            Toggle[] newToggles = null;
            float waitTime = 0f;
            const float maxWaitTime = 2f;

            while (waitTime < maxWaitTime)
            {
                await Async.DelayFrames(1);

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
                    LogDebug($"ClickDropdown selecting option {optionIndex}: '{targetToggle.name}'");
                    var togglePos = InputInjector.GetScreenPosition(targetToggle.gameObject);
                    await ClickAtPosition(togglePos, targetToggle.name);
                    return;
                }

                await Task.Delay(50);
                waitTime += 0.05f;
            }

            LogFail($"ClickDropdown", $"item at index {optionIndex} not found (found {newToggles?.Length ?? 0} new toggles)");
        }

        #endregion

        #region ItemContainer and FindItems

        /// <summary>
        /// Finds a container (ScrollRect, Dropdown, LayoutGroup) and its child items.
        /// </summary>
        public static async Task<ItemContainer> FindItems(Search containerSearch, Search itemSearch = null)
        {
            await using var action = await RunAction($"FindItems({containerSearch})");

            Component container = await Find<ScrollRect>(containerSearch, false, 2);
            container ??= await Find<VerticalLayoutGroup>(containerSearch, false, 1);
            container ??= await Find<HorizontalLayoutGroup>(containerSearch, false, 1);
            container ??= await Find<GridLayoutGroup>(containerSearch, false, 1);
            container ??= await Find<TMP_Dropdown>(containerSearch, false, 1);
            container ??= await Find<Dropdown>(containerSearch, false, 1);

            if (container == null)
            {
                action.Fail($"container not found");
            }

            var items = GetContainerItems(container);

            if (itemSearch != null)
            {
                items = items.Where(item => itemSearch.Matches(item.gameObject));
            }

            var itemsList = items.ToList();
            action.SetResult($"'{container.name}' with {itemsList.Count} items");
            return new ItemContainer(container, itemsList);
        }

        /// <summary>
        /// Finds a container by name and its child items.
        /// </summary>
        public static async Task<ItemContainer> FindItems(string containerName, Search itemSearch = null)
        {
            return await FindItems(new Search().Name(containerName), itemSearch);
        }

        static IEnumerable<RectTransform> GetContainerItems(Component container)
        {
            switch (container)
            {
                case ScrollRect scrollRect:
                {
                    var content = scrollRect.content ?? scrollRect.GetComponent<RectTransform>();
                    var items = new List<RectTransform>();
                    foreach (Transform child in content)
                    {
                        var rect = child.GetComponent<RectTransform>();
                        if (rect != null && child.gameObject.activeInHierarchy)
                            items.Add(rect);
                    }
                    return items.OrderBy(item => scrollRect.vertical ? -item.anchoredPosition.y : item.anchoredPosition.x);
                }

                case TMP_Dropdown tmpDropdown:
                {
                    var template = tmpDropdown.template;
                    if (template != null && template.gameObject.activeInHierarchy)
                    {
                        var content = template.GetComponentInChildren<ToggleGroup>()?.transform ?? template;
                        return content.GetComponentsInChildren<RectTransform>()
                            .Where(r => r.GetComponent<Toggle>() != null)
                            .OrderBy(r => -r.anchoredPosition.y);
                    }
                    return Enumerable.Empty<RectTransform>();
                }

                case Dropdown dropdown:
                {
                    var template = dropdown.template;
                    if (template != null && template.gameObject.activeInHierarchy)
                    {
                        var content = template.GetComponentInChildren<ToggleGroup>()?.transform ?? template;
                        return content.GetComponentsInChildren<RectTransform>()
                            .Where(r => r.GetComponent<Toggle>() != null)
                            .OrderBy(r => -r.anchoredPosition.y);
                    }
                    return Enumerable.Empty<RectTransform>();
                }

                case HorizontalLayoutGroup hlg:
                {
                    var items = new List<RectTransform>();
                    foreach (Transform child in hlg.transform)
                    {
                        var rect = child.GetComponent<RectTransform>();
                        if (rect != null && child.gameObject.activeInHierarchy)
                            items.Add(rect);
                    }
                    return items.OrderBy(r => r.anchoredPosition.x);
                }

                case VerticalLayoutGroup vlg:
                {
                    var items = new List<RectTransform>();
                    foreach (Transform child in vlg.transform)
                    {
                        var rect = child.GetComponent<RectTransform>();
                        if (rect != null && child.gameObject.activeInHierarchy)
                            items.Add(rect);
                    }
                    return items.OrderBy(r => -r.anchoredPosition.y);
                }

                case GridLayoutGroup glg:
                {
                    var items = new List<RectTransform>();
                    foreach (Transform child in glg.transform)
                    {
                        var rect = child.GetComponent<RectTransform>();
                        if (rect != null && child.gameObject.activeInHierarchy)
                            items.Add(rect);
                    }
                    return items.OrderBy(r => -r.anchoredPosition.y).ThenBy(r => r.anchoredPosition.x);
                }

                default:
                    return Enumerable.Empty<RectTransform>();
            }
        }

        #endregion

        #region Random Click

        /// <summary>
        /// Clicks a random clickable element on screen.
        /// </summary>
        public static async Task<Component> RandomClick(Search filter = null)
        {
            await using var action = await RunAction($"RandomClick(filter={filter?.ToString() ?? "none"})");
            var clickables = GetClickableElements(filter);
            if (!clickables.Any())
            {
                action.Warn("no clickable elements found");
                return null;
            }

            var target = clickables.ElementAt(RandomGenerator.Next(clickables.Count()));
            var screenPos = InputInjector.GetScreenPosition(target.gameObject);
            await ClickAtPosition(screenPos, target.gameObject.name);
            action.SetResult($"'{target.gameObject.name}'");
            return target;
        }

        /// <summary>
        /// Clicks a random element excluding certain searches.
        /// </summary>
        public static async Task<Component> RandomClickExcept(params Search[] exclude)
        {
            await using var action = await RunAction($"RandomClickExcept(excluding {exclude.Length} patterns)");
            var allClickables = GetClickableElements(null);
            var filtered = allClickables.Where(c =>
            {
                foreach (var ex in exclude)
                    if (ex.Matches(c.gameObject)) return false;
                return true;
            });

            if (!filtered.Any())
            {
                action.Warn("no clickable elements found after filtering");
                return null;
            }

            var target = filtered.ElementAt(RandomGenerator.Next(filtered.Count()));
            var screenPos = InputInjector.GetScreenPosition(target.gameObject);
            await ClickAtPosition(screenPos, target.gameObject.name);
            action.SetResult($"'{target.gameObject.name}'");
            return target;
        }

        static IEnumerable<Selectable> GetClickableElements(Search filter)
        {
            var allSelectables = UnityEngine.Object.FindObjectsByType<Selectable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(s => s.interactable && s.gameObject.activeInHierarchy);

            if (filter != null)
            {
                allSelectables = allSelectables.Where(s => filter.Matches(s.gameObject));
            }

            return allSelectables;
        }

        #endregion

        #region Auto-Explore

        /// <summary>
        /// Automatically explores the UI by clicking random elements.
        /// </summary>
        public static async Task<SimpleExploreResult> AutoExplore(
            SimpleExploreStopCondition stopCondition,
            float value,
            int? seed = null,
            float delayBetweenActions = 0.5f,
            bool tryBackOnStuck = false)
        {
            if (seed.HasValue)
                SetRandomSeed(seed.Value);

            var result = new SimpleExploreResult();
            var startTime = Now;
            int consecutiveNoClick = 0;

            await using var action = await RunAction($"AutoExplore(condition={stopCondition}, value={value})");

            while (Application.isPlaying)
            {
                result.TimeElapsed = Now - startTime;

                switch (stopCondition)
                {
                    case SimpleExploreStopCondition.Time:
                        if (result.TimeElapsed >= value)
                        {
                            result.StopReason = SimpleExploreStopCondition.Time;
                            action.SetResult($"time limit reached, {result.ActionsPerformed} actions in {result.TimeElapsed:F1}s");
                            return result;
                        }
                        break;
                    case SimpleExploreStopCondition.ActionCount:
                        if (result.ActionsPerformed >= (int)value)
                        {
                            result.StopReason = SimpleExploreStopCondition.ActionCount;
                            action.SetResult($"action count reached, {result.ActionsPerformed} actions in {result.TimeElapsed:F1}s");
                            return result;
                        }
                        break;
                    case SimpleExploreStopCondition.DeadEnd:
                        if (consecutiveNoClick >= 3)
                        {
                            result.StopReason = SimpleExploreStopCondition.DeadEnd;
                            action.SetResult($"dead end reached, {result.ActionsPerformed} actions in {result.TimeElapsed:F1}s");
                            return result;
                        }
                        break;
                }

                var clicked = await RandomClick();
                if (clicked != null)
                {
                    result.ActionsPerformed++;
                    result.ClickedElements.Add(clicked.gameObject.name);
                    consecutiveNoClick = 0;
                }
                else
                {
                    consecutiveNoClick++;
                    if (tryBackOnStuck)
                    {
                        await PressKey(Key.Escape);
                    }
                }

                await Wait(delayBetweenActions);
            }

            return result;
        }

        /// <summary>
        /// Auto-explores for a specified duration.
        /// </summary>
        public static async Task<SimpleExploreResult> AutoExploreForSeconds(float seconds, int? seed = null, float delayBetweenActions = 0.5f)
        {
            return await AutoExplore(SimpleExploreStopCondition.Time, seconds, seed, delayBetweenActions);
        }

        /// <summary>
        /// Auto-explores for a specified number of actions.
        /// </summary>
        public static async Task<SimpleExploreResult> AutoExploreForActions(int actionCount, int? seed = null, float delayBetweenActions = 0.5f)
        {
            return await AutoExplore(SimpleExploreStopCondition.ActionCount, actionCount, seed, delayBetweenActions);
        }

        /// <summary>
        /// Auto-explores until no more clickable elements are found.
        /// </summary>
        public static async Task<SimpleExploreResult> AutoExploreUntilDeadEnd(int? seed = null, float delayBetweenActions = 0.5f, bool tryBackOnStuck = false)
        {
            return await AutoExplore(SimpleExploreStopCondition.DeadEnd, 0, seed, delayBetweenActions, tryBackOnStuck);
        }

        #endregion
    }
}
