using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ODDGames.UITest.Samples
{
    /// <summary>
    /// Demonstrates form input and text field interactions.
    /// Shows text input, form validation, and dropdown selection.
    /// </summary>
    [UITest(
        Scenario = 9003,
        Feature = "Forms",
        Story = "User can fill out and submit forms",
        Severity = TestSeverity.Normal,
        Description = "Tests text input fields, dropdowns, sliders, and form submission",
        Tags = new[] { "sample", "forms", "input" }
    )]
    public class FormInputTest : UITestBehaviour
    {
        protected override async UniTask Test()
        {
            // Step 1: Wait for form to load
            await Wait("SampleFormPanel", seconds: 10);
            CaptureScreenshot("form_initial");

            await Wait(1);

            // Step 2: Enter username
            await TextInput("UsernameInput", "TestUser123");

            await Wait(1);

            // Step 3: Enter email
            await TextInput("EmailInput", "test@example.com");

            await Wait(1);

            // Step 4: Enter password
            await TextInput("PasswordInput", "SecurePass!23");

            await Wait(1);

            // Step 5: Interact with dropdown
            var dropdown = await Find<Dropdown>("CategoryDropdown", throwIfMissing: false);
            if (dropdown != null)
            {
                // Click dropdown to open
                await Click("CategoryDropdown");
                await Wait(1);

                // Click second option (index 1)
                await Click("*Item 1*", throwIfMissing: false);
                if (dropdown.value != 1)
                {
                    // Alternative: click by dropdown item pattern
                    await Click("CategoryDropdown/Dropdown List/Viewport/Content/Item 1*", throwIfMissing: false);
                }
            }

            await Wait(1);

            // Step 6: Adjust slider
            var slider = await Find<Slider>("VolumeSlider", throwIfMissing: false);
            if (slider != null)
            {
                // Drag to increase slider value
                await Drag("VolumeSlider", new Vector2(100, 0), duration: 0.5f);
                AddParameter("slider_value", slider.value.ToString("F2"));
            }

            await Wait(1);

            // Step 7: Check agreement checkbox
            var checkbox = await Find<Toggle>("AgreeToggle", throwIfMissing: false);
            if (checkbox != null && !checkbox.isOn)
            {
                await Click("AgreeToggle");
                await WaitFor(() => checkbox.isOn, seconds: 2, description: "checkbox checked");
            }

            CaptureScreenshot("form_filled");

            await Wait(1);

            // Step 8: Submit form
            await Click("SubmitButton");

            // Wait for success message or next screen
            var successMessage = await Find<Component>(
                new[] { "*Success*", "*Confirm*", "*Thank*" },
                throwIfMissing: false,
                seconds: 5
            );

            if (successMessage != null)
            {
                CaptureScreenshot("form_success");
            }
        }
    }
}
