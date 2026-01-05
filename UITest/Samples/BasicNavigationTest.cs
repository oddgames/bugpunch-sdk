using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ODDGames.UITest.Samples
{
    /// <summary>
    /// Demonstrates basic menu navigation patterns.
    /// This test shows how to wait for UI elements, click buttons, and navigate between screens.
    /// </summary>
    [UITest(
        Scenario = 9001,
        Feature = "Navigation",
        Story = "User can navigate through main menu screens",
        Severity = TestSeverity.Critical,
        Description = "Tests basic menu navigation: opening menus, navigating sub-menus, and returning to main screen",
        Tags = new[] { "sample", "navigation", "menu" }
    )]
    public class BasicNavigationTest : UITestBehaviour
    {
        protected override async UniTask Test()
        {
            // Step 1: Wait for main menu to load
            using (BeginStep("Wait for Main Menu"))
            {
                // Wait for any element that indicates the menu is ready
                // Using wildcards to match common naming patterns
                await Wait(new[] { "*MainMenu*", "*StartScreen*", "*HomeScreen*" }, seconds: 15);
                LogStep("Main menu is ready");
                CaptureScreenshot("main_menu_loaded");
            }

            // Step 2: Navigate to Settings
            using (BeginStep("Open Settings Menu"))
            {
                // Try common button names for settings
                await ClickAny("*Settings*", "*Options*", "*Config*");
                await Wait(1); // Brief wait for animation

                // Verify settings panel opened
                var settingsPanel = await Find<RectTransform>(
                    new[] { "*SettingsPanel*", "*OptionsPanel*", "*SettingsMenu*" },
                    throwIfMissing: false,
                    seconds: 5
                );

                if (settingsPanel != null)
                {
                    LogStep("Settings panel opened successfully");
                    CaptureScreenshot("settings_open");
                }
            }

            // Step 3: Return to main menu
            using (BeginStep("Return to Main Menu"))
            {
                // Try common back/close button patterns
                await ClickAny("*Back*", "*Close*", "*Return*", "*Exit*");
                await Wait(1);
                LogStep("Returned to main menu");
            }

            // Step 4: Test another menu if available (e.g., Credits)
            using (BeginStep("Open Credits or About"))
            {
                var creditsButton = await Find<Component>(
                    new[] { "*Credits*", "*About*", "*Info*" },
                    throwIfMissing: false,
                    seconds: 3
                );

                if (creditsButton != null)
                {
                    await Click(creditsButton.name);
                    await Wait(2);
                    CaptureScreenshot("credits_screen");

                    // Return to main
                    await ClickAny("*Back*", "*Close*", "*Return*");
                    LogStep("Credits screen tested");
                }
                else
                {
                    LogStep("No credits/about button found - skipping");
                }
            }

            CaptureScreenshot("test_complete");
            LogStep("Navigation test completed successfully");
        }
    }
}
