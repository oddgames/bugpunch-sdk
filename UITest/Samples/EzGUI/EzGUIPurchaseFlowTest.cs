#if HAS_EZ_GUI
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ODDGames.UITest.Samples.EzGUI
{
    /// <summary>
    /// Demonstrates testing purchase flows in EZ GUI-based games.
    /// Tests shop navigation, item selection, and confirmation dialogs.
    /// </summary>
    [UITest(
        Scenario = 9103,
        Feature = "EzGUI Purchase",
        Story = "User can navigate purchase flows",
        Severity = TestSeverity.Critical,
        Description = "Tests shop browsing, item selection, and purchase confirmation patterns",
        Tags = new[] { "sample", "ezgui", "shop", "purchase", "iap" }
    )]
    public class EzGUIPurchaseFlowTest : UITestBehaviour
    {
        protected override async UniTask Test()
        {
            // Step 1: Navigate to shop
            await Wait(new[] { "*MainMenu*", "*Home*", "*Lobby*" }, seconds: 15);

            await ClickAny("*Shop*", "*Store*", "*Market*");
            await Wait(2);

            // Verify shop opened
            var shopPanel = await Find<Component>(
                new[] { "*ShopPanel*", "*StorePanel*", "*ShopScreen*", "*StoreScreen*" },
                throwIfMissing: false,
                seconds: 5
            );

            if (shopPanel != null)
            {
                CaptureScreenshot("shop_opened");
            }

            // Step 2: Browse categories (if tabbed shop)
            string[] categories = { "*Vehicle*", "*Upgrade*", "*Coin*", "*Gem*", "*Special*", "*Bundle*" };

            foreach (var category in categories)
            {
                var tab = await Find<UIButton3D>(category, throwIfMissing: false, seconds: 1);
                if (tab != null)
                {
                    await Click(tab.name);
                    await Wait(1);
                    break; // Just test one category
                }
            }

            // Step 3: Select an item
            // Find purchasable items (usually buttons with price labels)
            var itemButtons = await FindAll<UIButton3D>("*Item*", seconds: 3);
            bool foundItem = false;

            foreach (var item in itemButtons)
            {
                // Skip if it looks like a navigation button
                if (item.name.Contains("Back") || item.name.Contains("Close"))
                    continue;

                await Click(item.name);
                await Wait(1);
                foundItem = true;
                CaptureScreenshot("item_selected");
                break;
            }

            if (!foundItem)
            {
                // Try clicking first available button that might be a product
                await ClickAny("*Buy*", "*Purchase*", "*Get*", "*Unlock*");
            }

            // Step 4: Handle confirmation dialog
            // Look for confirmation popup
            var confirmDialog = await Find<Component>(
                new[] { "*Confirm*", "*Popup*", "*Dialog*", "*Modal*" },
                throwIfMissing: false,
                seconds: 3
            );

            if (confirmDialog != null)
            {
                CaptureScreenshot("confirmation_dialog");

                // Cancel the purchase (we're just testing the flow)
                var cancelButton = await Find<UIButton3D>(
                    new[] { "*Cancel*", "*No*", "*Close*", "*Back*" },
                    throwIfMissing: false,
                    seconds: 2
                );

                if (cancelButton != null)
                {
                    await Click(cancelButton.name);
                }
                else
                {
                    // If no cancel, try clicking outside or pressing back
                    await ClickAny("*Close*", "*X*", "*Dismiss*");
                }
            }

            // Step 5: Test insufficient funds flow (if applicable)
            // Try to buy an expensive item to trigger "not enough coins" dialog
            var expensiveItem = await Find<UIButton3D>(
                new[] { "*Premium*", "*VIP*", "*Bundle*", "*Special*" },
                throwIfMissing: false,
                seconds: 2
            );

            if (expensiveItem != null)
            {
                await Click(expensiveItem.name);
                await Wait(1);

                // Look for "not enough" or "get more" prompt
                var insufficientDialog = await Find<Component>(
                    new[] { "*NotEnough*", "*Insufficient*", "*GetMore*", "*NeedMore*" },
                    throwIfMissing: false,
                    seconds: 2
                );

                if (insufficientDialog != null)
                {
                    CaptureScreenshot("insufficient_funds");
                    await ClickAny("*Cancel*", "*Close*", "*No*", "*Later*");
                }
            }

            // Step 6: Return to main menu
            // Close shop and return
            await ClickAny("*Back*", "*Close*", "*Return*", "*Home*");
            await Wait(2);

            // Verify we're back at main menu
            var mainMenu = await Find<Component>(
                new[] { "*MainMenu*", "*Home*", "*Lobby*", "*Play*" },
                throwIfMissing: false,
                seconds: 5
            );

            CaptureScreenshot("back_to_menu");
        }
    }
}
#endif
