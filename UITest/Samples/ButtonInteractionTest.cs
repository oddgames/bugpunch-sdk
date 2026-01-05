using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ODDGames.UITest.Samples
{
    /// <summary>
    /// Demonstrates various button interaction patterns.
    /// Shows clicking, finding by index, repeated clicks, and availability checks.
    /// </summary>
    [UITest(
        Scenario = 9002,
        Feature = "Buttons",
        Story = "User can interact with various button types",
        Severity = TestSeverity.Normal,
        Description = "Tests button clicking, toggles, repeated clicks, and button state verification",
        Tags = new[] { "sample", "buttons", "interaction" }
    )]
    public class ButtonInteractionTest : UITestBehaviour
    {
        protected override async UniTask Test()
        {
            // Step 1: Wait for the sample UI
            using (BeginStep("Wait for Sample UI"))
            {
                await Wait("SampleButtonPanel", seconds: 10);
                LogStep("Button panel loaded");
            }

            // Step 2: Test simple button click
            using (BeginStep("Click Simple Button"))
            {
                await Click("SimpleButton");
                LogStep("Simple button clicked");

                // Verify result label updated
                var resultLabel = await Find<Text>("ResultLabel", throwIfMissing: false);
                if (resultLabel != null)
                {
                    AddParameter("result_text", resultLabel.text);
                }
            }

            // Step 3: Test toggle button
            using (BeginStep("Test Toggle"))
            {
                var toggle = await Find<Toggle>("SampleToggle");
                bool initialState = toggle.isOn;
                AddParameter("initial_toggle_state", initialState.ToString());

                await Click("SampleToggle");
                await Wait(1);

                // Verify toggle state changed
                await WaitFor(() => toggle.isOn != initialState, seconds: 2, description: "toggle state change");
                LogStep($"Toggle state changed from {initialState} to {toggle.isOn}");
            }

            // Step 4: Test clicking by index (for lists of buttons)
            using (BeginStep("Click Button by Index"))
            {
                // Click the second item button (index 1)
                await Click("ItemButton", index: 1);
                LogStep("Clicked second item button");
                CaptureScreenshot("after_index_click");
            }

            // Step 5: Test repeated clicks
            using (BeginStep("Repeated Clicks"))
            {
                // Click increment button 5 times
                await Click("IncrementButton", repeat: 5);
                LogStep("Clicked increment button 5 times");

                var counterLabel = await Find<Text>("CounterLabel", throwIfMissing: false);
                if (counterLabel != null)
                {
                    AddParameter("counter_value", counterLabel.text);
                }
            }

            // Step 6: Test button availability (disabled buttons)
            using (BeginStep("Test Button Availability"))
            {
                // Try to find a disabled button - should fail with Enabled availability
                var disabledButton = await Find<Button>(
                    "DisabledButton",
                    throwIfMissing: false,
                    seconds: 2,
                    availability: Availability.Active | Availability.Enabled
                );

                if (disabledButton == null)
                {
                    LogStep("DisabledButton correctly not found when requiring Enabled availability");
                }

                // Find it without the Enabled requirement
                var buttonExists = await Find<Button>(
                    "DisabledButton",
                    throwIfMissing: false,
                    seconds: 2,
                    availability: Availability.Active
                );

                if (buttonExists != null)
                {
                    LogStep("DisabledButton found when only requiring Active availability");
                }
            }

            // Step 7: Test hold/long press
            using (BeginStep("Test Long Press"))
            {
                var holdButton = await Find<Component>("HoldButton", throwIfMissing: false, seconds: 2);
                if (holdButton != null)
                {
                    await Hold("HoldButton", seconds: 2f);
                    LogStep("Long press completed");
                }
                else
                {
                    LogStep("No hold button found - skipping long press test");
                }
            }

            CaptureScreenshot("button_test_complete");
            LogStep("Button interaction test completed");
        }
    }
}
