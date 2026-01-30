using System.Threading.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ODDGames.UIAutomation
{
    /// <summary>
    /// Exception thrown when a UIAutomation operation fails.
    /// </summary>
    public class UIAutomationException : Exception
    {
        public UIAutomationException(string message) : base(message) { }
        public UIAutomationException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Exception thrown when a UIAutomation operation times out.
    /// </summary>
    public class UIAutomationTimeoutException : UIAutomationException
    {
        public UIAutomationTimeoutException(string message) : base(message) { }
    }

    /// <summary>
    /// Exception thrown when a UIAutomation element is not found.
    /// </summary>
    public class UIAutomationNotFoundException : UIAutomationException
    {
        public UIAutomationNotFoundException(string message) : base(message) { }
    }

    /// <summary>
    /// Context passed to recovery handlers when an action fails.
    /// The recovery handler should perform the full action (e.g., dismiss blockers AND click the target).
    /// </summary>
    public class RecoveryContext
    {
        /// <summary>
        /// Description of the action that failed (e.g., "Click the button named 'Submit'").
        /// This describes what the AI should accomplish, not just what element to find.
        /// </summary>
        public string FailedAction { get; set; }

        /// <summary>Error message from the failed action.</summary>
        public string ErrorMessage { get; set; }

        /// <summary>Cancellation token for the recovery operation.</summary>
        public System.Threading.CancellationToken CancellationToken { get; set; }
    }

    /// <summary>
    /// Result of a recovery attempt.
    /// </summary>
    public class RecoveryResult
    {
        /// <summary>Whether recovery succeeded and the original action now works.</summary>
        public bool Success { get; set; }

        /// <summary>
        /// Whether the handler determined there was no blocker present.
        /// If true, the test failure is genuine (not flaky) and should fail normally.
        /// </summary>
        public bool NoBlockerFound { get; set; }

        /// <summary>Explanation of what was blocking and what was done to recover.</summary>
        public string Explanation { get; set; }
    }

    /// <summary>
    /// Wrapper for navigating object properties via reflection.
    /// Supports fluent chaining and iteration over arrays/lists.
    /// </summary>
    public class StaticPath : IEnumerable<StaticPath>
    {
        private readonly object _value;

        public StaticPath(object value) => _value = value;

        /// <summary>
        /// Gets a typed value from this object or navigates a sub-path.
        /// </summary>
        public T GetValue<T>(string subPath = null)
        {
            var value = string.IsNullOrEmpty(subPath) ? _value : NavigatePath(_value, subPath);

            if (value == null)
            {
                if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                    return default;
                return default;
            }

            if (value is T typedValue)
                return typedValue;

            try { return (T)Convert.ChangeType(value, typeof(T)); }
            catch { return default; }
        }

        /// <summary>
        /// Navigates to a property/field and returns a new StaticPath.
        /// </summary>
        public StaticPath Property(string name)
        {
            var value = NavigatePath(_value, name);
            return new StaticPath(value);
        }

        /// <summary>
        /// Iterates over array/list elements, yielding StaticPath for each.
        /// </summary>
        public IEnumerator<StaticPath> GetEnumerator()
        {
            if (_value == null)
                yield break;

            if (_value is IEnumerable enumerable && !(_value is string))
            {
                foreach (var item in enumerable)
                {
                    if (item != null)
                        yield return new StaticPath(item);
                }
            }
            else
            {
                yield return this;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private static object NavigatePath(object current, string path)
        {
            if (current == null || string.IsNullOrEmpty(path))
                return current;

            var parts = path.Split('.');
            foreach (var part in parts)
            {
                if (current == null) return null;

                var type = current.GetType();
                var prop = type.GetProperty(part, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    current = prop.GetValue(current);
                    continue;
                }

                var field = type.GetField(part, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    current = field.GetValue(current);
                    continue;
                }

                return null;
            }
            return current;
        }

        /// <summary>The raw wrapped value.</summary>
        public object Value => _value;

        /// <summary>Gets the value as string, or calls ToString() if not a string.</summary>
        public string StringValue => _value as string ?? _value?.ToString();

        /// <summary>Gets the value as bool. Returns false if not a bool.</summary>
        public bool BoolValue => _value is bool b && b;

        /// <summary>Gets the value as float. Returns 0 if not convertible.</summary>
        public float FloatValue
        {
            get
            {
                if (_value is float f) return f;
                if (_value is double d) return (float)d;
                if (_value is int i) return i;
                if (float.TryParse(_value?.ToString(), out var result)) return result;
                return 0f;
            }
        }

        /// <summary>Gets the value as int. Returns 0 if not convertible.</summary>
        public int IntValue
        {
            get
            {
                if (_value is int i) return i;
                if (_value is float f) return (int)f;
                if (_value is double d) return (int)d;
                if (int.TryParse(_value?.ToString(), out var result)) return result;
                return 0;
            }
        }

        /// <summary>Gets the GameObject if value is a Component or GameObject.</summary>
        public GameObject GameObject
        {
            get
            {
                if (_value is GameObject go) return go;
                if (_value is Component comp) return comp.gameObject;
                return null;
            }
        }
    }

    /// <summary>
    /// Swipe direction for gesture actions.
    /// </summary>
    public enum SwipeDirection { Left, Right, Up, Down }

    /// <summary>
    /// Stop condition for simple auto-exploration via ActionExecutor.
    /// </summary>
    public enum SimpleExploreStopCondition
    {
        Time,        // Stop after specified seconds
        ActionCount, // Stop after specified number of actions
        DeadEnd      // Stop when no more clickable elements
    }

    /// <summary>
    /// Result from simple auto-exploration via ActionExecutor.
    /// </summary>
    public class SimpleExploreResult
    {
        public int ActionsPerformed { get; set; }
        public float TimeElapsed { get; set; }
        public List<string> ClickedElements { get; } = new List<string>();
        public SimpleExploreStopCondition StopReason { get; set; }
    }

    /// <summary>
    /// Container result from FindItems, containing the container component and its child items.
    /// </summary>
    public class ItemContainer
    {
        public Component Container { get; }
        public ScrollRect ScrollRect => Container as ScrollRect;
        public IEnumerable<RectTransform> Items { get; }

        public ItemContainer(Component container, IEnumerable<RectTransform> items)
        {
            Container = container;
            Items = items;
        }

        public IEnumerator<(Component Container, RectTransform Item)> GetEnumerator()
        {
            foreach (var item in Items)
            {
                yield return (Container, item);
            }
        }
    }

    /// <summary>
    /// Static utility class providing all UI automation test actions.
    /// Use with 'using static ODDGames.UIAutomation.ActionExecutor;' for shorthand access.
    /// </summary>
    /// <example>
    /// using static ODDGames.UIAutomation.ActionExecutor;
    ///
    /// [TestFixture]
    /// public class MyTests
    /// {
    ///     [Test]
    ///     public async Task TestLogin()
    ///     {
    ///         await EnsureSceneLoaded("LoginScene");
    ///         await TextInput(Name("Username"), "testuser");
    ///         await Click(Name("LoginButton"));
    ///     }
    /// }
    /// </example>
    public static class ActionExecutor
    {
        #region Configuration

        /// <summary>
        /// Gets or sets the delay in milliseconds after a successful action.
        /// Default is 200ms. Increase for slower, more visible test playback.
        /// </summary>
        public static int Interval { get; set; } = 200;

        /// <summary>
        /// Gets or sets the polling interval in milliseconds during search/wait operations.
        /// Default is 100ms.
        /// </summary>
        public static int PollInterval { get; set; } = 100;

        /// <summary>
        /// When true, increases all intervals and enables verbose logging for debugging tests.
        /// </summary>
        public static bool DebugMode { get; set; } = false;

        /// <summary>
        /// Multiplier for all intervals when DebugMode is enabled. Default is 3x.
        /// </summary>
        public static float DebugIntervalMultiplier { get; set; } = 3f;

        static int EffectiveInterval => DebugMode ? (int)(Interval * DebugIntervalMultiplier) : Interval;
        static int EffectivePollInterval => DebugMode ? (int)(PollInterval * DebugIntervalMultiplier) : PollInterval;

        /// <summary>
        /// Optional callback that runs before each ActionExecutor action.
        /// Use this to dismiss dialogs, handle popups, or perform other pre-action checks.
        /// The callback receives the action name and should return quickly.
        /// Set to null to disable.
        /// </summary>
        /// <example>
        /// ActionExecutor.BeforeAction = async (actionName) =>
        /// {
        ///     // Dismiss any "Rate Us" dialog
        ///     var rateDialog = Find(Name("RateUsDialog"));
        ///     if (rateDialog != null)
        ///         await Click(Name("CloseButton"));
        /// };
        /// </example>
        public static System.Func<string, Task> BeforeAction { get; set; }

        /// <summary>
        /// Callback invoked when an action fails and recovery is enabled.
        /// The handler should attempt to recover (dismiss dialogs, navigate, etc.) and return a result.
        /// Set this to use AI recovery, coded recovery, or a custom implementation.
        /// </summary>
        /// <example>
        /// // Use AI recovery (requires ODDGames.UIAutomation.AI namespace)
        /// AINavigator.SetModelProvider(AITestSettings.Instance.CreateModelProvider());
        /// ActionExecutor.RecoveryHandler = AINavigator.CreateRecoveryHandler();
        ///
        /// // Use coded recovery
        /// ActionExecutor.RecoveryHandler = async (context) => {
        ///     // Check for common dialogs
        ///     var closeBtn = Find(Name("CloseButton"));
        ///     if (closeBtn != null) {
        ///         await Click(closeBtn);
        ///         return new RecoveryResult { Success = true, Explanation = "Dismissed dialog" };
        ///     }
        ///     return new RecoveryResult { Success = false, NoBlockerFound = true };
        /// };
        /// </example>
        public static System.Func<RecoveryContext, Task<RecoveryResult>> RecoveryHandler { get; set; }

        /// <summary>
        /// Set to true when recovery was used during the current test.
        /// Check this in TearDown to mark the test as unstable/flaky.
        /// Call <see cref="ResetRecoveryTracking"/> at the start of each test.
        /// </summary>
        public static bool RecoveryUsed { get; private set; } = false;

        /// <summary>
        /// Number of times recovery was invoked during the current test.
        /// </summary>
        public static int RecoveryCount { get; private set; } = 0;

        /// <summary>
        /// Explanation of what recovery actions were taken.
        /// Contains details about what blockers were encountered and how they were resolved.
        /// </summary>
        public static string RecoveryExplanation { get; private set; } = "";

        /// <summary>
        /// Resets recovery tracking. Call at the start of each test.
        /// </summary>
        public static void ResetRecoveryTracking()
        {
            RecoveryUsed = false;
            RecoveryCount = 0;
            RecoveryExplanation = "";
        }

        /// <summary>
        /// Records that recovery was used.
        /// </summary>
        internal static void RecordRecovery(string explanation)
        {
            RecoveryUsed = true;
            RecoveryCount++;
            if (!string.IsNullOrEmpty(RecoveryExplanation))
                RecoveryExplanation += "\n---\n";
            RecoveryExplanation += explanation;
        }

        // Legacy alias
        internal static void RecordAINavigation(string explanation) => RecordRecovery(explanation);

        /// <summary>
        /// Whether recovery is enabled.
        /// </summary>
        internal static bool IsRecoveryEnabled => RecoveryHandler != null;

        /// <summary>
        /// Resolves a search query to a GameObject, with automatic recovery on timeout.
        /// Polls for the element, and if not found within timeout, calls recovery handler
        /// to clear obstacles (dialogs, etc.) then continues polling.
        /// </summary>
        /// <param name="search">Search query</param>
        /// <param name="timeout">Total time to search</param>
        /// <param name="index">Index when multiple elements match</param>
        /// <param name="actionDescription">Description of the action (for recovery context)</param>
        /// <returns>The found element, or null if not found</returns>
        internal static async Task<GameObject> ResolveSearch(Search search, float timeout, int index, string actionDescription)
        {
            float startTime = Time.realtimeSinceStartup;
            float recoveryAttemptedAt = -1f;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                    return results[index];

                // Try recovery once after initial polling fails (at ~1/3 of timeout)
                float elapsed = Time.realtimeSinceStartup - startTime;
                if (recoveryAttemptedAt < 0 && elapsed > timeout * 0.3f && RecoveryHandler != null)
                {
                    recoveryAttemptedAt = elapsed;

                    var context = new RecoveryContext
                    {
                        FailedAction = actionDescription,
                        ErrorMessage = $"Element not found: {search}",
                        CancellationToken = default
                    };

                    var recoveryResult = await RecoveryHandler(context);
                    RecordRecovery(recoveryResult.Explanation ?? "Recovery attempted");

                    // If recovery says no blocker found, don't waste remaining time
                    if (recoveryResult.NoBlockerFound)
                        break;

                    // Otherwise continue polling - recovery may have cleared the way
                }

                await Task.Delay(100);
            }

            return null;
        }

        /// <summary>
        /// Resolves a search query to all matching GameObjects, with automatic recovery on timeout.
        /// </summary>
        internal static async Task<List<GameObject>> ResolveSearchAll(Search search, float timeout, string actionDescription)
        {
            float startTime = Time.realtimeSinceStartup;
            float recoveryAttemptedAt = -1f;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > 0)
                    return results;

                float elapsed = Time.realtimeSinceStartup - startTime;
                if (recoveryAttemptedAt < 0 && elapsed > timeout * 0.3f && RecoveryHandler != null)
                {
                    recoveryAttemptedAt = elapsed;

                    var context = new RecoveryContext
                    {
                        FailedAction = actionDescription,
                        ErrorMessage = $"No elements found: {search}",
                        CancellationToken = default
                    };

                    var recoveryResult = await RecoveryHandler(context);
                    RecordRecovery(recoveryResult.Explanation ?? "Recovery attempted");

                    if (recoveryResult.NoBlockerFound)
                        break;
                }

                await Task.Delay(100);
            }

            return new List<GameObject>();
        }

        /// <summary>
        /// Resolves a search query to a Dropdown by label, with automatic recovery on timeout.
        /// </summary>
        internal static async Task<(GameObject element, RectTransform template, int optionIndex)> ResolveDropdownByLabel(Search search, string optionLabel, float timeout, string actionDescription)
        {
            float startTime = Time.realtimeSinceStartup;
            float recoveryAttemptedAt = -1f;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                foreach (var go in search.FindAll())
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

                float elapsed = Time.realtimeSinceStartup - startTime;
                if (recoveryAttemptedAt < 0 && elapsed > timeout * 0.3f && RecoveryHandler != null)
                {
                    recoveryAttemptedAt = elapsed;

                    var context = new RecoveryContext
                    {
                        FailedAction = actionDescription,
                        ErrorMessage = $"Dropdown with option '{optionLabel}' not found: {search}",
                        CancellationToken = default
                    };

                    var recoveryResult = await RecoveryHandler(context);
                    RecordRecovery(recoveryResult.Explanation ?? "Recovery attempted");

                    if (recoveryResult.NoBlockerFound)
                        break;
                }

                await Task.Delay(100);
            }

            return (null, null, -1);
        }

        /// <summary>
        /// Resolves a search query to a Dropdown (legacy or TMP), with automatic recovery on timeout.
        /// </summary>
        internal static async Task<(GameObject element, RectTransform template)> ResolveDropdown(Search search, float timeout, string actionDescription)
        {
            float startTime = Time.realtimeSinceStartup;
            float recoveryAttemptedAt = -1f;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                foreach (var go in search.FindAll())
                {
                    var legacy = go.GetComponent<Dropdown>();
                    if (legacy != null)
                        return (go, legacy.template);

                    var tmp = go.GetComponent<TMPro.TMP_Dropdown>();
                    if (tmp != null)
                        return (go, tmp.template);
                }

                float elapsed = Time.realtimeSinceStartup - startTime;
                if (recoveryAttemptedAt < 0 && elapsed > timeout * 0.3f && RecoveryHandler != null)
                {
                    recoveryAttemptedAt = elapsed;

                    var context = new RecoveryContext
                    {
                        FailedAction = actionDescription,
                        ErrorMessage = $"Dropdown not found: {search}",
                        CancellationToken = default
                    };

                    var recoveryResult = await RecoveryHandler(context);
                    RecordRecovery(recoveryResult.Explanation ?? "Recovery attempted");

                    if (recoveryResult.NoBlockerFound)
                        break;
                }

                await Task.Delay(100);
            }

            return (null, null);
        }

        /// <summary>
        /// Resolves a search query to a component of type T, with automatic recovery on timeout.
        /// </summary>
        internal static async Task<T> ResolveSearch<T>(Search search, float timeout, string actionDescription) where T : Component
        {
            float startTime = Time.realtimeSinceStartup;
            float recoveryAttemptedAt = -1f;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                foreach (var go in search.FindAll())
                {
                    var component = go.GetComponent<T>();
                    if (component != null)
                        return component;
                }

                float elapsed = Time.realtimeSinceStartup - startTime;
                if (recoveryAttemptedAt < 0 && elapsed > timeout * 0.3f && RecoveryHandler != null)
                {
                    recoveryAttemptedAt = elapsed;

                    var context = new RecoveryContext
                    {
                        FailedAction = actionDescription,
                        ErrorMessage = $"Element with {typeof(T).Name} not found: {search}",
                        CancellationToken = default
                    };

                    var recoveryResult = await RecoveryHandler(context);
                    RecordRecovery(recoveryResult.Explanation ?? "Recovery attempted");

                    if (recoveryResult.NoBlockerFound)
                        break;
                }

                await Task.Delay(100);
            }

            return null;
        }

#if UNITY_INCLUDE_TESTS
        private static readonly System.Collections.Generic.List<Exception> _capturedExceptions = new System.Collections.Generic.List<Exception>();
        private static bool _captureHandlerAttached;

        /// <summary>
        /// When true, captures unobserved Task exceptions instead of letting them crash.
        /// Access captured exceptions via <see cref="CapturedExceptions"/>.
        /// </summary>
        public static bool CaptureUnobservedExceptions
        {
            get => _captureHandlerAttached;
            set
            {
                if (value && !_captureHandlerAttached)
                {
                    System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
                    _captureHandlerAttached = true;
                    _capturedExceptions.Clear();
                }
                else if (!value && _captureHandlerAttached)
                {
                    System.Threading.Tasks.TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
                    _captureHandlerAttached = false;
                }
            }
        }

        /// <summary>
        /// Gets the list of exceptions captured when <see cref="CaptureUnobservedExceptions"/> is true.
        /// </summary>
        public static System.Collections.Generic.IReadOnlyList<Exception> CapturedExceptions => _capturedExceptions;

        /// <summary>
        /// Clears the list of captured exceptions.
        /// </summary>
        public static void ClearCapturedExceptions() => _capturedExceptions.Clear();

        private static void OnUnobservedTaskException(object sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            if (e.Exception != null)
            {
                foreach (var inner in e.Exception.InnerExceptions)
                    _capturedExceptions.Add(inner);
            }
        }
#endif

        #endregion

        #region Random Generator

        static System.Random _randomGenerator = new System.Random();

        /// <summary>
        /// Random number generator for deterministic random clicks.
        /// </summary>
        public static System.Random RandomGenerator
        {
            get => _randomGenerator;
            private set => _randomGenerator = value;
        }

        /// <summary>
        /// Sets the random seed for deterministic random clicks.
        /// </summary>
        public static void SetRandomSeed(int seed)
        {
            RandomGenerator = new System.Random(seed);
            Debug.Log($"[UIAutomation] RandomSeed set to {seed}");
        }

        #endregion

        #region Search Helpers

        /// <summary>
        /// Creates a Search from a reflection path for accessing game state.
        /// </summary>
        public static Search Reflect(string path) => Search.Reflect(path);

        /// <summary>
        /// Creates a Search query that matches GameObjects by name. Supports * wildcards.
        /// </summary>
        public static Search Name(string pattern) => new Search().Name(pattern);

        /// <summary>
        /// Creates a Search query that matches GameObjects by visible text content.
        /// </summary>
        public static Search Text(string pattern) => new Search().Text(pattern);

        /// <summary>
        /// Creates a Search query that matches GameObjects having a specific component type.
        /// </summary>
        public static Search Type<T>() where T : Component => new Search().Type<T>();

        /// <summary>
        /// Creates a Search query that matches GameObjects having a component with the specified type name.
        /// </summary>
        public static Search Type(string typeName) => new Search().Type(typeName);

        /// <summary>
        /// Creates a Search query that matches GameObjects by their sprite name.
        /// </summary>
        public static Search Texture(string pattern) => new Search().Texture(pattern);

        /// <summary>
        /// Creates a Search query that matches GameObjects by their hierarchy path.
        /// </summary>
        public static Search Path(string pattern) => new Search().Path(pattern);

        /// <summary>
        /// Creates a Search query that matches GameObjects by their Unity tag.
        /// </summary>
        public static Search Tag(string tag) => new Search().Tag(tag);

        /// <summary>
        /// Creates a Search query that matches GameObjects by any property (name, text, sprite, or path).
        /// </summary>
        public static Search Any(string pattern) => new Search().Any(pattern);

        /// <summary>
        /// Creates a Search query that finds an interactable element adjacent to a text label.
        /// </summary>
        public static Search Adjacent(string textPattern, Direction direction = Direction.Right) => new Search().Adjacent(textPattern, direction);

        /// <summary>
        /// Creates a Search query that finds elements near a text label, ordered by distance.
        /// </summary>
        public static Search Near(string textPattern, Direction? direction = null) => new Search().Near(textPattern, direction);

        #endregion

        #region Logging

        static void Log(string message) => Debug.Log($"[UIAutomation] {message}");

        static void LogDebug(string message)
        {
            if (DebugMode)
                Debug.Log($"[UIAutomation:DEBUG] {message}");
        }

        static void LogStart(string action) => Log($"START: {action}");
        static void LogComplete(string action) => Log($"COMPLETE: {action}");
        static void LogComplete(string action, string result) => Log($"COMPLETE: {action} -> {result}");
        static void LogFail(string action, string reason) => Debug.LogWarning($"[UIAutomation] FAILED: {action} - {reason}");

        /// <summary>
        /// Async disposable action scope that logs START on creation, syncs to main thread, and logs COMPLETE on disposal.
        /// Use with 'await using' statement for automatic cleanup.
        /// </summary>
        private class ActionScopeInner
        {
            private readonly string _action;
            private string _result;
            private bool _disposed;
            private bool _failed;

            public ActionScopeInner(string action)
            {
                _action = action;
                Log($"START: {action}");
            }

            /// <summary>Sets a result message to be included in the COMPLETE log.</summary>
            public void SetResult(string result) => _result = result;

            /// <summary>Marks the action as failed with a reason. Prevents COMPLETE from being logged.</summary>
            public void Fail(string reason)
            {
                _failed = true;
                Debug.LogWarning($"[UIAutomation] FAILED: {_action} - {reason}");
            }

            public async ValueTask DisposeAsync()
            {
                if (_disposed) return;
                _disposed = true;

                // Ensure we're on main thread and events are processed
                await Async.ToMainThread();

                // Apply interval delay
                int delay = DebugMode ? (int)(Interval * DebugIntervalMultiplier) : Interval;
                if (delay > 0)
                    await Task.Delay(delay);

                if (!_failed)
                {
                    if (_result != null)
                        Log($"COMPLETE: {_action} -> {_result}");
                    else
                        Log($"COMPLETE: {_action}");
                }
            }
        }

        /// <summary>
        /// Awaitable wrapper that ensures main thread before creating ActionScope and implements IAsyncDisposable.
        /// Usage: await using var action = await RunAction("...");
        /// </summary>
        private struct ActionScope : IAsyncDisposable
        {
            private readonly string _action;
            private ActionScopeInner _inner;

            public ActionScope(string action)
            {
                _action = action;
                _inner = null;
            }

            /// <summary>Sets a result message to be included in the COMPLETE log.</summary>
            public void SetResult(string result) => _inner?.SetResult(result);

            /// <summary>Marks the action as failed with a reason. Prevents COMPLETE from being logged.</summary>
            public void Fail(string reason) => _inner?.Fail(reason);

            public async ValueTask DisposeAsync()
            {
                if (_inner != null)
                {
                    await _inner.DisposeAsync();
                }
            }

            /// <summary>Awaiter support - ensures main thread and creates inner scope.</summary>
            public ActionScopeAwaiter GetAwaiter() => new ActionScopeAwaiter(this);

            public struct ActionScopeAwaiter : System.Runtime.CompilerServices.INotifyCompletion
            {
                private readonly ActionScope _scope;
                private Task _mainThreadTask;

                public ActionScopeAwaiter(ActionScope scope)
                {
                    _scope = scope;
                    _mainThreadTask = Async.ToMainThread();
                }

                public bool IsCompleted => _mainThreadTask.IsCompleted;

                public void OnCompleted(Action continuation)
                {
                    _mainThreadTask.ContinueWith(_ => continuation(), TaskScheduler.FromCurrentSynchronizationContext());
                }

                public ActionScope GetResult()
                {
                    _mainThreadTask.GetAwaiter().GetResult();
                    var result = _scope;
                    result._inner = new ActionScopeInner(_scope._action);
                    return result;
                }
            }
        }

        /// <summary>Creates an ActionScope that ensures main thread, logs START now, and syncs/logs COMPLETE when disposed.</summary>
        private static ActionScope RunAction(string action) => new ActionScope(action);

        #endregion

        #region Scene Management

        /// <summary>
        /// Ensures a specific scene is loaded. If not already loaded, loads it.
        /// </summary>
        public static async Task EnsureSceneLoaded(string sceneName, float timeout = 30f)
        {
            if (SceneManager.GetActiveScene().name == sceneName)
            {
                Log($"EnsureSceneLoaded(\"{sceneName}\") - already loaded");
                await Task.Yield();
                return;
            }

            await using var action = await RunAction($"EnsureSceneLoaded(\"{sceneName}\", timeout={timeout}s)");
            var asyncOp = SceneManager.LoadSceneAsync(sceneName);

            float startTime = Time.realtimeSinceStartup;
            while (!asyncOp.isDone && (Time.realtimeSinceStartup - startTime) < timeout)
            {
                await Task.Yield();
            }

            if (!asyncOp.isDone)
            {
                action.Fail($"did not load within {timeout}s");
                throw new UIAutomationTimeoutException($"Scene '{sceneName}' did not load within {timeout} seconds");
            }

            // Let scene initialize
            await Async.DelayFrames(3);
            action.SetResult("loaded");
        }

        /// <summary>
        /// Waits until the scene changes from the current scene.
        /// </summary>
        public static async Task SceneChange(float seconds = 30)
        {
            string startScene = SceneManager.GetActiveScene().name;
            await using var action = await RunAction($"SceneChange(from=\"{startScene}\", timeout={seconds}s)");
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying)
            {
                if (SceneManager.GetActiveScene().name != startScene)
                {
                    string newScene = SceneManager.GetActiveScene().name;
                    action.SetResult($"changed to '{newScene}'");
                    return;
                }

                await Task.Delay(EffectivePollInterval);
            }

            action.Fail($"scene did not change within {seconds}s");
            throw new UIAutomationTimeoutException($"Scene did not change from '{startScene}' within {seconds} seconds");
        }

        /// <summary>
        /// Navigates back to the main menu by repeatedly clicking back/close buttons.
        /// </summary>
        public static async Task NavigateToMainMenu(
            Search mainMenuIdentifier,
            string[] backButtonPatterns = null,
            int maxAttempts = 20,
            float timeout = 60f)
        {
            await using var action = await RunAction($"NavigateToMainMenu({mainMenuIdentifier})");
            backButtonPatterns ??= new[] { "Back", "Close", "Exit", "Return", "*Back*", "*Close*", "*Exit*", "X" };

            float startTime = Time.realtimeSinceStartup;
            int attempts = 0;

            while (attempts < maxAttempts && (Time.realtimeSinceStartup - startTime) < timeout)
            {
                // Check if we've reached main menu
                var mainMenuElement = await Find<Transform>(mainMenuIdentifier, throwIfMissing: false, seconds: 0.5f);
                if (mainMenuElement != null)
                {
                    action.SetResult($"reached after {attempts} back actions");
                    return;
                }

                // Try to find and click a back button
                bool clickedBack = false;
                foreach (var pattern in backButtonPatterns)
                {
                    var search = new Search().Any(pattern).Type<Selectable>();
                    var backButton = await Find<Selectable>(search, throwIfMissing: false, seconds: 0.1f);
                    if (backButton != null && backButton.interactable)
                    {
                        LogDebug($"NavigateToMainMenu - clicking '{backButton.name}'");
                        await Click(search, searchTime: 0.5f);
                        clickedBack = true;
                        attempts++;
                        await Wait(0.5f); // Wait for UI transition
                        break;
                    }
                }

                if (!clickedBack)
                {
                    // Try pressing Escape key as fallback
                    LogDebug($"NavigateToMainMenu - no back button found, pressing Escape");
                    await PressKey(Key.Escape);
                    attempts++;
                    await Wait(0.5f);
                }
            }

            action.Fail($"could not reach within {maxAttempts} attempts or {timeout}s");
            throw new UIAutomationTimeoutException($"Could not navigate to main menu within {maxAttempts} attempts or {timeout} seconds");
        }

        #endregion

        #region Wait Operations

        /// <summary>
        /// Waits for the specified duration before continuing.
        /// </summary>
        public static async Task Wait(float seconds = 1f)
        {
            await using (await RunAction($"Wait({seconds}s)"))
            {
                await Task.Delay((int)(seconds * 1000));
            }
        }

        /// <summary>
        /// Waits until an element matching the search query appears.
        /// </summary>
        public static async Task Wait(Search search, int seconds = 10)
        {
            await using (await RunAction($"Wait({search}, timeout={seconds}s)"))
            {
                await Find<Transform>(search, true, seconds);
            }
        }

        /// <summary>
        /// Waits until a custom condition becomes true.
        /// </summary>
        public static async Task WaitFor(Func<bool> condition, float seconds = 60, string description = "condition")
        {
            await using var action = await RunAction($"WaitFor(\"{description}\", timeout={seconds}s)");

            var startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying)
            {
                if (condition())
                {
                    float elapsed = Time.realtimeSinceStartup - startTime;
                    action.SetResult($"satisfied after {elapsed:F2}s");
                    return;
                }

                await Task.Delay(EffectivePollInterval);
            }

            action.Fail($"condition not met within {seconds}s");
            throw new UIAutomationTimeoutException($"Condition '{description}' not met within {seconds} seconds");
        }

        /// <summary>
        /// Waits until the game achieves a target framerate.
        /// </summary>
        public static async Task WaitFramerate(int averageFps, float sampleDuration = 2f, float timeout = 60f)
        {
            await using var action = await RunAction($"WaitFramerate(target={averageFps}fps, timeout={timeout}s)");

            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                float sampleStart = Time.realtimeSinceStartup;
                int frameCount = 0;

                while ((Time.realtimeSinceStartup - sampleStart) < sampleDuration && Application.isPlaying)
                {
                    await Task.Yield();
                    frameCount++;
                }

                float currentFps = frameCount / (Time.realtimeSinceStartup - sampleStart);

                if (currentFps >= averageFps)
                {
                    action.SetResult($"achieved {currentFps:F1} FPS");
                    return;
                }
            }

            action.Fail($"did not reach target within {timeout}s");
            throw new UIAutomationTimeoutException($"Framerate did not reach {averageFps} FPS within {timeout} seconds");
        }

        #endregion

        #region Text Input

        /// <summary>
        /// Enters text into an input field.
        /// </summary>
        public static async Task TextInput(Search search, string input, float seconds = 10, bool pressEnter = false)
        {
            await using var action = await RunAction($"TextInput({search}, \"{input}\")");

            var tmpInput = await Find<TMP_InputField>(search, false, 0.1f);
            if (tmpInput != null)
            {
                await InputInjector.TypeIntoField(tmpInput.gameObject, input, clearFirst: true, pressEnter: pressEnter);
                action.SetResult($"TMP_InputField '{tmpInput.name}'");
                return;
            }

            var legacyInput = await Find<InputField>(search, true, seconds);
            await InputInjector.TypeIntoField(legacyInput.gameObject, input, clearFirst: true, pressEnter: pressEnter);
            action.SetResult($"InputField '{legacyInput.name}'");
        }

        #endregion

        #region Find Operations

        /// <summary>
        /// Finds an element matching the search query.
        /// </summary>
        public static async Task<T> Find<T>(Search search, bool throwIfMissing = true, float seconds = 10) where T : Component
        {
            await using var action = await RunAction($"Find<{typeof(T).Name}>({search})");

            var startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying)
            {
                var result = search.FindFirst();
                if (result != null)
                {
                    var component = result.GetComponent<T>() ?? result.GetComponentInChildren<T>() ?? result.GetComponentInParent<T>();
                    if (component != null)
                    {
                        action.SetResult($"'{result.name}'");
                        return component;
                    }
                }

                await Task.Delay(EffectivePollInterval);
            }

            if (throwIfMissing)
            {
                action.Fail($"element not found within {seconds}s");
                throw new UIAutomationTimeoutException($"Could not find {typeof(T).Name} matching {search} within {seconds} seconds");
            }

            action.Fail($"element not found within {seconds}s");
            return null;
        }

        /// <summary>
        /// Finds all elements matching the search query.
        /// </summary>
        public static async Task<IEnumerable<T>> FindAll<T>(Search search, float seconds = 10) where T : Component
        {
            LogDebug($"FindAll<{typeof(T).Name}> search={search}");

            var startTime = Time.realtimeSinceStartup;
            IEnumerable<T> results = Enumerable.Empty<T>();

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying)
            {
                var gameObjects = search.FindAll();
                results = gameObjects
                    .Select(go => go.GetComponent<T>() ?? go.GetComponentInChildren<T>() ?? go.GetComponentInParent<T>())
                    .Where(c => c != null);

                if (results.Any())
                {
                    LogDebug($"FindAll<{typeof(T).Name}> found {results.Count()} elements");
                    return results;
                }

                await Task.Delay(EffectivePollInterval);
            }

            return results;
        }

        #endregion

        #region Keyboard Input

        /// <summary>
        /// Simulates a key press and release using Input System Key enum.
        /// </summary>
        public static async Task PressKey(Key key)
        {
            await using (await RunAction($"PressKey({key})"))
            {
                await InputInjector.PressKey(key);
            }
        }

        /// <summary>
        /// Simulates a key press and release using KeyCode.
        /// </summary>
        public static async Task PressKey(KeyCode key)
        {
            await using (await RunAction($"PressKey({key})"))
            {
                var inputKey = KeyCodeToKey(key);
                if (inputKey != Key.None)
                {
                    await InputInjector.PressKey(inputKey);
                }
            }
        }

        /// <summary>
        /// Types a string of text by pressing each character key.
        /// </summary>
        public static async Task PressKeys(string text)
        {
            await using (await RunAction($"PressKeys(\"{text}\")"))
            {
                foreach (char c in text)
                {
                    var key = CharToKey(c);
                    if (key != Key.None)
                    {
                        await InputInjector.PressKey(key);
                        await Task.Delay(50);
                    }
                }
            }
        }

        /// <summary>
        /// Holds a key down for a specified duration.
        /// </summary>
        public static async Task HoldKey(Key key, float duration)
        {
            await using (await RunAction($"HoldKey({key}, {duration}s)"))
            {
                await InputInjector.HoldKey(key, duration);
            }
        }

        /// <summary>
        /// Holds multiple keys down simultaneously for a specified duration.
        /// </summary>
        public static async Task HoldKeys(float duration, params Key[] keys)
        {
            string keysStr = string.Join(", ", keys);
            await using (await RunAction($"HoldKeys([{keysStr}], {duration}s)"))
            {
                await InputInjector.HoldKeys(keys, duration);
            }
        }

        /// <summary>
        /// Holds a key down for a specified duration using KeyCode.
        /// </summary>
        public static async Task HoldKey(KeyCode key, float duration)
        {
            await using (await RunAction($"HoldKey({key}, {duration}s)"))
            {
                var inputKey = KeyCodeToKey(key);
                if (inputKey != Key.None)
                {
                    await InputInjector.HoldKey(inputKey, duration);
                }
            }
        }

        /// <summary>
        /// Holds multiple keys down simultaneously using KeyCode.
        /// </summary>
        public static async Task HoldKeys(float duration, params KeyCode[] keys)
        {
            string keysStr = string.Join(", ", keys);
            await using (await RunAction($"HoldKeys([{keysStr}], {duration}s)"))
            {
                var inputKeys = keys
                    .Select(k => KeyCodeToKey(k))
                    .Where(k => k != Key.None)
                    .ToArray();

                if (inputKeys.Length > 0)
                {
                    await InputInjector.HoldKeys(inputKeys, duration);
                }
            }
        }

        /// <summary>
        /// Simulates pressing a key using a character.
        /// </summary>
        public static async Task PressKey(char c)
        {
            await using (await RunAction($"PressKey('{c}')"))
            {
                var key = CharToKey(c);
                if (key != Key.None)
                {
                    await InputInjector.PressKey(key);
                }
            }
        }

        /// <summary>
        /// Simulates pressing a key using a key name string.
        /// </summary>
        public static async Task PressKey(string keyName)
        {
            await using (await RunAction($"PressKey(\"{keyName}\")"))
            {
                if (keyName.Length == 1)
                {
                    var charKey = CharToKey(keyName[0]);
                    if (charKey != Key.None)
                        await InputInjector.PressKey(charKey);
                    return;
                }

                if (Enum.TryParse<KeyCode>(keyName, true, out var keyCode))
                {
                    var inputKey = KeyCodeToKey(keyCode);
                    if (inputKey != Key.None)
                        await InputInjector.PressKey(inputKey);
                    return;
                }

                var mappedKey = keyName.ToLowerInvariant() switch
                {
                    "enter" => KeyCode.Return,
                    "esc" => KeyCode.Escape,
                    "up" => KeyCode.UpArrow,
                    "down" => KeyCode.DownArrow,
                    "left" => KeyCode.LeftArrow,
                    "right" => KeyCode.RightArrow,
                    "bs" or "backspace" => KeyCode.Backspace,
                    "del" => KeyCode.Delete,
                    _ => KeyCode.None
                };

                if (mappedKey != KeyCode.None)
                {
                    var inputKey = KeyCodeToKey(mappedKey);
                    if (inputKey != Key.None)
                        await InputInjector.PressKey(inputKey);
                }
            }
        }

        #endregion

        #region Key Conversion Helpers

        /// <summary>
        /// Converts a Unity KeyCode to an Input System Key.
        /// </summary>
        public static Key KeyCodeToKey(KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.A => Key.A, KeyCode.B => Key.B, KeyCode.C => Key.C, KeyCode.D => Key.D,
                KeyCode.E => Key.E, KeyCode.F => Key.F, KeyCode.G => Key.G, KeyCode.H => Key.H,
                KeyCode.I => Key.I, KeyCode.J => Key.J, KeyCode.K => Key.K, KeyCode.L => Key.L,
                KeyCode.M => Key.M, KeyCode.N => Key.N, KeyCode.O => Key.O, KeyCode.P => Key.P,
                KeyCode.Q => Key.Q, KeyCode.R => Key.R, KeyCode.S => Key.S, KeyCode.T => Key.T,
                KeyCode.U => Key.U, KeyCode.V => Key.V, KeyCode.W => Key.W, KeyCode.X => Key.X,
                KeyCode.Y => Key.Y, KeyCode.Z => Key.Z,
                KeyCode.Alpha0 => Key.Digit0, KeyCode.Alpha1 => Key.Digit1,
                KeyCode.Alpha2 => Key.Digit2, KeyCode.Alpha3 => Key.Digit3,
                KeyCode.Alpha4 => Key.Digit4, KeyCode.Alpha5 => Key.Digit5,
                KeyCode.Alpha6 => Key.Digit6, KeyCode.Alpha7 => Key.Digit7,
                KeyCode.Alpha8 => Key.Digit8, KeyCode.Alpha9 => Key.Digit9,
                KeyCode.Keypad0 => Key.Numpad0, KeyCode.Keypad1 => Key.Numpad1,
                KeyCode.Keypad2 => Key.Numpad2, KeyCode.Keypad3 => Key.Numpad3,
                KeyCode.Keypad4 => Key.Numpad4, KeyCode.Keypad5 => Key.Numpad5,
                KeyCode.Keypad6 => Key.Numpad6, KeyCode.Keypad7 => Key.Numpad7,
                KeyCode.Keypad8 => Key.Numpad8, KeyCode.Keypad9 => Key.Numpad9,
                KeyCode.F1 => Key.F1, KeyCode.F2 => Key.F2, KeyCode.F3 => Key.F3,
                KeyCode.F4 => Key.F4, KeyCode.F5 => Key.F5, KeyCode.F6 => Key.F6,
                KeyCode.F7 => Key.F7, KeyCode.F8 => Key.F8, KeyCode.F9 => Key.F9,
                KeyCode.F10 => Key.F10, KeyCode.F11 => Key.F11, KeyCode.F12 => Key.F12,
                KeyCode.Space => Key.Space,
                KeyCode.Return => Key.Enter, KeyCode.KeypadEnter => Key.NumpadEnter,
                KeyCode.Escape => Key.Escape,
                KeyCode.Tab => Key.Tab,
                KeyCode.Backspace => Key.Backspace,
                KeyCode.Delete => Key.Delete,
                KeyCode.Insert => Key.Insert,
                KeyCode.Home => Key.Home, KeyCode.End => Key.End,
                KeyCode.PageUp => Key.PageUp, KeyCode.PageDown => Key.PageDown,
                KeyCode.UpArrow => Key.UpArrow, KeyCode.DownArrow => Key.DownArrow,
                KeyCode.LeftArrow => Key.LeftArrow, KeyCode.RightArrow => Key.RightArrow,
                KeyCode.LeftShift => Key.LeftShift, KeyCode.RightShift => Key.RightShift,
                KeyCode.LeftControl => Key.LeftCtrl, KeyCode.RightControl => Key.RightCtrl,
                KeyCode.LeftAlt => Key.LeftAlt, KeyCode.RightAlt => Key.RightAlt,
                KeyCode.Minus => Key.Minus, KeyCode.Equals => Key.Equals,
                KeyCode.LeftBracket => Key.LeftBracket, KeyCode.RightBracket => Key.RightBracket,
                KeyCode.Backslash => Key.Backslash, KeyCode.Semicolon => Key.Semicolon,
                KeyCode.Quote => Key.Quote, KeyCode.Comma => Key.Comma,
                KeyCode.Period => Key.Period, KeyCode.Slash => Key.Slash,
                KeyCode.BackQuote => Key.Backquote,
                _ => Key.None
            };
        }

        /// <summary>
        /// Converts a character to an Input System Key.
        /// </summary>
        public static Key CharToKey(char c)
        {
            // Letters (case-insensitive)
            if (c >= 'a' && c <= 'z')
                return (Key)((int)Key.A + (c - 'a'));
            if (c >= 'A' && c <= 'Z')
                return (Key)((int)Key.A + (c - 'A'));

            // Digits
            if (c >= '0' && c <= '9')
                return (Key)((int)Key.Digit0 + (c - '0'));

            // Common symbols
            return c switch
            {
                ' ' => Key.Space,
                '-' => Key.Minus,
                '=' => Key.Equals,
                '[' => Key.LeftBracket,
                ']' => Key.RightBracket,
                '\\' => Key.Backslash,
                ';' => Key.Semicolon,
                '\'' => Key.Quote,
                ',' => Key.Comma,
                '.' => Key.Period,
                '/' => Key.Slash,
                '`' => Key.Backquote,
                '\n' => Key.Enter,
                '\t' => Key.Tab,
                _ => Key.None
            };
        }

        #endregion

        #region Click Actions

        /// <summary>
        /// Clicks on a UI element matching the search query.
        /// Searches for a matching element and clicks on its screen position.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element to click when multiple match (0-based)</param>
        /// <returns>True if element was found and clicked, false otherwise</returns>
        public static async Task<bool> Click(Search search, float searchTime = 10f, int index = 0)
        {
            await using var action = await RunAction($"Click({search})");

            var element = await ResolveSearch(search, searchTime, index, $"Click({search})");

            if (element != null)
            {
                var screenPos = InputInjector.GetScreenPosition(element);
                await InputInjector.InjectPointerTap(screenPos);
                action.SetResult($"'{element.name}' at ({screenPos.x:F0},{screenPos.y:F0})");
                return true;
            }

            action.Fail($"Element not found within {searchTime}s");
            return false;
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
        public static async Task ClickAt(Vector2 normalizedPosition)
        {
            var screenPosition = new Vector2(Screen.width * normalizedPosition.x, Screen.height * normalizedPosition.y);
            await using (await RunAction($"ClickAt(({screenPosition.x:F0},{screenPosition.y:F0}))"))
            {
                await InputInjector.InjectPointerTap(screenPosition);
            }
        }

        /// <summary>
        /// Clicks at a screen position specified by percentage.
        /// </summary>
        /// <param name="xPercent">Horizontal position as percentage (0-1)</param>
        /// <param name="yPercent">Vertical position as percentage (0-1)</param>
        public static async Task ClickAt(float xPercent, float yPercent)
        {
            await ClickAt(new Vector2(xPercent, yPercent));
        }

        /// <summary>
        /// Double-clicks on a UI element matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <returns>True if element was found and double-clicked, false otherwise</returns>
        public static async Task<bool> DoubleClick(Search search, float searchTime = 10f, int index = 0)
        {
            await using var action = await RunAction($"DoubleClick({search})");

            var element = await ResolveSearch(search, searchTime, index, $"DoubleClick({search})");

            if (element != null)
            {
                var screenPos = InputInjector.GetScreenPosition(element);
                await InputInjector.InjectPointerDoubleTap(screenPos);
                action.SetResult($"'{element.name}' at ({screenPos.x:F0},{screenPos.y:F0})");
                return true;
            }

            action.Fail($"Element not found within {searchTime}s");
            return false;
        }

        /// <summary>
        /// Double-clicks at a specific screen position.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to double-click</param>
        public static async Task DoubleClickAt(Vector2 screenPosition)
        {
            await using (await RunAction($"DoubleClickAt(({screenPosition.x:F0},{screenPosition.y:F0}))"))
            {
                await InputInjector.InjectPointerDoubleTap(screenPosition);
            }
        }

        /// <summary>
        /// Triple-clicks on a UI element matching the search query.
        /// Performs three rapid clicks in succession.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <returns>True if element was found and triple-clicked, false otherwise</returns>
        public static async Task<bool> TripleClick(Search search, float searchTime = 10f, int index = 0)
        {
            await using var action = await RunAction($"TripleClick({search})");

            var element = await ResolveSearch(search, searchTime, index, $"TripleClick({search})");

            if (element != null)
            {
                var screenPos = InputInjector.GetScreenPosition(element);
                await InputInjector.InjectPointerTripleTap(screenPos);
                action.SetResult($"'{element.name}' at ({screenPos.x:F0},{screenPos.y:F0})");
                return true;
            }

            action.Fail($"Element not found within {searchTime}s");
            return false;
        }

        /// <summary>
        /// Triple-clicks at a specific screen position.
        /// Performs three rapid clicks in succession.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to triple-click</param>
        public static async Task TripleClickAt(Vector2 screenPosition)
        {
            await using (await RunAction($"TripleClickAt(({screenPosition.x:F0},{screenPosition.y:F0}))"))
            {
                await InputInjector.InjectPointerTripleTap(screenPosition);
            }
        }

        /// <summary>
        /// Holds/long-presses on a UI element matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="seconds">Duration of the hold in seconds</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <returns>True if element was found and held, false otherwise</returns>
        public static async Task<bool> Hold(Search search, float seconds, float searchTime = 10f, int index = 0)
        {
            await using var action = await RunAction($"Hold({search}, {seconds}s)");

            var element = await ResolveSearch(search, searchTime, index, $"Hold({search})");

            if (element != null)
            {
                var screenPos = InputInjector.GetScreenPosition(element);
                await InputInjector.InjectPointerHold(screenPos, seconds);
                action.SetResult($"'{element.name}' at ({screenPos.x:F0},{screenPos.y:F0})");
                return true;
            }

            action.Fail($"Element not found within {searchTime}s");
            return false;
        }

        /// <summary>
        /// Holds/long-presses at a specific screen position.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to hold</param>
        /// <param name="seconds">Duration of the hold in seconds</param>
        public static async Task HoldAt(Vector2 screenPosition, float seconds)
        {
            await using (await RunAction($"HoldAt(({screenPosition.x:F0},{screenPosition.y:F0}), {seconds}s)"))
            {
                await InputInjector.InjectPointerHold(screenPosition, seconds);
            }
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
        public static async Task<bool> Type(Search search, string text, bool clearFirst = true, bool pressEnter = false, float searchTime = 10f, int index = 0)
        {
            await using var action = await RunAction($"Type({search}, \"{text}\")");

            var element = await ResolveSearch(search, searchTime, index, $"Type({search})");

            if (element != null)
            {
                await InputInjector.TypeIntoField(element, text, clearFirst, pressEnter);
                action.SetResult($"'{element.name}' (clear={clearFirst}, enter={pressEnter})");
                return true;
            }

            action.Fail($"Element not found within {searchTime}s");
            return false;
        }

        /// <summary>
        /// Types text into an input field. Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="inputField">The input field GameObject (TMP_InputField or InputField)</param>
        /// <param name="text">The text to type</param>
        /// <param name="clearFirst">Whether to clear existing text first</param>
        /// <param name="pressEnter">Whether to press Enter after typing</param>
        /// <summary>
        /// Types text without targeting a specific input field (assumes something is focused).
        /// </summary>
        /// <param name="text">The text to type</param>
        public static async Task TypeText(string text)
        {
            await using (await RunAction($"TypeText(\"{text}\")"))
            {
                await InputInjector.TypeText(text);
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
        /// <returns>True if element was found and dragged, false otherwise</returns>
        public static async Task<bool> Drag(Search search, Vector2 direction, float duration = 0.3f, float searchTime = 10f, int index = 0, float holdTime = 0.05f, PointerButton button = PointerButton.Left)
        {
            await using var action = await RunAction($"Drag({search}, direction=({direction.x:F0},{direction.y:F0}))");

            var element = await ResolveSearch(search, searchTime, index, $"Drag({search})");

            if (element != null)
            {
                var screenPos = InputInjector.GetScreenPosition(element);
                var endPos = screenPos + direction;
                await InputInjector.InjectPointerDrag(screenPos, endPos, duration, holdTime, button);
                action.SetResult($"'{element.name}' from ({screenPos.x:F0},{screenPos.y:F0}) by ({direction.x:F0},{direction.y:F0})");
                return true;
            }

            action.Fail($"Element not found within {searchTime}s");
            return false;
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
        /// <returns>True if both elements were found and drag completed, false otherwise</returns>
        public static async Task<bool> DragTo(Search fromSearch, Search toSearch, float duration = 0.3f, float searchTime = 10f, float holdTime = 0.05f, PointerButton button = PointerButton.Left)
        {
            await using var action = await RunAction($"DragTo({fromSearch} -> {toSearch})");

            var fromElement = await ResolveSearch(fromSearch, searchTime, 0, $"DragTo({fromSearch} -> ...)");
            if (fromElement == null)
            {
                action.Fail($"Source element not found within {searchTime}s");
                return false;
            }

            var toElement = await ResolveSearch(toSearch, searchTime, 0, $"DragTo(... -> {toSearch})");
            if (toElement == null)
            {
                action.Fail($"Target element not found within {searchTime}s");
                return false;
            }

            var fromPos = InputInjector.GetScreenPosition(fromElement);
            var toPos = InputInjector.GetScreenPosition(toElement);
            await InputInjector.InjectPointerDrag(fromPos, toPos, duration, holdTime, button);
            action.SetResult($"'{fromElement.name}' to '{toElement.name}'");
            return true;
        }

        /// <summary>
        /// Drags between two screen positions.
        /// </summary>
        /// <param name="startPosition">Start screen position</param>
        /// <param name="endPosition">End screen position</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        /// <param name="button">Which mouse button to use for dragging</param>
        public static async Task DragFromTo(Vector2 startPosition, Vector2 endPosition, float duration = 0.3f, float holdTime = 0.05f, PointerButton button = PointerButton.Left)
        {
            await using (await RunAction($"DragFromTo(({startPosition.x:F0},{startPosition.y:F0}) -> ({endPosition.x:F0},{endPosition.y:F0}))"))
            {
                await InputInjector.InjectPointerDrag(startPosition, endPosition, duration, holdTime, button);
            }
        }

        #endregion

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
        /// <returns>True if element was found and scrolled, false otherwise</returns>
        public static async Task<bool> Scroll(Search search, float delta, float searchTime = 10f, int index = 0)
        {
            await using var action = await RunAction($"Scroll({search}, delta={delta})");

            var element = await ResolveSearch(search, searchTime, index, $"Scroll({search})");

            if (element != null)
            {
                var screenPos = InputInjector.GetScreenPosition(element);
                await InputInjector.InjectScroll(screenPos, delta);
                action.SetResult($"'{element.name}' at ({screenPos.x:F0},{screenPos.y:F0})");
                return true;
            }

            action.Fail($"Element not found within {searchTime}s");
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
        public static async Task<bool> Scroll(Search search, string direction, float amount = 0.3f, float searchTime = 10f, int index = 0)
        {
            await using var action = await RunAction($"Scroll({search}, {direction}, amount={amount})");

            var element = await ResolveSearch(search, searchTime, index, $"Scroll({search})");

            if (element != null)
            {
                await InputInjector.ScrollElement(element, direction, amount);
                action.SetResult($"'{element.name}'");
                return true;
            }

            action.Fail($"Element not found within {searchTime}s");
            return false;
        }

        #endregion

        #region Slider/Scrollbar Actions

        /// <summary>
        /// Clicks on a slider at a specific position matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the Slider</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <returns>True if slider was found and clicked, false otherwise</returns>
        public static async Task<bool> ClickSlider(Search search, float normalizedValue, float searchTime = 10f)
        {
            await using var action = await RunAction($"ClickSlider({search}, {normalizedValue:F2})");

            var slider = await ResolveSearch<Slider>(search, searchTime, $"ClickSlider({search})");

            if (slider != null)
            {
                var clickPos = InputInjector.GetSliderClickPosition(slider, normalizedValue);
                await InputInjector.InjectPointerTap(clickPos);
                action.SetResult($"'{slider.name}'");
                return true;
            }

            action.Fail($"Slider not found within {searchTime}s");
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
        public static async Task<bool> DragSlider(Search search, float fromValue, float toValue, float duration = 0.3f, float searchTime = 10f, float holdTime = 0.05f)
        {
            await using var action = await RunAction($"DragSlider({search}, {fromValue:F2} -> {toValue:F2})");

            var slider = await ResolveSearch<Slider>(search, searchTime, $"DragSlider({search})");

            if (slider != null)
            {
                var startPos = InputInjector.GetSliderClickPosition(slider, fromValue);
                var endPos = InputInjector.GetSliderClickPosition(slider, toValue);
                await InputInjector.InjectPointerDrag(startPos, endPos, duration, holdTime);
                action.SetResult($"'{slider.name}'");
                return true;
            }

            action.Fail($"Slider not found within {searchTime}s");
            return false;
        }

        /// <summary>
        /// Sets a slider to a specific value matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the Slider</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <returns>True if slider was found and set, false otherwise</returns>
        public static async Task<bool> SetSlider(Search search, float normalizedValue, float searchTime = 10f)
        {
            await using var action = await RunAction($"SetSlider({search}, {normalizedValue:F2})");

            var slider = await ResolveSearch<Slider>(search, searchTime, $"SetSlider({search})");

            if (slider != null)
            {
                await InputInjector.SetSlider(slider, normalizedValue);
                action.SetResult($"'{slider.name}'");
                return true;
            }

            action.Fail($"Slider not found within {searchTime}s");
            return false;
        }

        /// <summary>
        /// Sets a scrollbar to a specific value matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the Scrollbar</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <returns>True if scrollbar was found and set, false otherwise</returns>
        public static async Task<bool> SetScrollbar(Search search, float normalizedValue, float searchTime = 10f)
        {
            await using var action = await RunAction($"SetScrollbar({search}, {normalizedValue:F2})");

            var scrollbar = await ResolveSearch<Scrollbar>(search, searchTime, $"SetScrollbar({search})");

            if (scrollbar != null)
            {
                await InputInjector.SetScrollbar(scrollbar, normalizedValue);
                action.SetResult($"'{scrollbar.name}'");
                return true;
            }

            action.Fail($"Scrollbar not found within {searchTime}s");
            return false;
        }

        #endregion

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
        public static async Task SwipeAt(Vector2 position, string direction, float normalizedDistance = 0.2f, float duration = 0.3f)
        {
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
        public static async Task PinchAt(Vector2 position, float scale, float duration = 0.3f)
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
        public static async Task Pinch(float scale, float duration = 0.3f)
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
        public static async Task PinchAt(float xPercent, float yPercent, float scale, float duration = 0.3f)
        {
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
        public static async Task TwoFingerSwipeAt(Vector2 position, string direction, float normalizedDistance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {
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
        public static async Task TwoFingerSwipe(SwipeDirection direction, float normalizedDistance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {
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
        public static async Task TwoFingerSwipeAt(float xPercent, float yPercent, SwipeDirection direction, float normalizedDistance = 0.2f, float duration = 0.3f, float fingerSpacing = 0.03f)
        {
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
        public static async Task RotateAt(Vector2 position, float degrees, float duration = 0.3f, float fingerDistance = 0.05f)
        {
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
        public static async Task RotateAt(float xPercent, float yPercent, float degrees, float duration = 0.3f, float fingerDistance = 0.05f)
        {
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
        public static async Task Rotate(float degrees, float duration = 0.3f, float fingerDistance = 0.05f)
        {
            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await using (await RunAction($"Rotate({degrees}°) at center"))
            {
                await InputInjector.InjectRotate(center, degrees, duration, fingerDistance);
            }
        }

        /// <summary>
        /// Performs a drag gesture from the screen center in a direction.
        /// </summary>
        /// <param name="direction">The direction to drag (pixel offset)</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        public static async Task Drag(Vector2 direction, float duration = 0.3f, float holdTime = 0.05f)
        {
            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var endPos = center + direction;
            await using (await RunAction($"Drag(direction=({direction.x:F0},{direction.y:F0})) from center"))
            {
                await InputInjector.InjectPointerDrag(center, endPos, duration, holdTime);
            }
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
        public static async Task<bool> ClickDropdown(Search search, int optionIndex, float searchTime = 10f)
        {
            await using var action = await RunAction($"ClickDropdown({search}, index={optionIndex})");

            var (element, template) = await ResolveDropdown(search, searchTime, $"ClickDropdown({search})");

            if (element != null)
            {
                await ClickDropdownItem(element, template, optionIndex);
                action.SetResult($"'{element.name}' index={optionIndex}");
                return true;
            }

            action.Fail($"Dropdown not found within {searchTime}s");
            return false;
        }

        /// <summary>
        /// Selects a dropdown option by label text using realistic click interactions.
        /// </summary>
        /// <param name="search">The search query to find the Dropdown or TMP_Dropdown</param>
        /// <param name="optionLabel">The text label of the option to select</param>
        /// <param name="searchTime">Maximum time to search for the dropdown</param>
        /// <returns>True if dropdown was found and option selected, false otherwise</returns>
        public static async Task<bool> ClickDropdown(Search search, string optionLabel, float searchTime = 10f)
        {
            await using var action = await RunAction($"ClickDropdown({search}, label=\"{optionLabel}\")");

            var (element, template, optionIndex) = await ResolveDropdownByLabel(search, optionLabel, searchTime, $"ClickDropdown({search})");

            if (element != null && optionIndex >= 0)
            {
                await ClickDropdownItem(element, template, optionIndex);
                action.SetResult($"'{element.name}' label=\"{optionLabel}\" (index={optionIndex})");
                return true;
            }

            action.Fail($"Dropdown or option '{optionLabel}' not found within {searchTime}s");
            return false;
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

        #region Utility Actions

        /// <summary>
        /// Clicks any one of the elements matching the search query.
        /// </summary>
        /// <param name="search">The search query to find elements</param>
        /// <param name="searchTime">Maximum time to search for elements</param>
        /// <returns>True if any element was found and clicked, false otherwise</returns>
        public static async Task<bool> ClickAny(Search search, float searchTime = 10f)
        {
            await using var action = await RunAction($"ClickAny({search})");
            var rnd = new System.Random((int)System.DateTime.Now.Millisecond);

            var elements = await ResolveSearchAll(search, searchTime, $"ClickAny({search})");

            if (elements.Count > 0)
            {
                // Randomly select one
                var target = elements.OrderBy(_ => rnd.Next()).First();
                var screenPos = InputInjector.GetScreenPosition(target);
                await ClickAtPosition(screenPos, target.name);
                action.SetResult($"'{target.name}' at ({screenPos.x:F0},{screenPos.y:F0}) from {elements.Count} matches");
                return true;
            }

            action.Fail($"No elements found within {searchTime}s");
            return false;
        }

        #endregion

        #region GetValue Methods

        /// <summary>
        /// Gets a value from a UI element found by the search.
        /// Supports: string (text), bool (toggle), float (slider/scrollbar), int, and arrays.
        /// </summary>
        /// <typeparam name="T">Type of value to get: string, bool, float, int, or T[]</typeparam>
        /// <param name="search">The search query to find the element</param>
        /// <returns>The value from the element</returns>
        /// <example>
        /// var text = GetValue&lt;string&gt;(Name("ScoreLabel"));
        /// var isOn = GetValue&lt;bool&gt;(Name("SoundToggle"));
        /// var volume = GetValue&lt;float&gt;(Name("VolumeSlider"));
        /// var items = GetValue&lt;string[]&gt;(Name("Dropdown"));
        /// </example>
        public static T GetValue<T>(Search search)
        {
            var go = search.FindFirst();
            if (go == null)
                throw new InvalidOperationException($"GetValue failed: element not found - Search: {search}");

            var type = typeof(T);

            // String - get text content
            if (type == typeof(string))
            {
                var tmp = go.GetComponent<TMPro.TMP_Text>();
                if (tmp != null)
                    return (T)(object)tmp.text;

                var legacy = go.GetComponent<UnityEngine.UI.Text>();
                if (legacy != null)
                    return (T)(object)legacy.text;

                var inputField = go.GetComponent<TMPro.TMP_InputField>();
                if (inputField != null)
                    return (T)(object)inputField.text;

                var legacyInput = go.GetComponent<UnityEngine.UI.InputField>();
                if (legacyInput != null)
                    return (T)(object)legacyInput.text;

                throw new InvalidOperationException($"GetValue<string> failed: '{go.name}' has no text component");
            }

            // Bool - get toggle state
            if (type == typeof(bool))
            {
                var toggle = go.GetComponent<UnityEngine.UI.Toggle>();
                if (toggle != null)
                    return (T)(object)toggle.isOn;

                throw new InvalidOperationException($"GetValue<bool> failed: '{go.name}' is not a Toggle");
            }

            // Float - get slider/scrollbar value
            if (type == typeof(float))
            {
                var slider = go.GetComponent<Slider>();
                if (slider != null)
                    return (T)(object)slider.value;

                var scrollbar = go.GetComponent<UnityEngine.UI.Scrollbar>();
                if (scrollbar != null)
                    return (T)(object)scrollbar.value;

                throw new InvalidOperationException($"GetValue<float> failed: '{go.name}' is not a Slider or Scrollbar");
            }

            // Int - get slider value as int, dropdown index, or text as int
            if (type == typeof(int))
            {
                var slider = go.GetComponent<Slider>();
                if (slider != null)
                    return (T)(object)(int)slider.value;

                var dropdown = go.GetComponent<UnityEngine.UI.Dropdown>();
                if (dropdown != null)
                    return (T)(object)dropdown.value;

                var tmpDropdown = go.GetComponent<TMPro.TMP_Dropdown>();
                if (tmpDropdown != null)
                    return (T)(object)tmpDropdown.value;

                // Try parsing text as int
                var tmp = go.GetComponent<TMPro.TMP_Text>();
                if (tmp != null && int.TryParse(tmp.text, out var intVal))
                    return (T)(object)intVal;

                var legacy = go.GetComponent<UnityEngine.UI.Text>();
                if (legacy != null && int.TryParse(legacy.text, out intVal))
                    return (T)(object)intVal;

                throw new InvalidOperationException($"GetValue<int> failed: '{go.name}' is not a Slider, Dropdown, or text with int value");
            }

            // String array - get dropdown options
            if (type == typeof(string[]))
            {
                var dropdown = go.GetComponent<UnityEngine.UI.Dropdown>();
                if (dropdown != null)
                    return (T)(object)dropdown.options.Select(o => o.text).ToArray();

                var tmpDropdown = go.GetComponent<TMPro.TMP_Dropdown>();
                if (tmpDropdown != null)
                    return (T)(object)tmpDropdown.options.Select(o => o.text).ToArray();

                throw new InvalidOperationException($"GetValue<string[]> failed: '{go.name}' is not a Dropdown");
            }

            throw new InvalidOperationException($"GetValue<{type.Name}> failed: unsupported type. Use string, bool, float, int, or string[]");
        }

        /// <summary>
        /// Gets a value from a static path using reflection.
        /// Resolves dot-separated paths like "GameManager.Instance.Score" or "Player.Health".
        /// </summary>
        /// <typeparam name="T">Type of value to get</typeparam>
        /// <param name="path">Dot-separated path to the value (e.g., "GameManager.Instance.Score")</param>
        /// <returns>The value at the path</returns>
        /// <example>
        /// var score = GetValue&lt;int&gt;("GameManager.Instance.Score");
        /// var name = GetValue&lt;string&gt;("Player.Instance.Name");
        /// var isReady = GetValue&lt;bool&gt;("GameManager.Instance.IsReady");
        /// var items = GetValue&lt;string[]&gt;("Inventory.Instance.ItemNames");
        /// </example>
        public static T GetValue<T>(string path)
        {
            var value = ResolveStaticPath(path);

            if (value == null)
            {
                if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                    throw new InvalidOperationException($"GetValue<{typeof(T).Name}> failed: {path} resolved to null");
                return default;
            }

            if (value is T typedValue)
                return typedValue;

            // Try conversion
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                throw new InvalidOperationException($"GetValue<{typeof(T).Name}> failed: cannot convert {value.GetType().Name} to {typeof(T).Name}");
            }
        }

        /// <summary>
        /// Creates a StaticPath from a dot-separated static path.
        /// Returns an iterable wrapper that can navigate properties and iterate over arrays/lists.
        /// </summary>
        /// <param name="path">Dot-separated path (e.g., "GameManager.Instance.AllPlayers")</param>
        /// <returns>StaticPath wrapper that can be iterated or navigated</returns>
        /// <example>
        /// // Iterate over array
        /// foreach (var truck in Static("Generator.Trucks.AllTrucks"))
        /// {
        ///     var health = truck.GetValue&lt;float&gt;("DamageController.Health");
        ///     var name = truck.GetValue&lt;string&gt;("Name");
        /// }
        ///
        /// // Navigate properties
        /// var player = Static("GameManager.Instance.Player");
        /// var score = player.GetValue&lt;int&gt;("Score");
        ///
        /// // Chain navigation
        /// var damage = Static("Generator.Trucks.PlayerTruck").Property("DamageController").GetValue&lt;float&gt;("Health");
        /// </example>
        public static StaticPath Static(string path)
        {
            var value = ResolveStaticPath(path);
            return new StaticPath(value);
        }

        #endregion

        #region Wait Methods

        /// <summary>
        /// Waits until an element matching the search exists.
        /// </summary>
        /// <param name="search">The search query</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <returns>True if element found, false if timeout</returns>
        public static async Task<bool> WaitFor(Search search, float timeout = 10f)
        {
            await using var action = await RunAction($"WaitFor({search}, timeout={timeout}s)");
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                var result = search.FindFirst();
                if (result != null)
                {
                    action.SetResult($"found '{result.name}'");
                    return true;
                }
                await Task.Delay(100);
            }

            action.Fail($"timed out after {timeout}s");
            return false;
        }

        /// <summary>
        /// Waits until an element's text matches the expected value.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="expectedText">Expected text content</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <returns>True if text matched, false if timeout</returns>
        public static async Task<bool> WaitFor(Search search, string expectedText, float timeout = 10f)
        {
            await using var action = await RunAction($"WaitFor({search}, text=\"{expectedText}\", timeout={timeout}s)");
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                var go = search.FindFirst();
                if (go != null)
                {
                    string actual = null;
                    var tmp = go.GetComponent<TMPro.TMP_Text>();
                    if (tmp != null)
                        actual = tmp.text;
                    else
                    {
                        var legacy = go.GetComponent<UnityEngine.UI.Text>();
                        if (legacy != null)
                            actual = legacy.text;
                    }

                    if (actual == expectedText)
                    {
                        float elapsed = Time.realtimeSinceStartup - startTime;
                        action.SetResult($"satisfied after {elapsed:F2}s");
                        return true;
                    }
                }
                await Task.Delay(100);
            }

            action.Fail($"timed out after {timeout}s");
            return false;
        }

        /// <summary>
        /// Waits until no element matching the search exists.
        /// </summary>
        /// <param name="search">The search query</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <returns>True if element gone, false if timeout</returns>
        public static async Task<bool> WaitForNot(Search search, float timeout = 10f)
        {
            await using var action = await RunAction($"WaitForNot({search}, timeout={timeout}s)");
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                var result = search.FindFirst();
                if (result == null)
                {
                    float elapsed = Time.realtimeSinceStartup - startTime;
                    action.SetResult($"satisfied after {elapsed:F2}s");
                    return true;
                }
                await Task.Delay(100);
            }

            action.Fail($"timed out after {timeout}s");
            return false;
        }

        /// <summary>
        /// Waits until a static path resolves to a truthy value.
        /// </summary>
        /// <param name="path">Dot-separated path to the value (e.g., "GameManager.Instance.IsReady")</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <returns>True if truthy, false if timeout</returns>
        public static async Task<bool> WaitFor(string path, float timeout = 10f)
        {
            await using var action = await RunAction($"WaitFor(path=\"{path}\", timeout={timeout}s)");
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                try
                {
                    var value = ResolveStaticPath(path);
                    if (IsTruthy(value))
                    {
                        float elapsed = Time.realtimeSinceStartup - startTime;
                        action.SetResult($"satisfied after {elapsed:F2}s");
                        return true;
                    }
                }
                catch
                {
                    // Path might not be resolvable yet, keep trying
                }

                await Task.Delay(100);
            }

            action.Fail($"timed out after {timeout}s");
            return false;
        }

        /// <summary>
        /// Waits until a static path equals an expected value.
        /// </summary>
        /// <typeparam name="T">Type of the value</typeparam>
        /// <param name="path">Dot-separated path to the value</param>
        /// <param name="expected">Expected value to wait for</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <returns>True if value matched, false if timeout</returns>
        public static async Task<bool> WaitFor<T>(string path, T expected, float timeout = 10f)
        {
            await using var action = await RunAction($"WaitFor(path=\"{path}\", expected={expected}, timeout={timeout}s)");
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                try
                {
                    var value = ResolveStaticPath(path);
                    if (value is T typedValue && Equals(typedValue, expected))
                    {
                        float elapsed = Time.realtimeSinceStartup - startTime;
                        action.SetResult($"satisfied after {elapsed:F2}s");
                        return true;
                    }
                    // Try conversion
                    if (value != null)
                    {
                        try
                        {
                            var converted = (T)Convert.ChangeType(value, typeof(T));
                            if (Equals(converted, expected))
                            {
                                float elapsed = Time.realtimeSinceStartup - startTime;
                                action.SetResult($"satisfied after {elapsed:F2}s");
                                return true;
                            }
                        }
                        catch { }
                    }
                }
                catch
                {
                    // Path might not be resolvable yet, keep trying
                }

                await Task.Delay(100);
            }

            action.Fail($"timed out after {timeout}s");
            return false;
        }

        /// <summary>
        /// Resolves a dot-separated path to a value using reflection.
        /// Searches all loaded assemblies for matching types.
        /// Public entry point for Search.Reflect().
        /// </summary>
        public static object ResolveStaticPathPublic(string path) => ResolveStaticPath(path);

        private static object ResolveStaticPath(string path)
        {
            LogDebug($"Reflect(\"{path}\")");
            if (string.IsNullOrWhiteSpace(path))
                throw new UIAutomationException("Reflect path cannot be null or empty");

            var parts = path.Split('.');

            // Try progressively longer type names to find the base type
            Type type = null;
            int memberStartIndex = 0;

            for (int i = 1; i <= parts.Length; i++)
            {
                var typeName = string.Join(".", parts, 0, i);
                type = FindTypeInAllAssemblies(typeName);
                if (type != null)
                {
                    memberStartIndex = i;
                    break;
                }
            }

            if (type == null)
            {
                // Try to find types ending with the first part of the path
                var firstPart = parts[0];
                var matches = FindTypesEndingWith(firstPart);

                if (matches.Count == 1)
                {
                    // Exactly one match - use it automatically
                    type = matches[0];
                    memberStartIndex = 1;
                }
                else
                {
                    var hint = matches.Count > 1
                        ? $" Multiple types found: {string.Join(", ", matches.Select(t => t.FullName).Take(5))}. Use full namespace to disambiguate."
                        : " Make sure to include the full namespace (e.g., 'MyNamespace.MyClass.Property').";
                    throw new UIAutomationException($"Reflect: Could not find type '{firstPart}' in path: {path}.{hint}");
                }
            }

            // If path resolves to just a type with no members, return the Type object
            // This allows subsequent Property() calls to access static members
            if (memberStartIndex >= parts.Length)
                return type;

            // Navigate through members
            object current = null;
            Type currentType = type;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

            for (int i = memberStartIndex; i < parts.Length; i++)
            {
                var part = parts[i];

                // Check for indexer syntax: PropertyName[index] or PropertyName["key"]
                var bracketStart = part.IndexOf('[');
                string memberName = bracketStart >= 0 ? part.Substring(0, bracketStart) : part;
                string indexerPart = bracketStart >= 0 ? part.Substring(bracketStart) : null;

                // Navigate to property/field first (if member name exists)
                if (!string.IsNullOrEmpty(memberName))
                {
                    // Try property first
                    var prop = currentType.GetProperty(memberName, flags);
                    if (prop != null)
                    {
                        current = prop.GetValue(current);
                        if (current == null && (indexerPart != null || i < parts.Length - 1))
                            throw new InvalidOperationException($"Null reference at '{memberName}' in path: {path}");
                        currentType = current?.GetType() ?? prop.PropertyType;
                    }
                    else
                    {
                        // Try field
                        var field = currentType.GetField(memberName, flags);
                        if (field != null)
                        {
                            current = field.GetValue(current);
                            if (current == null && (indexerPart != null || i < parts.Length - 1))
                                throw new InvalidOperationException($"Null reference at '{memberName}' in path: {path}");
                            currentType = current?.GetType() ?? field.FieldType;
                        }
                        else
                        {
                            // Check if this is a nested type (allows OuterClass.InnerClass syntax)
                            var nestedType = currentType.GetNestedType(memberName, BindingFlags.Public | BindingFlags.NonPublic);
                            if (nestedType != null)
                            {
                                // Switch to the nested type and continue navigation
                                currentType = nestedType;
                                current = null; // Nested types don't have an instance
                                // Don't apply indexer to types
                                if (indexerPart != null)
                                    throw new InvalidOperationException($"Cannot apply indexer to type '{nestedType.Name}' in path: {path}");
                                continue;
                            }

                            throw new InvalidOperationException($"Member '{memberName}' not found on type '{currentType.Name}' in path: {path}");
                        }
                    }
                }

                // Apply indexer if present
                if (indexerPart != null)
                {
                    current = ApplyIndexer(current, indexerPart, path);
                    if (current == null && i < parts.Length - 1)
                        throw new InvalidOperationException($"Null reference at indexer '{indexerPart}' in path: {path}");
                    currentType = current?.GetType() ?? typeof(object);
                }
            }

            // If we ended up with a type but no instance (e.g., navigated to a nested type),
            // return the Type object to allow subsequent Property() calls
            if (current == null && currentType != type)
                return currentType;

            return current;
        }

        /// <summary>
        /// Parses and applies an indexer expression like [0], [123], ["key"], or ['key'].
        /// Supports chained indexers like [0][1] or [0]["key"].
        /// </summary>
        private static object ApplyIndexer(object target, string indexerExpr, string fullPath)
        {
            if (target == null || string.IsNullOrEmpty(indexerExpr))
                return target;

            int pos = 0;
            while (pos < indexerExpr.Length && target != null)
            {
                if (indexerExpr[pos] != '[')
                    break;

                int closePos = FindMatchingBracket(indexerExpr, pos);
                if (closePos < 0)
                    throw new InvalidOperationException($"Unmatched '[' in indexer expression in path: {fullPath}");

                var indexContent = indexerExpr.Substring(pos + 1, closePos - pos - 1).Trim();

                // Check if it's a string key (quoted)
                if ((indexContent.StartsWith("\"") && indexContent.EndsWith("\"")) ||
                    (indexContent.StartsWith("'") && indexContent.EndsWith("'")))
                {
                    var key = indexContent.Substring(1, indexContent.Length - 2);
                    target = AccessStringIndexer(target, key, fullPath);
                }
                else if (int.TryParse(indexContent, out int index))
                {
                    target = AccessIntIndexer(target, index, fullPath);
                }
                else
                {
                    throw new InvalidOperationException($"Invalid indexer '{indexContent}' in path: {fullPath} - must be an integer or quoted string");
                }

                pos = closePos + 1;
            }

            return target;
        }

        /// <summary>
        /// Finds the matching closing bracket for an opening bracket at the given position.
        /// </summary>
        private static int FindMatchingBracket(string str, int openPos)
        {
            int depth = 0;
            bool inString = false;
            char stringChar = '\0';

            for (int i = openPos; i < str.Length; i++)
            {
                char c = str[i];

                if (inString)
                {
                    if (c == stringChar && (i == 0 || str[i - 1] != '\\'))
                        inString = false;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inString = true;
                    stringChar = c;
                }
                else if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Accesses an integer indexer on the given object.
        /// </summary>
        private static object AccessIntIndexer(object target, int index, string fullPath)
        {
            if (target == null)
                throw new InvalidOperationException($"Cannot access index [{index}] on null in path: {fullPath}");

            var type = target.GetType();

            // Handle arrays directly
            if (type.IsArray)
            {
                var array = (Array)target;
                if (index < 0 || index >= array.Length)
                    throw new IndexOutOfRangeException($"Index [{index}] is out of range for array of length {array.Length} in path: {fullPath}");
                return array.GetValue(index);
            }

            // Handle IList (List<T>, etc.)
            if (target is System.Collections.IList list)
            {
                if (index < 0 || index >= list.Count)
                    throw new IndexOutOfRangeException($"Index [{index}] is out of range for list of count {list.Count} in path: {fullPath}");
                return list[index];
            }

            // Try to find an indexer property with int parameter
            var indexer = type.GetProperty("Item", new[] { typeof(int) });
            if (indexer != null)
            {
                return indexer.GetValue(target, new object[] { index });
            }

            throw new InvalidOperationException($"Type '{type.Name}' does not support integer indexer access in path: {fullPath}");
        }

        /// <summary>
        /// Accesses a string indexer on the given object.
        /// </summary>
        private static object AccessStringIndexer(object target, string key, string fullPath)
        {
            if (target == null)
                throw new InvalidOperationException($"Cannot access index [\"{key}\"] on null in path: {fullPath}");

            var type = target.GetType();

            // Handle IDictionary
            if (target is System.Collections.IDictionary dict)
            {
                if (!dict.Contains(key))
                    throw new KeyNotFoundException($"Key \"{key}\" not found in dictionary in path: {fullPath}");
                return dict[key];
            }

            // Try to find an indexer property with string parameter
            var indexer = type.GetProperty("Item", new[] { typeof(string) });
            if (indexer != null)
            {
                return indexer.GetValue(target, new object[] { key });
            }

            throw new InvalidOperationException($"Type '{type.Name}' does not support string indexer access in path: {fullPath}");
        }

        // Cache for type lookups - maps short name to resolved Type
        private static readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();

        /// <summary>
        /// Searches all loaded assemblies for a type by name. Results are cached.
        /// </summary>
        private static Type FindTypeInAllAssemblies(string typeName)
        {
            if (_typeCache.TryGetValue(typeName, out var cached))
                return cached;

            // Check common Unity types first for performance
            Type result = typeName switch
            {
                "GameObject" => typeof(GameObject),
                "Debug" => typeof(Debug),
                "PlayerPrefs" => typeof(PlayerPrefs),
                "Time" => typeof(Time),
                "Application" => typeof(Application),
                "Screen" => typeof(Screen),
                "Input" => typeof(Input),
                "Resources" => typeof(Resources),
                _ => null
            };

            if (result == null)
            {
                // Search all loaded assemblies
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        result = assembly.GetType(typeName);
                        if (result != null)
                            break;
                    }
                    catch
                    {
                        // Some assemblies may throw on GetType, skip them
                    }
                }
            }

            // If not found, try converting dots to + for nested type syntax
            // This allows "OuterClass.InnerClass" to match "OuterClass+InnerClass"
            if (result == null && typeName.Contains("."))
            {
                var parts = typeName.Split('.');
                // Try progressively replacing dots with + from right to left
                for (int i = parts.Length - 1; i >= 1 && result == null; i--)
                {
                    var prefix = string.Join(".", parts, 0, i);
                    var suffix = string.Join("+", parts, i, parts.Length - i);
                    var nestedTypeName = prefix + "+" + suffix;

                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            result = assembly.GetType(nestedTypeName);
                            if (result != null)
                                break;
                        }
                        catch
                        {
                            // Some assemblies may throw on GetType, skip them
                        }
                    }
                }
            }

            if (result != null)
                _typeCache[typeName] = result;

            return result;
        }

        /// <summary>
        /// Finds all types whose name ends with the given suffix (e.g., "TestTrucks" finds "MyNamespace.TestTrucks").
        /// Also handles nested type patterns like "OuterClass+InnerClass" by checking FullName.
        /// Results are cached if exactly one match is found.
        /// </summary>
        private static List<Type> FindTypesEndingWith(string typeName)
        {
            // Check cache first - if we have an exact match, return it
            if (_typeCache.TryGetValue(typeName, out var cached))
                return new List<Type> { cached };

            var matches = new List<Type>();

            // Check if this looks like a nested type pattern (contains +)
            bool isNestedPattern = typeName.Contains("+");

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        // Simple name match
                        if (type.Name == typeName)
                        {
                            matches.Add(type);
                        }
                        // For nested type patterns like "OuterClass+InnerClass", check if FullName ends with it
                        else if (isNestedPattern && type.FullName != null &&
                                 (type.FullName.EndsWith("." + typeName) || type.FullName == typeName))
                        {
                            matches.Add(type);
                        }
                    }
                }
                catch
                {
                    // Some assemblies may throw, skip them
                }
            }

            // Cache if exactly one match
            if (matches.Count == 1)
                _typeCache[typeName] = matches[0];

            return matches;
        }

        /// <summary>
        /// Determines if a value is "truthy" (not null, not zero, not false, not empty string).
        /// </summary>
        private static bool IsTruthy(object value)
        {
            if (value == null)
                return false;
            if (value is bool b)
                return b;
            if (value is int i)
                return i != 0;
            if (value is float f)
                return f != 0f;
            if (value is double d)
                return d != 0.0;
            if (value is string s)
                return !string.IsNullOrEmpty(s);
            if (value is long l)
                return l != 0;
            return true; // Non-null object is truthy
        }

        #endregion

        #region Swipe with Direction Enum

        /// <summary>
        /// Performs a swipe gesture on an element.
        /// </summary>
        public static async Task Swipe(Search search, SwipeDirection direction, float distance = 0.2f, float duration = 0.3f, bool throwIfMissing = true, float searchTime = 10)
        {
            await using var action = await RunAction($"Swipe({search}, {direction})");

            var element = await Find<RectTransform>(search, throwIfMissing, searchTime);
            if (element == null)
            {
                action.Fail("Element not found");
                return;
            }

            await InputInjector.Swipe(element.gameObject, direction.ToString().ToLower(), distance, duration);
            action.SetResult($"'{element.name}'");
        }

        /// <summary>
        /// Performs a swipe gesture at screen center.
        /// </summary>
        public static async Task Swipe(SwipeDirection direction, float distance = 0.2f, float duration = 0.3f)
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
        public static async Task SwipeAt(float xPercent, float yPercent, SwipeDirection direction, float distance = 0.2f, float duration = 0.3f)
        {
            await using (await RunAction($"SwipeAt({xPercent:P0}, {yPercent:P0}, {direction})"))
            {
                var startPos = new Vector2(Screen.width * xPercent, Screen.height * yPercent);
                await SwipeAtInternal(startPos, direction.ToString().ToLower(), distance, duration);
            }
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
        public static async Task<GameObject> ScrollTo(Search scrollViewSearch, Search targetSearch, int maxScrollAttempts = 20, bool throwIfMissing = true, float searchTime = 10f)
        {
            await using var action = await RunAction($"ScrollTo({scrollViewSearch}, {targetSearch})");
            var scrollRect = await Find<ScrollRect>(scrollViewSearch, true, searchTime);

            if (scrollRect == null)
            {
                if (throwIfMissing)
                    throw new UIAutomationNotFoundException($"ScrollTo: Could not find ScrollRect matching: {scrollViewSearch}");
                action.Fail("ScrollRect not found");
                return null;
            }

            bool canScrollHorizontal = scrollRect.horizontal;
            bool canScrollVertical = scrollRect.vertical;

            LogDebug($"ScrollTo: found scroll view '{scrollRect.name}', horizontal={canScrollHorizontal}, vertical={canScrollVertical}");

            // First check if target is already visible
            var target = targetSearch.FindFirst();
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
            target = targetSearch.FindFirst();
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
                target = targetSearch.FindFirst();
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
            target = targetSearch.FindFirst();
            if (target != null)
            {
                var targetRect = target.GetComponent<RectTransform>();
                if (targetRect != null && IsVisibleInViewport(scrollRect, targetRect))
                {
                    action.SetResult($"found target '{target.name}' after {attempts} drags");
                    return target;
                }
            }

            action.Fail($"target not found after {attempts} drag attempts");

            if (throwIfMissing)
                throw new Exception($"ScrollTo: Could not find target matching '{targetSearch}' in scroll view after {attempts} drag attempts");

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
                action.Fail("target not found");
                return false;
            }

            var screenPos = InputInjector.GetScreenPosition(target);
            await ClickAtPosition(screenPos, target.name);
            action.SetResult($"clicked '{target.name}'");
            return true;
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
                throw new Exception($"FindItems could not find a supported container matching: {containerSearch}");
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

        #region GameObject Manipulation

        /// <summary>
        /// Disables a GameObject found by search.
        /// </summary>
        public static Task<Search.ActiveState> Disable(Search search, float searchTime = 10f) => search.Disable(searchTime);

        /// <summary>
        /// Enables a GameObject found by search.
        /// </summary>
        public static Task<Search.ActiveState> Enable(Search search, float searchTime = 10f) => search.Enable(searchTime);

        /// <summary>
        /// Freezes a GameObject (zero velocity, kinematic).
        /// </summary>
        public static Task<Search.FreezeState> Freeze(Search search, bool includeChildren = true, float searchTime = 10f) => search.Freeze(includeChildren, searchTime);

        /// <summary>
        /// Teleports a GameObject to a world position.
        /// </summary>
        public static Task<Search.PositionState> Teleport(Search search, Vector3 worldPosition, float searchTime = 10f) => search.Teleport(worldPosition, searchTime);

        /// <summary>
        /// Disables colliders on a GameObject.
        /// </summary>
        public static Task<Search.ColliderState> NoClip(Search search, bool includeChildren = true, float searchTime = 10f) => search.NoClip(includeChildren, searchTime);

        /// <summary>
        /// Enables colliders on a GameObject.
        /// </summary>
        public static Task<Search.ColliderState> Clip(Search search, bool includeChildren = true, float searchTime = 10f) => search.Clip(includeChildren, searchTime);

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
                action.Fail("no clickable elements found");
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
                action.Fail("no clickable elements found after filtering");
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
            var startTime = Time.realtimeSinceStartup;
            int consecutiveNoClick = 0;

            await using var action = await RunAction($"AutoExplore(condition={stopCondition}, value={value})");

            while (Application.isPlaying)
            {
                result.TimeElapsed = Time.realtimeSinceStartup - startTime;

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
