using System.Text;

namespace ODDGames.UIAutomation.AI
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
            sb.AppendLine("All actions that target elements use the 'search' parameter with a Search API query.");
            sb.AppendLine();
            sb.AppendLine("CLICK ACTIONS:");
            sb.AppendLine("  click - Click a UI element");
            sb.AppendLine("    Params: search (Search query) OR (x, y) coordinates (0-1 normalized)");
            sb.AppendLine("  double_click - Double-click an element");
            sb.AppendLine("    Params: search OR (x, y)");
            sb.AppendLine("  hold - Long press on an element");
            sb.AppendLine("    Params: search, duration (seconds, default: 1.0)");
            sb.AppendLine();
            sb.AppendLine("TEXT INPUT:");
            sb.AppendLine("  type - Type text into an input field");
            sb.AppendLine("    Params: search, text, clear_first (default: true), press_enter (default: false)");
            sb.AppendLine();
            sb.AppendLine("DRAG & SCROLL:");
            sb.AppendLine("  drag - Drag from one element to another or in a direction");
            sb.AppendLine("    Params: from (Search query), to (Search query) OR direction (up/down/left/right), distance, duration");
            sb.AppendLine("  scroll - Scroll a scrollable area");
            sb.AppendLine("    Params: search, direction (up/down/left/right), amount (0-1, default: 0.3)");
            sb.AppendLine();
            sb.AppendLine("TOUCH GESTURES:");
            sb.AppendLine("  swipe - Swipe gesture on an element");
            sb.AppendLine("    Params: search, direction (up/down/left/right), distance (0-1, default: 0.2), duration");
            sb.AppendLine("  two_finger_swipe - Two-finger swipe gesture (e.g., for map panning)");
            sb.AppendLine("    Params: search, direction (up/down/left/right), distance (0-1, default: 0.2), duration, finger_spacing");
            sb.AppendLine("  pinch - Pinch to zoom in/out");
            sb.AppendLine("    Params: search, scale (>1 = zoom in, <1 = zoom out), duration");
            sb.AppendLine("  rotate - Two-finger rotation gesture");
            sb.AppendLine("    Params: search, degrees (positive = clockwise, negative = counter-clockwise), duration");
            sb.AppendLine();
            sb.AppendLine("SLIDERS & SCROLLBARS:");
            sb.AppendLine("  set_slider - Set a slider to a value");
            sb.AppendLine("    Params: search, value (0-1)");
            sb.AppendLine("  set_scrollbar - Set a scrollbar position");
            sb.AppendLine("    Params: search, value (0-1)");
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

            sb.AppendLine("=== SEARCH QUERY FORMAT ===");
            sb.AppendLine("JSON: {\"base\": string, \"value\": string, \"direction\"?: string, \"chain\"?: array}");
            sb.AppendLine();
            sb.AppendLine("--- SEARCH METHODS (use as \"base\" or in \"chain\") ---");
            sb.AppendLine();
            sb.AppendLine("text");
            sb.AppendLine("  value: string - Match visible UI text (TMP_Text, Text). Wildcards: * matches any chars");
            sb.AppendLine("  Base: {\"base\": \"text\", \"value\": \"Submit\"}");
            sb.AppendLine("  Chain: {\"method\": \"text\", \"value\": \"Label*\"}");
            sb.AppendLine();
            sb.AppendLine("name");
            sb.AppendLine("  value: string - Match GameObject name. Wildcards: * matches any chars");
            sb.AppendLine("  Base: {\"base\": \"name\", \"value\": \"CloseButton\"}");
            sb.AppendLine("  Chain: {\"method\": \"name\", \"value\": \"*Panel\"}");
            sb.AppendLine();
            sb.AppendLine("type");
            sb.AppendLine("  value: string - Match component type (Button, Toggle, Slider, Scrollbar, InputField, TMP_InputField, Dropdown, TMP_Dropdown, ScrollRect, Image)");
            sb.AppendLine("  Base: {\"base\": \"type\", \"value\": \"Button\"}");
            sb.AppendLine("  Chain: {\"method\": \"type\", \"value\": \"Slider\"}");
            sb.AppendLine();
            sb.AppendLine("path");
            sb.AppendLine("  value: string - Match hierarchy path (parent/child). Wildcards: * matches any chars");
            sb.AppendLine("  Base: {\"base\": \"path\", \"value\": \"Canvas/Menu/*Button\"}");
            sb.AppendLine("  Chain: {\"method\": \"path\", \"value\": \"*/Settings/*\"}");
            sb.AppendLine();
            sb.AppendLine("tag");
            sb.AppendLine("  value: string - Match Unity tag (exact match, case-insensitive)");
            sb.AppendLine("  Base: {\"base\": \"tag\", \"value\": \"Player\"}");
            sb.AppendLine("  Chain: {\"method\": \"tag\", \"value\": \"UI\"}");
            sb.AppendLine();
            sb.AppendLine("texture");
            sb.AppendLine("  value: string - Match texture/sprite name on Image, RawImage, SpriteRenderer, or Renderer materials. Wildcards: * matches any chars");
            sb.AppendLine("  Base: {\"base\": \"texture\", \"value\": \"icon_*\"}");
            sb.AppendLine("  Chain: {\"method\": \"texture\", \"value\": \"btn_*\"}");
            sb.AppendLine();
            sb.AppendLine("any");
            sb.AppendLine("  value: string - Match text, name, texture, or path. Wildcards: * matches any chars");
            sb.AppendLine("  Base: {\"base\": \"any\", \"value\": \"Settings\"}");
            sb.AppendLine("  Chain: {\"method\": \"any\", \"value\": \"*Menu*\"}");
            sb.AppendLine();
            sb.AppendLine("near");
            sb.AppendLine("  value: string - Find element nearest to text label by Euclidean distance");
            sb.AppendLine("  direction?: string - Optional filter: \"right\", \"left\", \"above\", \"below\"");
            sb.AppendLine("  Base: {\"base\": \"near\", \"value\": \"Username:\", \"direction\": \"right\"}");
            sb.AppendLine("  Chain: {\"method\": \"near\", \"value\": \"Volume\"}");
            sb.AppendLine();
            sb.AppendLine("adjacent");
            sb.AppendLine("  value: string - Find nearest interactable adjacent to text label with alignment scoring");
            sb.AppendLine("  direction: string - Required: \"right\", \"left\", \"above\", \"below\"");
            sb.AppendLine("  Base: {\"base\": \"adjacent\", \"value\": \"Email:\", \"direction\": \"right\"}");
            sb.AppendLine("  Chain: {\"method\": \"adjacent\", \"value\": \"Password:\", \"direction\": \"right\"}");
            sb.AppendLine();
            sb.AppendLine("--- HIERARCHY FILTERS (chain only) ---");
            sb.AppendLine();
            sb.AppendLine("hasParent");
            sb.AppendLine("  value: string - Name pattern of immediate parent. Wildcards supported");
            sb.AppendLine("  OR search: object - Nested SearchQuery for complex matching");
            sb.AppendLine("  {\"method\": \"hasParent\", \"value\": \"Dialog\"}");
            sb.AppendLine("  {\"method\": \"hasParent\", \"search\": {\"base\": \"type\", \"value\": \"ScrollRect\"}}");
            sb.AppendLine();
            sb.AppendLine("hasAncestor");
            sb.AppendLine("  value: string - Name pattern of any ancestor (parent, grandparent, etc.)");
            sb.AppendLine("  OR search: object - Nested SearchQuery for complex matching");
            sb.AppendLine("  {\"method\": \"hasAncestor\", \"value\": \"*Panel\"}");
            sb.AppendLine();
            sb.AppendLine("hasChild");
            sb.AppendLine("  value: string - Name pattern of any immediate child");
            sb.AppendLine("  OR search: object - Nested SearchQuery for complex matching");
            sb.AppendLine("  {\"method\": \"hasChild\", \"value\": \"Icon\"}");
            sb.AppendLine();
            sb.AppendLine("hasDescendant");
            sb.AppendLine("  value: string - Name pattern of any descendant (child, grandchild, etc.)");
            sb.AppendLine("  OR search: object - Nested SearchQuery for complex matching");
            sb.AppendLine("  {\"method\": \"hasDescendant\", \"value\": \"Label\"}");
            sb.AppendLine();
            sb.AppendLine("hasSibling");
            sb.AppendLine("  value: string - Name pattern of any sibling (same parent)");
            sb.AppendLine("  OR search: object - Nested SearchQuery for complex matching");
            sb.AppendLine("  {\"method\": \"hasSibling\", \"value\": \"Checkbox\"}");
            sb.AppendLine();
            sb.AppendLine("--- SPATIAL FILTERS (chain only) ---");
            sb.AppendLine();
            sb.AppendLine("inRegion");
            sb.AppendLine("  value: string - Screen region: TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight");
            sb.AppendLine("  {\"method\": \"inRegion\", \"value\": \"TopRight\"}");
            sb.AppendLine();
            sb.AppendLine("--- VISIBILITY FILTERS (chain only) ---");
            sb.AppendLine();
            sb.AppendLine("visible");
            sb.AppendLine("  No params - Only elements visible on screen (not occluded, alpha > 0)");
            sb.AppendLine("  {\"method\": \"visible\"}");
            sb.AppendLine();
            sb.AppendLine("interactable");
            sb.AppendLine("  No params - Only elements that can receive input (Selectable.interactable = true)");
            sb.AppendLine("  {\"method\": \"interactable\"}");
            sb.AppendLine();
            sb.AppendLine("includeInactive");
            sb.AppendLine("  No params - Include inactive GameObjects (SetActive(false)) in search");
            sb.AppendLine("  {\"method\": \"includeInactive\"}");
            sb.AppendLine();
            sb.AppendLine("includeDisabled");
            sb.AppendLine("  No params - Include disabled Selectables (interactable=false) in search");
            sb.AppendLine("  {\"method\": \"includeDisabled\"}");
            sb.AppendLine();
            sb.AppendLine("--- SELECTION FILTERS (chain only) ---");
            sb.AppendLine();
            sb.AppendLine("first");
            sb.AppendLine("  No params - Return only the first matching element (by screen position)");
            sb.AppendLine("  {\"method\": \"first\"}");
            sb.AppendLine();
            sb.AppendLine("last");
            sb.AppendLine("  No params - Return only the last matching element (by screen position)");
            sb.AppendLine("  {\"method\": \"last\"}");
            sb.AppendLine();
            sb.AppendLine("skip");
            sb.AppendLine("  count: int - Skip the first N matching elements");
            sb.AppendLine("  {\"method\": \"skip\", \"count\": 2}");
            sb.AppendLine();
            sb.AppendLine("take");
            sb.AppendLine("  count: int - Return at most N matching elements");
            sb.AppendLine("  {\"method\": \"take\", \"count\": 3}");
            sb.AppendLine();
            sb.AppendLine("--- TRAVERSAL FILTERS (chain only) ---");
            sb.AppendLine();
            sb.AppendLine("getParent");
            sb.AppendLine("  No params - Navigate from matched element to its parent");
            sb.AppendLine("  {\"method\": \"getParent\"}");
            sb.AppendLine();
            sb.AppendLine("getChild");
            sb.AppendLine("  index: int - Navigate to child at index (0-based)");
            sb.AppendLine("  {\"method\": \"getChild\", \"index\": 0}");
            sb.AppendLine();
            sb.AppendLine("getSibling");
            sb.AppendLine("  offset: int - Navigate to sibling (+1 = next, -1 = previous)");
            sb.AppendLine("  {\"method\": \"getSibling\", \"offset\": 1}");
            sb.AppendLine();
            sb.AppendLine("--- EXAMPLES ---");
            sb.AppendLine("Simple: {\"base\": \"text\", \"value\": \"OK\"}");
            sb.AppendLine("With filter: {\"base\": \"text\", \"value\": \"OK\", \"chain\": [{\"method\": \"hasParent\", \"value\": \"Dialog\"}]}");
            sb.AppendLine("Multi-filter: {\"base\": \"type\", \"value\": \"Button\", \"chain\": [{\"method\": \"hasAncestor\", \"value\": \"Menu\"}, {\"method\": \"first\"}]}");
            sb.AppendLine("Chained search: {\"base\": \"type\", \"value\": \"InputField\", \"chain\": [{\"method\": \"name\", \"value\": \"*Email*\"}]}");
            sb.AppendLine("Near label: {\"base\": \"near\", \"value\": \"Username:\", \"direction\": \"right\"}");
            sb.AppendLine("Nested: {\"base\": \"text\", \"value\": \"Save\", \"chain\": [{\"method\": \"hasParent\", \"search\": {\"base\": \"type\", \"value\": \"ScrollRect\"}}]}");
            sb.AppendLine();

            sb.AppendLine("=== ELEMENT LIST FORMAT ===");
            sb.AppendLine("Each element shows: description [type] and a suggested search query.");
            sb.AppendLine();
            sb.AppendLine("- Use the suggested search JSON or construct your own based on the element info");
            sb.AppendLine("- [type]: button, toggle, slider, input, dropdown, scrollview, draggable, droptarget, clickable");
            sb.AppendLine("- [extra]: Additional state (slider value, toggle state, etc.)");
            sb.AppendLine();
            sb.AppendLine("Use the search JSON format in your 'search' parameter!");
            sb.AppendLine();

            sb.AppendLine("=== GUIDELINES ===");
            sb.AppendLine("1. NAVIGATION FIRST: Check if the element you need exists in the current element list.");
            sb.AppendLine("   - If it doesn't exist (e.g., no Slider on screen but test needs slider), navigate first!");
            sb.AppendLine("   - Look for menu buttons like 'Settings', 'Options', or category names that might lead to your target");
            sb.AppendLine("   - Click navigation buttons to reach the correct screen BEFORE attempting the goal action");
            sb.AppendLine("2. PREFER UNIQUE IDENTIFIERS: When an element has a descriptive unique name (like 'VolumeSlider', 'SubmitButton'), use Name() search - it's most reliable.");
            sb.AppendLine("   - Name({\"base\": \"name\", \"value\": \"VolumeSlider\"}) is better than Adjacent when the name is unique and descriptive");
            sb.AppendLine("   - Path is also reliable when unique: {\"base\": \"path\", \"value\": \"Canvas/Settings/VolumeSlider\"}");
            sb.AppendLine("3. Use Adjacent only when the element name is generic (like 'Slider', 'InputField') and you need the label context");
            sb.AppendLine("   - Adjacent REQUIRES a direction: 'right', 'left', 'above', or 'below' - NEVER omit direction");
            sb.AppendLine("   - Labels are typically 'left' or 'above' the element they describe");
            sb.AppendLine("4. Add chain filters like 'near' or 'hasParent' when multiple similar elements exist");
            sb.AppendLine("5. For drag-and-drop, use 'from' and 'to' parameters with search queries");
            sb.AppendLine("6. Only use (x, y) coordinates when targeting something NOT in the element list");
            sb.AppendLine("7. Think step by step - choose the most direct path to the goal");
            sb.AppendLine("8. If stuck after 2-3 tries, try a completely different approach");
            sb.AppendLine("9. Call pass/fail only when confident - don't give up too early");
            sb.AppendLine();
            sb.AppendLine("ACTION FEEDBACK:");
            sb.AppendLine("- After each action, you'll be told if the 'Screen state changed' or 'Screen state unchanged'");
            sb.AppendLine("- This tracks element positions, values, and visibility - not just visuals");
            sb.AppendLine("- For drags: 'unchanged' may mean the drop wasn't accepted - try a different target");
            sb.AppendLine("- For clicks: 'unchanged' may mean the button did something internal (no visible change)");
            sb.AppendLine();
            sb.AppendLine("WHEN TO REQUEST A SCREENSHOT:");
            sb.AppendLine("- The element list seems sparse but the test goal implies more UI should exist");
            sb.AppendLine("- You need to verify visual state (e.g., colors, images, animations completed)");
            sb.AppendLine("- You're looking for non-interactive content (text labels, icons, graphics)");
            sb.AppendLine("- A menu or popup should have appeared but you don't see expected elements");
            sb.AppendLine("- You're unsure about the current screen layout or navigation state");
            sb.AppendLine("Screenshots help when element discovery alone isn't enough context.");

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
