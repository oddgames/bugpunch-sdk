using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ODDGames.UIAutomation
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
    /// Use with 'using static ODDGames.UIAutomation.ActionExecutor;' for shorthand access.
    /// </summary>
    /// <example>
    /// using static ODDGames.UIAutomation.ActionExecutor;
    ///
    /// [TestFixture]
    /// public class MyTests
    /// {
    ///     [UnityTest]
    ///     public IEnumerator TestLogin()
    ///     {
    ///         return UniTask.ToCoroutine(async () =>
    ///         {
    ///             await EnsureSceneLoaded("LoginScene");
    ///             await TextInput(Name("Username"), "testuser");
    ///             await Click(Name("LoginButton"));
    ///         });
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
            Debug.Log($"[UITEST] RandomSeed set to {seed}");
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

        static void Log(string message) => Debug.Log($"[UIAUTOMATION] {message}");

        static void LogDebug(string message)
        {
            if (DebugMode)
                Debug.Log($"[UIAUTOMATION:DEBUG] {message}");
        }

        #endregion

        #region Scene Management

        /// <summary>
        /// Ensures a specific scene is loaded. If not already loaded, loads it.
        /// </summary>
        public static async UniTask EnsureSceneLoaded(string sceneName, float timeout = 30f)
        {
            if (SceneManager.GetActiveScene().name == sceneName)
            {
                await UniTask.Yield();
                return;
            }

            Debug.Log($"[UITEST] Loading scene: {sceneName}");
            var asyncOp = SceneManager.LoadSceneAsync(sceneName);

            float startTime = Time.realtimeSinceStartup;
            while (!asyncOp.isDone && (Time.realtimeSinceStartup - startTime) < timeout)
            {
                await UniTask.Yield();
            }

            if (!asyncOp.isDone)
                throw new TimeoutException($"Scene '{sceneName}' did not load within {timeout} seconds");

            // Let scene initialize
            await UniTask.DelayFrame(3);
            Debug.Log($"[UITEST] Scene loaded: {sceneName}");
        }

        /// <summary>
        /// Waits until the scene changes from the current scene.
        /// </summary>
        public static async UniTask SceneChange(float seconds = 30)
        {
            string startScene = SceneManager.GetActiveScene().name;
            float startTime = Time.realtimeSinceStartup;

            Debug.Log($"[UITEST] SceneChange - waiting for scene change from '{startScene}'");

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying)
            {
                if (SceneManager.GetActiveScene().name != startScene)
                {
                    Debug.Log($"[UITEST] SceneChange - scene changed to '{SceneManager.GetActiveScene().name}'");
                    await ActionComplete();
                    return;
                }

                await UniTask.Delay(EffectivePollInterval, true, PlayerLoopTiming.Update);
            }

            throw new TimeoutException($"Scene did not change from '{startScene}' within {seconds} seconds");
        }

        /// <summary>
        /// Navigates back to the main menu by repeatedly clicking back/close buttons.
        /// </summary>
        public static async UniTask NavigateToMainMenu(
            Search mainMenuIdentifier,
            string[] backButtonPatterns = null,
            int maxAttempts = 20,
            float timeout = 60f)
        {
            backButtonPatterns ??= new[] { "Back", "Close", "Exit", "Return", "*Back*", "*Close*", "*Exit*", "X" };

            float startTime = Time.realtimeSinceStartup;
            int attempts = 0;

            Debug.Log($"[UITEST] NavigateToMainMenu - looking for main menu indicator...");

            while (attempts < maxAttempts && (Time.realtimeSinceStartup - startTime) < timeout)
            {
                // Check if we've reached main menu
                var mainMenuElement = await Find<Transform>(mainMenuIdentifier, throwIfMissing: false, seconds: 0.5f);
                if (mainMenuElement != null)
                {
                    Debug.Log($"[UITEST] NavigateToMainMenu - reached main menu");
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
                        Debug.Log($"[UITEST] NavigateToMainMenu - clicking '{backButton.name}'");
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
                    Debug.Log($"[UITEST] NavigateToMainMenu - no back button found, pressing Escape");
                    await PressKey(Key.Escape);
                    attempts++;
                    await Wait(0.5f);
                }
            }

            throw new TimeoutException($"Could not navigate to main menu within {maxAttempts} attempts or {timeout} seconds");
        }

        #endregion

        #region Wait Operations

        /// <summary>
        /// Waits for the specified duration before continuing.
        /// </summary>
        public static async UniTask Wait(float seconds = 1f)
        {
            LogDebug($"Wait: {seconds}s");
            await UniTask.Delay((int)(seconds * 1000), true, PlayerLoopTiming.Update);
            await ActionComplete();
        }

        /// <summary>
        /// Waits until an element matching the search query appears.
        /// </summary>
        public static async UniTask Wait(Search search, int seconds = 10)
        {
            Debug.Log($"[UITEST] Wait ({seconds}s)");
            await Find<Transform>(search, true, seconds);
        }

        /// <summary>
        /// Waits until a custom condition becomes true.
        /// </summary>
        public static async UniTask WaitFor(Func<bool> condition, float seconds = 60, string description = "condition")
        {
            Debug.Log($"[UITEST] WaitFor ({seconds}) [{description}]");

            var startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying)
            {
                if (condition())
                {
                    LogDebug($"WaitFor: '{description}' satisfied after {Time.realtimeSinceStartup - startTime:F2}s");
                    await ActionComplete();
                    return;
                }

                await UniTask.Delay(EffectivePollInterval, true, PlayerLoopTiming.Update);
            }

            throw new TimeoutException($"Condition '{description}' not met within {seconds} seconds");
        }

        /// <summary>
        /// Waits until the game achieves a target framerate.
        /// </summary>
        public static async UniTask WaitFramerate(int averageFps, float sampleDuration = 2f, float timeout = 60f)
        {
            Debug.Log($"[UITEST] WaitFramerate - waiting for {averageFps} FPS");

            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                float sampleStart = Time.realtimeSinceStartup;
                int frameCount = 0;

                while ((Time.realtimeSinceStartup - sampleStart) < sampleDuration && Application.isPlaying)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update);
                    frameCount++;
                }

                float currentFps = frameCount / (Time.realtimeSinceStartup - sampleStart);

                if (currentFps >= averageFps)
                {
                    Debug.Log($"[UITEST] WaitFramerate - achieved {currentFps:F1} FPS");
                    await ActionComplete();
                    return;
                }
            }

            throw new TimeoutException($"Framerate did not reach {averageFps} FPS within {timeout} seconds");
        }

        static async UniTask ActionComplete()
        {
            await UniTask.Yield(PlayerLoopTiming.Update);
            int delay = DebugMode ? (int)(Interval * DebugIntervalMultiplier) : Interval;
            await UniTask.Delay(delay, true, PlayerLoopTiming.Update);
        }

        #endregion

        #region Text Input

        /// <summary>
        /// Enters text into an input field.
        /// </summary>
        public static async UniTask TextInput(Search search, string input, float seconds = 10, bool pressEnter = false)
        {
            Debug.Log($"[UITEST] TextInput '{input}'");

            var tmpInput = await Find<TMP_InputField>(search, false, 0.1f);
            if (tmpInput != null)
            {
                await InputInjector.TypeIntoField(tmpInput.gameObject, input, clearFirst: true, pressEnter: pressEnter);
                await ActionComplete();
                return;
            }

            var legacyInput = await Find<InputField>(search, true, seconds);
            await InputInjector.TypeIntoField(legacyInput.gameObject, input, clearFirst: true, pressEnter: pressEnter);
            await ActionComplete();
        }

        #endregion

        #region Find Operations

        /// <summary>
        /// Finds an element matching the search query.
        /// </summary>
        public static async UniTask<T> Find<T>(Search search, bool throwIfMissing = true, float seconds = 10) where T : Component
        {
            LogDebug($"Find<{typeof(T).Name}> search={search} timeout={seconds}s");

            var startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < seconds && Application.isPlaying)
            {
                var result = search.FindFirst();
                if (result != null)
                {
                    var component = result.GetComponent<T>() ?? result.GetComponentInChildren<T>() ?? result.GetComponentInParent<T>();
                    if (component != null)
                    {
                        LogDebug($"Find<{typeof(T).Name}> found: {result.name}");
                        return component;
                    }
                }

                await UniTask.Delay(EffectivePollInterval, true, PlayerLoopTiming.Update);
            }

            if (throwIfMissing)
                throw new TimeoutException($"Could not find {typeof(T).Name} matching {search} within {seconds} seconds");

            return null;
        }

        /// <summary>
        /// Finds all elements matching the search query.
        /// </summary>
        public static async UniTask<IEnumerable<T>> FindAll<T>(Search search, float seconds = 10) where T : Component
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

                await UniTask.Delay(EffectivePollInterval, true, PlayerLoopTiming.Update);
            }

            return results;
        }

        #endregion

        #region Keyboard Input

        /// <summary>
        /// Simulates a key press and release using Input System Key enum.
        /// </summary>
        public static async UniTask PressKey(Key key)
        {
            Debug.Log($"[UITEST] PressKey [{key}]");
            await InputInjector.PressKey(key);
            await ActionComplete();
        }

        /// <summary>
        /// Simulates a key press and release using KeyCode.
        /// </summary>
        public static async UniTask PressKey(KeyCode key)
        {
            Debug.Log($"[UITEST] PressKey [{key}]");
            var inputKey = KeyCodeToKey(key);
            if (inputKey != Key.None)
            {
                await InputInjector.PressKey(inputKey);
            }
            await ActionComplete();
        }

        /// <summary>
        /// Types a string of text by pressing each character key.
        /// </summary>
        public static async UniTask PressKeys(string text)
        {
            Debug.Log($"[UITEST] PressKeys '{text}'");
            foreach (char c in text)
            {
                var key = CharToKey(c);
                if (key != Key.None)
                {
                    await InputInjector.PressKey(key);
                    await UniTask.Delay(50, true, PlayerLoopTiming.Update);
                }
            }
            await ActionComplete();
        }

        /// <summary>
        /// Holds a key down for a specified duration.
        /// </summary>
        public static async UniTask HoldKey(Key key, float duration)
        {
            Debug.Log($"[UITEST] HoldKey [{key}] for {duration}s");
            await InputInjector.HoldKey(key, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Holds multiple keys down simultaneously for a specified duration.
        /// </summary>
        public static async UniTask HoldKeys(float duration, params Key[] keys)
        {
            Debug.Log($"[UITEST] HoldKeys [{string.Join(", ", keys)}] for {duration}s");
            await InputInjector.HoldKeys(keys, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Holds a key down for a specified duration using KeyCode.
        /// </summary>
        public static async UniTask HoldKey(KeyCode key, float duration)
        {
            Debug.Log($"[UITEST] HoldKey [{key}] for {duration}s");
            var inputKey = KeyCodeToKey(key);
            if (inputKey != Key.None)
            {
                await InputInjector.HoldKey(inputKey, duration);
                await ActionComplete();
            }
        }

        /// <summary>
        /// Holds multiple keys down simultaneously using KeyCode.
        /// </summary>
        public static async UniTask HoldKeys(float duration, params KeyCode[] keys)
        {
            var inputKeys = keys
                .Select(k => KeyCodeToKey(k))
                .Where(k => k != Key.None)
                .ToArray();

            if (inputKeys.Length > 0)
            {
                Debug.Log($"[UITEST] HoldKeys [{string.Join(", ", keys)}] for {duration}s");
                await InputInjector.HoldKeys(inputKeys, duration);
                await ActionComplete();
            }
        }

        /// <summary>
        /// Simulates pressing a key using a character.
        /// </summary>
        public static async UniTask PressKey(char c)
        {
            Debug.Log($"[UITEST] PressKey '{c}'");
            var key = CharToKey(c);
            if (key != Key.None)
            {
                await InputInjector.PressKey(key);
                await ActionComplete();
            }
        }

        /// <summary>
        /// Simulates pressing a key using a key name string.
        /// </summary>
        public static async UniTask PressKey(string keyName)
        {
            Debug.Log($"[UITEST] PressKey \"{keyName}\"");
            if (keyName.Length == 1)
            {
                await PressKey(keyName[0]);
                return;
            }

            if (Enum.TryParse<KeyCode>(keyName, true, out var keyCode))
            {
                await PressKey(keyCode);
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
                await PressKey(mappedKey);
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
        public static async UniTask<bool> Click(Search search, float searchTime = 10f, int index = 0)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                {
                    var target = results[index];
                    var screenPos = InputInjector.GetScreenPosition(target);
                    Log($"Click({search}) -> '{target.name}' at ({screenPos.x:F0},{screenPos.y:F0})");
                    await InputInjector.InjectPointerTap(screenPos);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Clicks on a target GameObject. Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="target">The GameObject to click on</param>
        internal static async UniTask Click(GameObject target)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Click target cannot be null");

            var screenPos = InputInjector.GetScreenPosition(target);
            Log($"Click '{target.name}' at ({screenPos.x:F0},{screenPos.y:F0})");
            await InputInjector.InjectPointerTap(screenPos);
        }

        /// <summary>
        /// Clicks at a specific screen position.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to click</param>
        public static async UniTask ClickAt(Vector2 screenPosition)
        {
            Log($"Click at ({screenPosition.x:F0}, {screenPosition.y:F0})");
            await InputInjector.InjectPointerTap(screenPosition);
        }

        /// <summary>
        /// Clicks at a specific screen position using X and Y coordinates.
        /// </summary>
        /// <param name="x">Screen X coordinate</param>
        /// <param name="y">Screen Y coordinate</param>
        public static async UniTask ClickAt(float x, float y)
        {
            await ClickAt(new Vector2(x, y));
        }

        /// <summary>
        /// Double-clicks on a UI element matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <returns>True if element was found and double-clicked, false otherwise</returns>
        public static async UniTask<bool> DoubleClick(Search search, float searchTime = 10f, int index = 0)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                {
                    var target = results[index];
                    var screenPos = InputInjector.GetScreenPosition(target);
                    Log($"DoubleClick({search}) -> '{target.name}' at ({screenPos.x:F0}, {screenPos.y:F0})");
                    await InputInjector.InjectPointerDoubleTap(screenPos);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Double-clicks on a target GameObject. Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="target">The GameObject to double-click on</param>
        internal static async UniTask DoubleClick(GameObject target)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "DoubleClick target cannot be null");

            var screenPos = InputInjector.GetScreenPosition(target);
            Log($"DoubleClick at ({screenPos.x:F0}, {screenPos.y:F0}) on '{target.name}'");
            await InputInjector.InjectPointerDoubleTap(screenPos);
        }

        /// <summary>
        /// Double-clicks at a specific screen position.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to double-click</param>
        public static async UniTask DoubleClickAt(Vector2 screenPosition)
        {
            Log($"DoubleClick at ({screenPosition.x:F0}, {screenPosition.y:F0})");
            await InputInjector.InjectPointerDoubleTap(screenPosition);
        }

        /// <summary>
        /// Triple-clicks on a UI element matching the search query.
        /// Performs three rapid clicks in succession.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <returns>True if element was found and triple-clicked, false otherwise</returns>
        public static async UniTask<bool> TripleClick(Search search, float searchTime = 10f, int index = 0)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                {
                    var target = results[index];
                    var screenPos = InputInjector.GetScreenPosition(target);
                    Log($"TripleClick({search}) -> '{target.name}' at ({screenPos.x:F0}, {screenPos.y:F0})");
                    await InputInjector.InjectPointerTripleTap(screenPos);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Triple-clicks on a target GameObject. Internal use only - prefer Search-based overloads.
        /// Performs three rapid clicks in succession.
        /// </summary>
        /// <param name="target">The GameObject to triple-click on</param>
        internal static async UniTask TripleClick(GameObject target)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "TripleClick target cannot be null");

            var screenPos = InputInjector.GetScreenPosition(target);
            Log($"TripleClick at ({screenPos.x:F0}, {screenPos.y:F0}) on '{target.name}'");
            await InputInjector.InjectPointerTripleTap(screenPos);
        }

        /// <summary>
        /// Triple-clicks at a specific screen position.
        /// Performs three rapid clicks in succession.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to triple-click</param>
        public static async UniTask TripleClickAt(Vector2 screenPosition)
        {
            Log($"TripleClick at ({screenPosition.x:F0}, {screenPosition.y:F0})");
            await InputInjector.InjectPointerTripleTap(screenPosition);
        }

        /// <summary>
        /// Holds/long-presses on a UI element matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="seconds">Duration of the hold in seconds</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <returns>True if element was found and held, false otherwise</returns>
        public static async UniTask<bool> Hold(Search search, float seconds, float searchTime = 10f, int index = 0)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                {
                    var target = results[index];
                    var screenPos = InputInjector.GetScreenPosition(target);
                    Log($"Hold({search}) -> '{target.name}' at ({screenPos.x:F0}, {screenPos.y:F0}) for {seconds}s");
                    await InputInjector.InjectPointerHold(screenPos, seconds);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Holds/long-presses on a target GameObject. Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="target">The GameObject to hold on</param>
        /// <param name="seconds">Duration of the hold in seconds</param>
        internal static async UniTask Hold(GameObject target, float seconds)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Hold target cannot be null");

            var screenPos = InputInjector.GetScreenPosition(target);
            Log($"Hold at ({screenPos.x:F0}, {screenPos.y:F0}) on '{target.name}' for {seconds}s");
            await InputInjector.InjectPointerHold(screenPos, seconds);
        }

        /// <summary>
        /// Holds/long-presses at a specific screen position.
        /// </summary>
        /// <param name="screenPosition">Screen coordinates to hold</param>
        /// <param name="seconds">Duration of the hold in seconds</param>
        public static async UniTask HoldAt(Vector2 screenPosition, float seconds)
        {
            Log($"Hold at ({screenPosition.x:F0}, {screenPosition.y:F0}) for {seconds}s");
            await InputInjector.InjectPointerHold(screenPosition, seconds);
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
        public static async UniTask<bool> Type(Search search, string text, bool clearFirst = true, bool pressEnter = false, float searchTime = 10f, int index = 0)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                {
                    var target = results[index];
                    Log($"Type({search}) -> '{target.name}' text=\"{text}\" (clear={clearFirst}, enter={pressEnter})");
                    await InputInjector.TypeIntoField(target, text, clearFirst, pressEnter);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Types text into an input field. Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="inputField">The input field GameObject (TMP_InputField or InputField)</param>
        /// <param name="text">The text to type</param>
        /// <param name="clearFirst">Whether to clear existing text first</param>
        /// <param name="pressEnter">Whether to press Enter after typing</param>
        internal static async UniTask Type(GameObject inputField, string text, bool clearFirst = true, bool pressEnter = false)
        {
            if (inputField == null)
                throw new System.ArgumentNullException(nameof(inputField), "Type target cannot be null");

            Log($"Type \"{text}\" into '{inputField.name}' (clear={clearFirst}, enter={pressEnter})");
            await InputInjector.TypeIntoField(inputField, text, clearFirst, pressEnter);
        }

        /// <summary>
        /// Types text without targeting a specific input field (assumes something is focused).
        /// </summary>
        /// <param name="text">The text to type</param>
        public static async UniTask TypeText(string text)
        {
            Log($"TypeText \"{text}\"");
            await InputInjector.TypeText(text);
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
        public static async UniTask<bool> Drag(Search search, Vector2 direction, float duration = 1.0f, float searchTime = 10f, int index = 0, float holdTime = 0.5f, PointerButton button = PointerButton.Left)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                {
                    var target = results[index];
                    var screenPos = InputInjector.GetScreenPosition(target);
                    var endPos = screenPos + direction;
                    Log($"Drag({search}) -> '{target.name}' from ({screenPos.x:F0}, {screenPos.y:F0}) by ({direction.x:F0}, {direction.y:F0}) over {duration}s (hold={holdTime}s, button={button})");
                    await InputInjector.InjectPointerDrag(screenPos, endPos, duration, holdTime, button);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

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
        public static async UniTask<bool> DragTo(Search fromSearch, Search toSearch, float duration = 1.0f, float searchTime = 10f, float holdTime = 0.5f, PointerButton button = PointerButton.Left)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var fromResults = fromSearch.FindAll();
                var toResults = toSearch.FindAll();
                if (fromResults.Count > 0 && toResults.Count > 0)
                {
                    var fromTarget = fromResults[0];
                    var toTarget = toResults[0];
                    var fromPos = InputInjector.GetScreenPosition(fromTarget);
                    var toPos = InputInjector.GetScreenPosition(toTarget);
                    Log($"DragTo({fromSearch}) -> '{fromTarget.name}' to ({toSearch}) -> '{toTarget.name}' over {duration}s (hold={holdTime}s, button={button})");
                    await InputInjector.InjectPointerDrag(fromPos, toPos, duration, holdTime, button);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Drags from a source GameObject in a direction. Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="source">The GameObject to drag from</param>
        /// <param name="direction">The direction to drag (pixel offset)</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        /// <param name="button">Which mouse button to use for dragging</param>
        internal static async UniTask Drag(GameObject source, Vector2 direction, float duration = 1.0f, float holdTime = 0.5f, PointerButton button = PointerButton.Left)
        {
            if (source == null)
                throw new System.ArgumentNullException(nameof(source), "Drag source cannot be null");

            var startPos = InputInjector.GetScreenPosition(source);
            var endPos = startPos + direction;
            Log($"Drag from ({startPos.x:F0}, {startPos.y:F0}) by ({direction.x:F0}, {direction.y:F0}) over {duration}s (hold={holdTime}s, button={button})");
            await InputInjector.InjectPointerDrag(startPos, endPos, duration, holdTime, button);
        }

        /// <summary>
        /// Drags from a source GameObject in a named direction. Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="source">The GameObject to drag from</param>
        /// <param name="direction">Direction name: "up", "down", "left", "right"</param>
        /// <param name="normalizedDistance">Distance as fraction of screen height (0-1)</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        /// <param name="button">Which mouse button to use for dragging</param>
        internal static async UniTask Drag(GameObject source, string direction, float normalizedDistance = 0.2f, float duration = 1.0f, float holdTime = 0.5f, PointerButton button = PointerButton.Left)
        {
            if (source == null)
                throw new System.ArgumentNullException(nameof(source), "Drag source cannot be null");

            var offset = InputInjector.GetDirectionOffset(direction, normalizedDistance);
            await Drag(source, offset, duration, holdTime, button);
        }

        /// <summary>
        /// Drags from a source GameObject to a target GameObject. Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="source">The GameObject to drag from</param>
        /// <param name="target">The GameObject to drag to</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        /// <param name="button">Which mouse button to use for dragging</param>
        internal static async UniTask DragTo(GameObject source, GameObject target, float duration = 1.0f, float holdTime = 0.5f, PointerButton button = PointerButton.Left)
        {
            if (source == null)
                throw new System.ArgumentNullException(nameof(source), "Drag source cannot be null");
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Drag target cannot be null");

            var startPos = InputInjector.GetScreenPosition(source);
            var endPos = InputInjector.GetScreenPosition(target);
            Log($"Drag from '{source.name}' to '{target.name}' over {duration}s (hold={holdTime}s, button={button})");
            await InputInjector.InjectPointerDrag(startPos, endPos, duration, holdTime, button);
        }

        /// <summary>
        /// Drags from a source GameObject to a target screen position. Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="source">The GameObject to drag from</param>
        /// <param name="targetPosition">The screen position to drag to</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        /// <param name="button">Which mouse button to use for dragging</param>
        internal static async UniTask DragTo(GameObject source, Vector2 targetPosition, float duration = 1.0f, float holdTime = 0.5f, PointerButton button = PointerButton.Left)
        {
            if (source == null)
                throw new System.ArgumentNullException(nameof(source), "Drag source cannot be null");

            var startPos = InputInjector.GetScreenPosition(source);
            Log($"Drag from '{source.name}' to ({targetPosition.x:F0}, {targetPosition.y:F0}) over {duration}s (hold={holdTime}s, button={button})");
            await InputInjector.InjectPointerDrag(startPos, targetPosition, duration, holdTime, button);
        }

        /// <summary>
        /// Drags between two screen positions.
        /// </summary>
        /// <param name="startPosition">Start screen position</param>
        /// <param name="endPosition">End screen position</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        /// <param name="button">Which mouse button to use for dragging</param>
        public static async UniTask DragFromTo(Vector2 startPosition, Vector2 endPosition, float duration = 1.0f, float holdTime = 0.5f, PointerButton button = PointerButton.Left)
        {
            Log($"Drag from ({startPosition.x:F0}, {startPosition.y:F0}) to ({endPosition.x:F0}, {endPosition.y:F0}) over {duration}s (hold={holdTime}s, button={button})");
            await InputInjector.InjectPointerDrag(startPosition, endPosition, duration, holdTime, button);
        }

        #endregion

        #region Scroll Actions

        /// <summary>
        /// Scrolls on a target GameObject. Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="target">The GameObject to scroll on</param>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        internal static async UniTask Scroll(GameObject target, float delta)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Scroll target cannot be null");

            var screenPos = InputInjector.GetScreenPosition(target);
            Log($"Scroll on '{target.name}' delta={delta}");
            await InputInjector.InjectScroll(screenPos, delta);
        }

        /// <summary>
        /// Scrolls on a target GameObject in a named direction. Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="target">The GameObject to scroll on</param>
        /// <param name="direction">Direction: "up", "down", "left", "right"</param>
        /// <param name="amount">Scroll amount (0-1 normalized)</param>
        internal static async UniTask Scroll(GameObject target, string direction, float amount = 0.3f)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Scroll target cannot be null");

            Log($"Scroll '{target.name}' {direction} amount={amount}");
            await InputInjector.ScrollElement(target, direction, amount);
        }

        /// <summary>
        /// Scrolls at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position to scroll at</param>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        public static async UniTask ScrollAt(Vector2 position, float delta)
        {
            Log($"Scroll at ({position.x:F0}, {position.y:F0}) delta={delta}");
            await InputInjector.InjectScroll(position, delta);
        }

        /// <summary>
        /// Scrolls on a UI element matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="delta">Scroll delta (positive = up, negative = down)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <param name="index">Index of the element when multiple match (0-based)</param>
        /// <returns>True if element was found and scrolled, false otherwise</returns>
        public static async UniTask<bool> Scroll(Search search, float delta, float searchTime = 10f, int index = 0)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                {
                    var target = results[index];
                    var screenPos = InputInjector.GetScreenPosition(target);
                    Log($"Scroll({search}) -> '{target.name}' at ({screenPos.x:F0}, {screenPos.y:F0}) delta={delta}");
                    await InputInjector.InjectScroll(screenPos, delta);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

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
        public static async UniTask<bool> Scroll(Search search, string direction, float amount = 0.3f, float searchTime = 10f, int index = 0)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > index)
                {
                    var target = results[index];
                    Log($"Scroll({search}) -> '{target.name}' {direction} amount={amount}");
                    await InputInjector.ScrollElement(target, direction, amount);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        #endregion

        #region Slider/Scrollbar Actions

        /// <summary>
        /// Sets a slider to a specific value by clicking.
        /// </summary>
        /// <param name="slider">The Slider component</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        public static async UniTask SetSlider(Slider slider, float normalizedValue)
        {
            if (slider == null)
                throw new System.ArgumentNullException(nameof(slider), "Slider cannot be null");

            Log($"SetSlider '{slider.name}' to {normalizedValue:F2}");
            await InputInjector.SetSlider(slider, normalizedValue);
        }

        /// <summary>
        /// Sets a slider to a specific value by clicking (finds Slider on GameObject). Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="target">GameObject with Slider component</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        internal static async UniTask SetSlider(GameObject target, float normalizedValue)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Slider target cannot be null");

            var slider = target.GetComponent<Slider>();
            if (slider == null)
                throw new System.InvalidOperationException($"GameObject '{target.name}' does not have a Slider component");

            await SetSlider(slider, normalizedValue);
        }

        /// <summary>
        /// Sets a scrollbar to a specific value by clicking.
        /// </summary>
        /// <param name="scrollbar">The Scrollbar component</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        public static async UniTask SetScrollbar(Scrollbar scrollbar, float normalizedValue)
        {
            if (scrollbar == null)
                throw new System.ArgumentNullException(nameof(scrollbar), "Scrollbar cannot be null");

            Log($"SetScrollbar '{scrollbar.name}' to {normalizedValue:F2}");
            await InputInjector.SetScrollbar(scrollbar, normalizedValue);
        }

        /// <summary>
        /// Sets a scrollbar to a specific value by clicking (finds Scrollbar on GameObject). Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="target">GameObject with Scrollbar component</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        internal static async UniTask SetScrollbar(GameObject target, float normalizedValue)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Scrollbar target cannot be null");

            var scrollbar = target.GetComponent<Scrollbar>();
            if (scrollbar == null)
                throw new System.InvalidOperationException($"GameObject '{target.name}' does not have a Scrollbar component");

            await SetScrollbar(scrollbar, normalizedValue);
        }

        /// <summary>
        /// Clicks on a slider at a specific position to set its value.
        /// </summary>
        /// <param name="slider">The Slider component</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        public static async UniTask ClickSlider(Slider slider, float normalizedValue)
        {
            if (slider == null)
                throw new System.ArgumentNullException(nameof(slider), "Slider cannot be null");

            var clickPos = InputInjector.GetSliderClickPosition(slider, normalizedValue);
            Log($"ClickSlider '{slider.name}' at {normalizedValue:F2} position ({clickPos.x:F0}, {clickPos.y:F0})");
            await InputInjector.InjectPointerTap(clickPos);
        }

        /// <summary>
        /// Drags a slider from one value to another.
        /// </summary>
        /// <param name="slider">The Slider component</param>
        /// <param name="fromValue">Starting value (0-1)</param>
        /// <param name="toValue">Ending value (0-1)</param>
        /// <param name="duration">Duration of the drag</param>
        public static async UniTask DragSlider(Slider slider, float fromValue, float toValue, float duration = 1.0f, float holdTime = 0.5f)
        {
            if (slider == null)
                throw new System.ArgumentNullException(nameof(slider), "Slider cannot be null");

            var startPos = InputInjector.GetSliderClickPosition(slider, fromValue);
            var endPos = InputInjector.GetSliderClickPosition(slider, toValue);
            Log($"DragSlider '{slider.name}' from {fromValue:F2} to {toValue:F2} (hold={holdTime}s)");
            await InputInjector.InjectPointerDrag(startPos, endPos, duration, holdTime);
        }

        /// <summary>
        /// Clicks on a slider at a specific position matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the Slider</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <returns>True if slider was found and clicked, false otherwise</returns>
        public static async UniTask<bool> ClickSlider(Search search, float normalizedValue, float searchTime = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                foreach (var go in results)
                {
                    var slider = go.GetComponent<Slider>();
                    if (slider != null)
                    {
                        Log($"ClickSlider({search}) -> '{go.name}' value={normalizedValue:F2}");
                        await ClickSlider(slider, normalizedValue);
                        return true;
                    }
                }
                await UniTask.Delay(100, true);
            }

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
        public static async UniTask<bool> DragSlider(Search search, float fromValue, float toValue, float duration = 1.0f, float searchTime = 10f, float holdTime = 0.5f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                foreach (var go in results)
                {
                    var slider = go.GetComponent<Slider>();
                    if (slider != null)
                    {
                        Log($"DragSlider({search}) -> '{go.name}' from {fromValue:F2} to {toValue:F2}");
                        await DragSlider(slider, fromValue, toValue, duration, holdTime);
                        return true;
                    }
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Sets a slider to a specific value matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the Slider</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <returns>True if slider was found and set, false otherwise</returns>
        public static async UniTask<bool> SetSlider(Search search, float normalizedValue, float searchTime = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                foreach (var go in results)
                {
                    var slider = go.GetComponent<Slider>();
                    if (slider != null)
                    {
                        Log($"SetSlider({search}) -> '{go.name}' value={normalizedValue:F2}");
                        await SetSlider(slider, normalizedValue);
                        return true;
                    }
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Sets a scrollbar to a specific value matching the search query.
        /// </summary>
        /// <param name="search">The search query to find the Scrollbar</param>
        /// <param name="normalizedValue">Target value (0-1)</param>
        /// <param name="searchTime">Maximum time to search for the element</param>
        /// <returns>True if scrollbar was found and set, false otherwise</returns>
        public static async UniTask<bool> SetScrollbar(Search search, float normalizedValue, float searchTime = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                foreach (var go in results)
                {
                    var scrollbar = go.GetComponent<Scrollbar>();
                    if (scrollbar != null)
                    {
                        Log($"SetScrollbar({search}) -> '{go.name}' value={normalizedValue:F2}");
                        await SetScrollbar(scrollbar, normalizedValue);
                        return true;
                    }
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        #endregion

        #region Gesture Actions

        /// <summary>
        /// Performs a swipe gesture on a target GameObject. Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="target">The GameObject to swipe on</param>
        /// <param name="direction">Direction: "up", "down", "left", "right"</param>
        /// <param name="normalizedDistance">Distance as fraction of screen (0-1)</param>
        /// <param name="duration">Duration of the swipe</param>
        internal static async UniTask Swipe(GameObject target, string direction, float normalizedDistance = 0.2f, float duration = 1.0f)
        {
            if (target == null)
                throw new System.ArgumentNullException(nameof(target), "Swipe target cannot be null");

            Log($"Swipe on '{target.name}' {direction} distance={normalizedDistance} duration={duration}s");
            await InputInjector.Swipe(target, direction, normalizedDistance, duration);
        }

        /// <summary>
        /// Performs a swipe gesture at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position to start the swipe</param>
        /// <param name="direction">Direction: "up", "down", "left", "right"</param>
        /// <param name="normalizedDistance">Distance as fraction of screen (0-1)</param>
        /// <param name="duration">Duration of the swipe</param>
        public static async UniTask SwipeAt(Vector2 position, string direction, float normalizedDistance = 0.2f, float duration = 1.0f)
        {
            var offset = InputInjector.GetDirectionOffset(direction, normalizedDistance);
            var endPos = position + offset;
            Log($"Swipe at ({position.x:F0}, {position.y:F0}) {direction}");
            await InputInjector.InjectMouseDrag(position, endPos, duration);
        }

        /// <summary>
        /// Performs a pinch gesture on a target GameObject. Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="target">The GameObject to pinch on</param>
        /// <param name="scale">Scale factor (less than 1 = zoom out, greater than 1 = zoom in)</param>
        /// <param name="duration">Duration of the pinch</param>
        internal static async UniTask Pinch(GameObject target, float scale, float duration = 1.0f)
        {
            Log($"Pinch on '{target?.name ?? "screen center"}' scale={scale} duration={duration}s");
            await InputInjector.Pinch(target, scale, duration);
        }

        /// <summary>
        /// Performs a pinch gesture at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position for the pinch center</param>
        /// <param name="scale">Scale factor</param>
        /// <param name="duration">Duration of the pinch</param>
        public static async UniTask PinchAt(Vector2 position, float scale, float duration = 1.0f)
        {
            Log($"Pinch at ({position.x:F0}, {position.y:F0}) scale={scale} duration={duration}s");
            await InputInjector.InjectPinch(position, scale, duration);
        }

        /// <summary>
        /// Performs a pinch gesture at a specific screen position with custom finger distance.
        /// </summary>
        /// <param name="position">Screen position for the pinch center</param>
        /// <param name="scale">Scale factor</param>
        /// <param name="duration">Duration of the pinch</param>
        /// <param name="fingerDistancePixels">Initial distance of each finger from center in pixels</param>
        public static async UniTask PinchAt(Vector2 position, float scale, float duration, float fingerDistancePixels)
        {
            Log($"Pinch at ({position.x:F0}, {position.y:F0}) scale={scale} duration={duration}s fingerDistance={fingerDistancePixels}px");
            await InputInjector.InjectPinch(position, scale, duration, fingerDistancePixels);
        }

        /// <summary>
        /// Performs a pinch gesture at the screen center.
        /// </summary>
        /// <param name="scale">Scale factor (less than 1 = zoom out, greater than 1 = zoom in)</param>
        /// <param name="duration">Duration of the pinch</param>
        public static async UniTask Pinch(float scale, float duration = 1.0f)
        {
            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Log($"Pinch at screen center scale={scale} duration={duration}s");
            await InputInjector.InjectPinch(center, scale, duration);
        }

        /// <summary>
        /// Performs a pinch gesture at a screen position specified by percentage.
        /// </summary>
        /// <param name="xPercent">Horizontal position as percentage (0-1)</param>
        /// <param name="yPercent">Vertical position as percentage (0-1)</param>
        /// <param name="scale">Scale factor</param>
        /// <param name="duration">Duration of the pinch</param>
        public static async UniTask PinchAt(float xPercent, float yPercent, float scale, float duration = 1.0f)
        {
            var position = new Vector2(Screen.width * xPercent, Screen.height * yPercent);
            await PinchAt(position, scale, duration);
        }

        /// <summary>
        /// Performs a two-finger swipe gesture on a target GameObject. Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="target">The GameObject to swipe on</param>
        /// <param name="direction">Direction: "up", "down", "left", "right"</param>
        /// <param name="normalizedDistance">Distance as fraction of screen (0-1)</param>
        /// <param name="duration">Duration of the swipe</param>
        /// <param name="fingerSpacing">Normalized spacing between fingers (0-1)</param>
        internal static async UniTask TwoFingerSwipe(GameObject target, string direction, float normalizedDistance = 0.2f, float duration = 1.0f, float fingerSpacing = 0.03f)
        {
            Log($"TwoFingerSwipe on '{target?.name ?? "screen center"}' {direction}");
            await InputInjector.TwoFingerSwipe(target, direction, normalizedDistance, duration, fingerSpacing);
        }

        /// <summary>
        /// Performs a two-finger swipe gesture at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position for the swipe center</param>
        /// <param name="direction">Direction: "up", "down", "left", "right"</param>
        /// <param name="normalizedDistance">Distance as fraction of screen (0-1)</param>
        /// <param name="duration">Duration of the swipe</param>
        /// <param name="fingerSpacing">Normalized spacing between fingers (0-1)</param>
        public static async UniTask TwoFingerSwipeAt(Vector2 position, string direction, float normalizedDistance = 0.2f, float duration = 1.0f, float fingerSpacing = 0.03f)
        {
            Log($"TwoFingerSwipe at ({position.x:F0}, {position.y:F0}) {direction}");
            await InputInjector.InjectTwoFingerSwipe(position, direction, normalizedDistance, duration, fingerSpacing);
        }

        /// <summary>
        /// Performs a two-finger swipe gesture at the screen center.
        /// </summary>
        /// <param name="direction">Swipe direction</param>
        /// <param name="normalizedDistance">Distance as fraction of screen (0-1)</param>
        /// <param name="duration">Duration of the swipe</param>
        /// <param name="fingerSpacing">Normalized spacing between fingers (0-1)</param>
        public static async UniTask TwoFingerSwipe(SwipeDirection direction, float normalizedDistance = 0.2f, float duration = 1.0f, float fingerSpacing = 0.03f)
        {
            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Log($"TwoFingerSwipe at screen center {direction}");
            await InputInjector.InjectTwoFingerSwipe(center, direction.ToString().ToLower(), normalizedDistance, duration, fingerSpacing);
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
        public static async UniTask TwoFingerSwipeAt(float xPercent, float yPercent, SwipeDirection direction, float normalizedDistance = 0.2f, float duration = 1.0f, float fingerSpacing = 0.03f)
        {
            var position = new Vector2(Screen.width * xPercent, Screen.height * yPercent);
            await TwoFingerSwipeAt(position, direction.ToString().ToLower(), normalizedDistance, duration, fingerSpacing);
        }

        /// <summary>
        /// Performs a rotation gesture on a target GameObject. Internal use only - prefer Search-based overloads.
        /// </summary>
        /// <param name="target">The GameObject to rotate on</param>
        /// <param name="degrees">Rotation angle (positive = clockwise, negative = counter-clockwise)</param>
        /// <param name="duration">Duration of the rotation</param>
        /// <param name="fingerDistance">Normalized distance from center for fingers (0-1)</param>
        internal static async UniTask Rotate(GameObject target, float degrees, float duration = 1.0f, float fingerDistance = 0.05f)
        {
            Log($"Rotate on '{target?.name ?? "screen center"}' degrees={degrees} duration={duration}s");
            await InputInjector.Rotate(target, degrees, duration, fingerDistance);
        }

        /// <summary>
        /// Performs a rotation gesture at a specific screen position.
        /// </summary>
        /// <param name="position">Screen position for the rotation center</param>
        /// <param name="degrees">Rotation angle</param>
        /// <param name="duration">Duration of the rotation</param>
        /// <param name="fingerDistance">Normalized distance from center for fingers (0-1)</param>
        public static async UniTask RotateAt(Vector2 position, float degrees, float duration = 1.0f, float fingerDistance = 0.05f)
        {
            Log($"Rotate at ({position.x:F0}, {position.y:F0}) degrees={degrees} duration={duration}s");
            await InputInjector.InjectRotate(position, degrees, duration, fingerDistance);
        }

        /// <summary>
        /// Performs a rotation gesture at a screen position specified by percentage.
        /// </summary>
        /// <param name="xPercent">Horizontal position as percentage (0-1)</param>
        /// <param name="yPercent">Vertical position as percentage (0-1)</param>
        /// <param name="degrees">Rotation angle</param>
        /// <param name="duration">Duration of the rotation</param>
        /// <param name="fingerDistance">Normalized distance from center for fingers (0-1)</param>
        public static async UniTask RotateAt(float xPercent, float yPercent, float degrees, float duration = 1.0f, float fingerDistance = 0.05f)
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
        public static async UniTask RotateAtPixels(Vector2 position, float degrees, float duration, float radiusPixels)
        {
            Log($"Rotate at ({position.x:F0}, {position.y:F0}) degrees={degrees} duration={duration}s radius={radiusPixels}px");
            await InputInjector.InjectRotatePixels(position, degrees, duration, radiusPixels);
        }

        /// <summary>
        /// Performs a rotation gesture at the screen center.
        /// </summary>
        /// <param name="degrees">Rotation angle (positive = clockwise, negative = counter-clockwise)</param>
        /// <param name="duration">Duration of the rotation</param>
        /// <param name="fingerDistance">Normalized distance from center for fingers (0-1)</param>
        public static async UniTask Rotate(float degrees, float duration = 1.0f, float fingerDistance = 0.05f)
        {
            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Log($"Rotate at screen center degrees={degrees} duration={duration}s");
            await InputInjector.InjectRotate(center, degrees, duration, fingerDistance);
        }

        /// <summary>
        /// Performs a drag gesture from the screen center in a direction.
        /// </summary>
        /// <param name="direction">The direction to drag (pixel offset)</param>
        /// <param name="duration">Duration of the drag in seconds</param>
        /// <param name="holdTime">Time to hold at start position before dragging</param>
        public static async UniTask Drag(Vector2 direction, float duration = 1.0f, float holdTime = 0.5f)
        {
            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var endPos = center + direction;
            Log($"Drag from screen center by ({direction.x:F0}, {direction.y:F0}) over {duration}s");
            await InputInjector.InjectPointerDrag(center, endPos, duration, holdTime);
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
        public static async UniTask<bool> ClickDropdown(Search search, int optionIndex, float searchTime = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                foreach (var go in results)
                {
                    // Try legacy Dropdown
                    var legacyDropdown = go.GetComponent<Dropdown>();
                    if (legacyDropdown != null)
                    {
                        Log($"ClickDropdown({search}) -> '{go.name}' index={optionIndex}");
                        await ClickDropdownItem(legacyDropdown.gameObject, legacyDropdown.template, optionIndex);
                        return true;
                    }

                    // Try TMP_Dropdown
                    var tmpDropdown = go.GetComponent<TMPro.TMP_Dropdown>();
                    if (tmpDropdown != null)
                    {
                        Log($"ClickDropdown({search}) -> '{go.name}' index={optionIndex}");
                        await ClickDropdownItem(tmpDropdown.gameObject, tmpDropdown.template, optionIndex);
                        return true;
                    }
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Selects a dropdown option by label text using realistic click interactions.
        /// </summary>
        /// <param name="search">The search query to find the Dropdown or TMP_Dropdown</param>
        /// <param name="optionLabel">The text label of the option to select</param>
        /// <param name="searchTime">Maximum time to search for the dropdown</param>
        /// <returns>True if dropdown was found and option selected, false otherwise</returns>
        public static async UniTask<bool> ClickDropdown(Search search, string optionLabel, float searchTime = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                foreach (var go in results)
                {
                    // Try legacy Dropdown
                    var legacyDropdown = go.GetComponent<Dropdown>();
                    if (legacyDropdown != null)
                    {
                        int optionIndex = legacyDropdown.options.FindIndex(o => o.text == optionLabel);
                        if (optionIndex >= 0)
                        {
                            Log($"ClickDropdown({search}) -> '{go.name}' label=\"{optionLabel}\" (index={optionIndex})");
                            await ClickDropdownItem(legacyDropdown.gameObject, legacyDropdown.template, optionIndex);
                            return true;
                        }
                    }

                    // Try TMP_Dropdown
                    var tmpDropdown = go.GetComponent<TMPro.TMP_Dropdown>();
                    if (tmpDropdown != null)
                    {
                        int optionIndex = tmpDropdown.options.FindIndex(o => o.text == optionLabel);
                        if (optionIndex >= 0)
                        {
                            Log($"ClickDropdown({search}) -> '{go.name}' label=\"{optionLabel}\" (index={optionIndex})");
                            await ClickDropdownItem(tmpDropdown.gameObject, tmpDropdown.template, optionIndex);
                            return true;
                        }
                    }
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Internal method to click a dropdown item after the dropdown has been found.
        /// </summary>
        private static async UniTask ClickDropdownItem(GameObject dropdownGO, RectTransform template, int optionIndex)
        {
            // Capture existing toggles before opening dropdown
            var existingToggles = new System.Collections.Generic.HashSet<Toggle>(
                UnityEngine.Object.FindObjectsByType<Toggle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));

            // Click the dropdown to open it
            await Click(dropdownGO);

            // Wait for new toggles to appear (the dropdown items)
            Toggle[] newToggles = null;
            float waitTime = 0f;
            const float maxWaitTime = 0.5f;

            while (waitTime < maxWaitTime)
            {
                await UniTask.DelayFrame(1);

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
                    Log($"ClickDropdown selecting option {optionIndex}: '{targetToggle.name}'");
                    await Click(targetToggle.gameObject);
                    return;
                }

                await UniTask.Delay(50, true);
                waitTime += 0.05f;
            }

            Debug.LogWarning($"[ActionExecutor] ClickDropdown - Item at index {optionIndex} not found (found {newToggles?.Length ?? 0} new toggles)");
        }

        #endregion

        #region Utility Actions

        /// <summary>
        /// Clicks any one of the elements matching the search query.
        /// </summary>
        /// <param name="search">The search query to find elements</param>
        /// <param name="searchTime">Maximum time to search for elements</param>
        /// <returns>True if any element was found and clicked, false otherwise</returns>
        public static async UniTask<bool> ClickAny(Search search, float searchTime = 10f)
        {
            float startTime = Time.realtimeSinceStartup;
            var rnd = new System.Random((int)System.DateTime.Now.Millisecond);

            while ((Time.realtimeSinceStartup - startTime) < searchTime && Application.isPlaying)
            {
                var results = search.FindAll();
                if (results.Count > 0)
                {
                    // Randomly select one
                    var target = results.OrderBy(_ => rnd.Next()).First();
                    var screenPos = InputInjector.GetScreenPosition(target);
                    Log($"ClickAny({search}) -> '{target.name}' at ({screenPos.x:F0}, {screenPos.y:F0}) (from {results.Count} matches)");
                    await Click(target);
                    return true;
                }
                await UniTask.Delay(100, true);
            }

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
        public static async UniTask<bool> WaitFor(Search search, float timeout = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                var result = search.FindFirst();
                if (result != null)
                {
                    Log($"WaitFor({search}) found '{result.name}'");
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            LogDebug($"WaitFor({search}) timed out after {timeout}s");
            return false;
        }

        /// <summary>
        /// Waits until an element's text matches the expected value.
        /// </summary>
        /// <param name="search">The search query to find the element</param>
        /// <param name="expectedText">Expected text content</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <returns>True if text matched, false if timeout</returns>
        public static async UniTask<bool> WaitFor(Search search, string expectedText, float timeout = 10f)
        {
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
                        Log($"WaitFor({search}, \"{expectedText}\") satisfied");
                        return true;
                    }
                }
                await UniTask.Delay(100, true);
            }

            return false;
        }

        /// <summary>
        /// Waits until no element matching the search exists.
        /// </summary>
        /// <param name="search">The search query</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <returns>True if element gone, false if timeout</returns>
        public static async UniTask<bool> WaitForNot(Search search, float timeout = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                var result = search.FindFirst();
                if (result == null)
                {
                    Log($"WaitForNot({search}) satisfied");
                    return true;
                }
                await UniTask.Delay(100, true);
            }

            LogDebug($"WaitForNot({search}) timed out after {timeout}s");
            return false;
        }

        /// <summary>
        /// Waits until a static path resolves to a truthy value.
        /// </summary>
        /// <param name="path">Dot-separated path to the value (e.g., "GameManager.Instance.IsReady")</param>
        /// <param name="timeout">Maximum time to wait in seconds</param>
        /// <returns>True if truthy, false if timeout</returns>
        public static async UniTask<bool> WaitFor(string path, float timeout = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                try
                {
                    var value = ResolveStaticPath(path);
                    if (IsTruthy(value))
                    {
                        Log($"WaitFor({path}) satisfied");
                        return true;
                    }
                }
                catch
                {
                    // Path might not be resolvable yet, keep trying
                }

                await UniTask.Delay(100, true);
            }

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
        public static async UniTask<bool> WaitFor<T>(string path, T expected, float timeout = 10f)
        {
            float startTime = Time.realtimeSinceStartup;

            while ((Time.realtimeSinceStartup - startTime) < timeout && Application.isPlaying)
            {
                try
                {
                    var value = ResolveStaticPath(path);
                    if (value is T typedValue && Equals(typedValue, expected))
                    {
                        Log($"WaitFor({path}, {expected}) satisfied");
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
                                Log($"WaitFor({path}, {expected}) satisfied");
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

                await UniTask.Delay(100, true);
            }

            LogDebug($"WaitFor({path}, {expected}) timed out after {timeout}s");
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
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or empty", nameof(path));

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
                    throw new InvalidOperationException($"Could not find type '{firstPart}' in path: {path}.{hint}");
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
        public static async UniTask Swipe(Search search, SwipeDirection direction, float distance = 0.2f, float duration = 1.0f, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] Swipe [{direction}] on {search}");

            var element = await Find<RectTransform>(search, throwIfMissing, searchTime);
            if (element == null) return;

            await InputInjector.Swipe(element.gameObject, direction.ToString().ToLower(), distance, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Performs a swipe gesture at screen center.
        /// </summary>
        public static async UniTask Swipe(SwipeDirection direction, float distance = 0.2f, float duration = 1.0f)
        {
            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Debug.Log($"[UITEST] Swipe [{direction}] at screen center");
            await SwipeAt(center, direction.ToString().ToLower(), distance, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Performs a swipe gesture at a specific screen position.
        /// </summary>
        public static async UniTask SwipeAt(float xPercent, float yPercent, SwipeDirection direction, float distance = 0.2f, float duration = 1.0f)
        {
            var startPos = new Vector2(Screen.width * xPercent, Screen.height * yPercent);
            Debug.Log($"[UITEST] SwipeAt ({xPercent:P0}, {yPercent:P0}) [{direction}]");
            await SwipeAt(startPos, direction.ToString().ToLower(), distance, duration);
            await ActionComplete();
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
        public static async UniTask<GameObject> ScrollTo(Search scrollViewSearch, Search targetSearch, int maxScrollAttempts = 20, bool throwIfMissing = true, float searchTime = 5)
        {
            Log($"ScrollTo: searching for scroll view");
            var scrollRect = await Find<ScrollRect>(scrollViewSearch, true, searchTime);

            if (scrollRect == null)
            {
                if (throwIfMissing)
                    throw new Exception($"ScrollTo: Could not find ScrollRect matching: {scrollViewSearch}");
                return null;
            }

            bool canScrollHorizontal = scrollRect.horizontal;
            bool canScrollVertical = scrollRect.vertical;

            Log($"ScrollTo: found scroll view '{scrollRect.name}', horizontal={canScrollHorizontal}, vertical={canScrollVertical}");

            // First check if target is already visible
            var target = targetSearch.FindFirst();
            if (target != null)
            {
                var targetRect = target.GetComponent<RectTransform>();
                if (targetRect != null && IsVisibleInViewport(scrollRect, targetRect))
                {
                    Log($"ScrollTo: target '{target.name}' already visible");
                    await ActionComplete();
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
                        Log($"ScrollTo: found target '{target.name}' after {attempts} drags");
                        await ActionComplete();
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
                await UniTask.DelayFrame(2);

                attempts++;
            }

            // Final check after all attempts
            target = targetSearch.FindFirst();
            if (target != null)
            {
                var targetRect = target.GetComponent<RectTransform>();
                if (targetRect != null && IsVisibleInViewport(scrollRect, targetRect))
                {
                    Log($"ScrollTo: found target '{target.name}' after {attempts} drags");
                    await ActionComplete();
                    return target;
                }
            }

            Log($"ScrollTo: target not found after {attempts} drag attempts");

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
        public static async UniTask<bool> ScrollToAndClick(Search scrollViewSearch, Search targetSearch, int maxScrollAttempts = 20, bool throwIfMissing = true, float searchTime = 5)
        {
            var target = await ScrollTo(scrollViewSearch, targetSearch, maxScrollAttempts, throwIfMissing, searchTime);

            if (target == null)
                return false;

            Log($"ScrollToAndClick: clicking on '{target.name}'");
            await Click(target);
            await ActionComplete();
            return true;
        }

        #endregion

        #region ItemContainer and FindItems

        /// <summary>
        /// Finds a container (ScrollRect, Dropdown, LayoutGroup) and its child items.
        /// </summary>
        public static async UniTask<ItemContainer> FindItems(Search containerSearch, Search itemSearch = null)
        {
            Component container = await Find<ScrollRect>(containerSearch, false, 2);
            container ??= await Find<VerticalLayoutGroup>(containerSearch, false, 1);
            container ??= await Find<HorizontalLayoutGroup>(containerSearch, false, 1);
            container ??= await Find<GridLayoutGroup>(containerSearch, false, 1);
            container ??= await Find<TMP_Dropdown>(containerSearch, false, 1);
            container ??= await Find<Dropdown>(containerSearch, false, 1);

            if (container == null)
                throw new Exception($"FindItems could not find a supported container matching: {containerSearch}");

            var items = GetContainerItems(container);

            if (itemSearch != null)
            {
                items = items.Where(item => itemSearch.Matches(item.gameObject));
            }

            return new ItemContainer(container, items);
        }

        /// <summary>
        /// Finds a container by name and its child items.
        /// </summary>
        public static async UniTask<ItemContainer> FindItems(string containerName, Search itemSearch = null)
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
        public static UniTask<Search.ActiveState> Disable(Search search, float searchTime = 10f) => search.Disable(searchTime);

        /// <summary>
        /// Enables a GameObject found by search.
        /// </summary>
        public static UniTask<Search.ActiveState> Enable(Search search, float searchTime = 10f) => search.Enable(searchTime);

        /// <summary>
        /// Freezes a GameObject (zero velocity, kinematic).
        /// </summary>
        public static UniTask<Search.FreezeState> Freeze(Search search, bool includeChildren = true, float searchTime = 10f) => search.Freeze(includeChildren, searchTime);

        /// <summary>
        /// Teleports a GameObject to a world position.
        /// </summary>
        public static UniTask<Search.PositionState> Teleport(Search search, Vector3 worldPosition, float searchTime = 10f) => search.Teleport(worldPosition, searchTime);

        /// <summary>
        /// Disables colliders on a GameObject.
        /// </summary>
        public static UniTask<Search.ColliderState> NoClip(Search search, bool includeChildren = true, float searchTime = 10f) => search.NoClip(includeChildren, searchTime);

        /// <summary>
        /// Enables colliders on a GameObject.
        /// </summary>
        public static UniTask<Search.ColliderState> Clip(Search search, bool includeChildren = true, float searchTime = 10f) => search.Clip(includeChildren, searchTime);

        #endregion

        #region Random Click

        /// <summary>
        /// Clicks a random clickable element on screen.
        /// </summary>
        public static async UniTask<Component> RandomClick(Search filter = null)
        {
            var clickables = GetClickableElements(filter);
            if (!clickables.Any())
            {
                Debug.Log("[UITEST] RandomClick - No clickable elements found");
                return null;
            }

            var target = clickables.ElementAt(RandomGenerator.Next(clickables.Count()));
            Debug.Log($"[UITEST] RandomClick - Selected '{target.gameObject.name}'");
            await Click(target.gameObject);
            return target;
        }

        /// <summary>
        /// Clicks a random element excluding certain searches.
        /// </summary>
        public static async UniTask<Component> RandomClickExcept(params Search[] exclude)
        {
            var allClickables = GetClickableElements(null);
            var filtered = allClickables.Where(c =>
            {
                foreach (var ex in exclude)
                    if (ex.Matches(c.gameObject)) return false;
                return true;
            });

            if (!filtered.Any())
            {
                Debug.Log("[UITEST] RandomClickExcept - No clickable elements found");
                return null;
            }

            var target = filtered.ElementAt(RandomGenerator.Next(filtered.Count()));
            Debug.Log($"[UITEST] RandomClickExcept - Selected '{target.gameObject.name}'");
            await Click(target.gameObject);
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
        public static async UniTask<SimpleExploreResult> AutoExplore(
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

            Debug.Log($"[UITEST] AutoExplore started - StopCondition: {stopCondition}, Value: {value}");

            while (Application.isPlaying)
            {
                result.TimeElapsed = Time.realtimeSinceStartup - startTime;

                switch (stopCondition)
                {
                    case SimpleExploreStopCondition.Time:
                        if (result.TimeElapsed >= value)
                        {
                            result.StopReason = SimpleExploreStopCondition.Time;
                            return result;
                        }
                        break;
                    case SimpleExploreStopCondition.ActionCount:
                        if (result.ActionsPerformed >= (int)value)
                        {
                            result.StopReason = SimpleExploreStopCondition.ActionCount;
                            return result;
                        }
                        break;
                    case SimpleExploreStopCondition.DeadEnd:
                        if (consecutiveNoClick >= 3)
                        {
                            result.StopReason = SimpleExploreStopCondition.DeadEnd;
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
        public static async UniTask<SimpleExploreResult> AutoExploreForSeconds(float seconds, int? seed = null, float delayBetweenActions = 0.5f)
        {
            return await AutoExplore(SimpleExploreStopCondition.Time, seconds, seed, delayBetweenActions);
        }

        /// <summary>
        /// Auto-explores for a specified number of actions.
        /// </summary>
        public static async UniTask<SimpleExploreResult> AutoExploreForActions(int actionCount, int? seed = null, float delayBetweenActions = 0.5f)
        {
            return await AutoExplore(SimpleExploreStopCondition.ActionCount, actionCount, seed, delayBetweenActions);
        }

        /// <summary>
        /// Auto-explores until no more clickable elements are found.
        /// </summary>
        public static async UniTask<SimpleExploreResult> AutoExploreUntilDeadEnd(int? seed = null, float delayBetweenActions = 0.5f, bool tryBackOnStuck = false)
        {
            return await AutoExplore(SimpleExploreStopCondition.DeadEnd, 0, seed, delayBetweenActions, tryBackOnStuck);
        }

        #endregion
    }
}
