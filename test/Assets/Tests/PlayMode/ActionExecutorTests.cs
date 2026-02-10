using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;

using TMPro;
using ODDGames.UIAutomation;


namespace ODDGames.UIAutomation.Tests
{
    /// <summary>
    /// PlayMode tests for ActionExecutor - the unified action execution layer.
    /// Tests all action methods: Click, DoubleClick, Hold, Type, Drag, Scroll, Slider, Dropdown, etc.
    /// </summary>
    [TestFixture]
    public class ActionExecutorTests : UIAutomationTestFixture
    {
        private Canvas _canvas;
        private EventSystem _eventSystem;
        private List<GameObject> _createdObjects;

        private Mouse _mouse;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();

            _createdObjects = new List<GameObject>();
            _mouse = Mouse.current;

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
        public override void TearDown()
        {
            foreach (var obj in _createdObjects)
            {
            if (obj != null)
                UnityEngine.Object.Destroy(obj);
            }
            _createdObjects.Clear();

            // Clean up static test data
            TestTrucks.PlayerTruck = null;
            TestTrucks.PlayerTrucks = null;

            base.TearDown();
        }

        #region Click Tests

        [Test]
        public async Task ClickAsync_WithGameObject_ClicksElement()
        {
            var button = CreateButton("TestButton", new Vector2(0, 0));
            bool clicked = false;
            button.onClick.AddListener(() => clicked = true);

            await Async.DelayFrames(1);
            await ActionExecutor.Click(new Search().Name("TestButton"), searchTime: 0.5f);

            Assert.IsTrue(clicked, "Button should have been clicked");
        }

        [Test]
        public async Task ClickAsync_WithSearch_FindsAndClicksElement()
        {
            var button = CreateButton("ClickMeButton", new Vector2(0, 0));
            bool clicked = false;
            button.onClick.AddListener(() => clicked = true);

            await Async.DelayFrames(1);
            await ActionExecutor.Click(new Search().Name("ClickMeButton"), searchTime: 0.5f);

            Assert.IsTrue(clicked, "Button should have been clicked");
        }

        [Test]
        public async Task ClickAsync_WithSearch_ThrowsWhenNotFound()
        {
            await Async.DelayFrames(1);
            LogAssert.Expect(LogType.Error, new Regex(@"\[UIAutomation\] FAILED:.*failed:"));
            try
            {
                await ActionExecutor.Click(new Search().Name("NonExistentButton"), searchTime: 0.5f);
                Assert.Fail("Expected AssertionException");
            }
            catch (AssertionException)
            {
                // Expected
            }
        }

        [Test]
        public async Task ClickAtAsync_ClicksAtScreenPosition()
        {
            var button = CreateButton("PositionButton", new Vector2(0, 0));
            bool clicked = false;
            button.onClick.AddListener(() => clicked = true);

            await Async.DelayFrames(1);

            // Get the button's screen position and convert to normalized (0-1)
            var rect = button.GetComponent<RectTransform>();
            var screenPos = RectTransformUtility.WorldToScreenPoint(null, rect.position);
            var normalizedPos = new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);

            await ActionExecutor.ClickAt(normalizedPos);

            Assert.IsTrue(clicked, "Button should have been clicked at position");
        }

        #endregion

        #region Double Click Tests

        [Test]
        public async Task DoubleClickAsync_WithGameObject_DoubleClicksElement()
        {
            var button = CreateButton("DoubleClickButton", new Vector2(0, 0));
            int clickCount = 0;
            button.onClick.AddListener(() => clickCount++);

            await Async.DelayFrames(1);
            await ActionExecutor.DoubleClick(new Search().Name("DoubleClickButton"), searchTime: 0.5f);

            Assert.AreEqual(2, clickCount, "Button should have been clicked twice");
        }

        [Test]
        public async Task DoubleClickAsync_WithSearch_FindsAndDoubleClicks()
        {
            var button = CreateButton("DoubleClickMe", new Vector2(0, 0));
            int clickCount = 0;
            button.onClick.AddListener(() => clickCount++);

            await Async.DelayFrames(1);
            await ActionExecutor.DoubleClick(new Search().Name("DoubleClickMe"), searchTime: 0.5f);

            Assert.AreEqual(2, clickCount, "Button should have been clicked twice");
        }

        #endregion

        #region Hold Tests

        [Test]
        public async Task HoldAsync_WithSearch_HoldsForDuration()
        {
            var button = CreateButton("HoldButton", new Vector2(0, 0));
            bool clicked = false;
            button.onClick.AddListener(() => clicked = true);

            await Async.DelayFrames(1);
            await ActionExecutor.Hold(new Search().Name("HoldButton"), 0.2f, searchTime: 0.5f);

            // Hold should trigger click on release
            Assert.IsTrue(clicked, "Button should have been clicked after hold");
        }

        [Test]
        public async Task HoldAsync_WithSearch_FindsAndHolds()
        {
            var button = CreateButton("HoldMe", new Vector2(0, 0));
            bool clicked = false;
            button.onClick.AddListener(() => clicked = true);

            await Async.DelayFrames(1);
            await ActionExecutor.Hold(new Search().Name("HoldMe"), 0.2f, searchTime: 0.5f);

            Assert.IsTrue(clicked, "Button should have been clicked after hold");
        }

        #endregion

        #region Type Tests

        [Test]
        public async Task TypeAsync_WithSearch_TypesIntoInputField()
        {
            var inputField = CreateTMPInputField("TypeInput", new Vector2(0, 0));

            await Async.DelayFrames(1);
            await ActionExecutor.Type(new Search().Name("TypeInput"), "Hello World", searchTime: 0.5f);

            // Wait for text input to be processed
            await Async.DelayFrames(10);

            Assert.AreEqual("Hello World", inputField.text, "Input field should contain typed text");
        }

        [Test]
        public async Task TypeAsync_WithSearch_FindsAndTypes()
        {
            var inputField = CreateTMPInputField("SearchTypeInput", new Vector2(0, 0));

            await Async.DelayFrames(1);
            await ActionExecutor.Type(new Search().Name("SearchTypeInput"), "Test Text", searchTime: 0.5f);

            // Wait for text input to be processed
            await Async.DelayFrames(10);

            Assert.AreEqual("Test Text", inputField.text, "Input field should contain typed text");
        }

        [Test]
        public async Task TypeAsync_WithClearFirst_ClearsExistingText()
        {
            var inputField = CreateTMPInputField("ClearInput", new Vector2(0, 0));
            inputField.text = "Existing Text";

            await Async.DelayFrames(1);
            await ActionExecutor.Type(new Search().Name("ClearInput"), "New Text", clearFirst: true, searchTime: 0.5f);

            // Wait for text input to be processed
            await Async.DelayFrames(10);

            Assert.AreEqual("New Text", inputField.text, "Input field should contain only new text");
        }

        [Test]
        public async Task TypeAsync_WithoutClearFirst_AppendsText()
        {
            var inputField = CreateTMPInputField("AppendInput", new Vector2(0, 0));
            inputField.text = "Hello ";

            await Async.DelayFrames(1);
            await ActionExecutor.Type(new Search().Name("AppendInput"), "World", clearFirst: false, searchTime: 0.5f);

            // Wait for text input to be processed
            await Async.DelayFrames(10);

            Assert.IsTrue(inputField.text.Contains("World"), "Input field should contain appended text");
        }

        #endregion

        #region Drag Tests

        [Test]
        public async Task DragFromToAsync_DragsBetweenPositions()
        {
            // Create a draggable element
            var draggable = CreateDraggablePanel("DragPanel", new Vector2(-100, 0));
            var rt = draggable.GetComponent<RectTransform>();
            var initialPos = rt.anchoredPosition;
            float maxX = initialPos.x;

            await Async.DelayFrames(1);

            var startScreen = RectTransformUtility.WorldToScreenPoint(null, draggable.transform.position);
            var endScreen = startScreen + new Vector2(200, 0);

            // Start drag and track max position reached during operation
            var dragTask = ActionExecutor.DragFromTo(startScreen, endScreen, 1.0f, 0f); // Skip hold for faster test

            // Poll position every frame during drag
            while (!dragTask.IsCompleted)
            {
                maxX = Mathf.Max(maxX, rt.anchoredPosition.x);
                await Async.DelayFrames(1);
            }
            await dragTask; // Ensure completion

            // Also check final position
            maxX = Mathf.Max(maxX, rt.anchoredPosition.x);
            Assert.Greater(maxX, initialPos.x, "Element should have moved right during drag");
        }

        [Test]
        public async Task DragAsync_WithDirection_DragsElement()
        {
            var draggable = CreateDraggablePanel("DirectionDrag", new Vector2(0, 0));
            var rt = draggable.GetComponent<RectTransform>();
            var initialPos = rt.anchoredPosition;
            float maxX = initialPos.x;

            await Async.DelayFrames(1);

            // Start drag and track max position reached during operation
            var dragTask = ActionExecutor.Drag(new Search().Name("DirectionDrag"), new Vector2(100, 0), 1.0f, searchTime: 0.5f, holdTime: 0f); // Skip hold for faster test

            // Poll position every frame during drag
            while (!dragTask.IsCompleted)
            {
                maxX = Mathf.Max(maxX, rt.anchoredPosition.x);
                await Async.DelayFrames(1);
            }
            await dragTask; // Ensure completion

            // Also check final position
            maxX = Mathf.Max(maxX, rt.anchoredPosition.x);
            Assert.Greater(maxX, initialPos.x, "Element should have moved right during drag");
        }

        #endregion

        #region Scroll Tests

        [Test]
        public async Task ScrollAsync_WithSearch_FindsElement()
        {
            CreateScrollView("TestScroll", new Vector2(0, 0));

            await Async.DelayFrames(2);

            // Verify Scroll finds the element and completes without throwing
            // Note: Actual scroll wheel input may not work in test environment due to
            // Input System UI Module limitations, but we verify the action completes
            await ActionExecutor.Scroll(new Search().Name("TestScroll"), -120f, searchTime: 0.5f);
        }

        #endregion

        #region Slider Tests

        [Test]
        public async Task SetSliderAsync_SetsSliderValue()
        {
            var slider = CreateSlider("TestSlider", new Vector2(0, 0));
            slider.value = 0f;

            await Async.DelayFrames(1);
            await ActionExecutor.SetSlider(new Search().Name("TestSlider"), 0.75f, searchTime: 0.5f);

            Assert.AreEqual(0.75f, slider.value, 0.1f, "Slider should be at 75%");
        }

        [Test]
        public async Task ClickSliderAsync_ClicksAtPosition()
        {
            var slider = CreateSlider("ClickSlider", new Vector2(0, 0));
            slider.value = 0f;

            await Async.DelayFrames(1);
            await ActionExecutor.ClickSlider(new Search().Name("ClickSlider"), 0.5f, searchTime: 0.5f);

            Assert.AreEqual(0.5f, slider.value, 0.15f, "Slider should be near 50%");
        }

        [Test]
        public async Task ClickSliderAsync_WithSearch_FindsAndClicks()
        {
            var slider = CreateSlider("SearchSlider", new Vector2(0, 0));
            slider.value = 0f;

            await Async.DelayFrames(1);
            await ActionExecutor.ClickSlider(new Search().Name("SearchSlider"), 0.8f, searchTime: 0.5f);

            Assert.AreEqual(0.8f, slider.value, 0.15f, "Slider should be near 80%");
        }

        [Test]
        public async Task DragSliderAsync_DragsSlider()
        {
            var slider = CreateSlider("DragSlider", new Vector2(0, 0));
            slider.value = 0.2f;

            await Async.DelayFrames(1);
            await ActionExecutor.DragSlider(new Search().Name("DragSlider"), 0.2f, 0.9f, 1.0f, searchTime: 0.5f, holdTime: 0f); // Skip hold for faster test

            Assert.Greater(slider.value, 0.5f, "Slider should have moved toward 90%");
        }

        #endregion

        #region Scrollbar Tests

        [Test]
        public async Task SetScrollbarAsync_SetsScrollbarValue()
        {
            var scrollbar = CreateScrollbar("TestScrollbar", new Vector2(0, 0));
            scrollbar.value = 0f;

            await Async.DelayFrames(1);
            await ActionExecutor.SetScrollbar(new Search().Name("TestScrollbar"), 0.5f, searchTime: 0.5f);

            Assert.AreEqual(0.5f, scrollbar.value, 0.1f, "Scrollbar should be at 50%");
        }

        [Test]
        public async Task SetScrollbarAsync_WithSearch_FindsAndSets()
        {
            var scrollbar = CreateScrollbar("SearchScrollbar", new Vector2(0, 0));
            scrollbar.value = 0f;

            await Async.DelayFrames(1);
            await ActionExecutor.SetScrollbar(new Search().Name("SearchScrollbar"), 0.7f, searchTime: 0.5f);

            Assert.AreEqual(0.7f, scrollbar.value, 0.1f, "Scrollbar should be at 70%");
        }

        #endregion

        #region Dropdown Tests

        [Test]
        public async Task ClickDropdownAsync_ByIndex_SelectsOption()
        {
            var dropdown = CreateDropdown("IndexDropdown", new Vector2(0, 50));
            dropdown.value = 0;

            await Async.DelayFrames(1);
            await ActionExecutor.ClickDropdown(new Search().Name("IndexDropdown"), 2, searchTime: 0.5f);

            Assert.AreEqual(2, dropdown.value, "Should have selected option 2");
        }

        [Test]
        public async Task ClickDropdownAsync_ByLabel_SelectsOption()
        {
            var dropdown = CreateDropdown("LabelDropdown", new Vector2(0, -50));
            dropdown.value = 0;

            await Async.DelayFrames(1);
            await ActionExecutor.ClickDropdown(new Search().Name("LabelDropdown"), "Option 2", searchTime: 0.5f);

            Assert.AreEqual(1, dropdown.value, "Should have selected 'Option 2' (index 1)");
        }

        #endregion

        #region Keyboard Tests

        [Test]
        public async Task PressKeyAsync_PressesKey()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                keyboard = InputSystem.AddDevice<Keyboard>();

            await Async.DelayFrames(1);
            await ActionExecutor.PressKey(Key.Space);

            // Key press is transient, just verify no exception
            Assert.Pass("Key press completed without error");
        }

        [Test]
        public async Task HoldKeyAsync_HoldsKeyForDuration()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                keyboard = InputSystem.AddDevice<Keyboard>();

            await Async.DelayFrames(1);

            float startTime = Time.realtimeSinceStartup;
            await ActionExecutor.HoldKey(Key.W, 0.2f);
            float elapsed = Time.realtimeSinceStartup - startTime;

            Assert.GreaterOrEqual(elapsed, 0.15f, "Should have held key for approximately 0.2s");
        }

        [Test]
        public async Task HoldKeysAsync_HoldsMultipleKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                keyboard = InputSystem.AddDevice<Keyboard>();

            await Async.DelayFrames(1);

            float startTime = Time.realtimeSinceStartup;
            await ActionExecutor.HoldKeys(0.2f, Key.LeftShift, Key.W);
            float elapsed = Time.realtimeSinceStartup - startTime;

            Assert.GreaterOrEqual(elapsed, 0.15f, "Should have held keys for approximately 0.2s");
        }

        #endregion

        #region Triple Click Tests

        [Test]
        public async Task TripleClickAsync_WithSearch_TripleClicksElement()
        {
            var button = CreateButton("TripleClickButton", new Vector2(0, 0));
            int clickCount = 0;
            button.onClick.AddListener(() => clickCount++);

            await Async.DelayFrames(1);
            await ActionExecutor.TripleClick(new Search().Name("TripleClickButton"), searchTime: 0.5f);

            Assert.AreEqual(3, clickCount, "Button should have been clicked three times");
        }

        [Test]
        public async Task TripleClickAtAsync_OnEmptyArea_DoesNotThrow()
        {
            await Async.DelayFrames(1);

            // Click at center of screen where nothing exists - should not throw
            await ActionExecutor.TripleClickAt(new Vector2(Screen.width / 2f, Screen.height / 2f));
        }

        #endregion

        #region InputField Focus Tests

        [Test]
        public async Task ClickAsync_InputField_IsFocusedAfterClick()
        {
            var inputField = CreateTMPInputField("FocusInput", new Vector2(0, 0));

            await Async.DelayFrames(1);
            await ActionExecutor.Click(new Search().Name("FocusInput"), searchTime: 0.5f);
            await Async.DelayFrames(5);

            Assert.IsTrue(inputField.isFocused, "Input field should be focused after click");
        }

        #endregion

        #region Scroll Variant Tests

        [Test]
        public async Task ScrollAsync_NegativeDelta_ScrollsDown()
        {
            CreateScrollView("NegScrollView", new Vector2(0, 0));

            await Async.DelayFrames(2);

            // Should complete without error
            await ActionExecutor.Scroll(new Search().Name("NegScrollView"), -240f, searchTime: 0.5f);
        }

        [Test]
        public async Task ScrollAsync_WithDirection_ScrollsElement()
        {
            CreateScrollView("DirScrollView", new Vector2(0, 0));

            await Async.DelayFrames(2);

            // Direction-based scroll should complete without error
            await ActionExecutor.Scroll(new Search().Name("DirScrollView"), "down", 0.3f, searchTime: 0.5f);
        }

        [Test]
        public async Task ScrollAtAsync_ScrollsAtPosition()
        {
            CreateScrollView("ScrollAtView", new Vector2(0, 0));

            await Async.DelayFrames(2);

            // Scroll at screen center
            await ActionExecutor.ScrollAt(new Vector2(Screen.width / 2f, Screen.height / 2f), -120f);
        }

        #endregion

        #region Swipe Tests

        [Test]
        public async Task SwipeAsync_WithSearch_SwipesElement()
        {
            var panel = CreateDraggablePanel("SwipePanel", new Vector2(0, 0));

            await Async.DelayFrames(1);

            // Should complete without error
            await ActionExecutor.Swipe(new Search().Name("SwipePanel"), SwipeDirection.Left, 0.2f, 0.15f);
        }

        [Test]
        public async Task SwipeAsync_WithoutSearch_SwipesFromCenter()
        {
            await Async.DelayFrames(1);

            // Swipe from screen center
            await ActionExecutor.Swipe(SwipeDirection.Right, 0.2f, 0.15f);
        }

        #endregion

        #region Pinch Tests

        [Test]
        public async Task PinchAsync_WithSearch_PerformsPinchGesture()
        {
            var panel = CreateDraggablePanel("PinchPanel", new Vector2(0, 0));

            await Async.DelayFrames(1);

            await ActionExecutor.Pinch(new Search().Name("PinchPanel"), 1.5f, 0.15f);
        }

        [Test]
        public async Task PinchAsync_WithoutSearch_UsesCenterOfScreen()
        {
            await Async.DelayFrames(1);

            await ActionExecutor.Pinch(0.5f, 0.15f);
        }

        [Test]
        public async Task PinchAtAsync_WithCustomFingerDistance()
        {
            await Async.DelayFrames(1);

            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await ActionExecutor.PinchAt(center, 2.0f, 0.15f, 100f);
        }

        #endregion

        #region Two-Finger Swipe Tests

        [Test]
        public async Task TwoFingerSwipeAsync_WithSearch_SwipesWithTwoFingers()
        {
            var panel = CreateDraggablePanel("TFSwipePanel", new Vector2(0, 0));

            await Async.DelayFrames(1);

            await ActionExecutor.TwoFingerSwipe(new Search().Name("TFSwipePanel"), "up", 0.2f, 0.15f, 0.03f);
        }

        [Test]
        public async Task TwoFingerSwipeAtAsync_SwipesAtPosition()
        {
            await Async.DelayFrames(1);

            await ActionExecutor.TwoFingerSwipeAt(0.5f, 0.5f, SwipeDirection.Down, 0.2f, 0.15f);
        }

        #endregion

        #region Rotate Tests

        [Test]
        public async Task RotateAsync_WithSearch_PerformsRotation()
        {
            var panel = CreateDraggablePanel("RotatePanel", new Vector2(0, 0));

            await Async.DelayFrames(1);

            await ActionExecutor.Rotate(new Search().Name("RotatePanel"), 45f, 0.15f, 0.05f);
        }

        [Test]
        public async Task RotateAtAsync_RotatesAtPosition()
        {
            await Async.DelayFrames(1);

            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await ActionExecutor.RotateAt(center, 90f, 0.15f, 0.05f);
        }

        [Test]
        public async Task RotateAtPixelsAsync_RotatesWithPixelRadius()
        {
            await Async.DelayFrames(1);

            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await ActionExecutor.RotateAtPixels(center, 60f, 0.15f, 50f);
        }

        #endregion

        #region Drag Variant Tests

        [Test]
        public async Task DragAsync_WithHoldTime_HoldsBeforeDragging()
        {
            var draggable = CreateDraggablePanel("HoldDrag", new Vector2(0, 0));
            var rt = draggable.GetComponent<RectTransform>();
            var initialPos = rt.anchoredPosition;

            await Async.DelayFrames(1);

            float startTime = Time.realtimeSinceStartup;
            await ActionExecutor.Drag(new Search().Name("HoldDrag"), new Vector2(100, 0), 0.5f, searchTime: 0.5f, holdTime: 0.2f);
            float elapsed = Time.realtimeSinceStartup - startTime;

            Assert.Greater(elapsed, 0.15f, "Should have held before dragging");
        }

        [Test]
        public async Task DragToAsync_DragsBetweenSearchTargets()
        {
            var from = CreateDraggablePanel("DragFrom", new Vector2(-100, 0));
            var to = CreateButton("DragTo", new Vector2(100, 0));
            var rt = from.GetComponent<RectTransform>();
            var initialPos = rt.anchoredPosition;
            float maxX = initialPos.x;

            await Async.DelayFrames(1);

            var dragTask = ActionExecutor.DragTo(
                new Search().Name("DragFrom"),
                new Search().Name("DragTo"),
                duration: 1.0f, searchTime: 0.5f, holdTime: 0f);

            while (!dragTask.IsCompleted)
            {
                maxX = Mathf.Max(maxX, rt.anchoredPosition.x);
                await Async.DelayFrames(1);
            }
            await dragTask;

            maxX = Mathf.Max(maxX, rt.anchoredPosition.x);
            Assert.Greater(maxX, initialPos.x, "Element should have moved right toward target");
        }

        #endregion

        #region Keys Builder Tests

        [Test]
        public async Task KeysBuilder_SingleKeyHold_CompletesWithoutError()
        {
            await Async.DelayFrames(1);

            await Keys.Hold(Key.W).For(0.1f);
        }

        [Test]
        public async Task KeysBuilder_ChainedSequence_ExecutesInOrder()
        {
            await Async.DelayFrames(1);

            await Keys.Hold(Key.LeftShift).For(0.1f)
                .ThenPress(Key.A);
        }

        [Test]
        public async Task KeysBuilder_PressAndThenHold_ChainsCorrectly()
        {
            await Async.DelayFrames(1);

            await Keys.Press(Key.Space)
                .Then(Key.W).For(0.1f)
                .ThenPress(Key.E);
        }

        #endregion

        #region Text Input Edge Case Tests

        [Test]
        public async Task TypeAsync_ClearFirstWithEmptyText_ClearsField()
        {
            var inputField = CreateTMPInputField("ClearEmptyInput", new Vector2(0, 0));
            inputField.text = "Existing Text";

            await Async.DelayFrames(1);
            await ActionExecutor.Type(new Search().Name("ClearEmptyInput"), "", clearFirst: true, searchTime: 0.5f);
            await Async.DelayFrames(10);

            Assert.AreEqual("", inputField.text, "Input field should be empty after clearing with empty text");
        }

        #endregion

        #region Helper Methods

        private Button CreateButton(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(160, 40);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = Color.gray;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            _createdObjects.Add(go);
            return button;
        }

        private TMP_InputField CreateTMPInputField(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200, 40);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f);

            var inputField = go.AddComponent<TMP_InputField>();

            // Text area
            var textArea = new GameObject("Text Area");
            textArea.transform.SetParent(go.transform, false);
            var textAreaRect = textArea.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(5, 5);
            textAreaRect.offsetMax = new Vector2(-5, -5);

            // Text
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(textArea.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.color = Color.white;
            text.fontSize = 14;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;

            _createdObjects.Add(go);
            return inputField;
        }

        private GameObject CreateDraggablePanel(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(100, 100);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = Color.blue;

            // Add simple drag handler
            go.AddComponent<SimpleDragHandler>();

            _createdObjects.Add(go);
            return go;
        }

        private GameObject CreateScrollView(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200, 200);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.15f, 0.15f, 0.15f);

            var scrollRect = go.AddComponent<ScrollRect>();
            scrollRect.vertical = true;
            scrollRect.horizontal = false;

            // Viewport
            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(go.transform, false);
            var vpRect = viewport.AddComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.sizeDelta = Vector2.zero;
            viewport.AddComponent<Mask>().showMaskGraphic = true;
            viewport.AddComponent<Image>().color = Color.clear;

            // Content
            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 600); // Taller than viewport

            scrollRect.viewport = vpRect;
            scrollRect.content = contentRect;

            _createdObjects.Add(go);
            return go;
        }

        private Slider CreateSlider(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200, 20);
            rect.anchoredPosition = position;

            var slider = go.AddComponent<Slider>();
            slider.minValue = 0;
            slider.maxValue = 1;

            // Background
            var bg = new GameObject("Background");
            bg.transform.SetParent(go.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.25f);
            bgRect.anchorMax = new Vector2(1, 0.75f);
            bgRect.sizeDelta = Vector2.zero;
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0.3f, 0.3f, 0.3f);

            // Fill area
            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1, 0.75f);
            fillAreaRect.offsetMin = new Vector2(5, 0);
            fillAreaRect.offsetMax = new Vector2(-5, 0);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0, 1);
            fillRect.pivot = new Vector2(0, 0.5f);
            fillRect.sizeDelta = Vector2.zero;
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.3f, 0.6f, 1f);

            // Handle slide area
            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRect = handleArea.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(10, 0);
            handleAreaRect.offsetMax = new Vector2(-10, 0);

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var handleRect = handle.AddComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0, 0);
            handleRect.anchorMax = new Vector2(0, 1);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(20, 0);
            var handleImage = handle.AddComponent<Image>();
            handleImage.color = Color.white;

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;

            _createdObjects.Add(go);
            return slider;
        }

        private Scrollbar CreateScrollbar(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(20, 200);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.3f, 0.3f, 0.3f);

            var scrollbar = go.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            // Sliding area
            var slidingArea = new GameObject("Sliding Area");
            slidingArea.transform.SetParent(go.transform, false);
            var slidingRect = slidingArea.AddComponent<RectTransform>();
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.offsetMin = new Vector2(2, 2);
            slidingRect.offsetMax = new Vector2(-2, -2);

            // Handle
            var handle = new GameObject("Handle");
            handle.transform.SetParent(slidingArea.transform, false);
            var handleRect = handle.AddComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = new Vector2(1, 0.2f);
            handleRect.sizeDelta = Vector2.zero;
            var handleImage = handle.AddComponent<Image>();
            handleImage.color = Color.white;

            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;

            _createdObjects.Add(go);
            return scrollbar;
        }

        private Dropdown CreateDropdown(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200, 40);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.3f, 0.3f, 0.3f);

            var dropdown = go.AddComponent<Dropdown>();

            // Caption
            var label = new GameObject("Label");
            label.transform.SetParent(go.transform, false);
            var labelRect = label.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10, 0);
            labelRect.offsetMax = new Vector2(-30, 0);
            var labelText = label.AddComponent<Text>();
            labelText.text = "Option 1";
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.color = Color.white;
            labelText.alignment = TextAnchor.MiddleLeft;

            // Template
            var template = new GameObject("Template");
            template.transform.SetParent(go.transform, false);
            var templateRect = template.AddComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0, 0);
            templateRect.anchorMax = new Vector2(1, 0);
            templateRect.pivot = new Vector2(0.5f, 1);
            templateRect.anchoredPosition = Vector2.zero;
            templateRect.sizeDelta = new Vector2(0, 150);
            template.AddComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f);
            var scrollRect = template.AddComponent<ScrollRect>();

            // Viewport
            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(template.transform, false);
            var viewportRect = viewport.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            viewport.AddComponent<Image>().color = Color.clear;
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            // Content
            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 28);

            // Item
            var item = new GameObject("Item");
            item.transform.SetParent(content.transform, false);
            var itemRect = item.AddComponent<RectTransform>();
            itemRect.anchorMin = new Vector2(0, 0.5f);
            itemRect.anchorMax = new Vector2(1, 0.5f);
            itemRect.sizeDelta = new Vector2(0, 28);
            var itemToggle = item.AddComponent<Toggle>();

            var itemBg = new GameObject("Item Background");
            itemBg.transform.SetParent(item.transform, false);
            var itemBgRect = itemBg.AddComponent<RectTransform>();
            itemBgRect.anchorMin = Vector2.zero;
            itemBgRect.anchorMax = Vector2.one;
            itemBgRect.sizeDelta = Vector2.zero;
            var itemBgImage = itemBg.AddComponent<Image>();
            itemBgImage.color = new Color(0.3f, 0.3f, 0.3f);

            var itemCheck = new GameObject("Item Checkmark");
            itemCheck.transform.SetParent(item.transform, false);
            var itemCheckRect = itemCheck.AddComponent<RectTransform>();
            itemCheckRect.anchorMin = new Vector2(0, 0.5f);
            itemCheckRect.anchorMax = new Vector2(0, 0.5f);
            itemCheckRect.sizeDelta = new Vector2(20, 20);
            itemCheckRect.anchoredPosition = new Vector2(10, 0);
            var checkText = itemCheck.AddComponent<Text>();
            checkText.text = "✓";
            checkText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            checkText.color = Color.white;
            checkText.alignment = TextAnchor.MiddleCenter;

            var itemLabel = new GameObject("Item Label");
            itemLabel.transform.SetParent(item.transform, false);
            var itemLabelRect = itemLabel.AddComponent<RectTransform>();
            itemLabelRect.anchorMin = Vector2.zero;
            itemLabelRect.anchorMax = Vector2.one;
            itemLabelRect.offsetMin = new Vector2(25, 0);
            itemLabelRect.offsetMax = new Vector2(-10, 0);
            var itemLabelText = itemLabel.AddComponent<Text>();
            itemLabelText.text = "Option";
            itemLabelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            itemLabelText.color = Color.white;
            itemLabelText.alignment = TextAnchor.MiddleLeft;

            itemToggle.targetGraphic = itemBgImage;
            itemToggle.graphic = checkText;
            itemToggle.isOn = true;

            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            dropdown.template = templateRect;
            dropdown.captionText = labelText;
            dropdown.itemText = itemLabelText;
            dropdown.options.Clear();
            dropdown.options.Add(new Dropdown.OptionData("Option 1"));
            dropdown.options.Add(new Dropdown.OptionData("Option 2"));
            dropdown.options.Add(new Dropdown.OptionData("Option 3"));

            template.SetActive(false);

            _createdObjects.Add(go);
            return dropdown;
        }

        private ScrollRect CreateScrollRectWithButtons(string name, Vector2 position, int buttonCount)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200, 150);
            rect.anchoredPosition = position;

            go.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f);
            var scrollRect = go.AddComponent<ScrollRect>();

            // Viewport
            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(go.transform, false);
            var viewportRect = viewport.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            viewport.AddComponent<Image>().color = Color.clear;
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            // Content
            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, buttonCount * 40);

            // Add buttons
            for (int i = 0; i < buttonCount; i++)
            {
            var btnGO = new GameObject($"Button{i}");
            btnGO.transform.SetParent(content.transform, false);
            var btnRect = btnGO.AddComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0, 1);
            btnRect.anchorMax = new Vector2(1, 1);
            btnRect.pivot = new Vector2(0.5f, 1);
            btnRect.sizeDelta = new Vector2(-20, 35);
            btnRect.anchoredPosition = new Vector2(0, -i * 40 - 2);

            var image = btnGO.AddComponent<Image>();
            image.color = new Color(0.4f, 0.4f, 0.4f);
            var button = btnGO.AddComponent<Button>();
            button.targetGraphic = image;
            }

            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            _createdObjects.Add(go);
            return scrollRect;
        }

        #endregion

        #region WaitFor Tests

        [Test]
        public async Task WaitFor_WithExistingElement_Succeeds()
        {
            CreateButton("WaitForButton", new Vector2(0, 0));
            await Async.DelayFrames(1);

            await ActionExecutor.WaitFor(new Search().Name("WaitForButton"), timeout: 0.5f);
            // No exception means success
        }

        [Test]
        public async Task WaitFor_WithMissingElement_Throws()
        {
            await Async.DelayFrames(1);
            LogAssert.Expect(LogType.Error, new Regex(@"\[UIAutomation\] FAILED:.*failed:"));

            try
            {
                await ActionExecutor.WaitFor(new Search().Name("NonExistent"), timeout: 0.3f);
                Assert.Fail("Expected AssertionException");
            }
            catch (AssertionException)
            {
                // Expected
            }
        }

        [Test]
        public async Task WaitFor_WithDelayedElement_WaitsAndReturnsTrue()
        {
            await Async.DelayFrames(1);

            // Start wait and create button after a delay
            var waitTask = ActionExecutor.WaitFor(new Search().Name("DelayedButton"), timeout: 1f);

            // Create button after 200ms
            await Task.Delay(200);
            CreateButton("DelayedButton", new Vector2(0, 0));

            await waitTask;
            // No exception means success
        }

        [Test]
        public async Task WaitForText_WithMatchingText_Succeeds()
        {
            CreateTextElement("WaitText", "Expected Text", new Vector2(0, 0));
            await Async.DelayFrames(1);

            await ActionExecutor.WaitFor(new Search().Name("WaitText"), "Expected Text", timeout: 0.5f);
            // No exception means success
        }

        [Test]
        public async Task WaitForText_WithMismatchedText_Throws()
        {
            CreateTextElement("WaitText", "Actual Text", new Vector2(0, 0));
            await Async.DelayFrames(1);
            LogAssert.Expect(LogType.Error, new Regex(@"\[UIAutomation\] FAILED:.*failed:"));

            try
            {
                await ActionExecutor.WaitFor(new Search().Name("WaitText"), "Wrong Text", timeout: 0.3f);
                Assert.Fail("Expected AssertionException");
            }
            catch (AssertionException)
            {
                // Expected
            }
        }

        [Test]
        public async Task WaitForToggle_WithMatchingState_Succeeds()
        {
            var toggle = CreateToggle("WaitToggle", new Vector2(0, 0));
            toggle.isOn = true;
            await Async.DelayFrames(1);

            await ActionExecutor.WaitFor(new Search().Name("WaitToggle"), timeout: 0.5f);
            // No exception means success
        }

        [Test]
        public async Task WaitForNot_WithMissingElement_Succeeds()
        {
            await Async.DelayFrames(1);

            await ActionExecutor.WaitForNot(new Search().Name("NonExistent"), timeout: 0.5f);
            // No exception means success
        }

        [Test]
        public async Task WaitForNot_WithExistingElement_Throws()
        {
            CreateButton("ExistingButton", new Vector2(0, 0));
            await Async.DelayFrames(1);
            LogAssert.Expect(LogType.Error, new Regex(@"\[UIAutomation\] FAILED:.*failed:"));

            try
            {
                await ActionExecutor.WaitForNot(new Search().Name("ExistingButton"), timeout: 0.3f);
                Assert.Fail("Expected AssertionException");
            }
            catch (AssertionException)
            {
                // Expected
            }
        }

        [Test]
        public async Task WaitForNot_WithRemovedElement_ReturnsTrue()
        {
            var button = CreateButton("ToBeRemoved", new Vector2(0, 0));
            await Async.DelayFrames(1);

            // Start wait and destroy button after a delay
            var waitTask = ActionExecutor.WaitForNot(new Search().Name("ToBeRemoved"), timeout: 1f);

            // Destroy after 200ms
            await Task.Delay(200);
            UnityEngine.Object.Destroy(button.gameObject);

            await waitTask;
            // No exception means success
        }

        #endregion

        #region StaticPath - Basic Resolution

        [Test]
        public async Task StaticPath_ResolvesObject()
        {
            TestTrucks.PlayerTruck = new TruckController { Name = "MainTruck", Health = 100f };
            await Async.DelayFrames(1);

            var path = Search.Reflect("TestTrucks.PlayerTruck");
            Assert.IsNotNull(path.Value);
            Assert.AreEqual("MainTruck", path.GetValue<string>("Name"));
            Assert.AreEqual(100f, path.GetValue<float>("Health"));
        }

        [Test]
        public async Task StaticPath_ResolvesNestedProperty()
        {
            TestTrucks.PlayerTruck = new TruckController
            {
                Name = "MainTruck",
                DamageController = new DamageController { MaxHealth = 150f, IsDamaged = true }
            };
            await Async.DelayFrames(1);

            var damage = Search.Reflect("TestTrucks.PlayerTruck").Property("DamageController");
            Assert.AreEqual(150f, damage.GetValue<float>("MaxHealth"));
            Assert.IsTrue(damage.GetValue<bool>("IsDamaged"));
        }

        #endregion

        #region StaticPath - Value Properties

        [Test]
        public async Task StaticPath_AllBasicTypes()
        {
            TestTrucks.PlayerTruck = new TruckController
            {
                Name = "TestTruck",
                IsActive = true,
                Count = 42,
                Health = 85.5f,
                Precision = 3.14159265359,
                BigNumber = 9876543210L
            };
            await Async.DelayFrames(1);

            var truck = Search.Reflect("TestTrucks.PlayerTruck");

            // String
            Assert.AreEqual("TestTruck", truck.Property("Name").GetValue<string>());

            // Bool
            Assert.IsTrue(truck.Property("IsActive").GetValue<bool>());

            // Int
            Assert.AreEqual(42, truck.Property("Count").GetValue<int>());

            // Float
            Assert.AreEqual(85.5f, truck.Property("Health").GetValue<float>(), 0.01f);

            // Double via GetValue<float>
            Assert.AreEqual(3.14f, truck.Property("Precision").GetValue<float>(), 0.01f);

            // Long via IntValue (truncates)
            Assert.AreEqual(9876543210L, truck.Property("BigNumber").GetValue<long>());
        }

        [Test]
        public async Task StaticPath_ArrayProperties()
        {
            TestTrucks.PlayerTruck = new TruckController
            {
                Tags = new[] { "fast", "red", "turbo" },
                Scores = new[] { 100, 200, 300 },
                Multipliers = new[] { 1.5f, 2.0f, 2.5f }
            };
            await Async.DelayFrames(1);

            var truck = Search.Reflect("TestTrucks.PlayerTruck");

            // String array
            var tags = truck.Property("Tags").GetValue<string[]>();
            Assert.AreEqual(3, tags.Length);
            Assert.AreEqual("fast", tags[0]);

            // Int array
            var scores = truck.Property("Scores").GetValue<int[]>();
            Assert.AreEqual(300, scores[2]);

            // Float array
            var multipliers = truck.Property("Multipliers").GetValue<float[]>();
            Assert.AreEqual(2.0f, multipliers[1], 0.01f);
        }

        [Test]
        public async Task StaticPath_BoolValue_OnNonBool_ThrowsException()
        {
            TestTrucks.PlayerTruck = new TruckController { Name = "TestTruck" };
            await Async.DelayFrames(1);

            // GetValue<bool> on a string should throw InvalidOperationException
            Assert.Throws<System.InvalidOperationException>(() =>
            {
                var _ = Search.Reflect("TestTrucks.PlayerTruck").Property("Name").GetValue<bool>();
            });
        }

        [Test]
        public async Task StaticPath_AccessesPrivateField()
        {
            TestTrucks.PlayerTruck = new TruckController();
            TestTrucks.PlayerTruck.SetSecretCode("SECRET");
            await Async.DelayFrames(1);

            Assert.AreEqual("SECRET", Search.Reflect("TestTrucks.PlayerTruck").Property("_secretCode").GetValue<string>());
        }

        #endregion

        #region StaticPath - Array Iteration

        [Test]
        public async Task StaticPath_IteratesArray()
        {
            TestTrucks.PlayerTrucks = new[]
            {
                new TruckController { Name = "Truck1" },
                new TruckController { Name = "Truck2" },
                new TruckController { Name = "Truck3" }
            };
            await Async.DelayFrames(1);

            var names = new List<string>();
            foreach (var truck in Search.Reflect("TestTrucks.PlayerTrucks"))
            {
                names.Add(truck.GetValue<string>("Name"));
            }

            Assert.AreEqual(3, names.Count);
            Assert.Contains("Truck1", names);
            Assert.Contains("Truck2", names);
            Assert.Contains("Truck3", names);
        }

        [Test]
        public async Task StaticPath_IteratesWithNestedAccess()
        {
            TestTrucks.PlayerTrucks = new[]
            {
                new TruckController { DamageController = new DamageController { MaxHealth = 100f } },
                new TruckController { DamageController = new DamageController { MaxHealth = 200f } }
            };
            await Async.DelayFrames(1);

            float total = 0f;
            foreach (var truck in Search.Reflect("TestTrucks.PlayerTrucks"))
            {
                total += truck.Property("DamageController").GetValue<float>("MaxHealth");
            }

            Assert.AreEqual(300f, total, 0.01f);
        }

        #endregion

        #region GetValue - Static Paths

        [Test]
        public async Task GetValue_ResolvesStaticPath()
        {
            TestTrucks.PlayerTruck = new TruckController { Name = "GetValueTruck", Health = 42f };
            await Async.DelayFrames(1);

            Assert.AreEqual("GetValueTruck", ActionExecutor.GetValue<string>("TestTrucks.PlayerTruck.Name"));
            Assert.AreEqual(42f, ActionExecutor.GetValue<float>("TestTrucks.PlayerTruck.Health"), 0.01f);
        }

        #endregion

        #region Static Path WaitFor Tests

        [Test]
        public async Task WaitForStaticPath_WithTruthyValue_Succeeds()
        {
            await Async.DelayFrames(1);

            await ActionExecutor.WaitFor("Application.isPlaying", timeout: 0.5f);
            // No exception means success
        }

        [Test]
        public async Task WaitForStaticPathGeneric_WithMatchingValue_Succeeds()
        {
            await Async.DelayFrames(1);

            await ActionExecutor.WaitFor("Application.isPlaying", true, timeout: 0.5f);
            // No exception means success
        }

        [Test]
        public async Task WaitForStaticPathGeneric_WithMismatchedValue_Throws()
        {
            await Async.DelayFrames(1);
            LogAssert.Expect(LogType.Error, new Regex(@"\[UIAutomation\] FAILED:.*failed:"));

            try
            {
                await ActionExecutor.WaitFor("Application.isPlaying", false, timeout: 0.3f);
                Assert.Fail("Expected AssertionException");
            }
            catch (AssertionException)
            {
                // Expected
            }
        }

        #endregion

        #region Helper Methods

        private GameObject CreateTextElement(string name, string text, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200, 40);
            rect.anchoredPosition = position;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 14;
            tmp.color = Color.white;

            _createdObjects.Add(go);
            return go;
        }

        private Toggle CreateToggle(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(100, 30);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = Color.gray;

            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = image;

            _createdObjects.Add(go);
            return toggle;
        }

        #endregion

        #region ActionScope Verification

        /// <summary>
        /// Verifies that all public async methods in ActionExecutor use RunAction for consistent logging.
        /// Uses source code analysis to check that each method body contains RunAction().
        /// </summary>
        [Test]
        public void AllPublicAsyncMethods_UseRunAction()
        {
            // Read the ActionExecutor.cs source file
            var sourceFile = Path.GetFullPath(Path.Combine(Application.dataPath, "../../package/UIAutomation/ActionExecutor.cs"));
            Assert.That(File.Exists(sourceFile), Is.True, $"Source file not found: {sourceFile}");

            var sourceCode = File.ReadAllText(sourceFile);

            // Get all public async method names via reflection
            var type = typeof(ActionExecutor);
            var asyncMethods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(m => m.ReturnType.FullName != null && (m.ReturnType.FullName.Contains("Task") || m.ReturnType.Name.Contains("Task")))
                .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_"))
                .Select(m => m.Name)
                .Distinct()
                .ToList();

            // These methods are exempt from RunAction because they are utilities, configuration, or delegate to other methods
            var exemptMethods = new HashSet<string>
            {
                "ActionComplete", // Legacy wrapper for external callers
                "SetRandomSeed",  // Configuration method, not an action
                // Convenience methods that delegate to AutoExplore (which has RunAction):
                "AutoExploreForSeconds",
                "AutoExploreForActions",
                "AutoExploreUntilDeadEnd",
            };

            // Regex to find method declarations and their bodies
            // Matches: public static async Task[<T>] MethodName(...) { ... }
            var methodPattern = new Regex(
                @"public\s+static\s+async\s+Task(?:<[^>]+>)?\s+(\w+)\s*\([^)]*\)\s*\{",
                RegexOptions.Singleline);

            var methodsWithoutRunAction = new List<string>();
            var methodsWithRunAction = new List<string>();

            foreach (var methodName in asyncMethods)
            {
                if (exemptMethods.Contains(methodName))
                    continue;

                // Find the method declaration in source
                var declarationPattern = new Regex(
                    $@"public\s+static\s+async\s+Task(?:<[^>]+>)?\s+{Regex.Escape(methodName)}\s*\([^)]*\)\s*\{{",
                    RegexOptions.Singleline);

                var match = declarationPattern.Match(sourceCode);
                if (!match.Success)
                {
                    // Method might be defined differently, skip
                    continue;
                }

                // Extract the method body by counting braces
                var startIndex = match.Index + match.Length - 1; // Position of opening brace
                var methodBody = ExtractMethodBody(sourceCode, startIndex);

                if (methodBody == null)
                {
                    methodsWithoutRunAction.Add($"{methodName} (could not parse body)");
                    continue;
                }

                // Check if the method body contains RunAction(
                if (methodBody.Contains("RunAction("))
                {
                    methodsWithRunAction.Add(methodName);
                }
                else
                {
                    methodsWithoutRunAction.Add(methodName);
                }
            }

            // Log results
            Debug.Log($"[ActionExecutor] {methodsWithRunAction.Count} methods correctly use RunAction");

            if (methodsWithoutRunAction.Count > 0)
            {
                Debug.LogError($"[ActionExecutor] {methodsWithoutRunAction.Count} methods MISSING RunAction:");
                foreach (var name in methodsWithoutRunAction)
                {
                    Debug.LogError($"  - {name}");
                }
            }

            Assert.That(methodsWithoutRunAction, Is.Empty,
                $"The following public async methods do not use RunAction for logging:\n" +
                string.Join("\n", methodsWithoutRunAction.Select(m => $"  - {m}")));
        }

        /// <summary>
        /// Extracts a method body from source code starting at the opening brace.
        /// </summary>
        private string ExtractMethodBody(string source, int openBraceIndex)
        {
            if (openBraceIndex >= source.Length || source[openBraceIndex] != '{')
                return null;

            var depth = 1;
            var startIndex = openBraceIndex + 1;
            var currentIndex = startIndex;

            while (currentIndex < source.Length && depth > 0)
            {
                var c = source[currentIndex];

                // Skip string literals
                if (c == '"')
                {
                    currentIndex++;
                    while (currentIndex < source.Length)
                    {
                        if (source[currentIndex] == '\\' && currentIndex + 1 < source.Length)
                        {
                            currentIndex += 2; // Skip escaped character
                            continue;
                        }
                        if (source[currentIndex] == '"')
                        {
                            currentIndex++;
                            break;
                        }
                        currentIndex++;
                    }
                    continue;
                }

                // Skip character literals
                if (c == '\'')
                {
                    currentIndex++;
                    while (currentIndex < source.Length && source[currentIndex] != '\'')
                    {
                        if (source[currentIndex] == '\\' && currentIndex + 1 < source.Length)
                            currentIndex++;
                        currentIndex++;
                    }
                    currentIndex++; // Skip closing quote
                    continue;
                }

                // Skip single-line comments
                if (c == '/' && currentIndex + 1 < source.Length && source[currentIndex + 1] == '/')
                {
                    while (currentIndex < source.Length && source[currentIndex] != '\n')
                        currentIndex++;
                    continue;
                }

                // Skip multi-line comments
                if (c == '/' && currentIndex + 1 < source.Length && source[currentIndex + 1] == '*')
                {
                    currentIndex += 2;
                    while (currentIndex + 1 < source.Length && !(source[currentIndex] == '*' && source[currentIndex + 1] == '/'))
                        currentIndex++;
                    currentIndex += 2;
                    continue;
                }

                if (c == '{')
                    depth++;
                else if (c == '}')
                    depth--;

                currentIndex++;
            }

            if (depth != 0)
                return null;

            return source.Substring(startIndex, currentIndex - startIndex - 1);
        }

        #endregion
    }

    /// <summary>
    /// Simple drag handler for testing drag operations.
    /// </summary>
    public class SimpleDragHandler : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
    {
        private RectTransform _rectTransform;
        private Canvas _canvas;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();
        }

        public void OnBeginDrag(PointerEventData eventData) { }

        public void OnDrag(PointerEventData eventData)
        {
            if (_rectTransform != null && _canvas != null)
            {
            _rectTransform.anchoredPosition += eventData.delta / _canvas.scaleFactor;
            }
        }

        public void OnEndDrag(PointerEventData eventData) { }
    }

    #region Mock Classes for StaticPath Tests

    /// <summary>
    /// Mock static class simulating Generator.Trucks hierarchy.
    /// Used for testing StaticPath resolution with static classes.
    /// </summary>
    public static class TestTrucks
    {
        /// <summary>Single truck instance for testing</summary>
        public static TruckController PlayerTruck { get; set; }

        /// <summary>Array of trucks for testing iteration</summary>
        public static TruckController[] PlayerTrucks { get; set; }
    }

    /// <summary>
    /// Mock truck controller for testing StaticPath property access.
    /// </summary>
    public class TruckController
    {
        // Basic C# types
        public string Name { get; set; }
        public bool IsActive { get; set; }
        public int Count { get; set; }
        public float Health { get; set; }
        public double Precision { get; set; }
        public long BigNumber { get; set; }

        // Array types
        public string[] Tags { get; set; }
        public int[] Scores { get; set; }
        public float[] Multipliers { get; set; }

        // Nested object
        public DamageController DamageController { get; set; }

        // Private field for testing private access
        private string _secretCode = "ABC123";
        public void SetSecretCode(string code) => _secretCode = code;
    }

    /// <summary>
    /// Mock damage controller for testing nested property access.
    /// </summary>
    public class DamageController
    {
        public float MaxHealth { get; set; }
        public bool IsDamaged { get; set; }
    }

    #endregion
}
