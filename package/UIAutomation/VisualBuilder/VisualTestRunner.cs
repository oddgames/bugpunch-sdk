using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using NUnit.Framework;
using ODDGames.UIAutomation.AI;

namespace ODDGames.UIAutomation.VisualBuilder
{
    /// <summary>
    /// Result of executing a single visual block.
    /// </summary>
    public class VisualBlockResult
    {
        /// <summary>The block that was executed</summary>
        public VisualBlock Block { get; set; }

        /// <summary>Index of the block in the test sequence</summary>
        public int StepIndex { get; set; }

        /// <summary>Whether the block executed successfully</summary>
        public bool Success { get; set; }

        /// <summary>Error message if the block failed</summary>
        public string Error { get; set; }

        /// <summary>Time taken to execute in milliseconds</summary>
        public float ExecutionTimeMs { get; set; }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        public static VisualBlockResult Succeeded(VisualBlock block, int stepIndex, float executionTimeMs)
        {
            return new VisualBlockResult
            {
                Block = block,
                StepIndex = stepIndex,
                Success = true,
                ExecutionTimeMs = executionTimeMs
            };
        }

        /// <summary>
        /// Creates a failed result.
        /// </summary>
        public static VisualBlockResult Failed(VisualBlock block, int stepIndex, string error, float executionTimeMs = 0)
        {
            return new VisualBlockResult
            {
                Block = block,
                StepIndex = stepIndex,
                Success = false,
                Error = error,
                ExecutionTimeMs = executionTimeMs
            };
        }
    }

    /// <summary>
    /// Result of executing a complete visual test.
    /// </summary>
    public class VisualTestResult
    {
        /// <summary>The test that was executed</summary>
        public VisualTest Test { get; set; }

        /// <summary>Whether the entire test passed</summary>
        public bool Passed { get; set; }

        /// <summary>Overall error message if the test failed</summary>
        public string Error { get; set; }

        /// <summary>Results for each block executed</summary>
        public List<VisualBlockResult> BlockResults { get; set; } = new();

        /// <summary>Total execution time in milliseconds</summary>
        public float TotalExecutionTimeMs { get; set; }

        /// <summary>Number of blocks that executed successfully</summary>
        public int SuccessCount => BlockResults.FindAll(r => r.Success).Count;

        /// <summary>Number of blocks that failed</summary>
        public int FailCount => BlockResults.FindAll(r => !r.Success).Count;

        /// <summary>Index of the first failed block, or -1 if all passed</summary>
        public int FirstFailedIndex => BlockResults.FindIndex(r => !r.Success);
    }

    /// <summary>
    /// Progress information for test execution.
    /// </summary>
    public class VisualTestProgress
    {
        /// <summary>Current step being executed (0-based)</summary>
        public int CurrentStep { get; set; }

        /// <summary>Total number of steps</summary>
        public int TotalSteps { get; set; }

        /// <summary>Current block being executed</summary>
        public VisualBlock CurrentBlock { get; set; }

        /// <summary>Status message</summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// Execution engine for visual tests.
    /// Converts VisualBlock list to executable actions using InputInjector and ElementDiscovery.
    /// </summary>
    public static class VisualTestRunner
    {
        private const int DefaultTimeoutMs = 10000;
        private const int ElementWaitDelayMs = 100;
        private const int PostActionDelayMs = 50;

        /// <summary>
        /// Gets display text for a selector, returning fallback if null or invalid.
        /// Used for error messages to always show meaningful text.
        /// </summary>
        private static string GetSelectorDisplay(ElementSelector selector, string fallback = "(no target)")
        {
            if (selector == null) return fallback;
            if (!selector.IsValid()) return fallback;
            var text = selector.GetDisplayText();
            return string.IsNullOrEmpty(text) ? fallback : text;
        }

        /// <summary>
        /// Runs a visual test asynchronously.
        /// </summary>
        /// <param name="test">The visual test to run</param>
        /// <param name="progress">Optional progress callback</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Test result with per-step results</returns>
        public static async Task<VisualTestResult> RunAsync(
            VisualTest test,
            Action<VisualTestProgress> progress = null,
            CancellationToken ct = default)
        {
            if (test == null)
                throw new ArgumentNullException(nameof(test));

            var result = new VisualTestResult { Test = test };
            var startTime = Time.realtimeSinceStartup;

            try
            {
                // Load start scene if specified
                if (!string.IsNullOrEmpty(test.startScene))
                {
                    progress?.Invoke(new VisualTestProgress
                    {
                        CurrentStep = -1,
                        TotalSteps = test.blocks.Count,
                        Message = $"Loading scene: {test.startScene}"
                    });

                    await LoadSceneAsync(test.startScene, ct);
                    await Task.Delay(500, cancellationToken: ct); // Allow scene to settle
                }

                // Execute each block
                for (int i = 0; i < test.blocks.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var block = test.blocks[i];

                    progress?.Invoke(new VisualTestProgress
                    {
                        CurrentStep = i,
                        TotalSteps = test.blocks.Count,
                        CurrentBlock = block,
                        Message = $"Step {i + 1}/{test.blocks.Count}: {block.GetDisplayText()}"
                    });

                    var blockResult = await ExecuteBlockAsync(block, i, ct);
                    result.BlockResults.Add(blockResult);

                    if (!blockResult.Success)
                    {
                        result.Passed = false;
                        result.Error = $"Step {i + 1} failed: {blockResult.Error}";
                        result.TotalExecutionTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                        return result;
                    }
                }

                result.Passed = true;
                result.TotalExecutionTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;
            }
            catch (OperationCanceledException)
            {
                result.Passed = false;
                result.Error = "Test cancelled";
                result.TotalExecutionTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.Error = $"Unexpected error: {ex.Message}";
                result.TotalExecutionTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                Debug.LogException(ex);
            }

            return result;
        }

        /// <summary>
        /// Executes a single visual block.
        /// </summary>
        public static async Task<VisualBlockResult> ExecuteBlockAsync(
            VisualBlock block,
            int stepIndex = 0,
            CancellationToken ct = default)
        {
            var startTime = Time.realtimeSinceStartup;

            try
            {
                switch (block.type)
                {
                    case BlockType.Click:
                        await ActionExecutor.Click(GetSearchOrThrow(block.target, "Click"), DefaultTimeoutMs / 1000f);
                        break;

                    case BlockType.DoubleClick:
                        await ActionExecutor.DoubleClick(GetSearchOrThrow(block.target, "DoubleClick"), DefaultTimeoutMs / 1000f);
                        break;

                    case BlockType.TripleClick:
                        await ActionExecutor.TripleClick(GetSearchOrThrow(block.target, "TripleClick"), DefaultTimeoutMs / 1000f);
                        break;

                    case BlockType.Hold:
                        await ActionExecutor.Hold(GetSearchOrThrow(block.target, "Hold"), block.holdSeconds, DefaultTimeoutMs / 1000f);
                        break;

                    case BlockType.Type:
                        await ActionExecutor.Type(GetSearchOrThrow(block.target, "Type"), block.text, block.clearFirst, block.pressEnter, DefaultTimeoutMs / 1000f);
                        break;

                    case BlockType.Drag:
                        {
                            var fromSearch = GetSearchOrThrow(block.target, "Drag");
                            if (block.dragTarget != null && block.dragTarget.IsValid())
                                await ActionExecutor.DragTo(fromSearch, GetSearchOrThrow(block.dragTarget, "Drag target"), block.dragDuration, DefaultTimeoutMs / 1000f);
                            else if (!string.IsNullOrEmpty(block.dragDirection))
                                await ActionExecutor.Drag(fromSearch, InputInjector.GetDirectionOffset(block.dragDirection, block.dragDistance), block.dragDuration, DefaultTimeoutMs / 1000f);
                            else
                                throw new InvalidOperationException("Drag block has no target or direction specified");
                        }
                        break;

                    case BlockType.Scroll:
                        await ActionExecutor.Scroll(GetSearchOrThrow(block.target, "Scroll"), block.scrollDirection, block.scrollAmount, DefaultTimeoutMs / 1000f);
                        break;

                    case BlockType.Wait:
                        Debug.Log($"[VisualTestRunner] Waiting {block.waitSeconds}s");
                        await Task.Delay(Mathf.Max(0, (int)(block.waitSeconds * 1000f)), cancellationToken: ct);
                        break;

                    case BlockType.Assert:
                        await ExecuteAssertAsync(block, ct);
                        break;

                    case BlockType.KeyPress:
                        {
                            var keyName = block.keyName ?? "Escape";
                            if (!Enum.TryParse<UnityEngine.InputSystem.Key>(keyName, true, out var key))
                                throw new InvalidOperationException($"Unknown key: {keyName}");
                            await ActionExecutor.PressKey(key);
                        }
                        break;

                    case BlockType.KeyHold:
                        {
                            var keyNames = (block.keyHoldKeys ?? "W").Split(',', StringSplitOptions.RemoveEmptyEntries);
                            var keys = new List<UnityEngine.InputSystem.Key>();
                            foreach (var kn in keyNames)
                            {
                                if (Enum.TryParse<UnityEngine.InputSystem.Key>(kn.Trim(), true, out var k))
                                    keys.Add(k);
                            }
                            if (keys.Count == 0)
                                throw new InvalidOperationException($"No valid keys specified: {block.keyHoldKeys}");
                            await ActionExecutor.HoldKeys(block.keyHoldDuration, keys.ToArray());
                        }
                        break;

                    case BlockType.WaitForElement:
                        await ActionExecutor.WaitFor(GetSearchOrThrow(block.target, "WaitForElement"), block.waitTimeout);
                        break;

                    case BlockType.ScrollUntil:
                        {
                            var targetSearch = GetSearchOrThrow(block.target, "ScrollUntil");
                            Search scrollViewSearch = block.scrollContainer?.IsValid() == true
                                ? GetSearchOrThrow(block.scrollContainer, "ScrollUntil container")
                                : new Search().Type<ScrollRect>();
                            await ActionExecutor.ScrollTo(scrollViewSearch, targetSearch, block.scrollMaxAttempts);
                        }
                        break;

                    case BlockType.Screenshot:
                        await ActionExecutor.Screenshot(block.screenshotName);
                        break;

                    case BlockType.Log:
                        Debug.Log($"[VisualTest] {block.logMessage ?? "(no message)"}");
                        break;

                    case BlockType.RunCode:
                        await ExecuteRunCodeAsync(block, ct);
                        break;

                    case BlockType.SetSlider:
                        await ActionExecutor.SetSlider(GetSearchOrThrow(block.target, "SetSlider"), block.sliderValue / 100f);
                        break;

                    case BlockType.SetScrollbar:
                        await ActionExecutor.SetScrollbar(GetSearchOrThrow(block.target, "SetScrollbar"), block.scrollbarValue / 100f);
                        break;

                    case BlockType.ClickDropdown:
                        {
                            var search = GetSearchOrThrow(block.target, "ClickDropdown");
                            if (block.dropdownIndex >= 0)
                                await ActionExecutor.ClickDropdown(search, block.dropdownIndex);
                            else if (!string.IsNullOrEmpty(block.dropdownLabel))
                                await ActionExecutor.ClickDropdown(search, block.dropdownLabel);
                            else
                                throw new InvalidOperationException("ClickDropdown requires either dropdownIndex or dropdownLabel");
                        }
                        break;

                    case BlockType.Swipe:
                        {
                            var search = GetSearchOrThrow(block.target, "Swipe");
                            if (!Enum.TryParse<ODDGames.UIAutomation.SwipeDirection>(block.swipeDirection, true, out var direction))
                                throw new InvalidOperationException($"Unknown swipe direction: {block.swipeDirection}");
                            await ActionExecutor.Swipe(search, direction, block.swipeDistance, block.swipeDuration);
                        }
                        break;

                    case BlockType.Pinch:
                        if (block.pinchCenterPosition.HasValue)
                            await ActionExecutor.PinchAt(block.pinchCenterPosition.Value, block.pinchScale, block.pinchDuration);
                        else if (block.target != null && block.target.IsValid())
                            await ActionExecutor.Pinch(GetSearchOrThrow(block.target, "Pinch"), block.pinchScale, block.pinchDuration);
                        else
                            await ActionExecutor.Pinch(block.pinchScale, block.pinchDuration);
                        break;

                    case BlockType.TwoFingerSwipe:
                        if (block.target != null && block.target.IsValid())
                            await ActionExecutor.TwoFingerSwipe(GetSearchOrThrow(block.target, "TwoFingerSwipe"), block.swipeDirection, block.swipeDistance, block.swipeDuration, block.twoFingerSpacing);
                        else
                            await ActionExecutor.TwoFingerSwipeAt(new Vector2(Screen.width / 2f, Screen.height / 2f), block.swipeDirection, block.swipeDistance, block.swipeDuration, block.twoFingerSpacing);
                        break;

                    case BlockType.Rotate:
                        if (block.target != null && block.target.IsValid())
                            await ActionExecutor.Rotate(GetSearchOrThrow(block.target, "Rotate"), block.rotateDegrees, block.rotateDuration, block.rotateFingerDistance);
                        else
                            await ActionExecutor.Rotate(block.rotateDegrees, block.rotateDuration, block.rotateFingerDistance);
                        break;

                    case BlockType.ForEach:
                    case BlockType.EndForEach:
                        // ForEach/EndForEach are handled by the test runner's loop logic, not here
                        break;

                    default:
                        return VisualBlockResult.Failed(block, stepIndex, $"Unknown block type: {block.type}");
                }

                // Post-action delay for UI to update
                await Task.Delay(PostActionDelayMs, cancellationToken: ct);

                var executionTime = (Time.realtimeSinceStartup - startTime) * 1000f;
                return VisualBlockResult.Succeeded(block, stepIndex, executionTime);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var executionTime = (Time.realtimeSinceStartup - startTime) * 1000f;
                Debug.LogError($"[VisualTestRunner] Block execution failed: {ex.Message}");
                return VisualBlockResult.Failed(block, stepIndex, ex.Message, executionTime);
            }
        }

        #region Block Execution Methods

        private static Search GetSearchOrThrow(ElementSelector selector, string actionName)
        {
            if (selector == null || !selector.IsValid())
                throw new InvalidOperationException($"{actionName} target selector is invalid");

            var search = selector.ToSearch();
            if (search == null)
                throw new InvalidOperationException($"{actionName} target could not be converted to Search");

            return search;
        }

        private static async Task ExecuteRunCodeAsync(VisualBlock block, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(block.codeBody))
                throw new InvalidOperationException("RunCode block has no code");

            Debug.Log($"[VisualTestRunner] Executing custom code...");

            if (block.codeIsAsync)
            {
                await RuntimeCodeCompiler.ExecuteAsync(block.codeBody);
            }
            else
            {
                RuntimeCodeCompiler.Execute(block.codeBody);
                await Task.Yield();
            }

            Debug.Log($"[VisualTestRunner] Custom code executed");
        }

        private static async Task ExecuteAssertAsync(VisualBlock block, CancellationToken ct)
        {
            await Task.Yield(); // Allow a frame for UI to update

            switch (block.assertCondition)
            {
                case AssertCondition.ElementExists:
                    await AssertElementExistsAsync(block, true, ct);
                    break;

                case AssertCondition.ElementNotExists:
                    await AssertElementExistsAsync(block, false, ct);
                    break;

                case AssertCondition.TextEquals:
                    await AssertTextAsync(block, false, ct);
                    break;

                case AssertCondition.TextContains:
                    await AssertTextAsync(block, true, ct);
                    break;

                case AssertCondition.ToggleIsOn:
                    await AssertToggleStateAsync(block, true, ct);
                    break;

                case AssertCondition.ToggleIsOff:
                    await AssertToggleStateAsync(block, false, ct);
                    break;

                case AssertCondition.SliderValue:
                    await AssertSliderValueAsync(block, ct);
                    break;

                case AssertCondition.DropdownIndex:
                    await AssertDropdownIndexAsync(block, ct);
                    break;

                case AssertCondition.DropdownText:
                    await AssertDropdownTextAsync(block, ct);
                    break;

                case AssertCondition.InputValue:
                    await AssertInputValueAsync(block, ct);
                    break;

                case AssertCondition.CustomExpression:
                    await AssertCustomExpressionAsync(block, ct);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown assert condition: {block.assertCondition}");
            }
        }

        #endregion

        #region Assert Helpers

        private static async Task AssertElementExistsAsync(VisualBlock block, bool shouldExist, CancellationToken ct)
        {
            var element = await TryResolveElementAsync(block.target, ct);
            bool exists = element?.gameObject != null;

            if (shouldExist)
                Assert.IsTrue(exists, $"Element not found: {GetSelectorDisplay(block.target)}");
            else
                Assert.IsFalse(exists, $"Element should not exist but found: {GetSelectorDisplay(block.target)}");
        }

        private static async Task AssertTextAsync(VisualBlock block, bool containsMode, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);
            Assert.IsNotNull(element?.gameObject, $"Assert target not found: {GetSelectorDisplay(block.target)}");

            string actualText = GetElementText(element.gameObject);
            string expectedText = block.assertExpected ?? "";

            if (containsMode)
                Assert.That(actualText, Does.Contain(expectedText).IgnoreCase, $"Text does not contain '{expectedText}'");
            else
                Assert.AreEqual(expectedText, actualText, $"Text mismatch");
        }

        private static async Task AssertToggleStateAsync(VisualBlock block, bool value, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);
            Assert.IsNotNull(element?.gameObject, $"Assert target not found: {GetSelectorDisplay(block.target)}");

            var toggle = element.gameObject.GetComponent<Toggle>();
            Assert.IsNotNull(toggle, $"Element '{element.name}' is not a Toggle");
            Assert.AreEqual(value, toggle.isOn, $"Toggle '{element.name}' is {(toggle.isOn ? "ON" : "OFF")} but expected {(value ? "ON" : "OFF")}");
        }

        private static async Task AssertSliderValueAsync(VisualBlock block, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);
            Assert.IsNotNull(element?.gameObject, $"Assert target not found: {GetSelectorDisplay(block.target)}");

            var slider = element.gameObject.GetComponent<Slider>();
            Assert.IsNotNull(slider, $"Element '{element.name}' is not a Slider");

            float actual = slider.normalizedValue;
            float expected = block.assertFloatValue;
            float variance = block.assertVariance;

            Assert.That(actual, Is.EqualTo(expected).Within(variance), $"Slider '{element.name}' value mismatch");
        }

        private static async Task AssertDropdownIndexAsync(VisualBlock block, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);
            Assert.IsNotNull(element?.gameObject, $"Assert target not found: {GetSelectorDisplay(block.target)}");

            int actual = -1;
            var tmpDropdown = element.gameObject.GetComponent<TMPro.TMP_Dropdown>();
            if (tmpDropdown != null)
                actual = tmpDropdown.value;
            else
            {
                var legacyDropdown = element.gameObject.GetComponent<Dropdown>();
                if (legacyDropdown != null)
                    actual = legacyDropdown.value;
            }

            Assert.That(actual, Is.GreaterThanOrEqualTo(0), $"Element '{element.name}' is not a Dropdown");
            Assert.AreEqual(block.assertIntValue, actual, $"Dropdown '{element.name}' index mismatch");
        }

        private static async Task AssertDropdownTextAsync(VisualBlock block, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);
            Assert.IsNotNull(element?.gameObject, $"Assert target not found: {GetSelectorDisplay(block.target)}");

            string actual = null;
            var tmpDropdown = element.gameObject.GetComponent<TMPro.TMP_Dropdown>();
            if (tmpDropdown != null && tmpDropdown.options.Count > tmpDropdown.value)
                actual = tmpDropdown.options[tmpDropdown.value].text;
            else
            {
                var legacyDropdown = element.gameObject.GetComponent<Dropdown>();
                if (legacyDropdown != null && legacyDropdown.options.Count > legacyDropdown.value)
                    actual = legacyDropdown.options[legacyDropdown.value].text;
            }

            Assert.IsNotNull(actual, $"Element '{element.name}' is not a Dropdown or has no selection");
            Assert.AreEqual(block.assertExpected, actual, $"Dropdown '{element.name}' text mismatch");
        }

        private static async Task AssertInputValueAsync(VisualBlock block, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);
            Assert.IsNotNull(element?.gameObject, $"Assert target not found: {GetSelectorDisplay(block.target)}");

            string actual = null;
            var tmpInput = element.gameObject.GetComponent<TMPro.TMP_InputField>();
            if (tmpInput != null)
                actual = tmpInput.text;
            else
            {
                var legacyInput = element.gameObject.GetComponent<InputField>();
                if (legacyInput != null)
                    actual = legacyInput.text;
            }

            Assert.IsNotNull(actual, $"Element '{element.name}' is not an InputField");
            Assert.AreEqual(block.assertExpected, actual, $"InputField '{element.name}' value mismatch");
        }

        private static async Task AssertCustomExpressionAsync(VisualBlock block, CancellationToken ct)
        {
            await Task.Yield();

            Assert.IsFalse(string.IsNullOrWhiteSpace(block.assertExpression), "No custom expression specified");

            var result = RuntimeCodeCompiler.EvaluateBoolExpression(block.assertExpression);
            Assert.IsTrue(result, $"Expression returned false: {block.assertExpression}");
        }

        #endregion

        #region Element Resolution

        /// <summary>
        /// Resolves an ElementSelector to an actual ElementInfo.
        /// Throws if element cannot be found.
        /// </summary>
        private static async Task<ElementInfo> ResolveElementAsync(
            ElementSelector selector,
            CancellationToken ct,
            int timeoutMs = DefaultTimeoutMs)
        {
            var element = await TryResolveElementAsync(selector, ct, timeoutMs);
            if (element == null)
            {
                throw new InvalidOperationException($"Element not found: {GetSelectorDisplay(selector, "(null selector)")}");
            }
            return element;
        }

        /// <summary>
        /// Tries to resolve an ElementSelector to an actual ElementInfo.
        /// Returns null if element cannot be found within timeout.
        /// </summary>
        private static async Task<ElementInfo> TryResolveElementAsync(
            ElementSelector selector,
            CancellationToken ct,
            int timeoutMs = DefaultTimeoutMs)
        {
            if (selector == null || !selector.IsValid())
                return null;

            var startTime = Time.realtimeSinceStartup;
            var timeout = timeoutMs / 1000f;

            while (Time.realtimeSinceStartup - startTime < timeout)
            {
                ct.ThrowIfCancellationRequested();

                var elements = ElementDiscovery.DiscoverElements();
                var matched = FindMatchingElement(elements, selector);

                if (matched != null)
                    return matched;

                await Task.Delay(ElementWaitDelayMs, cancellationToken: ct);
            }

            return null;
        }

        /// <summary>
        /// Finds an element matching the selector from the discovered elements.
        /// </summary>
        private static ElementInfo FindMatchingElement(List<ElementInfo> elements, ElementSelector selector)
        {
            // Convert selector to Search and execute
            var search = selector.ToSearch();
            if (search == null)
                return null;

            // Find the first matching GameObject using the Search API
            var go = search.FindFirst();
            if (go == null)
                return null;

            // Find the corresponding ElementInfo if it exists in discovered elements
            var existingElement = elements?.FirstOrDefault(e => e.gameObject == go);
            if (existingElement != null)
                return existingElement;

            // Element not in discovered list (not a Selectable) - create a minimal ElementInfo
            var bounds = InputInjector.GetScreenBounds(go);
            return new ElementInfo
            {
                id = go.name,
                gameObject = go,
                name = go.name,
                type = "element",
                bounds = bounds,
                normalizedBounds = new Rect(
                    bounds.x / Screen.width,
                    bounds.y / Screen.height,
                    bounds.width / Screen.width,
                    bounds.height / Screen.height
                ),
                isEnabled = go.activeInHierarchy
            };
        }

        private static ElementInfo FindByNamePattern(List<ElementInfo> elements, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return null;

            // Check for exact match first
            foreach (var e in elements)
            {
                if (string.Equals(e.name, pattern, StringComparison.OrdinalIgnoreCase))
                    return e;
            }

            // Try wildcard matching
            if (pattern.Contains("*") || pattern.Contains("?"))
            {
                var regex = WildcardToRegex(pattern);
                foreach (var e in elements)
                {
                    if (regex.IsMatch(e.name))
                        return e;
                }
            }

            // Try contains match
            foreach (var e in elements)
            {
                if (e.name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return e;
            }

            return null;
        }

        private static ElementInfo FindByTextPattern(List<ElementInfo> elements, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return null;

            var matches = ElementDiscovery.FindElementsByText(elements, pattern);
            return matches.Count > 0 ? matches[0] : null;
        }

        private static ElementInfo FindByPath(List<ElementInfo> elements, string pathPattern)
        {
            if (string.IsNullOrEmpty(pathPattern))
                return null;

            foreach (var e in elements)
            {
                if (e.path != null && e.path.EndsWith(pathPattern, StringComparison.OrdinalIgnoreCase))
                    return e;
            }

            return null;
        }

        private static Regex WildcardToRegex(string pattern)
        {
            var escaped = Regex.Escape(pattern);
            escaped = escaped.Replace("\\*", ".*").Replace("\\?", ".");
            return new Regex($"^{escaped}$", RegexOptions.IgnoreCase);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Loads a scene asynchronously.
        /// </summary>
        private static async Task LoadSceneAsync(string sceneName, CancellationToken ct)
        {
            var asyncOp = SceneManager.LoadSceneAsync(sceneName);
            if (asyncOp == null)
                throw new InvalidOperationException($"Failed to load scene: {sceneName}");

            while (!asyncOp.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        /// <summary>
        /// Gets the text content of a UI element.
        /// </summary>
        private static string GetElementText(GameObject go)
        {
            // Check TMP_Text first
            var tmpText = go.GetComponentInChildren<TMP_Text>();
            if (tmpText != null)
                return tmpText.text ?? "";

            // Check legacy Text
            var legacyText = go.GetComponentInChildren<Text>();
            if (legacyText != null)
                return legacyText.text ?? "";

            // Check TMP_InputField
            var tmpInput = go.GetComponent<TMP_InputField>();
            if (tmpInput != null)
                return tmpInput.text ?? "";

            // Check legacy InputField
            var legacyInput = go.GetComponent<InputField>();
            if (legacyInput != null)
                return legacyInput.text ?? "";

            return "";
        }

        #endregion
    }
}
