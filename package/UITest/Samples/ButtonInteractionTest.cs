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
            await Wait("SampleButtonPanel", seconds: 10);
            await Wait(1);

            // Step 2: Test simple button click
            await Click("SimpleButton");

            // Verify result label updated
            var resultLabel = await Find<Text>("ResultLabel", throwIfMissing: false);
            if (resultLabel != null)
            {
                AddParameter("result_text", resultLabel.text);
            }
            await Wait(1);

            // Step 3: Test toggle button
            var toggle = await Find<Toggle>("SampleToggle");
            bool initialState = toggle.isOn;
            AddParameter("initial_toggle_state", initialState.ToString());

            await Click("SampleToggle");
            await Wait(1);

            // Verify toggle state changed
            await WaitFor(() => toggle.isOn != initialState, seconds: 2, description: "toggle state change");
            await Wait(1);

            // Step 4: Test clicking by index (for lists of buttons)
            // Click the second item button (index 1)
            await Click("ItemButton", index: 1);
            CaptureScreenshot("after_index_click");
            await Wait(1);

            // Step 5: Test repeated clicks
            // Click increment button 5 times
            await Click("IncrementButton", repeat: 5);

            var counterLabel = await Find<Text>("CounterLabel", throwIfMissing: false);
            if (counterLabel != null)
            {
                AddParameter("counter_value", counterLabel.text);
            }
            await Wait(1);

            // Step 6: Test button availability (disabled buttons)
            // Try to find a disabled button - should fail with Enabled availability
            var disabledButton = await Find<Button>(
                "DisabledButton",
                throwIfMissing: false,
                seconds: 2,
                availability: Availability.Active | Availability.Enabled
            );

            // Find it without the Enabled requirement
            var buttonExists = await Find<Button>(
                "DisabledButton",
                throwIfMissing: false,
                seconds: 2,
                availability: Availability.Active
            );
            await Wait(1);

            // Step 7: Test hold/long press
            var holdButton = await Find<Component>("HoldButton", throwIfMissing: false, seconds: 2);
            if (holdButton != null)
            {
                await Hold("HoldButton", seconds: 2f);
            }
            await Wait(1);

            CaptureScreenshot("button_test_complete");
        }
    }
}
