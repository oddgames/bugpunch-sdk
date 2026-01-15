using System;
using UnityEngine;

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// Base class for AI actions.
    /// </summary>
    public abstract class AIAction
    {
        /// <summary>Type of action (e.g., "click", "type")</summary>
        public abstract string ActionType { get; }

        /// <summary>Human-readable description of the action</summary>
        public abstract string Description { get; }

        /// <summary>Target element (if applicable)</summary>
        public ElementInfo TargetElement { get; set; }
    }

    /// <summary>
    /// Click action - clicks an element or screen position.
    /// </summary>
    public class ClickAction : AIAction
    {
        public override string ActionType => "click";

        public string SearchQuery { get; set; }
        public Vector2? ScreenPosition { get; set; }

        public override string Description =>
            TargetElement != null
                ? $"Click '{TargetElement.text ?? TargetElement.name}'"
                : ScreenPosition.HasValue
                    ? $"Click at ({ScreenPosition.Value.x:F2}, {ScreenPosition.Value.y:F2})"
                    : $"Click {SearchQuery}";
    }

    /// <summary>
    /// Type action - types text into an input field.
    /// </summary>
    public class TypeAction : AIAction
    {
        public override string ActionType => "type";

        public string SearchQuery { get; set; }
        public string Text { get; set; }
        public bool ClearFirst { get; set; } = true;
        public bool PressEnter { get; set; } = false;

        public override string Description =>
            $"Type \"{(Text.Length > 20 ? Text.Substring(0, 17) + "..." : Text)}\" into {TargetElement?.name ?? SearchQuery}";
    }

    /// <summary>
    /// Drag action - drags from one position to another.
    /// </summary>
    public class DragAction : AIAction
    {
        public override string ActionType => "drag";

        public string FromSearch { get; set; }
        public string ToSearch { get; set; }
        public string Direction { get; set; } // "up", "down", "left", "right"
        public float Distance { get; set; } = 200f;
        public float Duration { get; set; } = 0.3f;

        public ElementInfo FromElement { get; set; }
        public ElementInfo ToElement { get; set; }

        public override string Description
        {
            get
            {
                if (ToElement != null)
                    return $"Drag from {FromElement?.name ?? FromSearch} to {ToElement.name}";
                if (!string.IsNullOrEmpty(Direction))
                    return $"Drag {FromElement?.name ?? FromSearch} {Direction} by {Distance}px";
                return $"Drag {FromElement?.name ?? FromSearch}";
            }
        }
    }

    /// <summary>
    /// Scroll action - scrolls a scrollable area.
    /// </summary>
    public class ScrollAction : AIAction
    {
        public override string ActionType => "scroll";

        public string SearchQuery { get; set; }
        public string Direction { get; set; } // "up", "down", "left", "right"
        public float Amount { get; set; } = 0.3f;

        public override string Description =>
            $"Scroll {TargetElement?.name ?? SearchQuery} {Direction}";
    }

    /// <summary>
    /// Wait action - waits for a duration.
    /// </summary>
    public class WaitAction : AIAction
    {
        public override string ActionType => "wait";

        public float Seconds { get; set; }

        public override string Description => $"Wait {Seconds:F1}s";
    }

    /// <summary>
    /// Pass action - declares test passed.
    /// </summary>
    public class PassAction : AIAction
    {
        public override string ActionType => "pass";

        public string Reason { get; set; }

        public override string Description => $"PASS: {Reason ?? "Test goal achieved"}";
    }

    /// <summary>
    /// Fail action - declares test failed.
    /// </summary>
    public class FailAction : AIAction
    {
        public override string ActionType => "fail";

        public string Reason { get; set; }

        public override string Description => $"FAIL: {Reason}";
    }

    /// <summary>
    /// Screenshot action - requests a screenshot to be sent to the AI.
    /// Used when the AI needs visual clarification to proceed.
    /// </summary>
    public class ScreenshotAction : AIAction
    {
        public override string ActionType => "screenshot";

        public string Reason { get; set; }

        public override string Description => $"Request screenshot: {Reason ?? "Need visual clarification"}";
    }

    /// <summary>
    /// Double-click action - double-clicks an element.
    /// </summary>
    public class DoubleClickAction : AIAction
    {
        public override string ActionType => "double_click";

        public string SearchQuery { get; set; }
        public Vector2? ScreenPosition { get; set; }

        public override string Description =>
            TargetElement != null
                ? $"Double-click '{TargetElement.text ?? TargetElement.name}'"
                : ScreenPosition.HasValue
                    ? $"Double-click at ({ScreenPosition.Value.x:F2}, {ScreenPosition.Value.y:F2})"
                    : $"Double-click {SearchQuery}";
    }

    /// <summary>
    /// Triple-click action - performs three rapid clicks on an element.
    /// </summary>
    public class TripleClickAction : AIAction
    {
        public override string ActionType => "triple_click";

        public string SearchQuery { get; set; }
        public Vector2? ScreenPosition { get; set; }

        public override string Description =>
            TargetElement != null
                ? $"Triple-click '{TargetElement.text ?? TargetElement.name}'"
                : ScreenPosition.HasValue
                    ? $"Triple-click at ({ScreenPosition.Value.x:F2}, {ScreenPosition.Value.y:F2})"
                    : $"Triple-click {SearchQuery}";
    }

    /// <summary>
    /// Hold action - long press on an element.
    /// </summary>
    public class HoldAction : AIAction
    {
        public override string ActionType => "hold";

        public string SearchQuery { get; set; }
        public float Duration { get; set; } = 1f;

        public override string Description =>
            $"Hold '{TargetElement?.text ?? TargetElement?.name ?? SearchQuery}' for {Duration:F1}s";
    }

    /// <summary>
    /// Key press action - presses a keyboard key.
    /// </summary>
    public class KeyPressAction : AIAction
    {
        public override string ActionType => "key_press";

        public string Key { get; set; }

        public override string Description => $"Press key '{Key}'";
    }

    /// <summary>
    /// Key hold action - holds one or more keys for a duration.
    /// </summary>
    public class KeyHoldAction : AIAction
    {
        public override string ActionType => "key_hold";

        public string[] Keys { get; set; }
        public float Duration { get; set; } = 0.5f;

        public override string Description =>
            $"Hold keys [{string.Join("+", Keys ?? Array.Empty<string>())}] for {Duration:F1}s";
    }

    /// <summary>
    /// Swipe action - swipes in a direction on an element.
    /// </summary>
    public class SwipeAction : AIAction
    {
        public override string ActionType => "swipe";

        public string SearchQuery { get; set; }
        public string Direction { get; set; } // "up", "down", "left", "right"
        public float Distance { get; set; } = 0.2f; // Normalized distance (0-1)
        public float Duration { get; set; } = 0.3f;

        public override string Description =>
            $"Swipe {Direction} on '{TargetElement?.text ?? TargetElement?.name ?? SearchQuery}'";
    }

    /// <summary>
    /// Pinch action - pinch to zoom in or out.
    /// </summary>
    public class PinchAction : AIAction
    {
        public override string ActionType => "pinch";

        public string SearchQuery { get; set; }
        public float Scale { get; set; } = 1.5f; // >1 = zoom in, <1 = zoom out
        public float Duration { get; set; } = 0.5f;

        public override string Description =>
            Scale > 1f
                ? $"Pinch out (zoom in) on '{TargetElement?.text ?? TargetElement?.name ?? SearchQuery}'"
                : $"Pinch in (zoom out) on '{TargetElement?.text ?? TargetElement?.name ?? SearchQuery}'";
    }

    /// <summary>
    /// Set slider action - sets a slider to a specific value.
    /// </summary>
    public class SetSliderAction : AIAction
    {
        public override string ActionType => "set_slider";

        public string SearchQuery { get; set; }
        public float Value { get; set; } // 0-1 normalized value

        public override string Description =>
            $"Set slider '{TargetElement?.text ?? TargetElement?.name ?? SearchQuery}' to {Value:P0}";
    }

    /// <summary>
    /// Set scrollbar action - sets a scrollbar to a specific position.
    /// </summary>
    public class SetScrollbarAction : AIAction
    {
        public override string ActionType => "set_scrollbar";

        public string SearchQuery { get; set; }
        public float Value { get; set; } // 0-1 normalized value

        public override string Description =>
            $"Set scrollbar '{TargetElement?.text ?? TargetElement?.name ?? SearchQuery}' to {Value:P0}";
    }

    /// <summary>
    /// Click dropdown action - selects an option from a dropdown by index or label.
    /// </summary>
    public class ClickDropdownAction : AIAction
    {
        public override string ActionType => "click_dropdown";

        public string SearchQuery { get; set; }
        public int OptionIndex { get; set; } = -1; // -1 means use label instead
        public string OptionLabel { get; set; }

        public override string Description =>
            OptionIndex >= 0
                ? $"Select option {OptionIndex} in dropdown '{TargetElement?.text ?? TargetElement?.name ?? SearchQuery}'"
                : $"Select '{OptionLabel}' in dropdown '{TargetElement?.text ?? TargetElement?.name ?? SearchQuery}'";
    }

    /// <summary>
    /// Two-finger swipe action - swipes with two fingers in a direction.
    /// </summary>
    public class TwoFingerSwipeAction : AIAction
    {
        public override string ActionType => "two_finger_swipe";

        public string SearchQuery { get; set; }
        public string Direction { get; set; } // "up", "down", "left", "right"
        public float Distance { get; set; } = 0.2f; // Normalized distance (0-1)
        public float Duration { get; set; } = 0.3f;
        public float FingerSpacing { get; set; } = 0.03f; // Distance between fingers

        public override string Description =>
            $"Two-finger swipe {Direction} on '{TargetElement?.text ?? TargetElement?.name ?? SearchQuery}'";
    }

    /// <summary>
    /// Rotate action - rotates with two fingers around a center point.
    /// </summary>
    public class RotateAction : AIAction
    {
        public override string ActionType => "rotate";

        public string SearchQuery { get; set; }
        public float Degrees { get; set; } = 90f; // Positive = clockwise, negative = counter-clockwise
        public float Duration { get; set; } = 0.5f;
        public float FingerDistance { get; set; } = 0.05f; // Distance from center for each finger

        public override string Description =>
            Degrees > 0
                ? $"Rotate clockwise {Degrees}° on '{TargetElement?.text ?? TargetElement?.name ?? SearchQuery}'"
                : $"Rotate counter-clockwise {-Degrees}° on '{TargetElement?.text ?? TargetElement?.name ?? SearchQuery}'";
    }

    /// <summary>
    /// Result of executing an action.
    /// </summary>
    public class ActionResult
    {
        /// <summary>Whether the action executed successfully</summary>
        public bool Success { get; set; }

        /// <summary>Error message if failed</summary>
        public string Error { get; set; }

        /// <summary>Screenshot taken after the action</summary>
        public byte[] ScreenshotAfter { get; set; }

        /// <summary>Screen hash after the action</summary>
        public string ScreenHashAfter { get; set; }

        /// <summary>Whether the screen/element state changed as a result of this action</summary>
        public bool ScreenChanged { get; set; }

        /// <summary>Time taken to execute in milliseconds</summary>
        public float ExecutionTimeMs { get; set; }

        public static ActionResult Succeeded(byte[] screenshot = null, string hash = null, bool changed = false) => new ActionResult
        {
            Success = true,
            ScreenshotAfter = screenshot,
            ScreenHashAfter = hash,
            ScreenChanged = changed
        };

        public static ActionResult Failed(string error) => new ActionResult
        {
            Success = false,
            Error = error
        };
    }
}
