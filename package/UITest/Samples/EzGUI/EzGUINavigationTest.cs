#if HAS_EZ_GUI
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ODDGames.UITest.Samples.EzGUI
{
    /// <summary>
    /// Demonstrates testing navigation flows in EZ GUI-based games.
    /// Typical pattern for MTD-style menu navigation.
    /// </summary>
    [UITest(
        Scenario = 9102,
        Feature = "EzGUI Navigation",
        Story = "User can navigate through EZ GUI menus",
        Severity = TestSeverity.Critical,
        Description = "Tests typical EZ GUI menu navigation patterns as used in MTD",
        Tags = new[] { "sample", "ezgui", "navigation", "mtd" }
    )]
    public class EzGUINavigationTest : UITestBehaviour
    {
        protected override async UniTask Test()
        {
            // Step 1: Wait for main menu
            await Wait(new[] { "*MainMenu*", "*StartScreen*", "*HomeScreen*", "*LobbyScreen*" }, seconds: 20);
            CaptureScreenshot("main_menu");

            // Step 2: Navigate to garage/vehicle selection (MTD pattern)
            var garageButton = await Find<UIButton3D>(
                new[] { "*Garage*", "*Vehicle*", "*Car*", "*Truck*", "*Select*" },
                throwIfMissing: false,
                seconds: 5
            );

            if (garageButton != null)
            {
                await Click(garageButton.name);
                await Wait(2);
                CaptureScreenshot("garage_screen");

                // Navigate back
                await ClickAny("*Back*", "*Close*", "*Return*");
                await Wait(1);
            }

            // Step 3: Test shop/store flow (common in mobile games)
            var shopButton = await Find<UIButton3D>(
                new[] { "*Shop*", "*Store*", "*Buy*", "*Purchase*" },
                throwIfMissing: false,
                seconds: 3
            );

            if (shopButton != null)
            {
                await Click(shopButton.name);
                await Wait(2);
                CaptureScreenshot("shop_screen");

                // Test scrolling through items if there's a scroll view
                var scrollArea = await Find<Component>(
                    new[] { "*Scroll*", "*List*", "*Grid*" },
                    throwIfMissing: false,
                    seconds: 2
                );

                if (scrollArea != null)
                {
                    await Drag(scrollArea.name, new Vector2(0, -200), duration: 0.5f);
                    await Wait(1);
                    await Drag(scrollArea.name, new Vector2(0, 200), duration: 0.5f);
                }

                await ClickAny("*Back*", "*Close*", "*Return*", "*Cancel*");
                await Wait(1);
            }

            // Step 4: Test settings/options flow
            await ClickAny("*Settings*", "*Options*", "*Config*", "*Gear*");
            await Wait(2);
            CaptureScreenshot("settings_screen");

            // Look for common settings toggles
            var soundToggle = await Find<Component>(
                new[] { "*Sound*", "*Audio*", "*Music*", "*SFX*" },
                throwIfMissing: false,
                seconds: 2
            );

            if (soundToggle != null)
            {
                await Click(soundToggle.name);
                await Wait(1);
                await Click(soundToggle.name); // Toggle back
            }

            await ClickAny("*Back*", "*Close*", "*Return*", "*Cancel*", "*Apply*", "*Save*");
            await Wait(1);

            // Step 5: Start a game/level (if available)
            var playButton = await Find<UIButton3D>(
                new[] { "*Play*", "*Start*", "*Race*", "*Begin*", "*Go*" },
                throwIfMissing: false,
                seconds: 3
            );

            if (playButton != null)
            {
                CaptureScreenshot("before_play");
                await Click(playButton.name);

                // Wait for scene transition
                await SceneChange(seconds: 60);

                // Wait for gameplay to stabilize
                await WaitFramerate(averageFps: 25, sampleDuration: 3f, timeout: 30f);
                CaptureScreenshot("gameplay");

                // Simulate brief play session
                await SimulatePlay(seconds: 5);
            }
        }
    }
}
#endif
