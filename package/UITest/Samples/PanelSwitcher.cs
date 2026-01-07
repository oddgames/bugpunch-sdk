using UnityEngine;
using UnityEngine.UI;

namespace ODDGames.UITest.Samples
{
    /// <summary>
    /// Handles panel switching for the comprehensive sample scene.
    /// Automatically wires up navigation buttons to show/hide panels.
    /// </summary>
    public class PanelSwitcher : MonoBehaviour
    {
        private GameObject mainMenu;
        private GameObject settingsPanel;
        private GameObject buttonPanel;
        private GameObject formPanel;
        private GameObject dragPanel;
        private GameObject keyboardPanel;

        private void Awake()
        {
            // Find all panels (including inactive ones)
            mainMenu = FindChildByName(transform, "MainMenu");
            settingsPanel = FindChildByName(transform, "SettingsPanel");
            buttonPanel = FindChildByName(transform, "SampleButtonPanel");
            formPanel = FindChildByName(transform, "SampleFormPanel");
            dragPanel = FindChildByName(transform, "SampleDragPanel");
            keyboardPanel = FindChildByName(transform, "SampleKeyboardPanel");

            // Wire up main menu buttons
            if (mainMenu != null)
            {
                WireButton(mainMenu, "SettingsButton", () => ShowPanel(settingsPanel));
                WireButton(mainMenu, "ButtonsButton", () => ShowPanel(buttonPanel));
                WireButton(mainMenu, "FormsButton", () => ShowPanel(formPanel));
                WireButton(mainMenu, "DragButton", () => ShowPanel(dragPanel));
                WireButton(mainMenu, "KeyboardButton", () => ShowPanel(keyboardPanel));
            }

            // Wire up back buttons
            WireBackButton(settingsPanel);
            WireBackButton(buttonPanel);
            WireBackButton(formPanel);
            WireBackButton(dragPanel);
            WireBackButton(keyboardPanel);

            // Wire up simple button result
            if (buttonPanel != null)
            {
                var simpleButtonGo = FindChildByName(buttonPanel.transform, "SimpleButton");
                var resultLabelGo = FindChildByName(buttonPanel.transform, "ResultLabel");
                if (simpleButtonGo != null && resultLabelGo != null)
                {
                    var simpleButton = simpleButtonGo.GetComponent<Button>();
                    var resultLabel = resultLabelGo.GetComponent<Text>();
                    if (simpleButton != null && resultLabel != null)
                    {
                        simpleButton.onClick.AddListener(() => resultLabel.text = "Button clicked!");
                    }
                }

                // Wire up increment button
                var incrementButtonGo = FindChildByName(buttonPanel.transform, "IncrementButton");
                var counterLabelGo = FindChildByName(buttonPanel.transform, "CounterLabel");
                if (incrementButtonGo != null && counterLabelGo != null)
                {
                    var incrementButton = incrementButtonGo.GetComponent<Button>();
                    var counterLabel = counterLabelGo.GetComponent<Text>();
                    if (incrementButton != null && counterLabel != null)
                    {
                        int counter = 0;
                        incrementButton.onClick.AddListener(() =>
                        {
                            counter++;
                            counterLabel.text = $"Counter: {counter}";
                        });
                    }
                }
            }

            // Wire up form submit
            if (formPanel != null)
            {
                var submitButtonGo = FindChildByName(formPanel.transform, "SubmitButton");
                var successMessage = FindChildByName(formPanel.transform, "SuccessMessage");
                if (submitButtonGo != null && successMessage != null)
                {
                    var submitButton = submitButtonGo.GetComponent<Button>();
                    if (submitButton != null)
                    {
                        submitButton.onClick.AddListener(() => successMessage.SetActive(true));
                    }
                }
            }
        }

        private void WireButton(GameObject panel, string buttonName, UnityEngine.Events.UnityAction action)
        {
            var buttonGo = FindChildByName(panel.transform, buttonName);
            if (buttonGo != null)
            {
                var button = buttonGo.GetComponent<Button>();
                if (button != null)
                {
                    button.onClick.AddListener(action);
                }
            }
        }

        private void WireBackButton(GameObject panel)
        {
            if (panel == null) return;
            var backButtonGo = FindChildByName(panel.transform, "BackButton");
            if (backButtonGo != null)
            {
                var backButton = backButtonGo.GetComponent<Button>();
                if (backButton != null)
                {
                    backButton.onClick.AddListener(() => ShowPanel(mainMenu));
                }
            }
        }

        private void ShowPanel(GameObject panelToShow)
        {
            // Hide all panels
            if (mainMenu != null) mainMenu.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(false);
            if (buttonPanel != null) buttonPanel.SetActive(false);
            if (formPanel != null) formPanel.SetActive(false);
            if (dragPanel != null) dragPanel.SetActive(false);
            if (keyboardPanel != null) keyboardPanel.SetActive(false);

            // Show the requested panel
            if (panelToShow != null) panelToShow.SetActive(true);
        }

        /// <summary>
        /// Finds a child GameObject by name, including inactive children.
        /// Searches recursively through all descendants.
        /// </summary>
        private GameObject FindChildByName(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child.gameObject;

                // Search recursively
                var found = FindChildByName(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
