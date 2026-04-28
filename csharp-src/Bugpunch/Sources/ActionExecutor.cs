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
    /// Use with 'using static ODDGames.Bugpunch.ActionExecutor;' for shorthand access.
    /// </summary>
    /// <example>
    /// using static ODDGames.Bugpunch.ActionExecutor;
    ///
    /// [TestFixture]
    /// public class MyTests
    /// {
    ///     [Test]
    ///     public async Task TestLogin()
    ///     {
    ///         await LoadScene("LoginScene");
    ///         await TextInput(Name("Username"), "testuser");
    ///         await Click(Name("LoginButton"));
    ///     }
    /// }
    /// </example>
    public static partial class ActionExecutor
    {
        #region Configuration

        /// <summary>
        /// Gets or sets the delay in milliseconds after a successful action.
        /// Default is 50ms. Increase for slower, more visible test playback.
        /// </summary>
        public static int Interval { get; set; } = 50;

        /// <summary>
        /// Gets or sets the polling interval in milliseconds during search/wait operations.
        /// Default is 100ms.
        /// </summary>
        public static int PollInterval { get; set; } = 100;

        /// <summary>
        /// Gets or sets the default search timeout in seconds for finding UI elements.
        /// Default is 10s for tests. Set to 1s for interactive/bridge usage where fast failure + retry is preferred.
        /// </summary>
        public static float DefaultSearchTime { get; set; } = 10f;

        /// <summary>
        /// When true, increases all intervals and enables verbose logging for debugging tests.
        /// </summary>
        private static bool _debugMode;
        public static bool DebugMode
        {
            get => _debugMode;
            set { _debugMode = value; InputInjector.DebugMode = value; }
        }

        /// <summary>Resolves -1 sentinel to DefaultSearchTime.</summary>
        static float ResolveSearchTime(float searchTime) => searchTime < 0 ? DefaultSearchTime : searchTime;

        /// <summary>
        /// Multiplier for all intervals when DebugMode is enabled. Default is 3x.
        /// </summary>
        public static float DebugIntervalMultiplier { get; set; } = 3f;

        /// <summary>
        /// When true, shows visual overlay for all input actions (clicks, drags, scrolls, key presses).
        /// Uses GL drawing that works over the entire screen regardless of canvas setup.
        /// </summary>
        public static bool ShowInputOverlay
        {
            get => InputVisualizer.Enabled;
            set => InputVisualizer.Enabled = value;
        }

        static int EffectiveInterval => DebugMode ? (int)(Interval * DebugIntervalMultiplier) : Interval;
        static int EffectivePollInterval => DebugMode ? (int)(PollInterval * DebugIntervalMultiplier) : PollInterval;

        /// <summary>
        /// Gets the current time in seconds using system time (unaffected by Unity timeScale).
        /// </summary>
        static float Now => (float)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

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

        #endregion

        #region Button Click Tracking

        private static readonly Dictionary<UnityEngine.UI.Button, UnityEngine.Events.UnityAction> _buttonListeners = new();
        private static string _lastClickedButton = null;

        /// <summary>
        /// Enables tracking of all button clicks in the scene.
        /// Call this to debug which buttons are actually receiving click events.
        /// </summary>
        public static void EnableButtonClickTracking()
        {
            DisableButtonClickTracking(); // Clear any existing listeners

            var buttons = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (var button in buttons)
            {
                var btn = button; // Capture for closure
                UnityEngine.Events.UnityAction listener = () =>
                {
                    _lastClickedButton = btn.name;
                    var screenPos = InputInjector.GetScreenPosition(btn.gameObject);
                    Debug.Log($"[ButtonTracker] CLICKED: '{btn.name}' at ({screenPos.x:F0},{screenPos.y:F0}) interactable={btn.interactable}");
                };
                button.onClick.AddListener(listener);
                _buttonListeners[button] = listener;
            }

            Debug.Log($"[ButtonTracker] Now tracking {buttons.Length} buttons");
        }

        /// <summary>
        /// Disables button click tracking and removes all listeners.
        /// </summary>
        public static void DisableButtonClickTracking()
        {
            foreach (var kvp in _buttonListeners)
            {
                if (kvp.Key != null)
                    kvp.Key.onClick.RemoveListener(kvp.Value);
            }
            _buttonListeners.Clear();
            _lastClickedButton = null;
        }

        /// <summary>
        /// Gets the name of the last button that was clicked (via Unity's onClick event).
        /// Returns null if no button has been clicked since tracking was enabled.
        /// </summary>
        public static string LastClickedButton => _lastClickedButton;

        #endregion

        /// <summary>
        /// Checks if a screen position is within screen bounds and can be clicked.
        /// </summary>
        public static bool IsScreenPositionClickable(Vector2 screenPos)
        {
            return screenPos.x >= 0 && screenPos.x <= Screen.width &&
                   screenPos.y >= 0 && screenPos.y <= Screen.height;
        }

        /// <summary>
        /// Finds a component of type T on matching elements.
        /// </summary>
        internal static async Task<T> FindComponent<T>(Search search, float timeout) where T : Component
        {
            var elements = await search.FindAll(timeout);
            foreach (var go in elements)
            {
                var component = go.GetComponent<T>();
                if (component != null)
                    return component;
            }

            return null;
        }

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

        #region Validation Helpers

        private static void ValidateNormalized(float value, string paramName)
        {
            if (value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(paramName, value,
                    $"{paramName} must be between 0 and 1, got {value}");
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

        static void LogFail(string action, string reason) => Debug.LogWarning($"[UIAutomation] FAILED: {action} - {reason}");

        /// <summary>
        /// Throws a test failure exception for proper Unity Test Runner integration.
        /// </summary>
        static void ThrowTestFailure(string message)
        {
            throw new BugpunchTestFailureException(message);
        }

        public class BugpunchTestFailureException : Exception
        {
            public BugpunchTestFailureException(string message) : base(message) { }
        }

        /// <summary>
        /// Async disposable action scope that logs START on creation, syncs to main thread, and logs COMPLETE on disposal.
        /// Use with 'await using' statement for automatic cleanup.
        /// </summary>
        private class ActionScopeInner
        {
            private readonly string _action;
            private readonly string _callerFile;
            private readonly int _callerLine;
            private readonly string _callerMethod;
            private string _result;
            private bool _disposed;
            private bool _failed;

            public ActionScopeInner(string action, string callerFile, int callerLine, string callerMethod)
            {
                _action = action;

                // Walk the stack to find the test caller (first frame outside UIAutomation namespace)
                if (callerFile == null)
                {
                    var trace = new StackTrace(true);
                    for (int i = 0; i < trace.FrameCount; i++)
                    {
                        var frame = trace.GetFrame(i);
                        var method = frame.GetMethod();
                        if (method == null) continue;
                        var ns = method.DeclaringType?.Namespace;
                        if (ns != null && ns.StartsWith("ODDGames.Bugpunch")) continue;
                        // Skip system/runtime frames
                        if (ns != null && (ns.StartsWith("System") || ns.StartsWith("Microsoft"))) continue;
                        if (string.IsNullOrEmpty(frame.GetFileName())) continue;

                        callerFile = frame.GetFileName();
                        callerLine = frame.GetFileLineNumber();
                        callerMethod = method.Name;
                        break;
                    }
                }

                _callerFile = callerFile;
                _callerLine = callerLine;
                _callerMethod = callerMethod;
                TestReport.LogAction($"START: {action}", callerFile, callerLine, callerMethod);
            }

            /// <summary>Sets a result message to be included in the COMPLETE log.</summary>
            public void SetResult(string result)
            {
                _result = result;
                TestReport.CaptureScreenshot($"{_action}_success");
                TestReport.LogAction($"COMPLETE: {_action} -> {result}", _callerFile, _callerLine, _callerMethod);
            }

            /// <summary>Marks the action as failed with a reason. Logs warning only, does not throw.</summary>
            public void Warn(string reason)
            {
                _failed = true;
                TestReport.LogAction($"WARN: {_action} - {reason}", _callerFile, _callerLine, _callerMethod);
            }

            /// <summary>Marks the action as failed with a reason.</summary>
            public void Fail(string reason)
            {
                _failed = true;
                TestReport.RecordFailure();
                TestReport.LogAction($"FAILED: {_action} - {reason}", _callerFile, _callerLine, _callerMethod);
                var message = $"{_action} failed: {reason}";
                // Throw test failure - uses NUnit in test builds, InvalidOperationException in player builds
                ThrowTestFailure(message);
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

                if (!_failed && _result == null)
                {
                    TestReport.LogAction($"COMPLETE: {_action}", _callerFile, _callerLine, _callerMethod);
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
            private readonly string _callerFile;
            private readonly int _callerLine;
            private readonly string _callerMethod;
            private ActionScopeInner _inner;

            public ActionScope(string action, string callerFile, int callerLine, string callerMethod)
            {
                _action = action;
                _callerFile = callerFile;
                _callerLine = callerLine;
                _callerMethod = callerMethod;
                _inner = null;
            }

            /// <summary>Sets a result message to be included in the COMPLETE log.</summary>
            public void SetResult(string result) => _inner?.SetResult(result);

            /// <summary>Marks the action as failed with a reason. Logs warning only, does not throw.</summary>
            public void Warn(string reason) => _inner?.Warn(reason);

            /// <summary>Marks the action as failed with a reason. Logs warning and throws NUnit.Framework.AssertionException.</summary>
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
                private readonly TaskAwaiter _innerAwaiter;

                public ActionScopeAwaiter(ActionScope scope)
                {
                    _scope = scope;
                    _innerAwaiter = Async.ToMainThread().GetAwaiter();
                }

                public bool IsCompleted => _innerAwaiter.IsCompleted;

                public void OnCompleted(Action continuation)
                {
                    // Use the inner task's awaiter to preserve proper continuation context
                    _innerAwaiter.OnCompleted(continuation);
                }

                public ActionScope GetResult()
                {
                    _innerAwaiter.GetResult();
                    var result = _scope;
                    result._inner = new ActionScopeInner(_scope._action, _scope._callerFile, _scope._callerLine, _scope._callerMethod);
                    return result;
                }
            }
        }

        /// <summary>Creates an ActionScope that ensures main thread, logs START now, and syncs/logs COMPLETE when disposed.</summary>
        private static ActionScope RunAction(string action) => new ActionScope(action, null, 0, null);

        #endregion

        #region Scene Management

        /// <summary>
        /// Loads test data from a Resources zip file.
        /// The zip can contain:
        /// - files/ folder: extracted to Application.persistentDataPath
        /// - playerprefs.json: restored to PlayerPrefs
        /// </summary>
        /// <param name="resourcePath">Path relative to Resources folder, without extension (e.g., "TestData/SaveGame")</param>
        /// <param name="clearFiles">If true, clears existing files in persistentDataPath before loading</param>
        public static async Task LoadTestData(string resourcePath, bool clearFiles = true)
        {
            await using var action = await RunAction($"LoadTestData(\"{resourcePath}\")");

            // Try loading the resource - Unity strips .bytes extension but keeps .zip if present
            var zipAsset = Resources.Load<UnityEngine.TextAsset>(resourcePath);

            // If not found, try with .zip suffix (for files named foo.zip.bytes)
            if (zipAsset == null && !resourcePath.EndsWith(".zip"))
            {
                zipAsset = Resources.Load<UnityEngine.TextAsset>(resourcePath + ".zip");
            }

            if (zipAsset == null)
            {
                action.Warn("resource not found");
                ThrowTestFailure($"Test data resource not found: {resourcePath} (tried with and without .zip suffix)");
            }

            var path = Application.persistentDataPath;

            // Clear existing files if requested
            if (clearFiles && System.IO.Directory.Exists(path))
            {
                foreach (var file in System.IO.Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories))
                {
                    try { System.IO.File.Delete(file); }
                    catch { /* ignore */ }
                }
                foreach (var dir in System.IO.Directory.GetDirectories(path))
                {
                    try { System.IO.Directory.Delete(dir, true); }
                    catch { /* ignore */ }
                }
            }

            // Clear PlayerPrefs safely: snapshot Unity-internal keys, DeleteAll, restore them
            if (clearFiles)
            {
                ClearPlayerPrefsSafe();
            }

            int fileCount = 0;
            bool restoredPrefs = false;

            using var stream = new System.IO.MemoryStream(zipAsset.bytes);
            using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                // Handle playerprefs.json at root
                if (entry.FullName == "playerprefs.json")
                {
                    using var reader = new System.IO.StreamReader(entry.Open());
                    var json = reader.ReadToEnd();
                    RestorePlayerPrefsFromJson(json);
                    restoredPrefs = true;
                    continue;
                }

                // Handle files/ folder -> persistentDataPath
                // Normalize path separators (zip might have mixed / and \)
                var fullName = entry.FullName.Replace('\\', '/');
                if (fullName.StartsWith("files/"))
                {
                    var relativePath = fullName.Substring(6);
                    var destPath = System.IO.Path.Combine(path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
                    var destDir = System.IO.Path.GetDirectoryName(destPath);
                    if (!System.IO.Directory.Exists(destDir))
                        System.IO.Directory.CreateDirectory(destDir);

                    using var entryStream = entry.Open();
                    using var fileStream = System.IO.File.Create(destPath);
                    entryStream.CopyTo(fileStream);
                    fileCount++;
                }
            }

            Resources.UnloadAsset(zipAsset);
            action.SetResult($"{fileCount} files" + (restoredPrefs ? " + PlayerPrefs" : ""));
        }

        /// <summary>
        /// Unity-internal PlayerPrefs keys that must survive DeleteAll.
        /// Addressables, Unity Services, and IAP store state in these keys.
        /// </summary>
        private static readonly string[] UnityInternalPlayerPrefsKeys =
        {
            "AddressablesRuntimeDataPath",
            "AddressablesRuntimeBuildLog",
            "UnityInstallationId",
            "unity.cloud_userid",
            "unity.player_sessionid"
        };

        /// <summary>
        /// Clears all PlayerPrefs while preserving Unity-internal keys (e.g. Addressables).
        /// Snapshots known Unity keys, calls DeleteAll, then restores them.
        /// </summary>
        private static void ClearPlayerPrefsSafe()
        {
            // Snapshot Unity-internal keys before clearing
            var saved = new System.Collections.Generic.List<(string key, string value)>();
            foreach (var key in UnityInternalPlayerPrefsKeys)
            {
                if (UnityEngine.PlayerPrefs.HasKey(key))
                {
                    saved.Add((key, UnityEngine.PlayerPrefs.GetString(key)));
                }
            }

            UnityEngine.PlayerPrefs.DeleteAll();

            // Restore Unity-internal keys
            foreach (var (key, value) in saved)
            {
                UnityEngine.PlayerPrefs.SetString(key, value);
            }

            UnityEngine.PlayerPrefs.Save();
        }

        private static void RestorePlayerPrefsFromJson(string json)
        {
            // Simple JSON parsing for PlayerPrefs
            // Format: { "entries": [{ "key": "...", "value": "...", "type": "string|int|float" }, ...] }
            var wrapper = UnityEngine.JsonUtility.FromJson<PlayerPrefsData>(json);
            if (wrapper?.entries != null)
            {
                foreach (var entry in wrapper.entries)
                {
                    switch (entry.type)
                    {
                        case "int":
                            UnityEngine.PlayerPrefs.SetInt(entry.key, int.Parse(entry.value));
                            break;
                        case "float":
                            UnityEngine.PlayerPrefs.SetFloat(entry.key, float.Parse(entry.value));
                            break;
                        default:
                            UnityEngine.PlayerPrefs.SetString(entry.key, entry.value);
                            break;
                    }
                }
                UnityEngine.PlayerPrefs.Save();
            }
        }

        [System.Serializable]
        private class PlayerPrefsData { public PlayerPrefsEntry[] entries; }

        [System.Serializable]
        private class PlayerPrefsEntry { public string key; public string value; public string type; }

        /// <summary>
        /// Waits until the frame rate stabilizes at or above the specified FPS.
        /// Useful after scene loads or heavy operations.
        /// </summary>
        /// <param name="minFps">Minimum FPS to consider stable (default 20).</param>
        /// <param name="stableFrames">Number of consecutive frames at target FPS (default 5).</param>
        /// <param name="timeout">Maximum time to wait (default 10 seconds).</param>
        public static async Task WaitForStableFrameRate(float minFps = 20f, int stableFrames = 5, float timeout = 10f)
        {
            await using var action = await RunAction($"WaitForStableFrameRate(minFps={minFps}, stableFrames={stableFrames}, timeout={timeout}s)");

            float startTime = Now;
            int consecutiveStableFrames = 0;
            float minDeltaTime = 1f / minFps;

            while ((Now - startTime) < timeout && Application.isPlaying)
            {
                await Async.DelayFrames(1);

                // Use unscaledDeltaTime to ignore TimeScale
                if (Time.unscaledDeltaTime <= minDeltaTime)
                {
                    consecutiveStableFrames++;
                    if (consecutiveStableFrames >= stableFrames)
                    {
                        float elapsed = Now - startTime;
                        action.SetResult($"stable after {elapsed:F2}s");
                        return;
                    }
                }
                else
                {
                    consecutiveStableFrames = 0;
                }
            }

            float totalElapsed = Now - startTime;
            action.SetResult($"timed out after {totalElapsed:F2}s (did not stabilize)");
        }

        /// <summary>
        /// Waits until the scene changes from the current scene.
        /// </summary>
        public static async Task SceneChange(float seconds = 30)
        {
            string startScene = SceneManager.GetActiveScene().name;
            await using var action = await RunAction($"SceneChange(from=\"{startScene}\", timeout={seconds}s)");
            float startTime = Now;

            while ((Now - startTime) < seconds && Application.isPlaying)
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

            float startTime = Now;
            int attempts = 0;

            while (attempts < maxAttempts && (Now - startTime) < timeout)
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

            var startTime = Now;

            while ((Now - startTime) < seconds && Application.isPlaying)
            {
                if (condition())
                {
                    float elapsed = Now - startTime;
                    action.SetResult($"satisfied after {elapsed:F2}s");
                    return;
                }

                await Task.Delay(EffectivePollInterval);
            }

            action.Fail($"condition not met within {seconds}s");
        }

        /// <summary>
        /// Waits until the game achieves a target framerate.
        /// </summary>
        public static async Task WaitFramerate(int averageFps, float sampleDuration = 2f, float timeout = 60f)
        {
            await using var action = await RunAction($"WaitFramerate(target={averageFps}fps, timeout={timeout}s)");

            float startTime = Now;

            while ((Now - startTime) < timeout && Application.isPlaying)
            {
                float sampleStart = Now;
                int frameCount = 0;

                while ((Now - sampleStart) < sampleDuration && Application.isPlaying)
                {
                    await Task.Yield();
                    frameCount++;
                }

                float currentFps = frameCount / (Now - sampleStart);

                if (currentFps >= averageFps)
                {
                    action.SetResult($"achieved {currentFps:F1} FPS");
                    return;
                }
            }

            action.Fail($"did not reach target within {timeout}s");
        }

        #endregion

        #region Find Operations

        /// <summary>
        /// Finds an element matching the search query.
        /// </summary>
        public static async Task<T> Find<T>(Search search, bool throwIfMissing = true, float seconds = 10) where T : Component
        {
            await using var action = await RunAction($"Find<{typeof(T).Name}>({search})");

            var result = await search.Find(seconds);
            if (result != null)
            {
                var component = result.GetComponent<T>() ?? result.GetComponentInChildren<T>() ?? result.GetComponentInParent<T>();
                if (component != null)
                {
                    action.SetResult($"'{result.name}'");
                    return component;
                }
            }

            if (throwIfMissing)
            {
                action.Fail($"element not found within {seconds}s");
            }

            action.Warn($"element not found within {seconds}s");
            return null;
        }

        /// <summary>
        /// Finds all elements matching the search query.
        /// </summary>
        public static async Task<IEnumerable<T>> FindAll<T>(Search search, float seconds = 10) where T : Component
        {
            LogDebug($"FindAll<{typeof(T).Name}> search={search}");

            var gameObjects = await search.FindAll(seconds);
            var results = gameObjects
                .Select(go => go.GetComponent<T>() ?? go.GetComponentInChildren<T>() ?? go.GetComponentInParent<T>())
                .Where(c => c != null);

            LogDebug($"FindAll<{typeof(T).Name}> found {results.Count()} elements");
            return results;
        }

        #endregion

        #region Receiver Logging Helpers

        /// <summary>
        /// Gets the full hierarchy path of a GameObject for debug logging.
        /// </summary>
        static string GetHierarchyPath(GameObject go)
        {
            if (go == null) return "(none)";
            var path = go.name;
            var t = go.transform.parent;
            while (t != null)
            {
                path = t.name + "/" + path;
                t = t.parent;
            }
            return path;
        }

        /// <summary>
        /// Formats receiver list for logging.
        /// </summary>
        static string FormatReceivers(List<GameObject> receivers)
        {
            if (receivers == null || receivers.Count == 0)
                return "no receivers";
            return string.Join(", ", receivers.Select(GetHierarchyPath));
        }

        /// <summary>
        /// Builds a detailed error message when a search fails.
        /// Includes receiver info and guidance when RequiresReceiver() is used.
        /// </summary>
        static string BuildSearchFailureMessage(Search search, float searchTime)
        {
            // Use rich diagnostics when available (interactive editor mode)
            if (TestReport.IsActive)
                return TestReport.BuildFailureReport(search, searchTime);

            var msg = $"Element not found within {ResolveSearchTime(searchTime)}s";

            // If the search uses RequiresReceiver, provide additional context
            if (search.UsesReceiverFilter)
            {
                var receivers = search.Receivers;
                if (receivers != null && receivers.Count > 0)
                {
                    msg += $". Receivers found at position: {FormatReceivers(receivers.ToList())}";
                }
                else
                {
                    msg += ". No receivers found at element position";
                }
                msg += ". To click without receiver validation, remove .RequiresReceiver() from the search";
            }

            return msg;
        }

        #endregion

        #region GetValue Methods

        /// <summary>
        /// Gets a value from a UI element found by the search.
        /// Supports: string (text), bool (toggle), float (slider/scrollbar), int, and arrays.
        /// </summary>
        /// <typeparam name="T">Type of value to get: string, bool, float, int, or T[]</typeparam>
        /// <param name="search">The search query to find the element</param>
        /// <param name="timeout">Maximum time to search for the element</param>
        /// <returns>The value from the element</returns>
        /// <example>
        /// var text = await GetValue&lt;string&gt;(Name("ScoreLabel"));
        /// var isOn = await GetValue&lt;bool&gt;(Name("SoundToggle"));
        /// var volume = await GetValue&lt;float&gt;(Name("VolumeSlider"));
        /// var items = await GetValue&lt;string[]&gt;(Name("Dropdown"));
        /// </example>
        public static async Task<T> GetValue<T>(Search search, float timeout = 10f)
        {
            var go = await search.Find(timeout);
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
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when element is not found within timeout</exception>
        public static async Task WaitFor(Search search, float timeout = 10f)
        {
            await using var action = await RunAction($"WaitFor({search}, timeout={timeout}s)");

            var result = await search.Find(timeout);
            if (result != null)
            {
                action.SetResult($"found '{result.name}'");
                return;
            }

            action.Fail($"timed out after {timeout}s");
        }

        /// <summary>
        /// Waits until an element's text matches the expected value.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="expectedText">Expected text content</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when text does not match within timeout</exception>
        public static async Task WaitFor(Search search, string expectedText, float timeout = 10f)
        {
            await using var action = await RunAction($"WaitFor({search}, text=\"{expectedText}\", timeout={timeout}s)");
            float startTime = Now;

            while ((Now - startTime) < timeout && Application.isPlaying)
            {
                var go = await search.Find(0.5f); // Short timeout per attempt
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
                        float elapsed = Now - startTime;
                        action.SetResult($"satisfied after {elapsed:F2}s");
                        return;
                    }
                }
            }

            action.Fail($"timed out after {timeout}s");
        }

        /// <summary>
        /// Waits until no element matching the search exists.
        /// </summary>
        /// <param name="search">The search query</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when element still exists after timeout</exception>
        public static async Task WaitForNot(Search search, float timeout = 10f)
        {
            await using var action = await RunAction($"WaitForNot({search}, timeout={timeout}s)");
            float startTime = Now;

            while ((Now - startTime) < timeout && Application.isPlaying)
            {
                var result = await search.Find(0.5f); // Short timeout per attempt
                if (result == null)
                {
                    float elapsed = Now - startTime;
                    action.SetResult($"satisfied after {elapsed:F2}s");
                    return;
                }
            }

            action.Fail($"timed out after {timeout}s");
        }

        /// <summary>
        /// Waits until a static path resolves to a truthy value.
        /// </summary>
        /// <param name="path">Dot-separated path to the value (e.g., "GameManager.Instance.IsReady")</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when value is not truthy within timeout</exception>
        public static async Task WaitFor(string path, float timeout = 10f)
        {
            await using var action = await RunAction($"WaitFor(path=\"{path}\", timeout={timeout}s)");
            float startTime = Now;

            while ((Now - startTime) < timeout && Application.isPlaying)
            {
                try
                {
                    var value = ResolveStaticPath(path);
                    if (IsTruthy(value))
                    {
                        float elapsed = Now - startTime;
                        action.SetResult($"satisfied after {elapsed:F2}s");
                        return;
                    }
                }
                catch
                {
                    // Path might not be resolvable yet, keep trying
                }

                await Task.Delay(100);
            }

            action.Fail($"timed out after {timeout}s");
        }

        /// <summary>
        /// Waits until a static path equals an expected value.
        /// </summary>
        /// <typeparam name="T">Type of the value</typeparam>
        /// <param name="path">Dot-separated path to the value</param>
        /// <param name="expected">Expected value to wait for</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <exception cref="NUnit.Framework.AssertionException">Thrown when value does not match within timeout</exception>
        public static async Task WaitFor<T>(string path, T expected, float timeout = 10f)
        {
            await using var action = await RunAction($"WaitFor(path=\"{path}\", expected={expected}, timeout={timeout}s)");
            float startTime = Now;

            while ((Now - startTime) < timeout && Application.isPlaying)
            {
                try
                {
                    var value = ResolveStaticPath(path);
                    if (value is T typedValue && Equals(typedValue, expected))
                    {
                        float elapsed = Now - startTime;
                        action.SetResult($"satisfied after {elapsed:F2}s");
                        return;
                    }
                    // Try conversion
                    if (value != null)
                    {
                        try
                        {
                            var converted = (T)Convert.ChangeType(value, typeof(T));
                            if (Equals(converted, expected))
                            {
                                float elapsed = Now - startTime;
                                action.SetResult($"satisfied after {elapsed:F2}s");
                                return;
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
                ThrowTestFailure("Reflect path cannot be null or empty");

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
                    ThrowTestFailure($"Reflect: Could not find type '{firstPart}' in path: {path}.{hint}");
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

        #region Screenshot

        /// <summary>
        /// Takes a screenshot and saves it to the persistent data path.
        /// </summary>
        /// <param name="filename">Filename without extension. If null, uses timestamp.</param>
        /// <returns>Full path to the saved screenshot.</returns>
        public static async Task<string> Screenshot(string filename = null)
        {
            await using var action = await RunAction($"Screenshot({filename ?? "auto"})");

            // Wait for rendering
            await Task.Yield();

            if (string.IsNullOrEmpty(filename))
                filename = $"screenshot_{System.DateTime.UtcNow:yyyyMMdd_HHmmss}";

            var path = System.IO.Path.Combine(Application.persistentDataPath, $"{filename}.png");
            ScreenCapture.CaptureScreenshot(path);
            action.SetResult(path);
            return path;
        }

        #endregion

        #region Hierarchy Snapshot

        /// <summary>
        /// Captures a detailed hierarchy snapshot of the current UI state.
        /// Records component properties (text, colors, sizes, interactable states),
        /// child counts, and screen-space bounds for every UI element.
        /// The snapshot is stored in the current diagnostic session and viewable
        /// in the Diagnostics Viewer.
        /// </summary>
        /// <param name="maxDepth">Maximum tree depth to capture. -1 for unlimited (default).</param>
        public static async Task Snapshot(int maxDepth = -1)
        {
            await using var action = await RunAction($"Snapshot(depth={((maxDepth < 0) ? "unlimited" : maxDepth.ToString())})");

            // Wait for rendering to complete so bounds are accurate
            await Task.Yield();

            var filename = TestReport.CaptureDetailedSnapshot(maxDepth);
            TestReport.CaptureScreenshot("snapshot");

            if (filename != null)
                action.SetResult($"captured {filename}");
            else
                action.Warn("no active diagnostic session");
        }

        #endregion

        #region GameObject Manipulation

        /// <summary>
        /// Disables a GameObject found by search.
        /// </summary>
        public static Task<Search.ActiveState> Disable(Search search, float searchTime = -1f) => search.Disable(searchTime);

        /// <summary>
        /// Enables a GameObject found by search.
        /// </summary>
        public static Task<Search.ActiveState> Enable(Search search, float searchTime = -1f) => search.Enable(searchTime);

        /// <summary>
        /// Freezes a GameObject (zero velocity, kinematic).
        /// </summary>
        public static Task<Search.FreezeState> Freeze(Search search, bool includeChildren = true, float searchTime = -1f) => search.Freeze(includeChildren, searchTime);

        /// <summary>
        /// Teleports a GameObject to a world position.
        /// </summary>
        public static Task<Search.PositionState> Teleport(Search search, Vector3 worldPosition, float searchTime = -1f) => search.Teleport(worldPosition, searchTime);

        /// <summary>
        /// Disables colliders on a GameObject.
        /// </summary>
        public static Task<Search.ColliderState> NoClip(Search search, bool includeChildren = true, float searchTime = -1f) => search.NoClip(includeChildren, searchTime);

        /// <summary>
        /// Enables colliders on a GameObject.
        /// </summary>
        public static Task<Search.ColliderState> Clip(Search search, bool includeChildren = true, float searchTime = -1f) => search.Clip(includeChildren, searchTime);

        #endregion
    }
}
