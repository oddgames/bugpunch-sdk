using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODDGames.UIAutomation.VisualBuilder
{
    /// <summary>
    /// Types of visual blocks available in the test builder.
    /// </summary>
    public enum BlockType
    {
        Click,
        Type,
        Drag,
        Scroll,
        Wait,
        Assert,
        /// <summary>Long press/hold on an element</summary>
        Hold,
        /// <summary>Double click on an element</summary>
        DoubleClick,
        /// <summary>Triple click on an element (three rapid clicks)</summary>
        TripleClick,
        /// <summary>Set a slider to a specific percentage value</summary>
        SetSlider,
        /// <summary>Set a scrollbar to a specific position</summary>
        SetScrollbar,
        /// <summary>Select an option from a dropdown by index or label</summary>
        ClickDropdown,
        /// <summary>Press a keyboard key</summary>
        KeyPress,
        /// <summary>Hold a keyboard key for a duration (for movement controls)</summary>
        KeyHold,
        /// <summary>Wait for an element to appear</summary>
        WaitForElement,
        /// <summary>Scroll until target element becomes visible</summary>
        ScrollUntil,
        /// <summary>Take a screenshot for comparison</summary>
        Screenshot,
        /// <summary>Log a message to console</summary>
        Log,
        /// <summary>Records raw input events (pointer/key down to up) for playback</summary>
        RecordRawInput,
        /// <summary>Records a single action watching what is pressed/clicked</summary>
        RecordAction,
        /// <summary>Clears the persistent data path</summary>
        ClearPersistentData,
        /// <summary>Loads data file(s) to persistent data path</summary>
        LoadPersistentData,
        /// <summary>Loads a scene</summary>
        LoadScene,
        /// <summary>Calls a custom action by name</summary>
        CustomAction,
        /// <summary>Runs custom C# code (compiled at runtime)</summary>
        RunCode,
        /// <summary>Triggers a Unity Visual Scripting graph event</summary>
        VisualScript,
        /// <summary>Swipe gesture in a direction</summary>
        Swipe,
        /// <summary>Pinch gesture (zoom in/out)</summary>
        Pinch,
        /// <summary>Two-finger swipe gesture</summary>
        TwoFingerSwipe,
        /// <summary>Rotate gesture (two-finger rotation)</summary>
        Rotate,
        /// <summary>ForEach loop start - iterates over matching elements</summary>
        ForEach,
        /// <summary>End of ForEach loop</summary>
        EndForEach
    }

    /// <summary>
    /// Mode for persistent data operations.
    /// </summary>
    public enum PersistentDataMode
    {
        /// <summary>Clear all files in persistent data path</summary>
        ClearAll,
        /// <summary>Clear specific files matching a pattern</summary>
        ClearPattern,
        /// <summary>Copy files from test data folder to persistent data path</summary>
        LoadFromTestData,
        /// <summary>Restore from a named snapshot</summary>
        RestoreSnapshot
    }

    /// <summary>
    /// Assert condition types for Assert blocks.
    /// </summary>
    public enum AssertCondition
    {
        ElementExists,
        ElementNotExists,
        TextEquals,
        TextContains,
        ToggleIsOn,
        ToggleIsOff,
        /// <summary>Slider value equals expected (with variance)</summary>
        SliderValue,
        /// <summary>Dropdown selected index equals expected</summary>
        DropdownIndex,
        /// <summary>Dropdown selected text equals expected</summary>
        DropdownText,
        /// <summary>Input field value equals expected</summary>
        InputValue,
        /// <summary>Custom C# expression returns true</summary>
        CustomExpression
    }

    /// <summary>
    /// Types of recorded input events.
    /// </summary>
    public enum RecordedInputType
    {
        PointerDown,
        PointerUp,
        PointerMove,
        KeyDown,
        KeyUp,
        Scroll
    }

    /// <summary>
    /// A single recorded input event with timing.
    /// </summary>
    [Serializable]
    public class RecordedInputEvent
    {
        /// <summary>Type of input event</summary>
        public RecordedInputType type;

        /// <summary>Time since recording started (seconds)</summary>
        public float timestamp;

        /// <summary>Screen position for pointer events</summary>
        public Vector2 position;

        /// <summary>Key code for key events</summary>
        public KeyCode keyCode;

        /// <summary>Scroll delta for scroll events</summary>
        public Vector2 scrollDelta;

        /// <summary>Pointer button (0=left, 1=right, 2=middle)</summary>
        public int button;
    }

    /// <summary>
    /// A sequence of recorded input events from pointer/key down to up.
    /// Used for RecordRawInput and RecordAction blocks.
    /// </summary>
    [Serializable]
    public class RecordedInputSequence
    {
        /// <summary>All recorded events in order</summary>
        public List<RecordedInputEvent> events = new();

        /// <summary>Total duration of the recording</summary>
        public float duration;

        /// <summary>Description of what was recorded (e.g., "Click on Login Button")</summary>
        public string description;

        /// <summary>The element that was interacted with, if identifiable</summary>
        public ElementSelector targetElement;
    }

    /// <summary>
    /// A single action block in a visual test.
    /// Serializable data that can be converted to AIAction for execution.
    /// </summary>
    [Serializable]
    public class VisualBlock
    {
        /// <summary>Unique identifier for this block</summary>
        public string id;

        /// <summary>The type of action this block performs</summary>
        public BlockType type;

        /// <summary>Whether the block is collapsed in the UI</summary>
        public bool isCollapsed;

        /// <summary>Optional user comment/annotation</summary>
        public string comment;

        // === Target Element ===
        /// <summary>Element selector for Click, Type, Drag (from), Scroll, Assert</summary>
        public ElementSelector target;

        // === Type Block ===
        /// <summary>Text to type (for Type blocks)</summary>
        public string text;

        /// <summary>Whether to clear the field before typing</summary>
        public bool clearFirst = true;

        /// <summary>Whether to press Enter after typing</summary>
        public bool pressEnter;

        // === Drag Block ===
        /// <summary>Target element to drag to (for Drag blocks)</summary>
        public ElementSelector dragTarget;

        /// <summary>Direction for directional drag (up, down, left, right)</summary>
        public string dragDirection;

        /// <summary>Distance as fraction of screen height (0-1) for directional drag</summary>
        public float dragDistance = 0.2f;

        /// <summary>Duration of drag in seconds</summary>
        public float dragDuration = 0.3f;

        // === Scroll Block ===
        /// <summary>Scroll direction (up, down, left, right)</summary>
        public string scrollDirection = "down";

        /// <summary>Scroll amount (0-1)</summary>
        public float scrollAmount = 0.3f;

        // === Wait Block ===
        /// <summary>Wait duration in seconds</summary>
        public float waitSeconds = 1f;

        // === Hold Block ===
        /// <summary>Hold duration in seconds</summary>
        public float holdSeconds = 1f;

        // === SetSlider Block ===
        /// <summary>Target value as percentage (0-100)</summary>
        public float sliderValue = 50f;

        // === SetScrollbar Block ===
        /// <summary>Target scroll position as percentage (0-100)</summary>
        public float scrollbarValue = 50f;

        // === ClickDropdown Block ===
        /// <summary>Index of the dropdown option to select (-1 if using label)</summary>
        public int dropdownIndex = -1;

        /// <summary>Label text of the dropdown option to select (null if using index)</summary>
        public string dropdownLabel;

        // === KeyPress Block ===
        /// <summary>Key to press (e.g., "Escape", "Enter", "Space")</summary>
        public string keyName = "Escape";

        // === KeyHold Block ===
        /// <summary>Keys to hold (comma-separated, e.g., "W", "W,A", "LeftShift,W")</summary>
        public string keyHoldKeys = "W";

        /// <summary>Duration to hold the keys in seconds</summary>
        public float keyHoldDuration = 1f;

        // === WaitForElement Block ===
        /// <summary>Timeout for waiting for element (seconds)</summary>
        public float waitTimeout = 10f;

        // === ScrollUntil Block ===
        /// <summary>Scrollable container to scroll in</summary>
        public ElementSelector scrollContainer;

        /// <summary>Maximum number of scroll attempts before failing</summary>
        public int scrollMaxAttempts = 20;

        // === Screenshot Block ===
        /// <summary>Screenshot filename (without extension)</summary>
        public string screenshotName;

        // === Log Block ===
        /// <summary>Message to log</summary>
        public string logMessage;

        // === Assert Block ===
        /// <summary>Condition to check for Assert blocks</summary>
        public AssertCondition assertCondition;

        /// <summary>Expected value for Assert blocks (TextEquals, TextContains)</summary>
        public string assertExpected;

        /// <summary>Expected float value for SliderValue assert</summary>
        public float assertFloatValue;

        /// <summary>Allowed variance for float comparisons (e.g., 0.01 = 1%)</summary>
        public float assertVariance = 0.01f;

        /// <summary>Expected int value for DropdownIndex assert</summary>
        public int assertIntValue;

        /// <summary>C# expression for CustomExpression assert (should return bool)</summary>
        public string assertExpression;

        // === RecordRawInput / RecordAction Blocks ===
        /// <summary>Recorded input events from pointer/key down to up</summary>
        public RecordedInputSequence recordedInput;

        // === ClearPersistentData / LoadPersistentData Blocks ===
        /// <summary>Mode for persistent data operations</summary>
        public PersistentDataMode persistentDataMode;

        /// <summary>Pattern for file matching (e.g., "*.save", "player_*.json")</summary>
        public string filePattern;

        /// <summary>Name of the test data folder or snapshot to load from</summary>
        public string dataSourceName;

        // === LoadScene Block ===
        /// <summary>Scene name to load</summary>
        public string sceneName;

        /// <summary>Whether to use additive scene loading</summary>
        public bool additiveLoad;

        // === CustomAction Block ===
        /// <summary>Name of the custom action to invoke</summary>
        public string customActionName;

        /// <summary>JSON parameters for the custom action</summary>
        public string customActionParams;

        // === RunCode Block ===
        /// <summary>C# code to execute (compiled at runtime with Emit)</summary>
        public string codeBody;

        /// <summary>Whether the code is async (returns Task)</summary>
        public bool codeIsAsync;

        // === VisualScript Block ===
        /// <summary>GameObject name that has the ScriptMachine component</summary>
        public string visualScriptTarget;

        /// <summary>Event name to trigger on the Visual Scripting graph</summary>
        public string visualScriptEvent;

        /// <summary>Optional string argument to pass with the event</summary>
        public string visualScriptArg;

        // === Swipe Block ===
        /// <summary>Swipe direction (up, down, left, right)</summary>
        public string swipeDirection = "up";

        /// <summary>Swipe distance as fraction of screen (0-1)</summary>
        public float swipeDistance = 0.3f;

        /// <summary>Swipe duration in seconds</summary>
        public float swipeDuration = 0.3f;

        /// <summary>Starting position for swipe (optional, null = center of target or screen center)</summary>
        public Vector2? swipeStartPosition;

        // === Pinch Block ===
        /// <summary>Pinch scale (less than 1 = pinch in/zoom out, greater than 1 = pinch out/zoom in)</summary>
        public float pinchScale = 0.5f;

        /// <summary>Pinch duration in seconds</summary>
        public float pinchDuration = 0.5f;

        /// <summary>Center position for pinch (null = center of target or screen center)</summary>
        public Vector2? pinchCenterPosition;

        // === TwoFingerSwipe Block ===
        /// <summary>Spacing between the two fingers (normalized 0-1)</summary>
        public float twoFingerSpacing = 0.03f;

        // === Rotate Block ===
        /// <summary>Rotation angle in degrees (positive = clockwise, negative = counter-clockwise)</summary>
        public float rotateDegrees = 90f;

        /// <summary>Duration of rotation in seconds</summary>
        public float rotateDuration = 0.5f;

        /// <summary>Distance of fingers from center (normalized 0-1)</summary>
        public float rotateFingerDistance = 0.05f;

        // === ForEach Block ===
        /// <summary>Variable name to store current element (used in nested blocks)</summary>
        public string forEachVariable = "item";

        /// <summary>Maximum iterations (0 = unlimited)</summary>
        public int forEachMaxIterations = 100;

        /// <summary>
        /// Creates a deep copy of this block.
        /// </summary>
        public VisualBlock Clone()
        {
            return new VisualBlock
            {
                id = id,
                type = type,
                isCollapsed = isCollapsed,
                comment = comment,
                target = target?.Clone(),
                text = text,
                clearFirst = clearFirst,
                pressEnter = pressEnter,
                dragTarget = dragTarget?.Clone(),
                dragDirection = dragDirection,
                dragDistance = dragDistance,
                dragDuration = dragDuration,
                scrollDirection = scrollDirection,
                scrollAmount = scrollAmount,
                waitSeconds = waitSeconds,
                holdSeconds = holdSeconds,
                sliderValue = sliderValue,
                scrollbarValue = scrollbarValue,
                dropdownIndex = dropdownIndex,
                dropdownLabel = dropdownLabel,
                keyName = keyName,
                keyHoldKeys = keyHoldKeys,
                keyHoldDuration = keyHoldDuration,
                waitTimeout = waitTimeout,
                scrollContainer = scrollContainer?.Clone(),
                scrollMaxAttempts = scrollMaxAttempts,
                screenshotName = screenshotName,
                logMessage = logMessage,
                assertCondition = assertCondition,
                assertExpected = assertExpected,
                assertFloatValue = assertFloatValue,
                assertVariance = assertVariance,
                assertIntValue = assertIntValue,
                assertExpression = assertExpression,
                recordedInput = recordedInput, // Reference copy - deep copy if needed
                persistentDataMode = persistentDataMode,
                filePattern = filePattern,
                dataSourceName = dataSourceName,
                sceneName = sceneName,
                additiveLoad = additiveLoad,
                customActionName = customActionName,
                customActionParams = customActionParams,
                codeBody = codeBody,
                codeIsAsync = codeIsAsync,
                visualScriptTarget = visualScriptTarget,
                visualScriptEvent = visualScriptEvent,
                visualScriptArg = visualScriptArg,
                swipeDirection = swipeDirection,
                swipeDistance = swipeDistance,
                swipeDuration = swipeDuration,
                swipeStartPosition = swipeStartPosition,
                pinchScale = pinchScale,
                pinchDuration = pinchDuration,
                pinchCenterPosition = pinchCenterPosition,
                twoFingerSpacing = twoFingerSpacing,
                rotateDegrees = rotateDegrees,
                rotateDuration = rotateDuration,
                rotateFingerDistance = rotateFingerDistance,
                forEachVariable = forEachVariable,
                forEachMaxIterations = forEachMaxIterations
            };
        }

        /// <summary>
        /// Gets display text for a selector, returning fallback if null or invalid.
        /// </summary>
        private static string GetSelectorDisplay(ElementSelector selector, string fallback = "(no target)")
        {
            return selector != null && selector.IsValid() ? selector.GetDisplayText() : fallback;
        }

        /// <summary>
        /// Gets a human-readable description of this block.
        /// </summary>
        public string GetDisplayText()
        {
            return type switch
            {
                BlockType.Click => $"Click {GetSelectorDisplay(target)}",
                BlockType.DoubleClick => $"Double-click {GetSelectorDisplay(target)}",
                BlockType.TripleClick => $"Triple-click {GetSelectorDisplay(target)}",
                BlockType.Hold => $"Hold {GetSelectorDisplay(target)} {holdSeconds}s",
                BlockType.Type => $"Type \"{TruncateText(text, 20)}\" into {GetSelectorDisplay(target)}",
                BlockType.SetSlider => $"Set {GetSelectorDisplay(target)} to {sliderValue:F0}%",
                BlockType.SetScrollbar => $"Scroll {GetSelectorDisplay(target)} to {scrollbarValue:F0}%",
                BlockType.ClickDropdown => dropdownIndex >= 0
                    ? $"Select option {dropdownIndex} in {GetSelectorDisplay(target)}"
                    : $"Select \"{TruncateText(dropdownLabel, 15)}\" in {GetSelectorDisplay(target)}",
                BlockType.Drag => dragTarget != null && dragTarget.IsValid()
                    ? $"Drag {GetSelectorDisplay(target, "?")} to {dragTarget.GetDisplayText()}"
                    : $"Drag {GetSelectorDisplay(target, "?")} {dragDirection} {dragDistance:P0}",
                BlockType.Scroll => $"Scroll {scrollDirection} on {GetSelectorDisplay(target)}",
                BlockType.Wait => $"Wait {waitSeconds}s",
                BlockType.WaitForElement => $"Wait for {GetSelectorDisplay(target)} ({waitTimeout}s)",
                BlockType.ScrollUntil => $"Scroll until {GetSelectorDisplay(target)} in {GetSelectorDisplay(scrollContainer, "(auto)")}",
                BlockType.KeyPress => $"Press {keyName}",
                BlockType.KeyHold => $"Hold [{keyHoldKeys}] {keyHoldDuration}s",
                BlockType.Screenshot => $"Screenshot: {screenshotName ?? "auto"}",
                BlockType.Log => $"Log: {TruncateText(logMessage, 20)}",
                BlockType.Assert => GetAssertDisplayText(),
                BlockType.RecordRawInput => GetRecordedInputDisplayText("Raw Input"),
                BlockType.RecordAction => GetRecordedInputDisplayText("Action"),
                BlockType.ClearPersistentData => GetPersistentDataDisplayText("Clear"),
                BlockType.LoadPersistentData => GetPersistentDataDisplayText("Load"),
                BlockType.LoadScene => $"Load Scene: {sceneName ?? "(none)"}{(additiveLoad ? " (additive)" : "")}",
                BlockType.CustomAction => $"Custom: {customActionName ?? "(none)"}",
                BlockType.RunCode => $"Code: {TruncateText(codeBody, 30)}",
                BlockType.VisualScript => $"VS: {visualScriptEvent ?? "(no event)"} on {visualScriptTarget ?? "(any)"}",
                BlockType.Swipe => $"Swipe {swipeDirection} {swipeDistance:P0} on {GetSelectorDisplay(target, "(screen)")}",
                BlockType.Pinch => $"Pinch {(pinchScale < 1 ? "in" : "out")} {pinchScale:F1}x on {GetSelectorDisplay(target, "(screen)")}",
                BlockType.TwoFingerSwipe => $"2-Finger Swipe {swipeDirection} on {GetSelectorDisplay(target, "(screen)")}",
                BlockType.Rotate => $"Rotate {(rotateDegrees >= 0 ? "CW" : "CCW")} {Mathf.Abs(rotateDegrees)}° on {GetSelectorDisplay(target, "(screen)")}",
                BlockType.ForEach => $"ForEach {forEachVariable} in {GetSelectorDisplay(target)}",
                BlockType.EndForEach => "End ForEach",
                _ => type.ToString()
            };
        }

        private string GetPersistentDataDisplayText(string prefix)
        {
            return persistentDataMode switch
            {
                PersistentDataMode.ClearAll => $"{prefix}: Clear all persistent data",
                PersistentDataMode.ClearPattern => $"{prefix}: Clear \"{filePattern ?? "*"}\"",
                PersistentDataMode.LoadFromTestData => $"{prefix}: From test data \"{dataSourceName ?? "default"}\"",
                PersistentDataMode.RestoreSnapshot => $"{prefix}: Restore \"{dataSourceName ?? "default"}\"",
                _ => $"{prefix}: Persistent data"
            };
        }

        private string GetAssertDisplayText()
        {
            var targetText = GetSelectorDisplay(target);
            return assertCondition switch
            {
                AssertCondition.ElementExists => $"Assert {targetText} exists",
                AssertCondition.ElementNotExists => $"Assert {targetText} not exists",
                AssertCondition.TextEquals => $"Assert {targetText} text = \"{TruncateText(assertExpected, 15)}\"",
                AssertCondition.TextContains => $"Assert {targetText} contains \"{TruncateText(assertExpected, 15)}\"",
                AssertCondition.ToggleIsOn => $"Assert {targetText} is ON",
                AssertCondition.ToggleIsOff => $"Assert {targetText} is OFF",
                AssertCondition.SliderValue => $"Assert {targetText} value = {assertFloatValue:F2} (±{assertVariance:P0})",
                AssertCondition.DropdownIndex => $"Assert {targetText} index = {assertIntValue}",
                AssertCondition.DropdownText => $"Assert {targetText} selected = \"{TruncateText(assertExpected, 15)}\"",
                AssertCondition.InputValue => $"Assert {targetText} input = \"{TruncateText(assertExpected, 15)}\"",
                AssertCondition.CustomExpression => $"Assert: {TruncateText(assertExpression, 25)}",
                _ => "Assert"
            };
        }

        private string GetRecordedInputDisplayText(string prefix)
        {
            if (recordedInput == null || recordedInput.events.Count == 0)
                return $"{prefix}: (not recorded)";

            if (!string.IsNullOrEmpty(recordedInput.description))
                return $"{prefix}: {TruncateText(recordedInput.description, 30)}";

            return $"{prefix}: {recordedInput.events.Count} events ({recordedInput.duration:F1}s)";
        }

        private static string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLength ? text : text.Substring(0, maxLength - 3) + "...";
        }

        /// <summary>
        /// Gets the color for this block type (Scratch-inspired).
        /// </summary>
        public Color GetBlockColor()
        {
            return type switch
            {
                BlockType.Click => new Color32(0x4C, 0x97, 0xFF, 0xFF),  // Blue
                BlockType.DoubleClick => new Color32(0x3C, 0x87, 0xEF, 0xFF),  // Darker Blue
                BlockType.TripleClick => new Color32(0x2C, 0x77, 0xDF, 0xFF),  // Even Darker Blue
                BlockType.Hold => new Color32(0x6C, 0xA7, 0xFF, 0xFF),   // Lighter Blue
                BlockType.Type => new Color32(0x99, 0x66, 0xFF, 0xFF),   // Purple
                BlockType.SetSlider => new Color32(0xFF, 0x99, 0x33, 0xFF),   // Orange (slider)
                BlockType.SetScrollbar => new Color32(0x33, 0xCC, 0x99, 0xFF), // Teal (scrollbar)
                BlockType.ClickDropdown => new Color32(0x66, 0xAA, 0xDD, 0xFF), // Sky Blue (dropdown)
                BlockType.Drag => new Color32(0xFF, 0x8C, 0x1A, 0xFF),   // Orange
                BlockType.Scroll => new Color32(0x59, 0xC0, 0x59, 0xFF), // Green
                BlockType.Wait => new Color32(0xFF, 0xBF, 0x00, 0xFF),   // Yellow
                BlockType.WaitForElement => new Color32(0xE0, 0xA0, 0x00, 0xFF), // Dark Yellow
                BlockType.ScrollUntil => new Color32(0x50, 0xB0, 0x50, 0xFF),    // Green (scroll variant)
                BlockType.KeyPress => new Color32(0xA0, 0x80, 0xFF, 0xFF), // Light Purple
                BlockType.KeyHold => new Color32(0x90, 0x70, 0xEF, 0xFF),  // Purple (similar to KeyPress)
                BlockType.Screenshot => new Color32(0x40, 0xC0, 0xC0, 0xFF), // Teal
                BlockType.Log => new Color32(0x80, 0x80, 0x80, 0xFF),    // Gray
                BlockType.Assert => new Color32(0xFF, 0x66, 0x80, 0xFF), // Pink/Red
                BlockType.RecordRawInput => new Color32(0xE0, 0x40, 0x40, 0xFF), // Red
                BlockType.RecordAction => new Color32(0x40, 0xE0, 0xE0, 0xFF),   // Cyan
                BlockType.ClearPersistentData => new Color32(0xD0, 0x60, 0x60, 0xFF), // Dark Red
                BlockType.LoadPersistentData => new Color32(0x60, 0xA0, 0xD0, 0xFF),  // Light Blue
                BlockType.LoadScene => new Color32(0xA0, 0x80, 0xC0, 0xFF),           // Light Purple
                BlockType.CustomAction => new Color32(0x80, 0x80, 0x80, 0xFF),        // Gray
                BlockType.RunCode => new Color32(0x20, 0x80, 0x20, 0xFF),             // Dark Green
                BlockType.VisualScript => new Color32(0x00, 0xA0, 0x60, 0xFF),        // Teal Green (Unity VS color)
                BlockType.Swipe => new Color32(0xFF, 0x70, 0x40, 0xFF),               // Orange-Red (gesture)
                BlockType.Pinch => new Color32(0xFF, 0x50, 0x80, 0xFF),               // Pink-Red (gesture)
                BlockType.TwoFingerSwipe => new Color32(0xFF, 0x60, 0x30, 0xFF),      // Darker Orange-Red (gesture)
                BlockType.Rotate => new Color32(0xFF, 0x40, 0x90, 0xFF),              // Magenta (gesture)
                BlockType.ForEach => new Color32(0xE0, 0xA0, 0x00, 0xFF),             // Gold (loop)
                BlockType.EndForEach => new Color32(0xC0, 0x80, 0x00, 0xFF),          // Darker Gold (loop end)
                _ => Color.gray
            };
        }
    }
}
