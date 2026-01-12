using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ODDGames.UITest.Samples
{
    /// <summary>
    /// Sample demonstrating UITest framework capabilities.
    /// Tests navigation, buttons, forms, drag/drop, and keyboard input.
    /// </summary>
    [UITest(
        Scenario = 9000,
        Feature = "Comprehensive",
        Story = "User can interact with all UI element types",
        Severity = TestSeverity.Critical,
        Description = "Tests UITest capabilities: navigation, buttons, forms, drag/drop, keyboard input",
        Tags = new[] { "sample", "comprehensive", "all" }
    )]
    public class ComprehensiveSampleTest : UITestBehaviour
    {
        protected override async UniTask Test()
        {
            // Wait for main menu
            await Wait(Search.ByName("MainMenu"), seconds: 10);

            // ==========================================
            // Navigation & Settings
            // ==========================================
            // Search by name pattern
            await Click(Search.ByName("Settings*"));
            await Wait(Search.ByName("SettingsPanel"), seconds: 5);

            // Search by component type
            await Click(Search.ByType<Toggle>().Name("SoundToggle"));
            await Click(Search.ByType<Toggle>().Name("MusicToggle"));
            await Click("Back");
            await Wait(Search.ByName("MainMenu"), seconds: 5);

            // ==========================================
            // Buttons
            // ==========================================
            // Search by text content
            await Click(Search.ByText("Buttons"));
            await Wait(Search.ByName("SampleButtonPanel"), seconds: 5);

            // Search by sprite name
            await Click(Search.BySprite("btn_*_icon"), throwIfMissing: false, searchTime: 2);
            // Fallback to simple button if sprite not found
            await Click("Simple Button", throwIfMissing: false, searchTime: 1);

            await Click(Search.ByType<Toggle>().Name("SampleToggle"));
            await Click("Item 2", index: 0); // Click second item button by its text
            // Search with component property check
            await Click(Search.ByType<Button>().Name("IncrementButton").With<Button>(b => b.interactable), repeat: 3);

            await Click("Back");
            await Wait(Search.ByName("MainMenu"), seconds: 5);

            // ==========================================
            // Forms
            // ==========================================
            // Search by path pattern
            await Click(Search.ByPath("*Menu*/*FormsButton*"), throwIfMissing: false, searchTime: 2);
            // Fallback to text
            await Click("Forms", throwIfMissing: false, searchTime: 1);
            await Wait(Search.ByName("SampleFormPanel"), seconds: 5);

            // Form elements can be found by their adjacent label text
            await TextInput(Search.ByAdjacent("Username:"), "Test");
            await TextInput(Search.ByAdjacent("Email:"), "t@t.com");
            await ClickDropdown(Search.ByAdjacent("Category:"), 1, throwIfMissing: false, searchTime: 2);
            // Slider control - click at 75% position
            await ClickSlider(Search.ByAdjacent("Volume:"), 0.75f, throwIfMissing: false, searchTime: 2);
            // Search for any toggle that's not already on
            await Click(Search.ByType<Toggle>().Text("I agree*").With<Toggle>(t => !t.isOn), throwIfMissing: false, searchTime: 2);
            // Fallback if already toggled
            await Click("I agree to terms", throwIfMissing: false, searchTime: 1);
            await Click(Search.ByText("Submit"));

            await Click("Back");
            await Wait(Search.ByName("MainMenu"), seconds: 5);

            // ==========================================
            // Drag and Drop
            // ==========================================
            await Click("Drag & Drop");
            await Wait(Search.ByName("SampleDragPanel"), seconds: 5);

            // Test scroll - container elements use ByName
            await Drag(Search.ByName("ScrollView"), new Vector2(0, -100), duration: 0.3f, throwIfMissing: false);

            // Test drag and drop - draggable elements use ByName
            await DragTo(Search.ByName("DraggableItem"), Search.ByName("DropZone"), duration: 0.5f, throwIfMissing: false, searchTime: 2);
            CaptureScreenshot("after_drag_drop");

            await Click("Back");
            await Wait(Search.ByName("MainMenu"), seconds: 5);

            // ==========================================
            // Keyboard
            // ==========================================
            await Click("Keyboard");
            await Wait(Search.ByName("SampleKeyboardPanel"), seconds: 5);

            await Click(Search.ByName("KeyboardInput")); // Input fields are clicked by name
            await PressKeys("Hi");
            await PressKey(KeyCode.Tab);
            await PressKey(KeyCode.Escape);

            // ==========================================
            // Key Hold (for movement/driving controls)
            // ==========================================
            // Hold single key (walk forward)
            await HoldKey(KeyCode.W, 0.5f);

            // Hold multiple keys (diagonal movement)
            await HoldKeys(0.5f, KeyCode.W, KeyCode.A);

            // Using Keys fluent builder for complex sequences
            // Walk forward, then turn left, then sprint forward
            await Keys.Hold(UnityEngine.InputSystem.Key.W).For(0.3f)
                      .Then(UnityEngine.InputSystem.Key.A).For(0.2f)
                      .Then(UnityEngine.InputSystem.Key.LeftShift, UnityEngine.InputSystem.Key.W).For(0.3f);

            await Click("Back");
            await Wait(Search.ByName("MainMenu"), seconds: 5);

            // ==========================================
            // Advanced Input (DoubleClick, Scroll, Swipe, Touch Gestures)
            // ==========================================
            // Search combining name pattern and text content
            await Click(Search.ByName("*Button").Text("Advanced"), throwIfMissing: false, searchTime: 2);
            // Fallback to text
            await Click("Advanced Input", throwIfMissing: false, searchTime: 1);
            var advancedPanel = await Find<RectTransform>(Search.ByName("SampleAdvancedPanel"), throwIfMissing: false, seconds: 2);
            if (advancedPanel != null)
            {
                // Double-click test
                await DoubleClick("Double-Click Me", throwIfMissing: false, searchTime: 2);

                // Scroll wheel test (negative = scroll down) - area elements use ByName
                await Scroll(Search.ByName("ScrollArea"), -120f, throwIfMissing: false, searchTime: 2);

                // Swipe test (duration 1s for visibility)
                await Swipe(Search.ByName("SwipeArea"), SwipeDirection.Left, duration: 1f, throwIfMissing: false, searchTime: 2);
                await Swipe(Search.ByName("SwipeArea"), SwipeDirection.Right, duration: 1f, throwIfMissing: false, searchTime: 2);

                // Touch gesture tests (pinch, rotate, two-finger swipe) - 3D objects use ByName
                await Pinch(Search.ByName("GestureTarget"), 0.5f, duration: 0.5f, throwIfMissing: false, searchTime: 2); // Pinch in
                await Pinch(Search.ByName("GestureTarget"), 2.0f, duration: 0.5f, throwIfMissing: false, searchTime: 2); // Pinch out
                await Rotate(Search.ByName("GestureTarget"), 90f, duration: 0.5f, throwIfMissing: false, searchTime: 2); // Rotate 90 degrees
                await TwoFingerSwipe(Search.ByName("GestureTarget"), SwipeDirection.Up, duration: 0.5f, throwIfMissing: false, searchTime: 2);

                CaptureScreenshot("advanced_input_complete");

                // Navigate back to main menu
                await Click("Back", throwIfMissing: false, searchTime: 2);
            }

            CaptureScreenshot("test_complete");
        }
    }
}
