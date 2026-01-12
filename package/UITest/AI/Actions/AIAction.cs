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

        public string ElementId { get; set; }
        public Vector2? ScreenPosition { get; set; }

        public override string Description =>
            TargetElement != null
                ? $"Click '{TargetElement.text ?? TargetElement.name}'"
                : ScreenPosition.HasValue
                    ? $"Click at ({ScreenPosition.Value.x:F2}, {ScreenPosition.Value.y:F2})"
                    : $"Click element {ElementId}";
    }

    /// <summary>
    /// Type action - types text into an input field.
    /// </summary>
    public class TypeAction : AIAction
    {
        public override string ActionType => "type";

        public string ElementId { get; set; }
        public string Text { get; set; }
        public bool ClearFirst { get; set; } = true;
        public bool PressEnter { get; set; } = false;

        public override string Description =>
            $"Type \"{(Text.Length > 20 ? Text.Substring(0, 17) + "..." : Text)}\" into {TargetElement?.name ?? ElementId}";
    }

    /// <summary>
    /// Drag action - drags from one position to another.
    /// </summary>
    public class DragAction : AIAction
    {
        public override string ActionType => "drag";

        public string FromElementId { get; set; }
        public string ToElementId { get; set; }
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
                    return $"Drag from {FromElement?.name ?? FromElementId} to {ToElement.name}";
                if (!string.IsNullOrEmpty(Direction))
                    return $"Drag {FromElement?.name ?? FromElementId} {Direction} by {Distance}px";
                return $"Drag {FromElement?.name ?? FromElementId}";
            }
        }
    }

    /// <summary>
    /// Scroll action - scrolls a scrollable area.
    /// </summary>
    public class ScrollAction : AIAction
    {
        public override string ActionType => "scroll";

        public string ElementId { get; set; }
        public string Direction { get; set; } // "up", "down", "left", "right"
        public float Amount { get; set; } = 0.3f;

        public override string Description =>
            $"Scroll {TargetElement?.name ?? ElementId} {Direction}";
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

        /// <summary>Time taken to execute in milliseconds</summary>
        public float ExecutionTimeMs { get; set; }

        public static ActionResult Succeeded(byte[] screenshot = null, string hash = null) => new ActionResult
        {
            Success = true,
            ScreenshotAfter = screenshot,
            ScreenHashAfter = hash
        };

        public static ActionResult Failed(string error) => new ActionResult
        {
            Success = false,
            Error = error
        };
    }
}
