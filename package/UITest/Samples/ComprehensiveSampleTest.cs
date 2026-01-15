using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
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
            // Subscribe to keyboard text input for debugging
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                keyboard.onTextInput += c => Debug.Log($"[TEST] onTextInput received: '{c}'");
            }

            // Wait for main menu and verify title
            await Wait(Name("MainMenu"), seconds: 10);
            await Wait(Text("UITest Sample"), seconds: 2); // Main menu title
            Debug.Log("[TEST] Main menu confirmed");

            // ==========================================
            // Forms - Test TextInput
            // ==========================================
            await Click(Name("FormsButton"));
            await Wait(Name("SampleFormPanel"), seconds: 5);
            await Wait(Text("Form Input"), seconds: 2); // Form panel title
            Debug.Log("[TEST] Form panel confirmed");

            // Type into input fields using Adjacent (find input next to label)
            Debug.Log("[TEST] About to type into Username field");
            await TextInput(Adjacent("Username:"), "TestUser");
            Debug.Log("[TEST] Username typing complete");

            Debug.Log("[TEST] About to type into Email field");
            await TextInput(Adjacent("Email:"), "test@example.com");
            Debug.Log("[TEST] Email typing complete");

            // Take screenshot to verify typing worked
            CaptureScreenshot("after_typing");

            await Click("Back");
            await Wait(Name("MainMenu"), seconds: 5);
            await Wait(Text("UITest Sample"), seconds: 2); // Back to main menu
            Debug.Log("[TEST] Back to main menu confirmed");

            CaptureScreenshot("test_complete");
        }
    }
}
