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
            // Wait for any element that indicates the menu is ready
            // Using wildcards to match common naming patterns
            await Wait(new[] { "*MainMenu*", "*StartScreen*", "*HomeScreen*" }, seconds: 15);
            CaptureScreenshot("main_menu_loaded");

            await Wait(1);

            // Step 2: Navigate to Settings
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
                CaptureScreenshot("settings_open");
            }

            await Wait(1);

            // Step 3: Toggle settings options (Sound and Music)
            // Toggle Sound option
            var soundToggle = await Find<Component>(
                new[] { "*SoundToggle*", "*Sound*Toggle*", "*SFX*Toggle*" },
                throwIfMissing: false,
                seconds: 3
            );

            if (soundToggle != null)
            {
                await Click(soundToggle.name);
                await Wait(1);
            }

            // Toggle Music option
            var musicToggle = await Find<Component>(
                new[] { "*MusicToggle*", "*Music*Toggle*", "*BGM*Toggle*" },
                throwIfMissing: false,
                seconds: 3
            );

            if (musicToggle != null)
            {
                await Click(musicToggle.name);
                await Wait(1);
            }

            CaptureScreenshot("settings_toggled");

            await Wait(1);

            // Step 4: Return to main menu
            // Try common back/close button patterns
            await ClickAny("*Back*", "*Close*", "*Return*", "*Exit*");
            await Wait(1);

            await Wait(1);

            // Step 5: Test another menu if available (e.g., Credits)
            var creditsButton = await Find<Component>(
                new[] { "*Credits*", "*About*", "*Info*" },
                throwIfMissing: false,
                seconds: 3
            );

            if (creditsButton != null)
            {
                await Click(creditsButton.name);
                await Wait(1);
                CaptureScreenshot("credits_screen");

                await Wait(1);

                // Return to main
                await ClickAny("*Back*", "*Close*", "*Return*");
                await Wait(1);
            }

            CaptureScreenshot("test_complete");
        }
    }
}
