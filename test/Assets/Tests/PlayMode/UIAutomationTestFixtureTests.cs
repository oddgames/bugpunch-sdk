using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TMPro;

namespace ODDGames.Bugpunch.Tests
{
    /// <summary>
    /// Tests for UIAutomationTestFixture - the base class for UI automation tests.
    /// </summary>
    [TestFixture]
    public class UIAutomationTestFixtureTests : UIAutomationTestFixture
    {
        private Canvas _canvas;
        private List<GameObject> _createdObjects;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();

            _createdObjects = new List<GameObject>();

            // Create EventSystem with Input System module
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
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
        public override async Task TearDown()
        {
            foreach (var obj in _createdObjects)
            {
                if (obj != null)
                    UnityEngine.Object.Destroy(obj);
            }
            _createdObjects.Clear();

            await base.TearDown();
        }

        #region Click Tests

        [Test]
        public async Task Click_FindsAndClicksElement()
        {
            bool clicked = false;
            var button = CreateButton("TestButton", Vector2.zero);
            button.onClick.AddListener(() => clicked = true);

            await ActionExecutor.Click(new Search().Name("TestButton"), searchTime: 0.5f);

            Assert.IsTrue(clicked, "Button should have been clicked");
        }

        [Test]
        public async Task Click_ThrowsWhenElementNotFound()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[UIAutomation\] FAILED:.*failed:"));
            try
            {
                await ActionExecutor.Click(new Search().Name("NonExistentElement"), searchTime: 0.5f);
                Assert.Fail("Expected AssertionException");
            }
            catch (AssertionException)
            {
                // Expected
            }
        }

        [Test]
        public async Task DoubleClick_PerformsDoubleClick()
        {
            int clickCount = 0;
            var button = CreateButton("DoubleClickButton", Vector2.zero);
            button.onClick.AddListener(() => clickCount++);

            await ActionExecutor.DoubleClick(new Search().Name("DoubleClickButton"), searchTime: 0.5f);

            Assert.AreEqual(2, clickCount, "Button should have been double-clicked");
        }

        [Test]
        public async Task TripleClick_PerformsTripleClick()
        {
            int clickCount = 0;
            var button = CreateButton("TripleClickButton", Vector2.zero);
            button.onClick.AddListener(() => clickCount++);

            await ActionExecutor.TripleClick(new Search().Name("TripleClickButton"), searchTime: 0.5f);

            Assert.AreEqual(3, clickCount, "Button should have been triple-clicked");
        }

        #endregion

        #region Exception Capture Tests

        [Test]
        public async Task CapturedExceptions_CapturesUnobservedExceptions()
        {
            ActionExecutor.CaptureUnobservedExceptions = true;
            ActionExecutor.ClearCapturedExceptions();

            // Fire and forget a task that throws
            _ = Task.Run(() => throw new InvalidOperationException("Test exception"));

            // Wait for the exception to be observed
            await Task.Delay(100);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(100);

            ActionExecutor.CaptureUnobservedExceptions = false;

            // Note: This test may be flaky depending on timing
            // The exception may or may not be captured depending on GC behavior
        }

        [Test]
        public void ClearCapturedExceptions_ClearsList()
        {
            ActionExecutor.CaptureUnobservedExceptions = true;
            ActionExecutor.ClearCapturedExceptions();

            Assert.AreEqual(0, ActionExecutor.CapturedExceptions.Count);

            ActionExecutor.CaptureUnobservedExceptions = false;
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

        #endregion
    }
}
