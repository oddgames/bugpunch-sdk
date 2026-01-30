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
                        await ExecuteClickAsync(block, ct);
                        break;

                    case BlockType.DoubleClick:
                        await ExecuteDoubleClickAsync(block, ct);
                        break;

                    case BlockType.TripleClick:
                        await ExecuteTripleClickAsync(block, ct);
                        break;

                    case BlockType.Hold:
                        await ExecuteHoldAsync(block, ct);
                        break;

                    case BlockType.Type:
                        await ExecuteTypeAsync(block, ct);
                        break;

                    case BlockType.Drag:
                        await ExecuteDragAsync(block, ct);
                        break;

                    case BlockType.Scroll:
                        await ExecuteScrollAsync(block, ct);
                        break;

                    case BlockType.Wait:
                        await ExecuteWaitAsync(block, ct);
                        break;

                    case BlockType.Assert:
                        await ExecuteAssertAsync(block, ct);
                        break;

                    case BlockType.KeyPress:
                        await ExecuteKeyPressAsync(block, ct);
                        break;

                    case BlockType.KeyHold:
                        await ExecuteKeyHoldAsync(block, ct);
                        break;

                    case BlockType.WaitForElement:
                        await ExecuteWaitForElementAsync(block, ct);
                        break;

                    case BlockType.ScrollUntil:
                        await ExecuteScrollUntilAsync(block, ct);
                        break;

                    case BlockType.Screenshot:
                        await ExecuteScreenshotAsync(block, ct);
                        break;

                    case BlockType.Log:
                        ExecuteLog(block);
                        break;

                    case BlockType.RunCode:
                        await ExecuteRunCodeAsync(block, ct);
                        break;

                    case BlockType.VisualScript:
                        await ExecuteVisualScriptAsync(block, ct);
                        break;

                    case BlockType.SetSlider:
                        await ExecuteSetSliderAsync(block, ct);
                        break;

                    case BlockType.SetScrollbar:
                        await ExecuteSetScrollbarAsync(block, ct);
                        break;

                    case BlockType.ClickDropdown:
                        await ExecuteClickDropdownAsync(block, ct);
                        break;

                    case BlockType.Swipe:
                        await ExecuteSwipeAsync(block, ct);
                        break;

                    case BlockType.Pinch:
                        await ExecutePinchAsync(block, ct);
                        break;

                    case BlockType.TwoFingerSwipe:
                        await ExecuteTwoFingerSwipeAsync(block, ct);
                        break;

                    case BlockType.Rotate:
                        await ExecuteRotateAsync(block, ct);
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

        private static async Task ExecuteClickAsync(VisualBlock block, CancellationToken ct)
        {
            var search = GetSearchOrThrow(block.target, "Click");
            var success = await ActionExecutor.Click(search, DefaultTimeoutMs / 1000f);
            if (!success)
                throw new InvalidOperationException($"Click target not found: {GetSelectorDisplay(block.target)}");
        }

        private static async Task ExecuteDoubleClickAsync(VisualBlock block, CancellationToken ct)
        {
            var search = GetSearchOrThrow(block.target, "DoubleClick");
            var success = await ActionExecutor.DoubleClick(search, DefaultTimeoutMs / 1000f);
            if (!success)
                throw new InvalidOperationException($"DoubleClick target not found: {GetSelectorDisplay(block.target)}");
        }

        private static async Task ExecuteTripleClickAsync(VisualBlock block, CancellationToken ct)
        {
            var search = GetSearchOrThrow(block.target, "TripleClick");
            var success = await ActionExecutor.TripleClick(search, DefaultTimeoutMs / 1000f);
            if (!success)
                throw new InvalidOperationException($"TripleClick target not found: {GetSelectorDisplay(block.target)}");
        }

        private static async Task ExecuteHoldAsync(VisualBlock block, CancellationToken ct)
        {
            var search = GetSearchOrThrow(block.target, "Hold");
            var success = await ActionExecutor.Hold(search, block.holdSeconds, DefaultTimeoutMs / 1000f);
            if (!success)
                throw new InvalidOperationException($"Hold target not found: {GetSelectorDisplay(block.target)}");
        }

        private static async Task ExecuteTypeAsync(VisualBlock block, CancellationToken ct)
        {
            var search = GetSearchOrThrow(block.target, "Type");
            var success = await ActionExecutor.Type(search, block.text, block.clearFirst, block.pressEnter, DefaultTimeoutMs / 1000f);
            if (!success)
                throw new InvalidOperationException($"Type target not found: {GetSelectorDisplay(block.target)}");
        }

        private static async Task ExecuteDragAsync(VisualBlock block, CancellationToken ct)
        {
            var fromSearch = GetSearchOrThrow(block.target, "Drag source");

            // Determine end position and execute drag
            if (block.dragTarget != null && block.dragTarget.IsValid())
            {
                // Drag to another element
                var toSearch = GetSearchOrThrow(block.dragTarget, "Drag target");
                var success = await ActionExecutor.DragTo(fromSearch, toSearch, block.dragDuration, DefaultTimeoutMs / 1000f);
                if (!success)
                    throw new InvalidOperationException($"Drag failed: source or target not found");
            }
            else if (!string.IsNullOrEmpty(block.dragDirection))
            {
                // Directional drag
                var offset = InputInjector.GetDirectionOffset(block.dragDirection, block.dragDistance);
                var success = await ActionExecutor.Drag(fromSearch, offset, block.dragDuration, DefaultTimeoutMs / 1000f);
                if (!success)
                    throw new InvalidOperationException($"Drag source not found: {GetSelectorDisplay(block.target)}");
            }
            else
            {
                throw new InvalidOperationException("Drag block has no target or direction specified");
            }
        }

        private static async Task ExecuteScrollAsync(VisualBlock block, CancellationToken ct)
        {
            var search = GetSearchOrThrow(block.target, "Scroll");
            var success = await ActionExecutor.Scroll(search, block.scrollDirection, block.scrollAmount, DefaultTimeoutMs / 1000f);
            if (!success)
                throw new InvalidOperationException($"Scroll target not found: {GetSelectorDisplay(block.target)}");
        }

        private static Search GetSearchOrThrow(ElementSelector selector, string actionName)
        {
            if (selector == null || !selector.IsValid())
                throw new InvalidOperationException($"{actionName} target selector is invalid");

            var search = selector.ToSearch();
            if (search == null)
                throw new InvalidOperationException($"{actionName} target could not be converted to Search");

            return search;
        }

        private static async Task ExecuteWaitAsync(VisualBlock block, CancellationToken ct)
        {
            var waitMs = Mathf.Max(0, (int)(block.waitSeconds * 1000f));
            Debug.Log($"[VisualTestRunner] Waiting {block.waitSeconds}s");
            await Task.Delay(waitMs, cancellationToken: ct);
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

        private static async Task ExecuteVisualScriptAsync(VisualBlock block, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(block.visualScriptEvent))
                throw new InvalidOperationException("VisualScript block has no event name");

            Debug.Log($"[VisualTestRunner] Triggering Visual Script event '{block.visualScriptEvent}'...");

            // Find target GameObject(s)
            GameObject[] targets;
            if (!string.IsNullOrEmpty(block.visualScriptTarget))
            {
                var target = GameObject.Find(block.visualScriptTarget);
                if (target == null)
                    throw new InvalidOperationException($"VisualScript target not found: {block.visualScriptTarget}");
                targets = new[] { target };
            }
            else
            {
                // Trigger on all ScriptMachines in scene
                targets = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            }

            // Use reflection to call Visual Scripting API (avoids hard dependency)
            var triggered = false;
            foreach (var target in targets)
            {
                triggered |= TriggerVisualScriptEvent(target, block.visualScriptEvent, block.visualScriptArg);
            }

            if (!triggered)
            {
                Debug.LogWarning($"[VisualTestRunner] No Visual Scripting graphs received event '{block.visualScriptEvent}'");
            }

            await Task.Yield();
            Debug.Log($"[VisualTestRunner] Visual Script event triggered");
        }

        private static bool TriggerVisualScriptEvent(GameObject target, string eventName, string arg)
        {
            // Try to find Unity.VisualScripting.CustomEvent.Trigger via reflection
            // This avoids a hard dependency on the Visual Scripting package
            try
            {
                var vsAssembly = System.AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Unity.VisualScripting.Flow");

                if (vsAssembly == null)
                {
                    Debug.LogWarning("[VisualTestRunner] Unity Visual Scripting package not found");
                    return false;
                }

                var customEventType = vsAssembly.GetType("Unity.VisualScripting.CustomEvent");
                if (customEventType == null)
                {
                    Debug.LogWarning("[VisualTestRunner] CustomEvent type not found");
                    return false;
                }

                // CustomEvent.Trigger(GameObject target, string name, params object[] args)
                var triggerMethod = customEventType.GetMethod("Trigger",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null,
                    new[] { typeof(GameObject), typeof(string), typeof(object[]) },
                    null);

                if (triggerMethod == null)
                {
                    Debug.LogWarning("[VisualTestRunner] CustomEvent.Trigger method not found");
                    return false;
                }

                object[] args = string.IsNullOrEmpty(arg)
                    ? Array.Empty<object>()
                    : new object[] { arg };

                triggerMethod.Invoke(null, new object[] { target, eventName, args });
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VisualTestRunner] Failed to trigger Visual Script event: {ex.Message}");
                return false;
            }
        }

        private static async Task ExecuteKeyHoldAsync(VisualBlock block, CancellationToken ct)
        {
            var keyNames = (block.keyHoldKeys ?? "W").Split(',', StringSplitOptions.RemoveEmptyEntries);
            var keys = new List<UnityEngine.InputSystem.Key>();

            foreach (var keyName in keyNames)
            {
                var trimmed = keyName.Trim();
                if (Enum.TryParse<UnityEngine.InputSystem.Key>(trimmed, true, out var key))
                {
                    keys.Add(key);
                }
                else
                {
                    Debug.LogWarning($"[VisualTestRunner] Unknown key: {trimmed}");
                }
            }

            if (keys.Count == 0)
            {
                throw new InvalidOperationException($"No valid keys specified: {block.keyHoldKeys}");
            }

            Debug.Log($"[VisualTestRunner] Holding keys [{string.Join(", ", keys)}] for {block.keyHoldDuration}s");

            if (keys.Count == 1)
            {
                await ActionExecutor.HoldKey(keys[0], block.keyHoldDuration);
            }
            else
            {
                await ActionExecutor.HoldKeys(block.keyHoldDuration, keys.ToArray());
            }
        }

        private static async Task ExecuteKeyPressAsync(VisualBlock block, CancellationToken ct)
        {
            var keyName = block.keyName ?? "Escape";
            if (!Enum.TryParse<UnityEngine.InputSystem.Key>(keyName, true, out var key))
            {
                throw new InvalidOperationException($"Unknown key: {keyName}");
            }

            Debug.Log($"[VisualTestRunner] Pressing key: {key}");
            await ActionExecutor.PressKey(key);
        }

        private static async Task ExecuteWaitForElementAsync(VisualBlock block, CancellationToken ct)
        {
            Debug.Log($"[VisualTestRunner] Waiting for element: {GetSelectorDisplay(block.target)}");

            var timeoutMs = Mathf.Max(1000, (int)(block.waitTimeout * 1000f));
            var element = await TryResolveElementAsync(block.target, ct, timeoutMs);

            if (element == null)
            {
                throw new InvalidOperationException($"Element did not appear within {block.waitTimeout}s: {GetSelectorDisplay(block.target)}");
            }

            Debug.Log($"[VisualTestRunner] Element appeared: {element.name}");
        }

        private static async Task ExecuteScrollUntilAsync(VisualBlock block, CancellationToken ct)
        {
            Debug.Log($"[VisualTestRunner] ScrollUntil: Looking for {GetSelectorDisplay(block.target)}");

            // Find the scroll container (if specified, otherwise try to find one near the target)
            GameObject scrollContainerGo = null;
            ScrollRect scrollRect = null;

            if (block.scrollContainer != null && block.scrollContainer.IsValid())
            {
                var containerElement = await TryResolveElementAsync(block.scrollContainer, ct, 2000);
                if (containerElement?.gameObject != null)
                {
                    scrollRect = containerElement.gameObject.GetComponent<ScrollRect>();
                    if (scrollRect == null)
                        scrollRect = containerElement.gameObject.GetComponentInChildren<ScrollRect>();
                    scrollContainerGo = scrollRect?.gameObject;
                }
            }

            // If no container specified, try to find any ScrollRect in the scene
            if (scrollRect == null)
            {
                scrollRect = UnityEngine.Object.FindAnyObjectByType<ScrollRect>();
                scrollContainerGo = scrollRect?.gameObject;
            }

            if (scrollRect == null)
            {
                throw new InvalidOperationException("No ScrollRect found for ScrollUntil action");
            }

            var maxAttempts = Mathf.Max(1, block.scrollMaxAttempts);
            var scrollAmount = 0.2f; // Scroll 20% each attempt

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                // Check if target element is now visible
                var targetElement = await TryResolveElementAsync(block.target, ct, 200);
                if (targetElement?.gameObject != null)
                {
                    Debug.Log($"[VisualTestRunner] Found target after {attempt} scrolls: {targetElement.name}");
                    return;
                }

                // Scroll down (or in the direction the ScrollRect allows)
                var center = InputInjector.GetScreenPosition(scrollContainerGo);
                Vector2 scrollDelta;

                if (scrollRect.vertical)
                {
                    scrollDelta = new Vector2(0, -scrollAmount * 500f); // Negative = scroll down
                }
                else if (scrollRect.horizontal)
                {
                    scrollDelta = new Vector2(-scrollAmount * 500f, 0); // Negative = scroll right
                }
                else
                {
                    scrollDelta = new Vector2(0, -scrollAmount * 500f);
                }

                Debug.Log($"[VisualTestRunner] Scroll attempt {attempt + 1}/{maxAttempts}");
                await InputInjector.InjectScroll(center, scrollDelta);
                await Task.Delay(150, cancellationToken: ct); // Wait for scroll animation
            }

            throw new InvalidOperationException($"Element not found after {maxAttempts} scroll attempts: {GetSelectorDisplay(block.target)}");
        }

        private static async Task ExecuteScreenshotAsync(VisualBlock block, CancellationToken ct)
        {
            await Task.Yield(); // Wait for rendering

            var filename = block.screenshotName;
            if (string.IsNullOrEmpty(filename))
            {
                filename = $"screenshot_{System.DateTime.Now:yyyyMMdd_HHmmss}";
            }

            var path = System.IO.Path.Combine(Application.persistentDataPath, $"{filename}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[VisualTestRunner] Screenshot saved: {path}");
        }

        private static void ExecuteLog(VisualBlock block)
        {
            var message = block.logMessage ?? "(no message)";
            Debug.Log($"[VisualTest] {message}");
        }

        private static async Task ExecuteSetSliderAsync(VisualBlock block, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);
            if (element?.gameObject == null)
                throw new InvalidOperationException($"SetSlider target not found: {GetSelectorDisplay(block.target)}");

            var slider = element.gameObject.GetComponent<Slider>();
            if (slider == null)
                throw new InvalidOperationException($"Element '{element.name}' is not a Slider");

            // Convert percentage (0-100) to normalized (0-1)
            var normalizedValue = block.sliderValue / 100f;
            await ActionExecutor.SetSlider(block.target.ToSearch(), normalizedValue);
        }

        private static async Task ExecuteSetScrollbarAsync(VisualBlock block, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);
            if (element?.gameObject == null)
                throw new InvalidOperationException($"SetScrollbar target not found: {GetSelectorDisplay(block.target)}");

            var scrollbar = element.gameObject.GetComponent<Scrollbar>();
            if (scrollbar == null)
                throw new InvalidOperationException($"Element '{element.name}' is not a Scrollbar");

            // Convert percentage (0-100) to normalized (0-1)
            var normalizedValue = block.scrollbarValue / 100f;
            await ActionExecutor.SetScrollbar(block.target.ToSearch(), normalizedValue);
        }

        private static async Task ExecuteClickDropdownAsync(VisualBlock block, CancellationToken ct)
        {
            if (block.target == null || !block.target.IsValid())
                throw new InvalidOperationException("ClickDropdown requires a target element selector");

            var search = block.target.ToSearch();

            bool found;
            if (block.dropdownIndex >= 0)
            {
                found = await ActionExecutor.ClickDropdown(search, block.dropdownIndex);
            }
            else if (!string.IsNullOrEmpty(block.dropdownLabel))
            {
                found = await ActionExecutor.ClickDropdown(search, block.dropdownLabel);
            }
            else
            {
                throw new InvalidOperationException("ClickDropdown requires either dropdownIndex or dropdownLabel");
            }

            if (!found)
                throw new InvalidOperationException($"Dropdown not found: {GetSelectorDisplay(block.target)}");
        }

        private static async Task ExecuteSwipeAsync(VisualBlock block, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);
            if (element?.gameObject == null)
                throw new InvalidOperationException($"Swipe target not found: {GetSelectorDisplay(block.target)}");

            var screenPos = InputInjector.GetScreenPosition(element.gameObject);
            await ActionExecutor.SwipeAt(screenPos, block.swipeDirection, block.swipeDistance, block.swipeDuration);
        }

        private static async Task ExecutePinchAsync(VisualBlock block, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);

            // Block supports explicit center position override
            if (block.pinchCenterPosition.HasValue)
            {
                await ActionExecutor.PinchAt(block.pinchCenterPosition.Value, block.pinchScale, block.pinchDuration);
                return;
            }

            // Use element position if available, otherwise screen center
            var screenPos = element?.gameObject != null
                ? InputInjector.GetScreenPosition(element.gameObject)
                : new UnityEngine.Vector2(UnityEngine.Screen.width / 2f, UnityEngine.Screen.height / 2f);
            await ActionExecutor.PinchAt(screenPos, block.pinchScale, block.pinchDuration);
        }

        private static async Task ExecuteTwoFingerSwipeAsync(VisualBlock block, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);

            // Use element position if available, otherwise screen center
            var screenPos = element?.gameObject != null
                ? InputInjector.GetScreenPosition(element.gameObject)
                : new UnityEngine.Vector2(UnityEngine.Screen.width / 2f, UnityEngine.Screen.height / 2f);
            await ActionExecutor.TwoFingerSwipeAt(
                screenPos,
                block.swipeDirection,
                block.swipeDistance,
                block.swipeDuration,
                block.twoFingerSpacing);
        }

        private static async Task ExecuteRotateAsync(VisualBlock block, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);

            // Use element position if available, otherwise screen center
            var screenPos = element?.gameObject != null
                ? InputInjector.GetScreenPosition(element.gameObject)
                : new UnityEngine.Vector2(UnityEngine.Screen.width / 2f, UnityEngine.Screen.height / 2f);
            await ActionExecutor.RotateAt(
                screenPos,
                block.rotateDegrees,
                block.rotateDuration,
                block.rotateFingerDistance);
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

            if (shouldExist && !exists)
            {
                throw new InvalidOperationException($"Assert failed: Element not found: {GetSelectorDisplay(block.target)}");
            }
            else if (!shouldExist && exists)
            {
                throw new InvalidOperationException($"Assert failed: Element should not exist but found: {GetSelectorDisplay(block.target)}");
            }

            Debug.Log($"[VisualTestRunner] Assert {(shouldExist ? "exists" : "not exists")} passed for '{GetSelectorDisplay(block.target)}'");
        }

        private static async Task AssertTextAsync(VisualBlock block, bool containsMode, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);
            if (element?.gameObject == null)
                throw new InvalidOperationException($"Assert target not found: {GetSelectorDisplay(block.target)}");

            string actualText = GetElementText(element.gameObject);
            string expectedText = block.assertExpected ?? "";

            bool passed;
            if (containsMode)
            {
                passed = actualText.Contains(expectedText, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                passed = string.Equals(actualText, expectedText, StringComparison.Ordinal);
            }

            if (!passed)
            {
                var mode = containsMode ? "contain" : "equal";
                throw new InvalidOperationException($"Assert failed: Expected text to {mode} \"{expectedText}\" but got \"{actualText}\"");
            }

            Debug.Log($"[VisualTestRunner] Assert text {(containsMode ? "contains" : "equals")} passed: \"{actualText}\"");
        }

        private static async Task AssertToggleStateAsync(VisualBlock block, bool value, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);
            if (element?.gameObject == null)
                throw new InvalidOperationException($"Assert target not found: {GetSelectorDisplay(block.target)}");

            var toggle = element.gameObject.GetComponent<Toggle>();
            if (toggle == null)
            {
                throw new InvalidOperationException($"Assert failed: Element '{element.name}' is not a Toggle");
            }

            if (toggle.isOn != value)
            {
                throw new InvalidOperationException($"Assert failed: Toggle '{element.name}' is {(toggle.isOn ? "ON" : "OFF")} but expected {(value ? "ON" : "OFF")}");
            }

            Debug.Log($"[VisualTestRunner] Assert toggle state passed: {element.name} is {(toggle.isOn ? "ON" : "OFF")}");
        }

        private static async Task AssertSliderValueAsync(VisualBlock block, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);
            if (element?.gameObject == null)
                throw new InvalidOperationException($"Assert target not found: {GetSelectorDisplay(block.target)}");

            var slider = element.gameObject.GetComponent<Slider>();
            if (slider == null)
                throw new InvalidOperationException($"Assert failed: Element '{element.name}' is not a Slider");

            float actual = slider.normalizedValue;
            float expected = block.assertFloatValue;
            float variance = block.assertVariance;

            float diff = Mathf.Abs(actual - expected);
            if (diff > variance)
            {
                throw new InvalidOperationException($"Assert failed: Slider '{element.name}' value is {actual:F3} but expected {expected:F3} (±{variance:F3})");
            }

            Debug.Log($"[VisualTestRunner] Assert slider value passed: {element.name} = {actual:F3} (expected {expected:F3} ±{variance:F3})");
        }

        private static async Task AssertDropdownIndexAsync(VisualBlock block, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);
            if (element?.gameObject == null)
                throw new InvalidOperationException($"Assert target not found: {GetSelectorDisplay(block.target)}");

            int actual = -1;

            var tmpDropdown = element.gameObject.GetComponent<TMPro.TMP_Dropdown>();
            if (tmpDropdown != null)
            {
                actual = tmpDropdown.value;
            }
            else
            {
                var legacyDropdown = element.gameObject.GetComponent<Dropdown>();
                if (legacyDropdown != null)
                {
                    actual = legacyDropdown.value;
                }
            }

            if (actual < 0)
                throw new InvalidOperationException($"Assert failed: Element '{element.name}' is not a Dropdown");

            if (actual != block.assertIntValue)
            {
                throw new InvalidOperationException($"Assert failed: Dropdown '{element.name}' index is {actual} but expected {block.assertIntValue}");
            }

            Debug.Log($"[VisualTestRunner] Assert dropdown index passed: {element.name} = {actual}");
        }

        private static async Task AssertDropdownTextAsync(VisualBlock block, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);
            if (element?.gameObject == null)
                throw new InvalidOperationException($"Assert target not found: {GetSelectorDisplay(block.target)}");

            string actual = null;

            var tmpDropdown = element.gameObject.GetComponent<TMPro.TMP_Dropdown>();
            if (tmpDropdown != null && tmpDropdown.options.Count > tmpDropdown.value)
            {
                actual = tmpDropdown.options[tmpDropdown.value].text;
            }
            else
            {
                var legacyDropdown = element.gameObject.GetComponent<Dropdown>();
                if (legacyDropdown != null && legacyDropdown.options.Count > legacyDropdown.value)
                {
                    actual = legacyDropdown.options[legacyDropdown.value].text;
                }
            }

            if (actual == null)
                throw new InvalidOperationException($"Assert failed: Element '{element.name}' is not a Dropdown or has no selection");

            if (!string.Equals(actual, block.assertExpected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Assert failed: Dropdown '{element.name}' selected text is \"{actual}\" but expected \"{block.assertExpected}\"");
            }

            Debug.Log($"[VisualTestRunner] Assert dropdown text passed: {element.name} = \"{actual}\"");
        }

        private static async Task AssertInputValueAsync(VisualBlock block, CancellationToken ct)
        {
            var element = await ResolveElementAsync(block.target, ct);
            if (element?.gameObject == null)
                throw new InvalidOperationException($"Assert target not found: {GetSelectorDisplay(block.target)}");

            string actual = null;

            var tmpInput = element.gameObject.GetComponent<TMPro.TMP_InputField>();
            if (tmpInput != null)
            {
                actual = tmpInput.text;
            }
            else
            {
                var legacyInput = element.gameObject.GetComponent<InputField>();
                if (legacyInput != null)
                {
                    actual = legacyInput.text;
                }
            }

            if (actual == null)
                throw new InvalidOperationException($"Assert failed: Element '{element.name}' is not an InputField");

            if (!string.Equals(actual, block.assertExpected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Assert failed: InputField '{element.name}' value is \"{actual}\" but expected \"{block.assertExpected}\"");
            }

            Debug.Log($"[VisualTestRunner] Assert input value passed: {element.name} = \"{actual}\"");
        }

        private static async Task AssertCustomExpressionAsync(VisualBlock block, CancellationToken ct)
        {
            await Task.Yield();

            if (string.IsNullOrWhiteSpace(block.assertExpression))
                throw new InvalidOperationException("Assert failed: No custom expression specified");

            // Compile and execute the expression
            var result = RuntimeCodeCompiler.EvaluateBoolExpression(block.assertExpression);

            if (!result)
            {
                throw new InvalidOperationException($"Assert failed: Expression returned false: {block.assertExpression}");
            }

            Debug.Log($"[VisualTestRunner] Assert custom expression passed: {block.assertExpression}");
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
