using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ODDGames.UIAutomation
{
    /// <summary>
    /// Fast dialog dismissal recovery that looks for common UI patterns.
    /// Use this before AI recovery for quick dismissal of standard dialogs.
    ///
    /// Usage in UITestBase:
    /// <code>
    /// protected override void ConfigureRecoveryHandler()
    /// {
    ///     // Try dialog dismissal first, then fall back to AI
    ///     ActionExecutor.RecoveryHandler = DialogDismissal.CreateHandler(
    ///         fallback: AINavigator.CreateRecoveryHandler()
    ///     );
    /// }
    /// </code>
    ///
    /// Or standalone:
    /// <code>
    /// ActionExecutor.RecoveryHandler = DialogDismissal.CreateHandler();
    /// </code>
    /// </summary>
    public static class DialogDismissal
    {
        /// <summary>
        /// Default button name patterns to look for when dismissing dialogs.
        /// Matches buttons with these names (case-insensitive, supports wildcards).
        /// </summary>
        public static string[] DefaultButtonPatterns { get; set; } = new[]
        {
            // Close buttons
            "Close*", "*Close", "CloseButton", "ButtonClose", "BtnClose",
            "X", "ButtonX", "BtnX",

            // Dismiss/Cancel buttons
            "Cancel*", "*Cancel", "CancelButton", "ButtonCancel",
            "Dismiss*", "*Dismiss",
            "Back*", "*Back", "BackButton",

            // OK/Confirm (for info dialogs)
            "OK", "Ok", "Okay", "ButtonOK", "BtnOK",
            "Confirm*", "*Confirm",
            "Continue*", "*Continue",

            // Skip buttons
            "Skip*", "*Skip",
            "Later*", "*Later",
            "NotNow", "NoThanks",

            // Generic
            "Deny*", "Decline*", "No", "ButtonNo"
        };

        /// <summary>
        /// Default text patterns to look for on buttons.
        /// </summary>
        public static string[] DefaultTextPatterns { get; set; } = new[]
        {
            "Close", "Cancel", "OK", "Okay", "Back", "Skip", "Later",
            "Not Now", "No Thanks", "Dismiss", "Continue", "Got It",
            "I Understand", "Accept", "Deny", "Decline", "No", "X", "×"
        };

        /// <summary>
        /// Attempts to dismiss any visible dialog. Call directly for ad-hoc dismissal.
        /// Returns true if a dialog was dismissed.
        /// </summary>
        /// <example>
        /// // Dismiss any dialog before continuing
        /// await DialogDismissal.TryDismiss();
        ///
        /// // Or with custom patterns
        /// await DialogDismissal.TryDismiss(buttonPatterns: new[] { "MyCloseButton" });
        /// </example>
        public static async Task<bool> TryDismiss(
            string[] buttonPatterns = null,
            string[] textPatterns = null)
        {
            var patterns = buttonPatterns ?? DefaultButtonPatterns;
            var texts = textPatterns ?? DefaultTextPatterns;

            var dismissed = await TryDismissDialog(patterns, texts);

            if (dismissed != null)
            {
                Debug.Log($"[DialogDismissal] Dismissed '{dismissed}'");
                await Task.Yield();
                await Async.DelayFrames(2);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates a recovery handler that attempts to dismiss dialogs by clicking
        /// common close/dismiss buttons.
        /// </summary>
        /// <param name="fallback">Optional fallback handler if no dialog is found</param>
        /// <param name="buttonPatterns">Custom button name patterns (null = use defaults)</param>
        /// <param name="textPatterns">Custom button text patterns (null = use defaults)</param>
        /// <param name="maxAttempts">Maximum dismiss attempts per recovery call</param>
        public static Func<RecoveryContext, Task<RecoveryResult>> CreateHandler(
            Func<RecoveryContext, Task<RecoveryResult>> fallback = null,
            string[] buttonPatterns = null,
            string[] textPatterns = null,
            int maxAttempts = 3)
        {
            var patterns = buttonPatterns ?? DefaultButtonPatterns;
            var texts = textPatterns ?? DefaultTextPatterns;

            return async (context) =>
            {
                Debug.Log($"[DialogDismissal] Attempting recovery for: {context.FailedAction}");

                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    var dismissed = await TryDismissDialog(patterns, texts);

                    if (dismissed != null)
                    {
                        Debug.Log($"[DialogDismissal] Dismissed '{dismissed}' (attempt {attempt + 1})");

                        // Wait for UI to update
                        await Task.Yield();
                        await Async.DelayFrames(2);

                        return new RecoveryResult
                        {
                            Success = true,
                            Explanation = $"Dismissed dialog by clicking '{dismissed}'"
                        };
                    }
                }

                Debug.Log("[DialogDismissal] No dismissable dialog found");

                // No dialog found - try fallback if provided
                if (fallback != null)
                {
                    Debug.Log("[DialogDismissal] Trying fallback handler...");
                    return await fallback(context);
                }

                return new RecoveryResult
                {
                    Success = false,
                    NoBlockerFound = true,
                    Explanation = "No dismissable dialog found"
                };
            };
        }

        /// <summary>
        /// Attempts to find and click a dismiss button.
        /// Returns the name of the clicked button, or null if none found.
        /// </summary>
        private static async Task<string> TryDismissDialog(string[] buttonPatterns, string[] textPatterns)
        {
            // Strategy 1: Look for buttons by name pattern
            foreach (var pattern in buttonPatterns)
            {
                var button = FindClickableByNamePattern(pattern);
                if (button != null)
                {
                    var screenPos = InputInjector.GetScreenPosition(button);
                    if (ActionExecutor.IsScreenPositionClickable(screenPos))
                    {
                        await InputInjector.InjectPointerTap(screenPos);
                        return button.name;
                    }
                }
            }

            // Strategy 2: Look for buttons by text content
            foreach (var text in textPatterns)
            {
                var button = FindClickableByText(text);
                if (button != null)
                {
                    var screenPos = InputInjector.GetScreenPosition(button);
                    if (ActionExecutor.IsScreenPositionClickable(screenPos))
                    {
                        await InputInjector.InjectPointerTap(screenPos);
                        return $"Text:'{text}'";
                    }
                }
            }

            // Strategy 3: Look for modal overlay backgrounds that can be clicked to dismiss
            var overlay = FindDismissableOverlay();
            if (overlay != null)
            {
                var screenPos = InputInjector.GetScreenPosition(overlay);
                if (ActionExecutor.IsScreenPositionClickable(screenPos))
                {
                    await InputInjector.InjectPointerTap(screenPos);
                    return "Overlay background";
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a clickable button matching the name pattern.
        /// </summary>
        private static GameObject FindClickableByNamePattern(string pattern)
        {
            // Find all active Selectables (buttons, toggles, etc.)
            var selectables = UnityEngine.Object.FindObjectsByType<Selectable>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (var selectable in selectables)
            {
                if (!selectable.interactable) continue;

                var go = selectable.gameObject;
                if (WildcardMatch(go.name, pattern))
                {
                    return go;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a clickable element with the specified text.
        /// </summary>
        private static GameObject FindClickableByText(string text)
        {
            // Check TMP texts
            var tmpTexts = UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (var tmp in tmpTexts)
            {
                if (string.Equals(tmp.text?.Trim(), text, StringComparison.OrdinalIgnoreCase))
                {
                    // Find clickable parent
                    var clickable = tmp.GetComponentInParent<Selectable>();
                    if (clickable != null && clickable.interactable)
                    {
                        return clickable.gameObject;
                    }
                }
            }

            // Check legacy UI texts
            var legacyTexts = UnityEngine.Object.FindObjectsByType<Text>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (var txt in legacyTexts)
            {
                if (string.Equals(txt.text?.Trim(), text, StringComparison.OrdinalIgnoreCase))
                {
                    var clickable = txt.GetComponentInParent<Selectable>();
                    if (clickable != null && clickable.interactable)
                    {
                        return clickable.gameObject;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Finds an overlay/backdrop that can be clicked to dismiss a dialog.
        /// Looks for Image components with raycast target that cover most of the screen.
        /// </summary>
        private static GameObject FindDismissableOverlay()
        {
            var images = UnityEngine.Object.FindObjectsByType<Image>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            var screenWidth = Screen.width;
            var screenHeight = Screen.height;
            var screenArea = screenWidth * screenHeight;

            foreach (var image in images)
            {
                if (!image.raycastTarget) continue;

                var rect = image.rectTransform;
                Vector3[] corners = new Vector3[4];
                rect.GetWorldCorners(corners);

                // Check if this covers a significant portion of the screen
                var width = Mathf.Abs(corners[2].x - corners[0].x);
                var height = Mathf.Abs(corners[2].y - corners[0].y);
                var area = width * height;

                // Must cover at least 50% of screen and be semi-transparent
                if (area > screenArea * 0.5f)
                {
                    // Check if it's a backdrop (usually semi-transparent black/white)
                    var color = image.color;
                    if (color.a > 0.1f && color.a < 0.95f)
                    {
                        // Check if clicking it would trigger something (has button parent or event trigger)
                        var button = image.GetComponent<Button>();
                        var eventTrigger = image.GetComponent<UnityEngine.EventSystems.EventTrigger>();

                        if (button != null || eventTrigger != null)
                        {
                            return image.gameObject;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Simple wildcard pattern matching (* matches any characters).
        /// </summary>
        private static bool WildcardMatch(string text, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            if (string.IsNullOrEmpty(text)) return false;

            // Simple wildcard: * matches any sequence of characters
            if (pattern == "*") return true;

            var patternLower = pattern.ToLowerInvariant();
            var textLower = text.ToLowerInvariant();

            if (!pattern.Contains("*"))
            {
                return textLower == patternLower;
            }

            // Handle prefix wildcard: *Pattern
            if (pattern.StartsWith("*") && !pattern.EndsWith("*"))
            {
                var suffix = patternLower.Substring(1);
                return textLower.EndsWith(suffix);
            }

            // Handle suffix wildcard: Pattern*
            if (pattern.EndsWith("*") && !pattern.StartsWith("*"))
            {
                var prefix = patternLower.Substring(0, patternLower.Length - 1);
                return textLower.StartsWith(prefix);
            }

            // Handle both: *Pattern*
            if (pattern.StartsWith("*") && pattern.EndsWith("*"))
            {
                var middle = patternLower.Substring(1, patternLower.Length - 2);
                return textLower.Contains(middle);
            }

            return textLower == patternLower;
        }
    }
}
