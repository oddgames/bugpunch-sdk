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
            await Wait("MainMenu", seconds: 10);

            // ==========================================
            // Navigation & Settings
            // ==========================================
            await Click("SettingsButton");
            await Wait("SettingsPanel", seconds: 5);

            await Click("SoundToggle");
            await Click("MusicToggle");
            await Click("BackButton");
            await Wait("MainMenu", seconds: 5);

            // ==========================================
            // Buttons
            // ==========================================
            await Click("ButtonsButton");
            await Wait("SampleButtonPanel", seconds: 5);

            await Click("SimpleButton");
            await Click("SampleToggle");
            await Click("ItemButton", index: 1);
            await Click("IncrementButton", repeat: 3);

            await Click("BackButton");
            await Wait("MainMenu", seconds: 5);

            // ==========================================
            // Forms
            // ==========================================
            await Click("FormsButton");
            await Wait("SampleFormPanel", seconds: 5);

            await TextInput("UsernameInput", "Test");
            await TextInput("EmailInput", "t@t.com");
            await ClickDropdown("CategoryDropdown", 1, throwIfMissing: false, searchTime: 2);
            await ClickSlider("VolumeSlider", 0.75f, throwIfMissing: false, searchTime: 2);
            await Click("AgreeToggle");
            await Click("SubmitButton");

            await Click("BackButton");
            await Wait("MainMenu", seconds: 5);

            // ==========================================
            // Drag and Drop
            // ==========================================
            await Click("DragButton");
            await Wait("SampleDragPanel", seconds: 5);

            // Test scroll
            await Drag("ScrollView", new Vector2(0, -100), duration: 0.3f, throwIfMissing: false);

            // Test drag and drop
            await DragTo("DraggableItem", "DropZone", duration: 0.5f, throwIfMissing: false, searchTime: 2);
            CaptureScreenshot("after_drag_drop");

            await Click("BackButton");
            await Wait("MainMenu", seconds: 5);

            // ==========================================
            // Keyboard
            // ==========================================
            await Click("KeyboardButton");
            await Wait("SampleKeyboardPanel", seconds: 5);

            await Click("KeyboardInput");
            await PressKeys("Hi");
            await PressKey(KeyCode.Tab);
            await PressKey(KeyCode.Escape);

            await Click("BackButton");
            await Wait("MainMenu", seconds: 5);

            CaptureScreenshot("test_complete");
        }
    }
}
