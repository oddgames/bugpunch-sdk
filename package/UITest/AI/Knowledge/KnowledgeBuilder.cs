using System.Text;

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// Builds hierarchical knowledge context for AI tests.
    /// Combines global, group, and test-specific knowledge.
    /// </summary>
    public static class KnowledgeBuilder
    {
        /// <summary>
        /// Builds the complete context for an AI test.
        /// </summary>
        public static string BuildContext(AITest test, GlobalKnowledge globalKnowledge = null)
        {
            var sb = new StringBuilder();

            // 1. Global knowledge (from settings)
            if (globalKnowledge != null && !string.IsNullOrEmpty(globalKnowledge.context))
            {
                sb.AppendLine("=== PROJECT KNOWLEDGE ===");
                sb.AppendLine(globalKnowledge.context);
                sb.AppendLine();

                // Add common patterns
                if (globalKnowledge.commonPatterns != null && globalKnowledge.commonPatterns.Count > 0)
                {
                    sb.AppendLine("Common patterns:");
                    foreach (var pattern in globalKnowledge.commonPatterns)
                    {
                        sb.AppendLine($"• {pattern}");
                    }
                    sb.AppendLine();
                }
            }

            // 2. Group knowledge (if test has a group)
            if (test.Group != null && !string.IsNullOrEmpty(test.Group.knowledge))
            {
                sb.AppendLine("=== GROUP KNOWLEDGE ===");
                sb.AppendLine(test.Group.knowledge);
                sb.AppendLine();
            }

            // 3. Test-specific knowledge
            if (!string.IsNullOrEmpty(test.knowledge))
            {
                sb.AppendLine("=== TEST KNOWLEDGE ===");
                sb.AppendLine(test.knowledge);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Builds the system prompt for an AI test.
        /// </summary>
        public static string BuildSystemPrompt(AITest test, GlobalKnowledge globalKnowledge = null)
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are an AI testing agent that interacts with a Unity game/application UI.");
            sb.AppendLine("Your goal is to complete the test by performing UI actions.");
            sb.AppendLine();

            sb.AppendLine("=== RESPONSE FORMAT ===");
            sb.AppendLine("You MUST respond with a JSON object containing:");
            sb.AppendLine("- reasoning: Brief explanation of your decision");
            sb.AppendLine("- action: The action name (see available actions below)");
            sb.AppendLine("- Additional parameters based on the action");
            sb.AppendLine();

            sb.AppendLine("=== AVAILABLE ACTIONS ===");
            sb.AppendLine();
            sb.AppendLine("CLICK ACTIONS:");
            sb.AppendLine("  click - Click a UI element");
            sb.AppendLine("    Params: element_id OR (x, y) coordinates (0-1 normalized)");
            sb.AppendLine("  double_click - Double-click an element");
            sb.AppendLine("    Params: element_id OR (x, y)");
            sb.AppendLine("  hold - Long press on an element");
            sb.AppendLine("    Params: element_id, duration (seconds, default: 1.0)");
            sb.AppendLine();
            sb.AppendLine("TEXT INPUT:");
            sb.AppendLine("  type - Type text into an input field");
            sb.AppendLine("    Params: element_id, text, clear_first (default: true), press_enter (default: false)");
            sb.AppendLine();
            sb.AppendLine("DRAG & SCROLL:");
            sb.AppendLine("  drag - Drag from one element to another or in a direction");
            sb.AppendLine("    Params: from_element_id, to_element_id OR direction (up/down/left/right), distance, duration");
            sb.AppendLine("  scroll - Scroll a scrollable area");
            sb.AppendLine("    Params: element_id, direction (up/down/left/right), amount (0-1, default: 0.3)");
            sb.AppendLine();
            sb.AppendLine("TOUCH GESTURES:");
            sb.AppendLine("  swipe - Swipe gesture on an element");
            sb.AppendLine("    Params: element_id, direction (up/down/left/right), distance (0-1, default: 0.2), duration");
            sb.AppendLine("  two_finger_swipe - Two-finger swipe gesture (e.g., for map panning)");
            sb.AppendLine("    Params: element_id, direction (up/down/left/right), distance (0-1, default: 0.2), duration, finger_spacing");
            sb.AppendLine("  pinch - Pinch to zoom in/out");
            sb.AppendLine("    Params: element_id, scale (>1 = zoom in, <1 = zoom out), duration");
            sb.AppendLine("  rotate - Two-finger rotation gesture");
            sb.AppendLine("    Params: element_id, degrees (positive = clockwise, negative = counter-clockwise), duration");
            sb.AppendLine();
            sb.AppendLine("SLIDERS & SCROLLBARS:");
            sb.AppendLine("  set_slider - Set a slider to a value");
            sb.AppendLine("    Params: element_id, value (0-1)");
            sb.AppendLine("  set_scrollbar - Set a scrollbar position");
            sb.AppendLine("    Params: element_id, value (0-1)");
            sb.AppendLine();
            sb.AppendLine("KEYBOARD:");
            sb.AppendLine("  key_press - Press a keyboard key");
            sb.AppendLine("    Params: key (e.g., 'Enter', 'Escape', 'Tab', 'Space')");
            sb.AppendLine("  key_hold - Hold keys together (for combinations like Ctrl+C)");
            sb.AppendLine("    Params: keys (array), duration (default: 0.5)");
            sb.AppendLine();
            sb.AppendLine("CONTROL:");
            sb.AppendLine("  wait - Wait for animations/loading");
            sb.AppendLine("    Params: seconds (0.5-5.0)");
            sb.AppendLine("  screenshot - Request a screenshot for visual context");
            sb.AppendLine("  pass - Declare test PASSED (params: reason)");
            sb.AppendLine("  fail - Declare test FAILED (params: reason)");
            sb.AppendLine();

            sb.AppendLine("=== GUIDELINES ===");
            sb.AppendLine("1. Use the ELEMENT LIST as your primary source - it contains all interactable UI elements");
            sb.AppendLine("2. Click elements by their ID (e.g., 'e1', 'e2') - this is the most reliable method");
            sb.AppendLine("3. Input fields, sliders, toggles, and dropdowns show their ADJACENT LABEL in parentheses");
            sb.AppendLine("   Example: '[e3] input: (placeholder: Enter name) (label left: \"Username:\")' means the input next to 'Username:' label");
            sb.AppendLine("   Use the label to identify which input is which (e.g., type into the input with 'Username:' label)");
            sb.AppendLine("4. If you need visual context, use the 'screenshot' action to request an image");
            sb.AppendLine("5. Only use screen coordinates (x, y) when you see something NOT in the element list");
            sb.AppendLine("6. Think step by step, but be efficient - choose the most direct path");
            sb.AppendLine("7. If stuck, try a different approach or different elements");
            sb.AppendLine("8. Call pass() or fail() only when you're confident the goal is achieved/unachievable");
            sb.AppendLine();

            // Add knowledge hierarchy
            var knowledge = BuildContext(test, globalKnowledge);
            if (!string.IsNullOrEmpty(knowledge))
            {
                sb.AppendLine(knowledge);
            }

            sb.AppendLine("=== TEST GOAL ===");
            sb.AppendLine(test.prompt);
            sb.AppendLine();

            return sb.ToString();
        }

        /// <summary>
        /// Builds the user message for a test step.
        /// </summary>
        public static string BuildStepMessage(
            ScreenState screen,
            int actionNumber,
            int maxActions,
            string stuckSuggestion = null,
            string previousActionResult = null)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"[Step {actionNumber}/{maxActions}]");

            if (!string.IsNullOrEmpty(previousActionResult))
            {
                sb.AppendLine($"Previous action result: {previousActionResult}");
            }

            sb.AppendLine();
            sb.AppendLine("Current screen elements:");
            sb.AppendLine(screen.GetElementListPrompt());

            if (!string.IsNullOrEmpty(stuckSuggestion))
            {
                sb.AppendLine();
                sb.AppendLine($"Note: {stuckSuggestion}");
            }

            sb.AppendLine();
            sb.AppendLine("What action should I take next? Use a tool to interact with the UI, or call pass/fail if the test is complete.");

            return sb.ToString();
        }

        /// <summary>
        /// Builds a recovery message when the AI gets stuck.
        /// </summary>
        public static string BuildRecoveryMessage(
            ScreenState screen,
            string actionSummary,
            string stuckReason)
        {
            var sb = new StringBuilder();

            sb.AppendLine("=== RECOVERY MODE ===");
            sb.AppendLine($"The test appears to be stuck: {stuckReason}");
            sb.AppendLine();

            sb.AppendLine("Actions taken so far:");
            sb.AppendLine(actionSummary);
            sb.AppendLine();

            sb.AppendLine("Current screen elements:");
            sb.AppendLine(screen.GetElementListPrompt());
            sb.AppendLine();

            sb.AppendLine("Please analyze the situation and try a different approach.");
            sb.AppendLine("Consider: Is the current path correct? Should you try different elements?");

            return sb.ToString();
        }
    }
}
