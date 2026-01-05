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
            using (BeginStep("Wait for Form"))
            {
                await Wait("SampleFormPanel", seconds: 10);
                LogStep("Form panel loaded");
                CaptureScreenshot("form_initial");
            }

            // Step 2: Enter username
            using (BeginStep("Enter Username"))
            {
                await TextInput("UsernameInput", "TestUser123");
                LogStep("Username entered");
            }

            // Step 3: Enter email
            using (BeginStep("Enter Email"))
            {
                await TextInput("EmailInput", "test@example.com");
                LogStep("Email entered");
            }

            // Step 4: Enter password
            using (BeginStep("Enter Password"))
            {
                await TextInput("PasswordInput", "SecurePass!23");
                LogStep("Password entered");
            }

            // Step 5: Interact with dropdown
            using (BeginStep("Select Dropdown Option"))
            {
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
                    LogStep($"Selected dropdown option: {dropdown.options[dropdown.value].text}");
                }
                else
                {
                    LogStep("No dropdown found - skipping");
                }
            }

            // Step 6: Adjust slider
            using (BeginStep("Adjust Slider"))
            {
                var slider = await Find<Slider>("VolumeSlider", throwIfMissing: false);
                if (slider != null)
                {
                    // Drag to increase slider value
                    await Drag("VolumeSlider", new Vector2(100, 0), duration: 0.5f);
                    LogStep($"Slider adjusted to: {slider.value:F2}");
                    AddParameter("slider_value", slider.value.ToString("F2"));
                }
                else
                {
                    LogStep("No slider found - skipping");
                }
            }

            // Step 7: Check agreement checkbox
            using (BeginStep("Accept Terms"))
            {
                var checkbox = await Find<Toggle>("AgreeToggle", throwIfMissing: false);
                if (checkbox != null && !checkbox.isOn)
                {
                    await Click("AgreeToggle");
                    await WaitFor(() => checkbox.isOn, seconds: 2, description: "checkbox checked");
                    LogStep("Terms accepted");
                }
            }

            CaptureScreenshot("form_filled");

            // Step 8: Submit form
            using (BeginStep("Submit Form"))
            {
                await Click("SubmitButton");
                LogStep("Form submitted");

                // Wait for success message or next screen
                var successMessage = await Find<Component>(
                    new[] { "*Success*", "*Confirm*", "*Thank*" },
                    throwIfMissing: false,
                    seconds: 5
                );

                if (successMessage != null)
                {
                    LogStep("Success message displayed");
                    CaptureScreenshot("form_success");
                }
            }

            LogStep("Form input test completed");
        }
    }
}
