using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ODDGames.UIAutomation
{
    /// <summary>
    /// Static utility class providing all UI automation test actions.
    /// Use with 'using static ODDGames.UIAutomation.UIAutomation;' for shorthand access.
    /// </summary>
    /// <example>
    /// using static ODDGames.UIAutomation.UIAutomation;
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
    public static class UIAutomation
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

        static void LogDebug(string message)
        {
            if (DebugMode)
                Debug.Log($"[UITEST:DEBUG] {message}");
        }

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
                        await Click(search, throwIfMissing: false, searchTime: 0.5f);
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

        #region Keyboard Input

        /// <summary>
        /// Simulates a key press and release using Input System Key enum.
        /// </summary>
        public static async UniTask PressKey(Key key)
        {
            Debug.Log($"[UITEST] PressKey [{key}]");
            await ActionExecutor.PressKeyAsync(key);
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
                await ActionExecutor.PressKeyAsync(inputKey);
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
                    await ActionExecutor.PressKeyAsync(key);
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
            await ActionExecutor.HoldKeyAsync(key, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Holds multiple keys down simultaneously for a specified duration.
        /// </summary>
        public static async UniTask HoldKeys(float duration, params Key[] keys)
        {
            Debug.Log($"[UITEST] HoldKeys [{string.Join(", ", keys)}] for {duration}s");
            await ActionExecutor.HoldKeysAsync(keys, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Simulates pressing a key on a specific element found by search.
        /// </summary>
        public static async UniTask PressKey(KeyCode key, Search search, float seconds = 10)
        {
            var component = await Find<Component>(search, true, seconds);
            if (component != null)
            {
                Debug.Log($"[UITEST] PressKey [{key}] on '{component.name}'");
                var inputKey = KeyCodeToKey(key);
                if (inputKey != Key.None)
                {
                    await ActionExecutor.PressKeyAsync(inputKey);
                }
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
                await ActionExecutor.PressKeyAsync(key);
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

            if (System.Enum.TryParse<KeyCode>(keyName, true, out var keyCode))
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

        /// <summary>
        /// Holds a key down for a specified duration using KeyCode.
        /// </summary>
        public static async UniTask HoldKey(KeyCode key, float duration)
        {
            Debug.Log($"[UITEST] HoldKey [{key}] for {duration}s");
            var inputKey = KeyCodeToKey(key);
            if (inputKey != Key.None)
            {
                await ActionExecutor.HoldKeyAsync(inputKey, duration);
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
                await ActionExecutor.HoldKeysAsync(inputKeys, duration);
                await ActionComplete();
            }
        }

        #endregion

        #region Click Operations

        /// <summary>
        /// Clicks on an element matching the search query.
        /// </summary>
        public static async UniTask Click(Search search, bool throwIfMissing = true, float searchTime = 10, int index = 0)
        {
            Debug.Log($"[UITEST] Click search={search}");
            bool found = await ActionExecutor.ClickAsync(search, searchTime);
            if (!found && throwIfMissing)
                throw new TimeoutException($"Click failed: could not find element matching {search}");
            if (found)
                await ActionComplete();
        }

        /// <summary>
        /// Clicks at the center of the screen.
        /// </summary>
        public static async UniTask Click()
        {
            Debug.Log($"[UITEST] Click (center)");
            await ActionExecutor.ClickAtAsync(new Vector2(Screen.width / 2f, Screen.height / 2f));
            await ActionComplete();
        }

        /// <summary>
        /// Clicks at a specific screen position (0-1 percentage).
        /// </summary>
        public static async UniTask ClickAt(float xPercent, float yPercent)
        {
            Debug.Log($"[UITEST] ClickAt ({xPercent:F2}, {yPercent:F2})");
            var pos = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            await ActionExecutor.ClickAtAsync(pos);
            await ActionComplete();
        }

        /// <summary>
        /// Clicks on a specific GameObject.
        /// </summary>
        public static async UniTask Click(GameObject target)
        {
            Debug.Log($"[UITEST] Click '{target?.name}'");
            await ActionExecutor.ClickAsync(target);
            await ActionComplete();
        }

        /// <summary>
        /// Clicks a Component directly.
        /// </summary>
        public static async UniTask Click(Component component)
        {
            if (component != null)
            {
                await Click(component.gameObject);
            }
        }

        /// <summary>
        /// Double-clicks on an element matching the search query.
        /// </summary>
        public static async UniTask DoubleClick(Search search, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] DoubleClick search={search}");
            bool found = await ActionExecutor.DoubleClickAsync(search, searchTime);
            if (!found && throwIfMissing)
                throw new TimeoutException($"DoubleClick failed: could not find element matching {search}");
            if (found)
                await ActionComplete();
        }

        /// <summary>
        /// Double-clicks at the center of the screen.
        /// </summary>
        public static async UniTask DoubleClick()
        {
            Debug.Log($"[UITEST] DoubleClick (center)");
            await ActionExecutor.DoubleClickAtAsync(new Vector2(Screen.width / 2f, Screen.height / 2f));
            await ActionComplete();
        }

        /// <summary>
        /// Double-clicks at a screen position.
        /// </summary>
        public static async UniTask DoubleClickAt(float xPercent, float yPercent)
        {
            var pos = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            await ActionExecutor.DoubleClickAtAsync(pos);
            await ActionComplete();
        }

        /// <summary>
        /// Double-clicks on a GameObject.
        /// </summary>
        public static async UniTask DoubleClick(GameObject target)
        {
            await ActionExecutor.DoubleClickAsync(target);
            await ActionComplete();
        }

        /// <summary>
        /// Triple-clicks on an element matching the search query.
        /// </summary>
        public static async UniTask TripleClick(Search search, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] TripleClick search={search}");
            var element = await Find<Transform>(search, throwIfMissing, searchTime);
            if (element != null)
            {
                await ActionExecutor.TripleClickAsync(element.gameObject);
                await ActionComplete();
            }
        }

        /// <summary>
        /// Triple-clicks at the center of the screen.
        /// </summary>
        public static async UniTask TripleClick()
        {
            var pos = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await ActionExecutor.TripleClickAtAsync(pos);
            await ActionComplete();
        }

        /// <summary>
        /// Triple-clicks at a screen position.
        /// </summary>
        public static async UniTask TripleClickAt(float xPercent, float yPercent)
        {
            var pos = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            await ActionExecutor.TripleClickAtAsync(pos);
            await ActionComplete();
        }

        /// <summary>
        /// Triple-clicks on a GameObject.
        /// </summary>
        public static async UniTask TripleClick(GameObject target)
        {
            await ActionExecutor.TripleClickAsync(target);
            await ActionComplete();
        }

        /// <summary>
        /// Clicks any of the provided search patterns, trying each in order.
        /// </summary>
        public static async UniTask ClickAny(params string[] searches)
        {
            await ClickAny(searches, throwIfMissing: true, seconds: 5);
        }

        /// <summary>
        /// Clicks any of the provided search patterns.
        /// </summary>
        public static async UniTask ClickAny(string[] searches, bool throwIfMissing = true, float seconds = 5)
        {
            Debug.Log($"[UITEST] ClickAny [{string.Join(", ", searches)}]");

            foreach (var searchText in searches)
            {
                var search = new Search().Any(searchText);
                var element = await Find<Transform>(search, false, 0.1f);
                if (element != null)
                {
                    await Click(search, throwIfMissing: false, searchTime: 0.5f);
                    return;
                }
            }

            if (throwIfMissing)
                throw new TimeoutException($"ClickAny failed: none of [{string.Join(", ", searches)}] found");
        }

        /// <summary>
        /// Clicks any element matching the search.
        /// </summary>
        public static async UniTask ClickAny(Search search, float seconds = 10, bool throwIfMissing = true)
        {
            Debug.Log($"[UITEST] ClickAny search={search}");
            var element = await Find<Transform>(search, throwIfMissing, seconds);
            if (element != null)
            {
                await Click(element.gameObject);
            }
        }

        #endregion

        #region Hold Operations

        /// <summary>
        /// Holds (long press) on an element for a specified duration.
        /// </summary>
        public static async UniTask Hold(Search search, float seconds, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] Hold {seconds}s search={search}");
            bool found = await ActionExecutor.HoldAsync(search, seconds, searchTime);
            if (!found && throwIfMissing)
                throw new TimeoutException($"Hold failed: could not find element matching {search}");
            if (found)
                await ActionComplete();
        }

        #endregion

        #region Scroll Operations

        /// <summary>
        /// Scrolls on an element matching the search query.
        /// </summary>
        public static async UniTask Scroll(Search search, float delta, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] Scroll delta={delta} search={search}");
            bool found = await ActionExecutor.ScrollAsync(search, delta, searchTime);
            if (!found && throwIfMissing)
                throw new TimeoutException($"Scroll failed: could not find element matching {search}");
            if (found)
                await ActionComplete();
        }

        /// <summary>
        /// Scrolls at the center of the screen.
        /// </summary>
        public static async UniTask Scroll(float delta)
        {
            Debug.Log($"[UITEST] Scroll delta={delta} (center)");
            await ActionExecutor.ScrollAtAsync(new Vector2(Screen.width / 2f, Screen.height / 2f), delta);
            await ActionComplete();
        }

        /// <summary>
        /// Scrolls at a specific screen position.
        /// </summary>
        public static async UniTask ScrollAt(float xPercent, float yPercent, float delta)
        {
            var pos = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            await ActionExecutor.ScrollAtAsync(pos, delta);
            await ActionComplete();
        }

        #endregion

        #region Swipe Operations

        /// <summary>
        /// Direction for swipe gestures.
        /// </summary>
        public enum SwipeDirection { Left, Right, Up, Down }

        /// <summary>
        /// Swipes on an element in the specified direction.
        /// </summary>
        public static async UniTask Swipe(Search search, SwipeDirection direction, float distance = 0.2f, float duration = 1.0f, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] Swipe {direction} search={search}");
            var element = await Find<Transform>(search, throwIfMissing, searchTime);
            if (element != null)
            {
                await ActionExecutor.SwipeAsync(element.gameObject, direction.ToString().ToLower(), distance, duration);
                await ActionComplete();
            }
        }

        /// <summary>
        /// Swipes from the center of the screen.
        /// </summary>
        public static async UniTask Swipe(SwipeDirection direction, float distance = 0.2f, float duration = 1.0f)
        {
            Debug.Log($"[UITEST] Swipe {direction} (center)");
            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await ActionExecutor.SwipeAtAsync(center, direction.ToString().ToLower(), distance, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Swipes at a specific screen position.
        /// </summary>
        public static async UniTask SwipeAt(float xPercent, float yPercent, SwipeDirection direction, float distance = 0.2f, float duration = 1.0f)
        {
            var startPos = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            await ActionExecutor.SwipeAtAsync(startPos, direction.ToString().ToLower(), distance, duration);
            await ActionComplete();
        }

        #endregion

        #region Drag Operations

        /// <summary>
        /// Drags from center of screen in the specified direction.
        /// </summary>
        public static async UniTask Drag(Vector2 direction, float duration = 1.0f)
        {
            Debug.Log($"[UITEST] Drag direction={direction}");
            var startPos = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await ActionExecutor.DragFromToAsync(startPos, startPos + direction, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Drags on an element in the specified direction.
        /// </summary>
        public static async UniTask Drag(Search search, Vector2 direction, float duration = 1.0f, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] Drag search={search}");
            var element = await Find<Transform>(search, throwIfMissing, searchTime);
            if (element != null)
            {
                await ActionExecutor.DragAsync(element.gameObject, direction, duration);
                await ActionComplete();
            }
        }

        /// <summary>
        /// Drags from one position to another.
        /// </summary>
        public static async UniTask DragFromTo(Vector2 startPos, Vector2 endPos, float duration = 1.0f)
        {
            Debug.Log($"[UITEST] DragFromTo {startPos} -> {endPos}");
            await ActionExecutor.DragFromToAsync(startPos, endPos, duration);
            await ActionComplete();
        }

        /// <summary>
        /// Drags from one element to another.
        /// </summary>
        public static async UniTask DragTo(Search sourceSearch, Search targetSearch, float duration = 1.0f, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] DragTo source={sourceSearch} target={targetSearch}");
            var source = await Find<Transform>(sourceSearch, throwIfMissing, searchTime);
            var target = await Find<Transform>(targetSearch, throwIfMissing, searchTime);
            if (source != null && target != null)
            {
                var sourceRect = source.GetComponent<RectTransform>();
                var targetRect = target.GetComponent<RectTransform>();
                if (sourceRect != null && targetRect != null)
                {
                    var startPos = RectTransformUtility.WorldToScreenPoint(null, sourceRect.position);
                    var endPos = RectTransformUtility.WorldToScreenPoint(null, targetRect.position);
                    await ActionExecutor.DragFromToAsync(startPos, endPos, duration);
                    await ActionComplete();
                }
            }
        }

        /// <summary>
        /// Drags at a specific screen position.
        /// </summary>
        public static async UniTask DragAt(float xPercent, float yPercent, Vector2 direction, float duration = 1.0f)
        {
            var startPos = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            await ActionExecutor.DragFromToAsync(startPos, startPos + direction, duration);
            await ActionComplete();
        }

        #endregion

        #region Slider Operations

        /// <summary>
        /// Clicks on a slider at the specified percentage position.
        /// </summary>
        public static async UniTask ClickSlider(Search search, float percent, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] ClickSlider {percent:P0} search={search}");
            bool found = await ActionExecutor.SetSliderAsync(search, percent, searchTime);
            if (!found && throwIfMissing)
                throw new TimeoutException($"ClickSlider failed: could not find slider matching {search}");
            if (found)
                await ActionComplete();
        }

        /// <summary>
        /// Drags a slider from one position to another.
        /// </summary>
        public static async UniTask DragSlider(Search search, float fromPercent, float toPercent, bool throwIfMissing = true, float searchTime = 10, float duration = 1.0f)
        {
            Debug.Log($"[UITEST] DragSlider {fromPercent:P0} -> {toPercent:P0} search={search}");
            var slider = await Find<Slider>(search, throwIfMissing, searchTime);
            if (slider != null)
            {
                await ActionExecutor.DragSliderAsync(slider, fromPercent, toPercent, duration);
                await ActionComplete();
            }
        }

        #endregion

        #region Dropdown Operations

        /// <summary>
        /// Clicks on a dropdown and selects an option by index.
        /// </summary>
        public static async UniTask ClickDropdown(Search search, int optionIndex, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] ClickDropdown index={optionIndex} search={search}");
            bool found = await ActionExecutor.ClickDropdownAsync(search, optionIndex, searchTime);
            if (!found && throwIfMissing)
                throw new TimeoutException($"ClickDropdown failed: could not find dropdown matching {search}");
            if (found)
                await ActionComplete();
        }

        /// <summary>
        /// Clicks on a dropdown and selects an option by label text.
        /// </summary>
        public static async UniTask ClickDropdown(Search search, string optionLabel, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] ClickDropdown label='{optionLabel}' search={search}");
            bool found = await ActionExecutor.ClickDropdownAsync(search, optionLabel, searchTime);
            if (!found && throwIfMissing)
                throw new TimeoutException($"ClickDropdown failed: could not find dropdown matching {search}");
            if (found)
                await ActionComplete();
        }

        /// <summary>
        /// Clicks through all options in a dropdown.
        /// </summary>
        public static async UniTask ClickDropdownItems(Search search, int delayBetween = 0, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] ClickDropdownItems search={search}");

            var dropdown = await Find<TMP_Dropdown>(search, false, searchTime) as Component
                        ?? await Find<Dropdown>(search, throwIfMissing, searchTime);

            if (dropdown == null) return;

            int optionCount = dropdown is TMP_Dropdown tmp ? tmp.options.Count : ((Dropdown)dropdown).options.Count;

            for (int i = 0; i < optionCount; i++)
            {
                await ClickDropdown(search, i, throwIfMissing, 1f);
                if (delayBetween > 0)
                    await Wait(delayBetween / 1000f);
            }
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

        #region ScrollTo Operations

        /// <summary>
        /// Scrolls within a scroll view to find and bring a target element into view.
        /// </summary>
        public static async UniTask<GameObject> ScrollTo(Search scrollViewSearch, Search targetSearch, int maxScrollAttempts = 20, bool throwIfMissing = true, float searchTime = 5)
        {
            Debug.Log($"[UITEST] ScrollTo target={targetSearch} in scrollView={scrollViewSearch}");

            // Find the ScrollRect
            var scrollRect = await Find<ScrollRect>(scrollViewSearch, throwIfMissing, searchTime);
            if (scrollRect == null) return null;

            var viewport = scrollRect.viewport ?? scrollRect.GetComponent<RectTransform>();
            var content = scrollRect.content;

            if (content == null)
            {
                if (throwIfMissing)
                    throw new Exception("ScrollTo - ScrollRect has no content RectTransform assigned");
                return null;
            }

            // Get viewport bounds for visibility checks
            var canvas = scrollRect.GetComponentInParent<Canvas>();
            Camera cam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.worldCamera ?? Camera.main;

            // Calculate scroll distances based on viewport size
            Vector3[] corners = new Vector3[4];
            viewport.GetWorldCorners(corners);
            Vector2 viewportMin, viewportMax;
            if (cam != null)
            {
                viewportMin = cam.WorldToScreenPoint(corners[0]);
                viewportMax = cam.WorldToScreenPoint(corners[2]);
            }
            else
            {
                viewportMin = corners[0];
                viewportMax = corners[2];
            }

            float viewportWidth = Mathf.Abs(viewportMax.x - viewportMin.x);
            float viewportHeight = Mathf.Abs(viewportMax.y - viewportMin.y);
            float scrollDistance = Mathf.Min(viewportWidth, viewportHeight) * 0.4f;

            Vector2 scrollCenter = (viewportMin + viewportMax) / 2f;

            // Try to find target - first check if already visible
            int scrollAttempts = 0;

            while (scrollAttempts < maxScrollAttempts && Application.isPlaying)
            {
                await UniTask.Yield(); // Ensure layout is updated

                // Search for target element within the content
                var allTargets = targetSearch.FindAll();
                foreach (var target in allTargets)
                {
                    if (IsDescendantOf(target, content.gameObject) && IsInViewport(target, viewportMin, viewportMax, cam))
                    {
                        Debug.Log($"[UITEST] ScrollTo - found visible target: {target.name}");
                        await ActionComplete();
                        return target;
                    }
                }

                Vector2 dragDirection = Vector2.zero;
                bool canScrollVertical = scrollRect.vertical;
                bool canScrollHorizontal = scrollRect.horizontal;

                if (allTargets.Count > 0)
                {
                    // Target exists but not visible - scroll towards it
                    var nearestTarget = allTargets.FirstOrDefault(t => IsDescendantOf(t, content.gameObject));
                    if (nearestTarget != null)
                    {
                        var targetRT = nearestTarget.GetComponent<RectTransform>();
                        if (targetRT != null)
                        {
                            Vector2 targetScreenPos = cam != null
                                ? (Vector2)cam.WorldToScreenPoint(targetRT.position)
                                : (Vector2)targetRT.position;

                            if (canScrollVertical)
                            {
                                if (targetScreenPos.y < viewportMin.y)
                                    dragDirection.y = scrollDistance; // Target below - drag UP
                                else if (targetScreenPos.y > viewportMax.y)
                                    dragDirection.y = -scrollDistance; // Target above - drag DOWN
                            }
                            if (canScrollHorizontal)
                            {
                                if (targetScreenPos.x < viewportMin.x)
                                    dragDirection.x = scrollDistance; // Target left - drag RIGHT
                                else if (targetScreenPos.x > viewportMax.x)
                                    dragDirection.x = -scrollDistance; // Target right - drag LEFT
                            }
                        }
                    }
                }
                else
                {
                    // No targets found yet - do a sequential search by scrolling through content
                    if (canScrollVertical)
                        dragDirection.y = scrollDistance; // Drag UP to scroll down through content
                    if (canScrollHorizontal)
                        dragDirection.x = -scrollDistance; // Drag LEFT to scroll right through content
                }

                if (dragDirection == Vector2.zero)
                {
                    Debug.LogWarning("[UITEST] ScrollTo - ScrollRect has no scroll direction enabled");
                    break;
                }

                await ActionExecutor.DragFromToAsync(scrollCenter, scrollCenter + dragDirection, 0.15f);
                await UniTask.Delay(100, true);
                scrollAttempts++;
            }

            if (throwIfMissing)
                throw new TimeoutException($"ScrollTo - Could not find visible target after {maxScrollAttempts} scroll attempts");

            return null;
        }

        /// <summary>
        /// Scrolls to an element and clicks it.
        /// </summary>
        public static async UniTask ScrollToAndClick(Search scrollViewSearch, Search targetSearch, int maxScrollAttempts = 20, bool throwIfMissing = true, float searchTime = 5)
        {
            var element = await ScrollTo(scrollViewSearch, targetSearch, maxScrollAttempts, throwIfMissing, searchTime);
            if (element != null)
            {
                await Click(targetSearch, throwIfMissing, 1f);
            }
        }

        /// <summary>
        /// Clicks all items in a scroll view.
        /// </summary>
        public static async UniTask ClickScrollItems(Search search, int delayBetween = 0, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] ClickScrollItems search={search}");

            var scrollRect = await Find<ScrollRect>(search, throwIfMissing, searchTime);
            if (scrollRect == null || scrollRect.content == null) return;

            var selectables = scrollRect.content.GetComponentsInChildren<Selectable>(false)
                .Where(s => s.interactable && s.gameObject.activeInHierarchy)
                .ToList();

            foreach (var selectable in selectables)
            {
                await Click(selectable.gameObject);
                if (delayBetween > 0)
                    await Wait(delayBetween / 1000f);
            }
        }

        static bool IsDescendantOf(GameObject obj, GameObject potentialParent)
        {
            if (obj == null || potentialParent == null) return false;
            var t = obj.transform;
            while (t != null)
            {
                if (t.gameObject == potentialParent) return true;
                t = t.parent;
            }
            return false;
        }

        static bool IsInViewport(GameObject obj, Vector2 viewportMin, Vector2 viewportMax, Camera cam)
        {
            var rt = obj.GetComponent<RectTransform>();
            if (rt == null) return false;

            Vector2 screenPos = cam != null
                ? (Vector2)cam.WorldToScreenPoint(rt.position)
                : (Vector2)rt.position;

            return screenPos.x >= viewportMin.x && screenPos.x <= viewportMax.x &&
                   screenPos.y >= viewportMin.y && screenPos.y <= viewportMax.y;
        }

        #endregion

        #region Item Container Operations

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
        /// Finds a container (ScrollRect, Dropdown, LayoutGroup) and its child items.
        /// </summary>
        public static async UniTask<ItemContainer> FindItems(Search containerSearch, Search itemSearch = null)
        {
            // Try each supported container type in order of most common
            Component container = await Find<ScrollRect>(containerSearch, throwIfMissing: false, seconds: 2);
            container ??= await Find<VerticalLayoutGroup>(containerSearch, throwIfMissing: false, seconds: 1);
            container ??= await Find<HorizontalLayoutGroup>(containerSearch, throwIfMissing: false, seconds: 1);
            container ??= await Find<GridLayoutGroup>(containerSearch, throwIfMissing: false, seconds: 1);
            container ??= await Find<TMP_Dropdown>(containerSearch, throwIfMissing: false, seconds: 1);
            container ??= await Find<Dropdown>(containerSearch, throwIfMissing: false, seconds: 1);

            if (container == null)
                throw new Exception($"FindItems could not find a supported container (ScrollRect, Dropdown, LayoutGroup) matching: {containerSearch}");

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
                    // Order by position
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
                    // Order top-to-bottom, left-to-right (reading order)
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

        #region Static WaitFor

        /// <summary>
        /// Waits for an element to appear.
        /// </summary>
        public static UniTask<bool> WaitFor(Search search, float timeout = 10f) => ActionExecutor.WaitFor(search, timeout);

        /// <summary>
        /// Waits for an element to have specific text.
        /// </summary>
        public static UniTask<bool> WaitFor(Search search, string expectedText, float timeout = 10f) => ActionExecutor.WaitFor(search, expectedText, timeout);

        /// <summary>
        /// Waits for an element to disappear.
        /// </summary>
        public static UniTask<bool> WaitForNot(Search search, float timeout = 10f) => ActionExecutor.WaitForNot(search, timeout);

        /// <summary>
        /// Waits for a static path to be truthy.
        /// </summary>
        public static UniTask<bool> WaitFor(string path, float timeout = 10f) => ActionExecutor.WaitFor(path, timeout);

        #endregion

        #region Pinch Operations

        /// <summary>
        /// Performs a two-finger pinch gesture on an element.
        /// </summary>
        public static async UniTask Pinch(Search search, float scale, float duration = 1.0f, float fingerDistance = 0.05f, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] Pinch scale={scale}");
            var target = await Find<Transform>(search, throwIfMissing, searchTime);
            if (target != null)
            {
                var center = InputInjector.GetScreenPosition(target.gameObject);
                float distancePixels = fingerDistance * Screen.height;
                await ActionExecutor.PinchAtAsync(center, scale, duration, distancePixels);
                await ActionComplete();
            }
        }

        /// <summary>
        /// Performs a two-finger pinch gesture at screen center.
        /// </summary>
        public static async UniTask Pinch(float scale, float duration = 1.0f, float fingerDistance = 0.05f)
        {
            await PinchAt(0.5f, 0.5f, scale, duration, fingerDistance);
        }

        /// <summary>
        /// Performs a pinch gesture at a screen position specified as percentages.
        /// </summary>
        public static async UniTask PinchAt(float xPercent, float yPercent, float scale, float duration = 1.0f, float fingerDistance = 0.05f)
        {
            var center = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            float distancePixels = fingerDistance * Screen.height;
            await ActionExecutor.PinchAtAsync(center, scale, duration, distancePixels);
            await ActionComplete();
        }

        #endregion

        #region Two-Finger Swipe Operations

        /// <summary>
        /// Performs a two-finger swipe gesture on an element.
        /// </summary>
        public static async UniTask TwoFingerSwipe(Search search, SwipeDirection direction, float distance = 0.2f, float duration = 1.0f, float fingerSpacing = 0.03f, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] TwoFingerSwipe {direction}");
            var target = await Find<Transform>(search, throwIfMissing, searchTime);
            if (target != null)
            {
                var center = InputInjector.GetScreenPosition(target.gameObject);
                await ActionExecutor.TwoFingerSwipeAtAsync(center, direction.ToString().ToLower(), distance, duration, fingerSpacing);
                await ActionComplete();
            }
        }

        /// <summary>
        /// Performs a two-finger swipe gesture at screen center.
        /// </summary>
        public static async UniTask TwoFingerSwipe(SwipeDirection direction, float distance = 0.2f, float duration = 1.0f, float fingerSpacing = 0.03f)
        {
            await TwoFingerSwipeAt(0.5f, 0.5f, direction, distance, duration, fingerSpacing);
        }

        /// <summary>
        /// Performs a two-finger swipe gesture at a screen position.
        /// </summary>
        public static async UniTask TwoFingerSwipeAt(float xPercent, float yPercent, SwipeDirection direction, float distance = 0.2f, float duration = 1.0f, float fingerSpacing = 0.03f)
        {
            var center = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            await ActionExecutor.TwoFingerSwipeAtAsync(center, direction.ToString().ToLower(), distance, duration, fingerSpacing);
            await ActionComplete();
        }

        #endregion

        #region Rotation Operations

        /// <summary>
        /// Performs a two-finger rotation gesture on an element.
        /// </summary>
        public static async UniTask Rotate(Search search, float degrees, float duration = 1.0f, float fingerDistance = 0.05f, bool throwIfMissing = true, float searchTime = 10)
        {
            Debug.Log($"[UITEST] Rotate {degrees} degrees");
            var target = await Find<Transform>(search, throwIfMissing, searchTime);
            if (target != null)
            {
                var center = InputInjector.GetScreenPosition(target.gameObject);
                float radiusPixels = fingerDistance * Screen.height;
                await ActionExecutor.RotateAtPixelsAsync(center, degrees, duration, radiusPixels);
                await ActionComplete();
            }
        }

        /// <summary>
        /// Performs a rotation gesture at screen center.
        /// </summary>
        public static async UniTask Rotate(float degrees, float duration = 1.0f, float fingerDistance = 0.05f)
        {
            await RotateAt(0.5f, 0.5f, degrees, duration, fingerDistance);
        }

        /// <summary>
        /// Performs a rotation gesture at a screen position.
        /// </summary>
        public static async UniTask RotateAt(float xPercent, float yPercent, float degrees, float duration = 1.0f, float fingerDistance = 0.05f)
        {
            var center = new Vector2(xPercent * Screen.width, yPercent * Screen.height);
            float radiusPixels = fingerDistance * Screen.height;
            await ActionExecutor.RotateAtPixelsAsync(center, degrees, duration, radiusPixels);
            await ActionComplete();
        }

        #endregion

        #region Reporting

        /// <summary>
        /// Captures a screenshot and saves it to the temporary cache path.
        /// </summary>
        public static void CaptureScreenshot(string name = "screenshot")
        {
            string path = System.IO.Path.Combine(Application.temporaryCachePath, $"{name}_{DateTime.Now.Ticks}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[UITEST] Screenshot: {path}");
        }

        /// <summary>
        /// Attaches JSON data to the test report by logging it.
        /// </summary>
        public static void AttachJson(string name, object data)
        {
            string json = JsonUtility.ToJson(data, true);
            Debug.Log($"[UITEST] Attach JSON '{name}': {json}");
        }

        /// <summary>
        /// Attaches text content to the test report.
        /// </summary>
        public static void AttachText(string name, string content)
        {
            Debug.Log($"[UITEST] Attach Text '{name}': {content}");
        }

        /// <summary>
        /// Adds a named parameter to the test report.
        /// </summary>
        public static void AddParameter(string name, string value)
        {
            Debug.Log($"[UITEST] Parameter: {name}={value}");
        }

        /// <summary>
        /// Attaches a file to the test report by logging its path.
        /// </summary>
        public static void AttachFile(string name, string filePath, string mimeType)
        {
            if (System.IO.File.Exists(filePath))
            {
                Debug.Log($"[UITEST] Attach File '{name}': {filePath} ({mimeType})");
            }
        }

        /// <summary>
        /// Attaches a file to the test report by logging its path.
        /// </summary>
        public static void AttachFile(string filePath)
        {
            if (System.IO.File.Exists(filePath))
            {
                Debug.Log($"[UITEST] Attach File: {filePath}");
            }
        }

        #endregion

        #region Random Click and Auto-Explore

        /// <summary>
        /// Clicks a random clickable UI element.
        /// </summary>
        public static async UniTask<Component> RandomClick(Search filter = null)
        {
            var clickables = await GetClickableElements(filter);
            if (clickables.Count == 0)
            {
                Debug.Log("[UITEST] RandomClick - No clickable elements found");
                return null;
            }

            int index = RandomGenerator.Next(clickables.Count);
            var target = clickables[index];
            Debug.Log($"[UITEST] RandomClick - Selected '{target.gameObject.name}'");

            await Click(target.gameObject);
            return target;
        }

        /// <summary>
        /// Clicks a random clickable element, excluding specified patterns.
        /// </summary>
        public static async UniTask<Component> RandomClickExcept(params Search[] exclude)
        {
            var clickables = await GetClickableElements(null);

            foreach (var excludeSearch in exclude)
            {
                clickables = clickables.Where(c => !excludeSearch.Matches(c.gameObject)).ToList();
            }

            if (clickables.Count == 0)
            {
                Debug.Log("[UITEST] RandomClickExcept - No clickable elements found");
                return null;
            }

            int index = RandomGenerator.Next(clickables.Count);
            var target = clickables[index];
            Debug.Log($"[UITEST] RandomClickExcept - Selected '{target.gameObject.name}'");

            await Click(target.gameObject);
            return target;
        }

        static async UniTask<List<Component>> GetClickableElements(Search filter)
        {
            await UniTask.Yield();

            var allSelectables = GameObject.FindObjectsByType<Selectable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var clickables = new List<Component>();

            foreach (var selectable in allSelectables)
            {
                if (selectable == null) continue;
                if (!selectable.interactable) continue;
                if (!selectable.gameObject.activeInHierarchy) continue;

                var canvasGroup = selectable.GetComponentInParent<CanvasGroup>();
                if (canvasGroup != null && (canvasGroup.alpha <= 0 || !canvasGroup.interactable)) continue;

                if (filter != null && !filter.Matches(selectable.gameObject)) continue;

                clickables.Add(selectable);
            }

            return clickables;
        }

        /// <summary>
        /// Condition for stopping auto-exploration.
        /// </summary>
        public enum ExploreStopCondition
        {
            Time,
            ActionCount,
            DeadEnd,
            ElementAppears,
            ElementDisappears
        }

        /// <summary>
        /// Result of an auto-explore session.
        /// </summary>
        public class ExploreResult
        {
            public int ActionsPerformed { get; set; }
            public float DurationSeconds { get; set; }
            public bool ReachedDeadEnd { get; set; }
            public List<string> ClickedElements { get; } = new List<string>();
            public List<string> VisitedScenes { get; } = new List<string>();
            public ExploreStopCondition StopReason { get; set; }
        }

        static readonly string[] BackButtonPatterns = new[]
        {
            "*Back*", "*Close*", "*Exit*", "*Cancel*", "*Done*", "*Return*",
            "*Dismiss*", "*OK*", "*No*", "*X*", "BackButton", "CloseButton"
        };

        /// <summary>
        /// Auto-explores the UI by randomly clicking elements.
        /// </summary>
        public static async UniTask<ExploreResult> AutoExplore(
            ExploreStopCondition stopCondition,
            float value = 60f,
            int? seed = null,
            float delayBetweenActions = 0.5f,
            bool tryBackOnStuck = true)
        {
            if (seed.HasValue)
                SetRandomSeed(seed.Value);
            else
                SetRandomSeed(Environment.TickCount);

            var result = new ExploreResult();
            var startTime = Time.realtimeSinceStartup;
            var seenElements = new HashSet<string>();
            var currentScene = SceneManager.GetActiveScene().name;
            result.VisitedScenes.Add(currentScene);

            int consecutiveFailures = 0;
            const int maxConsecutiveFailures = 5;

            Debug.Log($"[UITEST] AutoExplore started - StopCondition: {stopCondition}, Value: {value}");

            while (Application.isPlaying)
            {
                result.DurationSeconds = Time.realtimeSinceStartup - startTime;

                // Check stop conditions
                switch (stopCondition)
                {
                    case ExploreStopCondition.Time:
                        if (result.DurationSeconds >= value)
                        {
                            result.StopReason = ExploreStopCondition.Time;
                            return result;
                        }
                        break;
                    case ExploreStopCondition.ActionCount:
                        if (result.ActionsPerformed >= (int)value)
                        {
                            result.StopReason = ExploreStopCondition.ActionCount;
                            return result;
                        }
                        break;
                    case ExploreStopCondition.DeadEnd:
                        if (consecutiveFailures >= maxConsecutiveFailures)
                        {
                            result.ReachedDeadEnd = true;
                            result.StopReason = ExploreStopCondition.DeadEnd;
                            return result;
                        }
                        break;
                }

                // Check for scene changes
                var newScene = SceneManager.GetActiveScene().name;
                if (newScene != currentScene)
                {
                    currentScene = newScene;
                    if (!result.VisitedScenes.Contains(newScene))
                        result.VisitedScenes.Add(newScene);
                    consecutiveFailures = 0;
                }

                // Get clickable elements
                var clickables = await GetClickableElements(null);
                if (clickables.Count == 0)
                {
                    consecutiveFailures++;
                    if (tryBackOnStuck && consecutiveFailures >= 2)
                        await TryClickBackButton();
                    await UniTask.Delay((int)(delayBetweenActions * 1000));
                    continue;
                }

                // Prefer elements we haven't clicked yet
                var newElements = clickables.Where(c => !seenElements.Contains(GetElementKey(c))).ToList();
                Component target;

                if (newElements.Count > 0)
                {
                    int index = RandomGenerator.Next(newElements.Count);
                    target = newElements[index];
                    consecutiveFailures = 0;
                }
                else
                {
                    consecutiveFailures++;
                    int index = RandomGenerator.Next(clickables.Count);
                    target = clickables[index];
                }

                var elementKey = GetElementKey(target);
                seenElements.Add(elementKey);
                result.ClickedElements.Add(target.gameObject.name);

                await Click(target.gameObject);
                result.ActionsPerformed++;

                await UniTask.Delay((int)(delayBetweenActions * 1000));
            }

            return result;
        }

        /// <summary>
        /// Auto-explores for a specified duration.
        /// </summary>
        public static async UniTask<ExploreResult> AutoExploreForSeconds(float seconds, int? seed = null, float delayBetweenActions = 0.5f)
        {
            return await AutoExplore(ExploreStopCondition.Time, seconds, seed, delayBetweenActions);
        }

        /// <summary>
        /// Auto-explores for a specified number of actions.
        /// </summary>
        public static async UniTask<ExploreResult> AutoExploreForActions(int actionCount, int? seed = null, float delayBetweenActions = 0.5f)
        {
            return await AutoExplore(ExploreStopCondition.ActionCount, actionCount, seed, delayBetweenActions);
        }

        /// <summary>
        /// Auto-explores until reaching a dead end.
        /// </summary>
        public static async UniTask<ExploreResult> AutoExploreUntilDeadEnd(int? seed = null, float delayBetweenActions = 0.5f, bool tryBackOnStuck = false)
        {
            return await AutoExplore(ExploreStopCondition.DeadEnd, 0, seed, delayBetweenActions, tryBackOnStuck);
        }

        /// <summary>
        /// Tries to click a back/exit/close button.
        /// </summary>
        public static async UniTask<bool> TryClickBackButton()
        {
            foreach (var pattern in BackButtonPatterns)
            {
                var backButton = await Find<Selectable>(new Search().Name(pattern), throwIfMissing: false, seconds: 0.1f);
                if (backButton != null && backButton.interactable)
                {
                    Debug.Log($"[UITEST] TryClickBackButton - clicking '{backButton.gameObject.name}'");
                    await Click(backButton.gameObject);
                    return true;
                }
            }
            return false;
        }

        static string GetElementKey(Component component)
        {
            if (component == null) return "";
            var path = new System.Text.StringBuilder();
            var transform = component.transform;
            while (transform != null)
            {
                if (path.Length > 0) path.Insert(0, "/");
                path.Insert(0, transform.name);
                transform = transform.parent;
            }
            return path.ToString();
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
    }
}
