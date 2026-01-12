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
            sb.AppendLine("- action: One of: click, type, drag, scroll, wait, pass, fail, screenshot");
            sb.AppendLine("- Additional parameters based on the action (see below)");
            sb.AppendLine();

            sb.AppendLine("=== AVAILABLE ACTIONS ===");
            sb.AppendLine("1. click - Click a UI element");
            sb.AppendLine("   Required: element_id (e.g., 'e1') OR x,y coordinates (0-1 normalized)");
            sb.AppendLine("   Example: {\"reasoning\": \"Need to open settings\", \"action\": \"click\", \"element_id\": \"e1\"}");
            sb.AppendLine();
            sb.AppendLine("2. type - Type text into an input field");
            sb.AppendLine("   Required: element_id, text");
            sb.AppendLine("   Example: {\"reasoning\": \"Entering username\", \"action\": \"type\", \"element_id\": \"e2\", \"text\": \"testuser\"}");
            sb.AppendLine();
            sb.AppendLine("3. drag - Drag from one element to another or in a direction");
            sb.AppendLine("   Required: from_element_id");
            sb.AppendLine("   Optional: to_element_id OR direction (up/down/left/right), distance");
            sb.AppendLine();
            sb.AppendLine("4. scroll - Scroll a scrollable area");
            sb.AppendLine("   Required: element_id, direction (up/down/left/right)");
            sb.AppendLine();
            sb.AppendLine("5. wait - Wait for animations or loading");
            sb.AppendLine("   Required: seconds (0.5-5.0)");
            sb.AppendLine();
            sb.AppendLine("6. screenshot - Request a screenshot to see the UI visually");
            sb.AppendLine("   Use when the element list alone is not enough to understand the UI layout");
            sb.AppendLine("   Example: {\"reasoning\": \"Need to see button layout\", \"action\": \"screenshot\"}");
            sb.AppendLine();
            sb.AppendLine("7. pass - Declare test PASSED when the goal is achieved");
            sb.AppendLine("   Required: reason");
            sb.AppendLine();
            sb.AppendLine("8. fail - Declare test FAILED when the goal cannot be achieved");
            sb.AppendLine("   Required: reason");
            sb.AppendLine();

            sb.AppendLine("=== GUIDELINES ===");
            sb.AppendLine("1. Use the ELEMENT LIST as your primary source - it contains all interactable UI elements");
            sb.AppendLine("2. Click elements by their ID (e.g., 'e1', 'e2') - this is the most reliable method");
            sb.AppendLine("3. If you need visual context, use the 'screenshot' action to request an image");
            sb.AppendLine("4. Only use screen coordinates (x, y) when you see something NOT in the element list");
            sb.AppendLine("5. Think step by step, but be efficient - choose the most direct path");
            sb.AppendLine("6. If stuck, try a different approach or different elements");
            sb.AppendLine("7. Call pass() or fail() only when you're confident the goal is achieved/unachievable");
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

            if (!string.IsNullOrEmpty(test.passCondition))
            {
                sb.AppendLine("=== PASS CONDITION ===");
                sb.AppendLine(test.passCondition);
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(test.failCondition))
            {
                sb.AppendLine("=== FAIL CONDITION ===");
                sb.AppendLine(test.failCondition);
                sb.AppendLine();
            }

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
