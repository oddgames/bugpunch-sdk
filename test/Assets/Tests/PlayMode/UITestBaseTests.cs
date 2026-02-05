using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.TestTools;
using ODDGames.UIAutomation.AI;
using TMPro;

namespace ODDGames.UIAutomation.Tests
{
    /// <summary>
    /// Tests for UITestBase - the base class for UI automation tests.
    /// Tests recovery handler, error suppression, and other features.
    /// </summary>
    [TestFixture]
    public class UITestBaseTests
    {
        private Canvas _canvas;
        private EventSystem _eventSystem;
        private List<GameObject> _createdObjects;

        [SetUp]
        public void SetUp()
        {
            _createdObjects = new List<GameObject>();

            // Reset recovery state
            ActionExecutor.RecoveryHandler = null;
            ActionExecutor.ResetRecoveryTracking();

            // Create EventSystem with Input System module
            var esGO = new GameObject("EventSystem");
            _eventSystem = esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
            _createdObjects.Add(esGO);

            // Create Canvas
            var canvasGO = new GameObject("Canvas");
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            _createdObjects.Add(canvasGO);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _createdObjects)
            {
                if (obj != null)
                    UnityEngine.Object.Destroy(obj);
            }
            _createdObjects.Clear();

            // Clean up
            ActionExecutor.RecoveryHandler = null;
            ActionExecutor.ResetRecoveryTracking();
        }

        #region Recovery Handler Tests

        [Test]
        public void RecoveryHandler_DefaultsToNull()
        {
            Assert.IsNull(ActionExecutor.RecoveryHandler);
        }

        [Test]
        public void RecoveryHandler_CanBeSet()
        {
            ActionExecutor.RecoveryHandler = async (ctx) => new RecoveryResult { Success = false };

            Assert.IsNotNull(ActionExecutor.RecoveryHandler);
        }

        [Test]
        public async Task ResetRecoveryTracking_ClearsState()
        {
            // Set up handler that will record recovery
            ActionExecutor.RecoveryHandler = async (ctx) =>
            {
                return new RecoveryResult { Success = false, Explanation = "Test" };
            };

            // Trigger recovery by failing to find an element
            // Must use ActionExecutor methods (not Search.Resolve) to trigger recovery handler
            // Use 0.5s timeout since recovery is only triggered after 30% of timeout has elapsed
            await ActionExecutor.Click(new Search().Name("Missing"), searchTime: 0.5f);

            Assert.IsTrue(ActionExecutor.RecoveryUsed);
            Assert.AreEqual(1, ActionExecutor.RecoveryCount);

            // Reset
            ActionExecutor.ResetRecoveryTracking();

            Assert.IsFalse(ActionExecutor.RecoveryUsed);
            Assert.AreEqual(0, ActionExecutor.RecoveryCount);
            Assert.IsEmpty(ActionExecutor.RecoveryExplanation);
        }

        [Test]
        public async Task Click_WithoutHandler_DoesNotCallRecovery()
        {
            // No recovery handler set
            ActionExecutor.RecoveryHandler = null;

            Assert.ThrowsAsync<UIAutomationTimeoutException>(async () =>
                await ActionExecutor.Click(new Search().Name("NonExistentElement"), searchTime: 0.5f));

            Assert.IsFalse(ActionExecutor.RecoveryUsed);
        }

        [Test]
        public async Task Click_WithHandler_CallsRecoveryOnFailure()
        {
            bool handlerCalled = false;

            ActionExecutor.RecoveryHandler = async (ctx) =>
            {
                handlerCalled = true;
                Assert.IsNotEmpty(ctx.FailedAction);
                Assert.IsNotEmpty(ctx.ErrorMessage);
                return new RecoveryResult { Success = false, NoBlockerFound = true };
            };

            Assert.ThrowsAsync<UIAutomationTimeoutException>(async () =>
                await ActionExecutor.Click(new Search().Name("NonExistentElement"), searchTime: 0.5f));

            Assert.IsTrue(handlerCalled, "Recovery handler should be called");
            Assert.IsTrue(ActionExecutor.RecoveryUsed);
        }

        [Test]
        public async Task Click_HandlerCanSucceed_WhenElementAppears()
        {
            // Create a button that doesn't exist yet
            Button button = null;

            ActionExecutor.RecoveryHandler = async (ctx) =>
            {
                // Simulate "dismissing a dialog" by creating the element
                button = CreateButton("TargetButton", Vector2.zero);
                await Task.Delay(50);

                // Return success - the element should now be findable
                return new RecoveryResult
                {
                    Success = true,
                    Explanation = "Created missing element"
                };
            };

            await ActionExecutor.Click(new Search().Name("TargetButton"), searchTime: 2f);

            // No exception means click succeeded after recovery created the element
            Assert.IsTrue(ActionExecutor.RecoveryUsed);
            Assert.AreEqual("Created missing element", ActionExecutor.RecoveryExplanation);
        }

        [Test]
        public async Task Click_NoBlockerFound_StopsEarly()
        {
            bool handlerCalled = false;

            ActionExecutor.RecoveryHandler = async (ctx) =>
            {
                handlerCalled = true;
                return new RecoveryResult
                {
                    Success = false,
                    NoBlockerFound = true,
                    Explanation = "No dialog blocking"
                };
            };

            Assert.ThrowsAsync<UIAutomationTimeoutException>(async () =>
                await ActionExecutor.Click(new Search().Name("NonExistent"), searchTime: 0.5f));

            Assert.IsTrue(handlerCalled);
            Assert.IsTrue(ActionExecutor.RecoveryUsed);
        }

        [Test]
        public async Task RecoveryCount_IncrementsOnEachAction()
        {
            int callCount = 0;

            ActionExecutor.RecoveryHandler = async (ctx) =>
            {
                callCount++;
                return new RecoveryResult { Success = false, NoBlockerFound = true };
            };

            // First click
            await ActionExecutor.Click(new Search().Name("Missing1"), searchTime: 0.5f);
            Assert.AreEqual(1, ActionExecutor.RecoveryCount);

            // Second click
            await ActionExecutor.Click(new Search().Name("Missing2"), searchTime: 0.5f);
            Assert.AreEqual(2, ActionExecutor.RecoveryCount);

            Assert.AreEqual(2, callCount);
        }

        #endregion

        #region RecoveryContext Tests

        [Test]
        public void RecoveryContext_HasAllRequiredProperties()
        {
            var context = new RecoveryContext
            {
                FailedAction = "Click(Name(\"Button\"))",
                ErrorMessage = "Element not found"
            };

            Assert.AreEqual("Click(Name(\"Button\"))", context.FailedAction);
            Assert.AreEqual("Element not found", context.ErrorMessage);
        }

        #endregion

        #region RecoveryResult Tests

        [Test]
        public void RecoveryResult_DefaultValues()
        {
            var result = new RecoveryResult();

            Assert.IsFalse(result.Success);
            Assert.IsFalse(result.NoBlockerFound);
            Assert.IsNull(result.Explanation);
        }

        [Test]
        public void RecoveryResult_CanSetAllProperties()
        {
            var result = new RecoveryResult
            {
                Success = true,
                NoBlockerFound = false,
                Explanation = "Dismissed dialog"
            };

            Assert.IsTrue(result.Success);
            Assert.IsFalse(result.NoBlockerFound);
            Assert.AreEqual("Dismissed dialog", result.Explanation);
        }

        #endregion

        #region Handler Registration Tests

        [Test]
        public async Task Search_Resolve_DoesNotCallRecovery()
        {
            // Search.Resolve doesn't call recovery - only ActionExecutor does
            bool called = false;
            ActionExecutor.RecoveryHandler = async (ctx) =>
            {
                called = true;
                return new RecoveryResult();
            };

            var result = await new Search().Name("Missing").Resolve(timeout: 0.1f);

            Assert.IsFalse(result.Found);
            Assert.IsFalse(called, "Search.Resolve should not call recovery handler");
        }

        #endregion

        #region Custom Recovery Handler Tests

        [Test]
        public async Task CustomHandler_CanDismissDialogs()
        {
            // Create a "dialog" that blocks the target
            var dialog = CreatePanel("BlockingDialog");
            var closeButton = CreateButton("CloseButton", Vector2.zero);
            closeButton.transform.SetParent(dialog.transform, false);

            bool dialogDismissed = false;

            ActionExecutor.RecoveryHandler = async (ctx) =>
            {
                // Look for close button
                var close = await new Search().Name("CloseButton").Find(0.5f);
                if (close != null)
                {
                    // "Click" it by destroying the dialog
                    UnityEngine.Object.Destroy(dialog);
                    dialogDismissed = true;
                    await Task.Delay(50);

                    // Now create the target element
                    CreateButton("TargetButton", Vector2.zero);

                    // Return success - ActionExecutor will continue polling and find the element
                    return new RecoveryResult { Success = true, Explanation = "Dismissed dialog" };
                }

                return new RecoveryResult { Success = false, NoBlockerFound = true };
            };

            // Use ActionExecutor.Click instead of Search.Resolve
            await ActionExecutor.Click(new Search().Name("TargetButton"), searchTime: 2f);

            Assert.IsTrue(dialogDismissed, "Dialog should be dismissed");
            // No exception means click succeeded after dialog dismissed
        }

        #endregion

        #region AI Integration Tests

        /// <summary>
        /// Tests that AI recovery actually sends screenshots and hierarchy to the AI.
        /// Requires AITestSettings to have a valid API key configured.
        /// </summary>
        [Test]
        public async Task AIRecovery_SendsScreenshotAndHierarchy_WhenBlockerPresent()
        {
            // Skip if no API key configured
            var settings = AITestSettings.Instance;
            if (settings == null || string.IsNullOrEmpty(settings.geminiApiKey))
            {
                Assert.Ignore("Skipping AI test - no API key configured in AITestSettings");
                return;
            }

            // Set up AI recovery handler
            var provider = settings.CreateModelProvider();
            AINavigator.SetModelProvider(provider);
            ActionExecutor.RecoveryHandler = AINavigator.CreateRecoveryHandler();

            // Create a "blocking" dialog with close button
            var dialog = CreatePanel("BlockingDialog");
            var closeButton = CreateButton("CloseButton", Vector2.zero);
            closeButton.transform.SetParent(dialog.transform, false);

            // Add text to make it clear what the AI should do
            var textGO = new GameObject("DialogText");
            textGO.transform.SetParent(dialog.transform, false);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.sizeDelta = new Vector2(180, 30);
            textRect.anchoredPosition = new Vector2(0, 50);
            var text = textGO.AddComponent<TextMeshProUGUI>();
            text.text = "Click Close to dismiss";
            text.fontSize = 14;
            text.alignment = TextAlignmentOptions.Center;
            _createdObjects.Add(textGO);

            // The target element doesn't exist yet
            // AI should see the dialog and try to dismiss it

            // Use ActionExecutor.Click which triggers recovery
            // This will throw since the target doesn't exist (AI can't create it)
            try
            {
                await ActionExecutor.Click(new Search().Name("TargetElement"), searchTime: 5f);
            }
            catch (UIAutomationTimeoutException)
            {
                // Expected - the target doesn't exist
            }

            // We expect recovery to be attempted (AI analyzed the screen)
            Assert.IsTrue(ActionExecutor.RecoveryUsed, "AI recovery should have been attempted");
            Assert.IsNotEmpty(ActionExecutor.RecoveryExplanation, "AI should provide explanation");

            Debug.Log($"[AI Test] Recovery explanation: {ActionExecutor.RecoveryExplanation}");
        }

        /// <summary>
        /// Tests that AI correctly identifies when there's no blocker (genuine failure).
        /// </summary>
        [Test]
        public async Task AIRecovery_IdentifiesGenuineFailure_WhenNoBlocker()
        {
            // Skip if no API key configured
            var settings = AITestSettings.Instance;
            if (settings == null || string.IsNullOrEmpty(settings.geminiApiKey))
            {
                Assert.Ignore("Skipping AI test - no API key configured in AITestSettings");
                return;
            }

            // Set up AI recovery handler
            var provider = settings.CreateModelProvider();
            AINavigator.SetModelProvider(provider);
            ActionExecutor.RecoveryHandler = AINavigator.CreateRecoveryHandler();

            // Create a clean screen with just a button (no dialogs/blockers)
            var button = CreateButton("ExistingButton", Vector2.zero);

            // Click something that doesn't exist - uses ActionExecutor which triggers recovery
            Assert.ThrowsAsync<UIAutomationTimeoutException>(async () =>
                await ActionExecutor.Click(new Search().Name("NonExistentElement"), searchTime: 5f));

            // AI should recognize there's no blocker and throw
            Assert.IsTrue(ActionExecutor.RecoveryUsed, "AI recovery should have been attempted");

            Debug.Log($"[AI Test] Genuine failure explanation: {ActionExecutor.RecoveryExplanation}");
        }

        /// <summary>
        /// Realistic test scenario: A normal test that expects to click a button,
        /// but a random dialog appears blocking the flow. AI should dismiss it and the test continues.
        ///
        /// Simulates: App shows a "Rate Us" dialog on startup. The test wants to click "Start Game"
        /// but that button doesn't appear until the dialog is dismissed.
        /// </summary>
        [Test]
        public async Task AIRecovery_DismissesDialog_TestContinues()
        {
            // Skip if no API key configured
            var settings = AITestSettings.Instance;
            if (settings == null || string.IsNullOrEmpty(settings.geminiApiKey))
            {
                Assert.Ignore("Skipping AI test - no API key configured in AITestSettings");
                return;
            }

            // Set up AI recovery handler (like UITestBase.ConfigureRecoveryHandler does)
            var provider = settings.CreateModelProvider();
            AINavigator.SetModelProvider(provider);
            ActionExecutor.RecoveryHandler = AINavigator.CreateRecoveryHandler();

            // Track if the button gets clicked
            bool startGameClicked = false;

            // Simulate a blocking dialog (like "Rate Us" that appears before main menu is accessible)
            var dialog = CreatePanel("RateUsDialog");
            var dialogRect = dialog.GetComponent<RectTransform>();
            dialogRect.sizeDelta = new Vector2(300, 200);

            // Add dialog title
            var titleGO = new GameObject("DialogTitle");
            titleGO.transform.SetParent(dialog.transform, false);
            var titleRect = titleGO.AddComponent<RectTransform>();
            titleRect.sizeDelta = new Vector2(280, 40);
            titleRect.anchoredPosition = new Vector2(0, 60);
            var titleText = titleGO.AddComponent<TextMeshProUGUI>();
            titleText.text = "Rate Our App!";
            titleText.fontSize = 18;
            titleText.alignment = TextAlignmentOptions.Center;
            _createdObjects.Add(titleGO);

            // Add "Not Now" button that dismisses dialog AND reveals the main UI
            var closeButton = CreateButton("NotNowButton", new Vector2(0, -50));
            closeButton.transform.SetParent(dialog.transform, false);
            closeButton.onClick.AddListener(() =>
            {
                Debug.Log("[AI Test] Dialog dismissed by AI clicking 'Not Now'!");
                UnityEngine.Object.Destroy(dialog);

                // After dismissing, the "Start Game" button appears (simulating main menu becoming accessible)
                var startButton = CreateButton("StartGameButton", new Vector2(0, 0));
                startButton.onClick.AddListener(() =>
                {
                    startGameClicked = true;
                    Debug.Log("[AI Test] Start Game button was clicked!");
                });

                // Add text to start button
                var startTextGO = new GameObject("StartText");
                startTextGO.transform.SetParent(startButton.transform, false);
                var startTextRect = startTextGO.AddComponent<RectTransform>();
                startTextRect.sizeDelta = new Vector2(100, 30);
                var startText = startTextGO.AddComponent<TextMeshProUGUI>();
                startText.text = "Start Game";
                startText.fontSize = 14;
                startText.alignment = TextAlignmentOptions.Center;
            });

            var closeTextGO = new GameObject("NotNowText");
            closeTextGO.transform.SetParent(closeButton.transform, false);
            var closeTextRect = closeTextGO.AddComponent<RectTransform>();
            closeTextRect.sizeDelta = new Vector2(80, 30);
            var closeText = closeTextGO.AddComponent<TextMeshProUGUI>();
            closeText.text = "Not Now";
            closeText.fontSize = 14;
            closeText.alignment = TextAlignmentOptions.Center;
            _createdObjects.Add(closeTextGO);

            await Task.Yield(); // Let UI settle

            // === THIS IS THE ACTUAL TEST ===
            // The test wants to click "Start Game", but it doesn't exist yet (dialog is blocking).
            // With AI recovery enabled, it should:
            // 1. Try to find "StartGameButton" - fails (doesn't exist)
            // 2. AI analyzes screen, sees "Rate Us" dialog with "Not Now" button
            // 3. AI clicks "Not Now" to dismiss
            // 4. Dialog destroyed, "StartGameButton" created
            // 5. ResolveSearch continues polling and finds "StartGameButton"
            // 6. Click performs the actual click

            await ActionExecutor.Click(new Search().Name("StartGameButton"), searchTime: 20f);

            // Verify the test completed successfully
            Assert.IsTrue(startGameClicked, "Start Game button should have been clicked after AI dismissed dialog");
            Assert.IsTrue(ActionExecutor.RecoveryUsed, "AI recovery should have been used");

            Debug.Log($"[AI Test] SUCCESS! Recovery explanation: {ActionExecutor.RecoveryExplanation}");
        }

        #endregion

        #region Helper Methods

        private Button CreateButton(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100, 50);
            rt.anchoredPosition = position;

            go.AddComponent<Image>();
            var button = go.AddComponent<Button>();

            _createdObjects.Add(go);
            return button;
        }

        private GameObject CreatePanel(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200, 200);
            rt.anchoredPosition = Vector2.zero;

            go.AddComponent<Image>();

            _createdObjects.Add(go);
            return go;
        }

        #endregion
    }
}
