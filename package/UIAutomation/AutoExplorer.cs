using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ODDGames.UIAutomation
{
    /// <summary>
    /// Static auto-exploration utility for monkey testing and CI integration.
    /// Can be invoked from batch mode, editor menus, or as a runtime component.
    /// </summary>
    public class AutoExplorer : MonoBehaviour
    {
        #region Static API

        /// <summary>
        /// Singleton instance for runtime access.
        /// </summary>
        public static AutoExplorer Instance { get; private set; }

#if UNITY_EDITOR
        /// <summary>
        /// Auto-starts exploration when entering play mode if triggered from Test Explorer.
        /// Uses RuntimeInitializeOnLoadMethod to ensure reliable startup.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CheckPendingExploration()
        {
            if (UnityEditor.EditorPrefs.GetBool("AutoExplorer_Pending", false))
            {
                UnityEditor.EditorPrefs.SetBool("AutoExplorer_Pending", false);

                var seconds = UnityEditor.EditorPrefs.GetFloat("AutoExplorer_Duration", 60f);
                var actions = UnityEditor.EditorPrefs.GetInt("AutoExplorer_Actions", 0);
                var deadEnd = UnityEditor.EditorPrefs.GetBool("AutoExplorer_DeadEnd", false);

                var settings = new ExploreSettings
                {
                    DurationSeconds = seconds,
                    MaxActions = actions,
                    StopOnDeadEnd = deadEnd,
                    Seed = null,
                    DelayBetweenActions = 0.5f,
                    TryBackOnStuck = true
                };

                StartExplorationAfterDelay(settings).Forget();
            }
        }

        private static async UniTaskVoid StartExplorationAfterDelay(ExploreSettings settings)
        {
            await UniTask.DelayFrame(30);
            Debug.Log($"[AutoExplorer] Starting from Test Explorer - Duration: {settings.DurationSeconds}s, Actions: {settings.MaxActions}, DeadEnd: {settings.StopOnDeadEnd}");
            var result = await StartExploration(settings);
            Debug.Log($"[AutoExplorer] Completed - {result.ActionsPerformed} actions in {result.DurationSeconds:F1}s. Reason: {result.StopReason}");
        }
#endif

        /// <summary>
        /// Whether auto-exploration is currently running.
        /// </summary>
        public static bool IsExploring { get; private set; }

        /// <summary>
        /// Current exploration result (updated in real-time during exploration).
        /// </summary>
        public static ExploreResult CurrentResult { get; private set; }

        /// <summary>
        /// Event fired when exploration completes.
        /// </summary>
        public static event Action<ExploreResult> OnExploreComplete;

        /// <summary>
        /// Event fired after each action during exploration.
        /// </summary>
        public static event Action<string, int> OnActionPerformed;

        /// <summary>
        /// Starts auto-exploration from a static context (batch mode, editor script, etc.).
        /// Creates a temporary GameObject with AutoExplorer component if needed.
        /// </summary>
        public static async UniTask<ExploreResult> StartExploration(ExploreSettings settings = null)
        {
            settings ??= new ExploreSettings();

            // Ensure we have an instance
            if (Instance == null)
            {
                var go = new GameObject("[AutoExplorer]");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<AutoExplorer>();
            }

            return await Instance.RunExploration(settings);
        }

        /// <summary>
        /// Stops the current exploration.
        /// </summary>
        public static void StopExploration()
        {
            if (Instance != null)
            {
                Instance._cancellationRequested = true;
            }
        }

        /// <summary>
        /// Entry point for batch mode execution via command line.
        /// Usage: Unity.exe -batchmode -executeMethod ODDGames.UIAutomation.AutoExplorer.RunBatch
        /// Supports arguments: -exploreSeconds, -exploreActions, -exploreSeed
        /// </summary>
        public static void RunBatch()
        {
            var args = Environment.GetCommandLineArgs();
            var settings = ParseCommandLineArgs(args);

            Debug.Log($"[AutoExplorer] Batch mode started - Duration: {settings.DurationSeconds}s, Actions: {settings.MaxActions}, Seed: {settings.Seed}");

            // Start exploration when play mode starts
            Application.quitting += () =>
            {
                Debug.Log("[AutoExplorer] Application quitting");
            };

            // For batch mode, we need to start play mode first
            // This will be picked up by PlayModeAutoExplorer
#if UNITY_EDITOR
            UnityEditor.EditorApplication.EnterPlaymode();
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state == UnityEditor.PlayModeStateChange.EnteredPlayMode)
                {
                    RunBatchExploration(settings).Forget();
                }
            };
#else
            RunBatchExploration(settings).Forget();
#endif
        }

        private static async UniTaskVoid RunBatchExploration(ExploreSettings settings)
        {
            await UniTask.DelayFrame(10); // Wait for scene to initialize

            try
            {
                var result = await StartExploration(settings);
                Debug.Log($"[AutoExplorer] Batch complete - Actions: {result.ActionsPerformed}, Duration: {result.DurationSeconds:F1}s, DeadEnd: {result.ReachedDeadEnd}");
                Debug.Log($"[AutoExplorer] Clicked elements: {string.Join(", ", result.ClickedElements.Take(20))}...");
                Debug.Log($"[AutoExplorer] Visited scenes: {string.Join(", ", result.VisitedScenes)}");

#if UNITY_EDITOR
                UnityEditor.EditorApplication.ExitPlaymode();
#else
                Application.Quit(result.ReachedDeadEnd ? 1 : 0);
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AutoExplorer] Batch failed: {ex.Message}\n{ex.StackTrace}");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.ExitPlaymode();
#else
                Application.Quit(1);
#endif
            }
        }

        private static ExploreSettings ParseCommandLineArgs(string[] args)
        {
            var settings = new ExploreSettings();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "-exploreseconds":
                        if (i + 1 < args.Length && float.TryParse(args[i + 1], out var seconds))
                            settings.DurationSeconds = seconds;
                        break;
                    case "-exploreactions":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var actions))
                            settings.MaxActions = actions;
                        break;
                    case "-exploreseed":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var seed))
                            settings.Seed = seed;
                        break;
                    case "-exploredelay":
                        if (i + 1 < args.Length && float.TryParse(args[i + 1], out var delay))
                            settings.DelayBetweenActions = delay;
                        break;
                    case "-exploreuntildeadend":
                        settings.StopOnDeadEnd = true;
                        settings.DurationSeconds = 0;
                        settings.MaxActions = 0;
                        break;
                }
            }

            return settings;
        }

        #endregion

        #region Inspector Settings (for runtime component usage)

        [Header("Stop Conditions")]
        [Tooltip("Maximum duration in seconds (0 = unlimited)")]
        public float durationSeconds = 60f;

        [Tooltip("Maximum number of actions (0 = unlimited)")]
        public int maxActions = 0;

        [Tooltip("Stop when no new clickable elements found")]
        public bool stopOnDeadEnd = false;

        [Header("Behavior")]
        [Tooltip("Random seed for reproducibility (-1 = random)")]
        public int seed = -1;

        [Tooltip("Delay between actions in seconds")]
        public float delayBetweenActions = 0.5f;

        [Tooltip("Try to click back/exit buttons when stuck")]
        public bool tryBackOnStuck = true;

        [Header("Auto-Start")]
        [Tooltip("Automatically start exploration on Awake")]
        public bool autoStart = false;

        [Tooltip("Delay before starting exploration")]
        public float autoStartDelay = 1f;

        #endregion

        #region Runtime

        private System.Random _random;
        private bool _cancellationRequested;
        private HashSet<string> _seenElements = new HashSet<string>();

        private static readonly string[] BackButtonPatterns = new[]
        {
            "*Back*", "*Close*", "*Exit*", "*Cancel*", "*Done*", "*Return*",
            "*Dismiss*", "*OK*", "*No*", "*X*", "BackButton", "CloseButton"
        };

        private static readonly string[] BackButtonTexts = new[]
        {
            "Back", "Close", "Exit", "Cancel", "Done", "OK", "X", "Return", "Dismiss"
        };

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            if (autoStart)
            {
                StartExplorationDelayed().Forget();
            }
        }

        private async UniTaskVoid StartExplorationDelayed()
        {
            await UniTask.Delay((int)(autoStartDelay * 1000));
            await RunExploration(GetSettingsFromInspector());
        }

        private ExploreSettings GetSettingsFromInspector()
        {
            return new ExploreSettings
            {
                DurationSeconds = durationSeconds,
                MaxActions = maxActions,
                StopOnDeadEnd = stopOnDeadEnd,
                Seed = seed >= 0 ? seed : (int?)null,
                DelayBetweenActions = delayBetweenActions,
                TryBackOnStuck = tryBackOnStuck
            };
        }

        /// <summary>
        /// Starts exploration via context menu.
        /// </summary>
        [ContextMenu("Start Exploration")]
        public void StartExplorationFromMenu()
        {
            RunExploration(GetSettingsFromInspector()).Forget();
        }

        /// <summary>
        /// Stops exploration via context menu.
        /// </summary>
        [ContextMenu("Stop Exploration")]
        public void StopExplorationFromMenu()
        {
            _cancellationRequested = true;
        }

        public async UniTask<ExploreResult> RunExploration(ExploreSettings settings)
        {
            if (IsExploring)
            {
                Debug.LogWarning("[AutoExplorer] Exploration already in progress");
                return CurrentResult;
            }

            IsExploring = true;
            _cancellationRequested = false;
            _seenElements.Clear();
            _currentSettings = settings;

            // Initialize random
            _random = settings.Seed.HasValue
                ? new System.Random(settings.Seed.Value)
                : new System.Random(Environment.TickCount);

            CurrentResult = new ExploreResult();
            var startTime = Time.realtimeSinceStartup;
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            CurrentResult.VisitedScenes.Add(currentScene);

            int consecutiveFailures = 0;
            const int maxConsecutiveFailures = 5;

            Debug.Log($"[AutoExplorer] Started - Duration: {settings.DurationSeconds}s, MaxActions: {settings.MaxActions}, Seed: {settings.Seed}, DeadEnd: {settings.StopOnDeadEnd}, ActionVariety: {settings.EnableActionVariety}, PriorityScoring: {settings.UsePriorityScoring}");

            try
            {
                while (Application.isPlaying && !_cancellationRequested)
                {
                    CurrentResult.DurationSeconds = Time.realtimeSinceStartup - startTime;

                    // Check stop conditions
                    if (settings.DurationSeconds > 0 && CurrentResult.DurationSeconds >= settings.DurationSeconds)
                    {
                        CurrentResult.StopReason = "Time limit reached";
                        break;
                    }

                    if (settings.MaxActions > 0 && CurrentResult.ActionsPerformed >= settings.MaxActions)
                    {
                        CurrentResult.StopReason = "Action count reached";
                        break;
                    }

                    if (settings.StopOnDeadEnd && consecutiveFailures >= maxConsecutiveFailures)
                    {
                        CurrentResult.ReachedDeadEnd = true;
                        CurrentResult.StopReason = "Dead end - no new elements";
                        break;
                    }

                    // Check for scene changes
                    var newScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                    if (newScene != currentScene)
                    {
                        currentScene = newScene;
                        if (!CurrentResult.VisitedScenes.Contains(newScene))
                            CurrentResult.VisitedScenes.Add(newScene);
                        Debug.Log($"[AutoExplorer] Scene changed: {newScene}");
                        consecutiveFailures = 0;
                    }

                    // Get clickable elements
                    var clickables = GetClickableElements();
                    if (clickables.Count == 0)
                    {
                        consecutiveFailures++;
                        if (settings.TryBackOnStuck && consecutiveFailures >= 2)
                        {
                            await TryClickBackButton();
                        }
                        await UniTask.Delay((int)(settings.DelayBetweenActions * 1000));
                        continue;
                    }

                    // Use priority scoring to select element
                    Selectable target = SelectElementWithPriority(clickables);

                    if (target == null)
                    {
                        consecutiveFailures++;
                        await UniTask.Delay((int)(settings.DelayBetweenActions * 1000));
                        continue;
                    }

                    // Check if this is a new element
                    var elementKey = GetElementKey(target);
                    var isNewElement = !_seenElements.Contains(elementKey);

                    if (isNewElement)
                    {
                        consecutiveFailures = 0;
                    }
                    else if (settings.TryBackOnStuck)
                    {
                        consecutiveFailures++;
                        if (consecutiveFailures >= 2)
                        {
                            if (await TryClickBackButton())
                            {
                                CurrentResult.ActionsPerformed++;
                                CurrentResult.ClickedElements.Add("[Back/Exit]");
                                OnActionPerformed?.Invoke("[Back/Exit]", CurrentResult.ActionsPerformed);
                                await UniTask.Delay((int)(settings.DelayBetweenActions * 1000));
                                continue;
                            }
                        }
                    }
                    else
                    {
                        consecutiveFailures++;
                    }

                    // Mark as seen
                    _seenElements.Add(elementKey);
                    CurrentResult.ClickedElements.Add(target.gameObject.name);

                    // Perform action based on element type (action variety)
                    var actionDescription = await PerformAction(target, settings);
                    Debug.Log($"[AutoExplorer] Action #{CurrentResult.ActionsPerformed + 1}: {actionDescription}");
                    CurrentResult.ActionsPerformed++;

                    OnActionPerformed?.Invoke(target.gameObject.name, CurrentResult.ActionsPerformed);

                    await UniTask.Delay((int)(settings.DelayBetweenActions * 1000));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AutoExplorer] Error: {ex.Message}\n{ex.StackTrace}");
                CurrentResult.StopReason = $"Error: {ex.Message}";
            }
            finally
            {
                IsExploring = false;
                CurrentResult.DurationSeconds = Time.realtimeSinceStartup - startTime;
                Debug.Log($"[AutoExplorer] Completed - Actions: {CurrentResult.ActionsPerformed}, Duration: {CurrentResult.DurationSeconds:F1}s, Reason: {CurrentResult.StopReason}");
                OnExploreComplete?.Invoke(CurrentResult);
            }

            return CurrentResult;
        }

        private ExploreSettings _currentSettings;

        private List<Selectable> GetClickableElements()
        {
            var allSelectables = FindObjectsByType<Selectable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var clickables = new List<Selectable>();

            foreach (var selectable in allSelectables)
            {
                if (selectable == null) continue;
                if (!selectable.interactable) continue;
                if (!selectable.gameObject.activeInHierarchy) continue;

                var canvasGroup = selectable.GetComponentInParent<CanvasGroup>();
                if (canvasGroup != null && (canvasGroup.alpha <= 0 || !canvasGroup.interactable)) continue;

                // Check exclusion patterns
                if (_currentSettings != null && IsExcluded(selectable))
                    continue;

                clickables.Add(selectable);
            }

            return clickables;
        }

        private bool IsExcluded(Selectable selectable)
        {
            var goName = selectable.gameObject.name;

            // Check name patterns
            if (_currentSettings.ExcludePatterns != null)
            {
                foreach (var pattern in _currentSettings.ExcludePatterns)
                {
                    if (MatchesPattern(goName, pattern))
                    {
                        Debug.Log($"[AutoExplorer] Excluding '{goName}' (matches pattern '{pattern}')");
                        return true;
                    }
                }
            }

            // Check text content
            if (_currentSettings.ExcludeTexts != null)
            {
                var text = GetElementText(selectable.gameObject);
                if (!string.IsNullOrEmpty(text))
                {
                    foreach (var excludeText in _currentSettings.ExcludeTexts)
                    {
                        if (text.Equals(excludeText, StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.Log($"[AutoExplorer] Excluding '{goName}' (text matches '{excludeText}')");
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private string GetElementText(GameObject go)
        {
            var tmpText = go.GetComponentInChildren<TMP_Text>();
            if (tmpText != null) return tmpText.text;

            var legacyText = go.GetComponentInChildren<Text>();
            if (legacyText != null) return legacyText.text;

            return null;
        }

        private float CalculatePriorityScore(Selectable selectable)
        {
            float score = 0f;
            var go = selectable.gameObject;

            // Newness bonus (highest priority)
            if (!_seenElements.Contains(GetElementKey(selectable)))
                score += 100f;

            // Modal/popup bonus - elements in CanvasGroup or with "Modal/Popup/Dialog" in parent
            var parent = go.transform.parent;
            while (parent != null)
            {
                var parentName = parent.name.ToLower();
                if (parentName.Contains("modal") || parentName.Contains("popup") || parentName.Contains("dialog") || parentName.Contains("overlay"))
                {
                    score += 50f;
                    break;
                }
                parent = parent.parent;
            }

            // Type-based scoring
            if (selectable is Button) score += 20f;
            else if (selectable is Toggle) score += 15f;
            else if (selectable is TMP_Dropdown || selectable is Dropdown) score += 10f;
            else if (selectable is Slider) score += 5f;
            else if (selectable is TMP_InputField || selectable is InputField) score += 5f;

            // Center of screen bonus
            var screenPos = InputInjector.GetScreenPosition(go);
            var screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var distFromCenter = Vector2.Distance(screenPos, screenCenter);
            var maxDist = Mathf.Sqrt(Screen.width * Screen.width + Screen.height * Screen.height) / 2f;
            var centerBonus = (1f - (distFromCenter / maxDist)) * 10f; // 0-10 points based on closeness to center
            score += centerBonus;

            return score;
        }

        private Selectable SelectElementWithPriority(List<Selectable> candidates)
        {
            if (candidates.Count == 0) return null;
            if (!_currentSettings.UsePriorityScoring)
            {
                return candidates[_random.Next(candidates.Count)];
            }

            // Calculate scores
            var scored = candidates.Select(c => new { Element = c, Score = CalculatePriorityScore(c) }).ToList();

            // Sort by score descending
            scored.Sort((a, b) => b.Score.CompareTo(a.Score));

            // Take top 3 and pick randomly among them (adds some variety)
            var topCandidates = scored.Take(3).ToList();
            return topCandidates[_random.Next(topCandidates.Count)].Element;
        }

        private async UniTask<string> PerformAction(Selectable element, ExploreSettings settings)
        {
            var goName = element.gameObject.name;

            // If action variety is disabled, just click
            if (!settings.EnableActionVariety)
            {
                await ClickElement(element);
                return $"Click '{goName}'";
            }

            // Handle different element types with appropriate actions
            if (element is Slider slider)
            {
                // Drag slider to random position
                var targetValue = (float)_random.NextDouble();
                await DragSlider(slider, targetValue);
                return $"Drag slider '{goName}' to {targetValue:P0}";
            }

            if (element is TMP_InputField tmpInput)
            {
                // Type into input field
                var testString = settings.TestInputStrings[_random.Next(settings.TestInputStrings.Length)];
                await TypeIntoInput(tmpInput, testString);
                return $"Type '{testString}' into '{goName}'";
            }

            if (element is InputField legacyInput)
            {
                var testString = settings.TestInputStrings[_random.Next(settings.TestInputStrings.Length)];
                await TypeIntoLegacyInput(legacyInput, testString);
                return $"Type '{testString}' into '{goName}'";
            }

            // Check if element is inside a ScrollRect - if so, sometimes scroll instead of click
            var parentScrollRect = element.GetComponentInParent<ScrollRect>();
            if (parentScrollRect != null && _random.Next(5) == 0) // 20% chance to scroll instead
            {
                var scrollDir = _random.Next(2) == 0 ? Vector2.up : Vector2.down;
                var scrollAmount = 0.2f + (float)_random.NextDouble() * 0.3f;
                await ScrollView(parentScrollRect, scrollDir * scrollAmount);
                return $"Scroll '{parentScrollRect.gameObject.name}' {(scrollDir.y > 0 ? "up" : "down")}";
            }

            if (element is TMP_Dropdown tmpDropdown)
            {
                // Click to open, then click random option
                await ClickElement(element);
                await UniTask.Delay(200); // Wait for dropdown to open
                var optionCount = tmpDropdown.options.Count;
                if (optionCount > 0)
                {
                    var targetIndex = _random.Next(optionCount);
                    await ClickDropdownOption(targetIndex);
                    return $"Select option {targetIndex} from '{goName}'";
                }
                return $"Click dropdown '{goName}'";
            }

            if (element is Dropdown dropdown)
            {
                await ClickElement(element);
                await UniTask.Delay(200);
                var optionCount = dropdown.options.Count;
                if (optionCount > 0)
                {
                    var targetIndex = _random.Next(optionCount);
                    await ClickDropdownOption(targetIndex);
                    return $"Select option {targetIndex} from '{goName}'";
                }
                return $"Click dropdown '{goName}'";
            }

            // Default: click
            await ClickElement(element);
            return $"Click '{goName}'";
        }

        private async UniTask DragSlider(Slider slider, float targetValue)
        {
            var rectTransform = slider.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                slider.value = Mathf.Lerp(slider.minValue, slider.maxValue, targetValue);
                return;
            }

            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);

            Vector2 startPos, endPos;
            if (slider.direction == Slider.Direction.LeftToRight || slider.direction == Slider.Direction.RightToLeft)
            {
                var y = (corners[0].y + corners[2].y) / 2f;
                var currentX = Mathf.Lerp(corners[0].x, corners[2].x, slider.normalizedValue);
                var targetX = Mathf.Lerp(corners[0].x, corners[2].x, targetValue);
                startPos = new Vector2(currentX, y);
                endPos = new Vector2(targetX, y);
            }
            else
            {
                var x = (corners[0].x + corners[2].x) / 2f;
                var currentY = Mathf.Lerp(corners[0].y, corners[2].y, slider.normalizedValue);
                var targetY = Mathf.Lerp(corners[0].y, corners[2].y, targetValue);
                startPos = new Vector2(x, currentY);
                endPos = new Vector2(x, targetY);
            }

            await InjectMouseDrag(startPos, endPos, 0.3f);
        }

        private async UniTask TypeIntoInput(TMP_InputField input, string text)
        {
            // Click to focus
            await ClickElement(input);
            await UniTask.Delay(100);

            // Clear existing text and type new text
            input.text = text;
            input.onValueChanged?.Invoke(text);
        }

        private async UniTask TypeIntoLegacyInput(InputField input, string text)
        {
            await ClickElement(input);
            await UniTask.Delay(100);
            input.text = text;
            input.onValueChanged?.Invoke(text);
        }

        private async UniTask ScrollView(ScrollRect scrollRect, Vector2 delta)
        {
            var content = scrollRect.content;
            if (content == null) return;

            var rectTransform = scrollRect.GetComponent<RectTransform>();
            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            var center = (corners[0] + corners[2]) / 2f;

            var startPos = new Vector2(center.x, center.y);
            var endPos = startPos + delta * Screen.height;

            await InjectMouseDrag(startPos, endPos, 0.3f);
        }

        private async UniTask ClickDropdownOption(int index)
        {
            // Find dropdown items - they're usually created dynamically
            var items = FindObjectsByType<Toggle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(t => t.gameObject.activeInHierarchy && t.GetComponentInParent<TMP_Dropdown>() != null || t.GetComponentInParent<Dropdown>() != null)
                .ToList();

            if (index < items.Count)
            {
                await ClickElement(items[index]);
            }
        }

        private async UniTask InjectMouseDrag(Vector2 startPos, Vector2 endPos, float duration)
        {
            await InputInjector.InjectPointerDrag(startPos, endPos, duration);
        }

        private async UniTask ClickElement(Selectable element)
        {
            var screenPos = InputInjector.GetScreenPosition(element.gameObject);
            await InputInjector.InjectPointerTap(screenPos);
        }

        private async UniTask<bool> TryClickBackButton()
        {
            // Try by name patterns
            foreach (var pattern in BackButtonPatterns)
            {
                var matches = FindObjectsByType<Selectable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .Where(s => s.interactable && s.gameObject.activeInHierarchy && MatchesPattern(s.gameObject.name, pattern))
                    .ToList();

                if (matches.Count > 0)
                {
                    Debug.Log($"[AutoExplorer] Back button found: {matches[0].gameObject.name}");
                    await ClickElement(matches[0]);
                    return true;
                }
            }

            // Try by text content
            foreach (var text in BackButtonTexts)
            {
                var buttons = FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .Where(b => b.interactable && b.gameObject.activeInHierarchy && HasText(b.gameObject, text))
                    .ToList();

                if (buttons.Count > 0)
                {
                    Debug.Log($"[AutoExplorer] Back button found by text: {buttons[0].gameObject.name}");
                    await ClickElement(buttons[0]);
                    return true;
                }
            }

            return false;
        }

        private bool MatchesPattern(string name, string pattern)
        {
            if (pattern.StartsWith("*") && pattern.EndsWith("*"))
            {
                var middle = pattern.Substring(1, pattern.Length - 2);
                return name.IndexOf(middle, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (pattern.StartsWith("*"))
            {
                return name.EndsWith(pattern.Substring(1), StringComparison.OrdinalIgnoreCase);
            }
            if (pattern.EndsWith("*"))
            {
                return name.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
        }

        private bool HasText(GameObject go, string text)
        {
            var tmpText = go.GetComponentInChildren<TMP_Text>();
            if (tmpText != null && tmpText.text.Equals(text, StringComparison.OrdinalIgnoreCase))
                return true;

            var legacyText = go.GetComponentInChildren<Text>();
            if (legacyText != null && legacyText.text.Equals(text, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private string GetElementKey(Selectable element)
        {
            var go = element.gameObject;
            return $"{go.name}_{go.transform.GetSiblingIndex()}_{go.transform.parent?.name ?? "root"}";
        }

        #endregion
    }

    /// <summary>
    /// Settings for auto-exploration.
    /// </summary>
    public class ExploreSettings
    {
        /// <summary>Maximum duration in seconds (0 = unlimited)</summary>
        public float DurationSeconds { get; set; } = 60f;

        /// <summary>Maximum number of actions (0 = unlimited)</summary>
        public int MaxActions { get; set; } = 0;

        /// <summary>Stop when no new clickable elements found</summary>
        public bool StopOnDeadEnd { get; set; } = false;

        /// <summary>Random seed for reproducibility (null = random)</summary>
        public int? Seed { get; set; } = null;

        /// <summary>Delay between actions in seconds</summary>
        public float DelayBetweenActions { get; set; } = 0.5f;

        /// <summary>Try to click back/exit buttons when stuck</summary>
        public bool TryBackOnStuck { get; set; } = true;

        /// <summary>
        /// Name patterns to exclude from clicking (e.g., "*Logout*", "*Delete*", "*Purchase*").
        /// Supports wildcards: * matches any characters, ? matches single character.
        /// </summary>
        public string[] ExcludePatterns { get; set; } = DefaultExcludePatterns;

        /// <summary>
        /// Text patterns to exclude from clicking (matches button text content).
        /// </summary>
        public string[] ExcludeTexts { get; set; } = DefaultExcludeTexts;

        /// <summary>
        /// Enable varied actions beyond clicking (scroll, drag sliders, type into inputs).
        /// </summary>
        public bool EnableActionVariety { get; set; } = true;

        /// <summary>
        /// Test strings to type into input fields when encountered.
        /// </summary>
        public string[] TestInputStrings { get; set; } = new[] { "Test", "test@example.com", "12345", "Hello World" };

        /// <summary>
        /// Use priority scoring to prefer certain elements (modals, new elements, center of screen).
        /// </summary>
        public bool UsePriorityScoring { get; set; } = true;

        /// <summary>Default patterns to exclude (dangerous actions).</summary>
        public static readonly string[] DefaultExcludePatterns = new[]
        {
            "*Logout*", "*SignOut*", "*Sign Out*", "*Log Out*",
            "*Delete*", "*Remove*", "*Erase*", "*Clear All*",
            "*Purchase*", "*Buy*", "*Pay*", "*Checkout*", "*IAP*",
            "*Quit*", "*Exit Game*",
            "*Reset*", "*Factory Reset*",
            "*Uninstall*", "*Unlink*"
        };

        /// <summary>Default text to exclude (dangerous actions).</summary>
        public static readonly string[] DefaultExcludeTexts = new[]
        {
            "Logout", "Sign Out", "Log Out",
            "Delete", "Remove", "Erase",
            "Purchase", "Buy Now", "Pay", "Checkout",
            "Quit", "Exit",
            "Reset", "Clear All",
            "Confirm Delete", "Yes, Delete"
        };
    }

    /// <summary>
    /// Result of an auto-exploration session.
    /// </summary>
    public class ExploreResult
    {
        public int ActionsPerformed { get; set; }
        public float DurationSeconds { get; set; }
        public bool ReachedDeadEnd { get; set; }
        public string StopReason { get; set; }
        public List<string> ClickedElements { get; } = new List<string>();
        public List<string> VisitedScenes { get; } = new List<string>();
    }
}
