#if HAS_EZ_GUI
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ODDGames.UITest.Samples.EzGUI
{
    /// <summary>
    /// Demonstrates testing EZ GUI (AnB Software) button interactions.
    /// Uses UIButton3D and AutoSpriteControlBase types from EZ GUI SDK.
    /// </summary>
    [UITest(
        Scenario = 9101,
        Feature = "EzGUI Buttons",
        Story = "User can interact with EZ GUI buttons",
        Severity = TestSeverity.Normal,
        Description = "Tests EZ GUI UIButton3D clicks, 3D button interactions, and sprite controls",
        Tags = new[] { "sample", "ezgui", "buttons", "anb" }
    )]
    public class EzGUIButtonTest : UITestBehaviour
    {
        protected override async UniTask Test()
        {
            // Step 1: Wait for EZ GUI panel to load
            using (BeginStep("Wait for EZ GUI Panel"))
            {
                // Wait for a panel that contains EZ GUI elements
                await Wait(new[] { "*Panel*", "*Menu*", "*Screen*" }, seconds: 15);
                LogStep("EZ GUI panel loaded");
                CaptureScreenshot("ezgui_initial");
            }

            // Step 2: Test UIButton3D click
            using (BeginStep("Click UIButton3D"))
            {
                // UIButton3D components are automatically registered via EzGUIClickableRegistration
                var button3D = await Find<UIButton3D>("*Button*", throwIfMissing: false, seconds: 5);

                if (button3D != null)
                {
                    await Click(button3D.name);
                    LogStep($"Clicked UIButton3D: {button3D.name}");
                    AddParameter("clicked_button", button3D.name);
                }
                else
                {
                    LogStep("No UIButton3D found - trying AutoSpriteControlBase");

                    // Try AutoSpriteControlBase
                    var spriteControl = await Find<AutoSpriteControlBase>("*Button*", throwIfMissing: false, seconds: 3);
                    if (spriteControl != null)
                    {
                        await Click(spriteControl.name);
                        LogStep($"Clicked AutoSpriteControlBase: {spriteControl.name}");
                    }
                }
            }

            // Step 3: Test multiple EZ GUI buttons in sequence
            using (BeginStep("Navigate EZ GUI Menus"))
            {
                // Find all UIButton3D components
                var buttons = await FindAll<UIButton3D>(seconds: 3);
                int buttonCount = 0;

                foreach (var button in buttons)
                {
                    buttonCount++;
                    if (buttonCount > 5) break; // Limit to first 5 buttons
                }

                AddParameter("ezgui_button_count", buttonCount.ToString());
                LogStep($"Found {buttonCount} EZ GUI buttons");

                // Click common navigation buttons
                await ClickAny("*Settings*", "*Options*", "*Menu*");
                await Wait(1);
                await ClickAny("*Back*", "*Close*", "*Return*", "*Cancel*");
            }

            // Step 4: Test button states
            using (BeginStep("Verify Button States"))
            {
                var allButtons = await FindAll<UIButton3D>(seconds: 3);

                foreach (var button in allButtons)
                {
                    // Check if button is interactable
                    bool isEnabled = button.gameObject.activeInHierarchy;
                    if (!isEnabled)
                    {
                        LogStep($"Button '{button.name}' is disabled");
                    }
                }
            }

            CaptureScreenshot("ezgui_test_complete");
            LogStep("EZ GUI button test completed");
        }
    }
}
#endif
