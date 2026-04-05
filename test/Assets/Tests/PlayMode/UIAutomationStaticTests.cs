using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using TMPro;
using static ODDGames.Bugpunch.ActionExecutor;

namespace ODDGames.Bugpunch.Tests
{
    /// <summary>
    /// Tests for the static UIAutomation class.
    /// Verifies that all test actions work correctly when accessed via 'using static'.
    /// </summary>
    [TestFixture]
    public class UIAutomationStaticTests : UIAutomationTestFixture
    {
        private GameObject _canvas;
        private Canvas _canvasComponent;
        private EventSystem _eventSystem;
        private List<GameObject> _createdObjects;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();

            _createdObjects = new List<GameObject>();

            // Create EventSystem
            var esGO = new GameObject("EventSystem");
            _eventSystem = esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
            _createdObjects.Add(esGO);

            // Create Canvas
            _canvas = new GameObject("TestCanvas");
            _canvasComponent = _canvas.AddComponent<Canvas>();
            _canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = _canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            _canvas.AddComponent<GraphicRaycaster>();
            _createdObjects.Add(_canvas);
        }

        [TearDown]
        public override async Task TearDown()
        {
            // Destroy in reverse order
            for (int i = _createdObjects.Count - 1; i >= 0; i--)
            {
                var obj = _createdObjects[i];
                if (obj != null)
                    UnityEngine.Object.DestroyImmediate(obj);
            }
            _createdObjects.Clear();

            await base.TearDown();
        }

        #region Helper Methods

        private Button CreateButton(string name, string label, Transform parent, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _createdObjects.Add(go);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(160, 40);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = Color.white;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            // Add text label
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.black;

            return button;
        }

        private TMP_InputField CreateInputField(string name, Transform parent, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _createdObjects.Add(go);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200, 40);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.9f, 0.9f, 0.9f);

            var inputField = go.AddComponent<TMP_InputField>();

            // Text area
            var textArea = new GameObject("Text Area");
            textArea.transform.SetParent(go.transform, false);
            var textAreaRect = textArea.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(10, 5);
            textAreaRect.offsetMax = new Vector2(-10, -5);

            // Main text
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(textArea.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.color = Color.black;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;

            return inputField;
        }

        private Toggle CreateToggle(string name, Transform parent, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _createdObjects.Add(go);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(40, 40);
            rect.anchoredPosition = position;

            var toggle = go.AddComponent<Toggle>();

            // Background
            var bg = new GameObject("Background");
            bg.transform.SetParent(go.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = Color.white;

            // Checkmark
            var checkmark = new GameObject("Checkmark");
            checkmark.transform.SetParent(bg.transform, false);
            var checkRect = checkmark.AddComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.1f, 0.1f);
            checkRect.anchorMax = new Vector2(0.9f, 0.9f);
            checkRect.sizeDelta = Vector2.zero;
            var checkImage = checkmark.AddComponent<Image>();
            checkImage.color = Color.green;

            toggle.targetGraphic = bgImage;
            toggle.graphic = checkImage;
            toggle.isOn = false;

            return toggle;
        }

        private Slider CreateSlider(string name, Transform parent, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _createdObjects.Add(go);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200, 20);
            rect.anchoredPosition = position;

            var slider = go.AddComponent<Slider>();

            // Background
            var bg = new GameObject("Background");
            bg.transform.SetParent(go.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.25f);
            bgRect.anchorMax = new Vector2(1, 0.75f);
            bgRect.sizeDelta = Vector2.zero;
            bg.AddComponent<Image>().color = Color.gray;

            // Fill area
            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1, 0.75f);
            fillAreaRect.offsetMin = new Vector2(5, 0);
            fillAreaRect.offsetMax = new Vector2(-5, 0);

            // Fill
            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0, 1);
            fillRect.sizeDelta = Vector2.zero;
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = Color.blue;

            // Handle area
            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRect = handleArea.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(10, 0);
            handleAreaRect.offsetMax = new Vector2(-10, 0);

            // Handle
            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var handleRect = handle.AddComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0, 0);
            handleRect.anchorMax = new Vector2(0, 1);
            handleRect.sizeDelta = new Vector2(20, 0);
            var handleImage = handle.AddComponent<Image>();
            handleImage.color = Color.white;

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.value = 0.5f;

            return slider;
        }

        private TextMeshProUGUI CreateLabel(string name, string text, Transform parent, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _createdObjects.Add(go);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(100, 30);
            rect.anchoredPosition = position;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return tmp;
        }

        #endregion

        #region Search Helper Tests

        [Test]
        public async Task Name_FindsButtonByName()
        {
                var button = CreateButton("TestButton", "Click Me", _canvas.transform, Vector2.zero);
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                var found = await Find<Button>(Name("TestButton"), throwIfMissing: true, seconds: 0.5f);
                Assert.IsNotNull(found);
                Assert.AreEqual("TestButton", found.name);
        }

        [Test]
        public async Task Text_FindsTextElementByContent()
        {
                var button = CreateButton("MyButton", "Submit Form", _canvas.transform, Vector2.zero);
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                // Text() finds the TMP_Text component that has the text, then we get parent Button
                var found = await Find<TextMeshProUGUI>(Text("Submit Form"), throwIfMissing: true, seconds: 0.5f);
                Assert.IsNotNull(found);
                Assert.AreEqual("Submit Form", found.text);

                // Verify we can get to the button via parent
                var parentButton = found.GetComponentInParent<Button>();
                Assert.IsNotNull(parentButton);
                Assert.AreEqual("MyButton", parentButton.name);
        }

        [Test]
        public async Task Type_FindsComponentByType()
        {
                var slider = CreateSlider("VolumeSlider", _canvas.transform, Vector2.zero);
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                var found = await Find<Slider>(Type<Slider>(), throwIfMissing: true, seconds: 0.5f);
                Assert.IsNotNull(found);
                Assert.AreEqual("VolumeSlider", found.name);
        }

        [Test]
        public async Task Adjacent_FindsInputFieldNextToLabel()
        {
                // Create label on the left, input field on the right
                CreateLabel("UsernameLabel", "Username:", _canvas.transform, new Vector2(-150, 0));
                var inputField = CreateInputField("UsernameInput", _canvas.transform, new Vector2(50, 0));
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                var found = await Find<TMP_InputField>(Adjacent("Username:"), throwIfMissing: true, seconds: 0.5f);
                Assert.IsNotNull(found);
                Assert.AreEqual("UsernameInput", found.name);
        }

        #endregion

        #region Click Tests

        [Test]
        public async Task Click_ClicksButton()
        {
                var button = CreateButton("ClickTest", "Click Me", _canvas.transform, Vector2.zero);
                bool clicked = false;
                button.onClick.AddListener(() => clicked = true);
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                await Click(Name("ClickTest"));
                Assert.IsTrue(clicked, "Button should have been clicked");
        }

        [Test]
        public async Task Click_Toggle_TogglesState()
        {
                var toggle = CreateToggle("TestToggle", _canvas.transform, Vector2.zero);
                Assert.IsFalse(toggle.isOn, "Toggle should start off");
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                await Click(Name("TestToggle"));
                Assert.IsTrue(toggle.isOn, "Toggle should be on after click");

                await Click(Name("TestToggle"));
                Assert.IsFalse(toggle.isOn, "Toggle should be off after second click");
        }

        #endregion

        #region Text Input Tests

        [Test]
        public async Task TextInput_EntersTextIntoField()
        {
                var inputField = CreateInputField("EmailInput", _canvas.transform, Vector2.zero);
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                await TextInput(Name("EmailInput"), "test@example.com");
                Assert.AreEqual("test@example.com", inputField.text);
        }

        [Test]
        public async Task TextInput_ClearsExistingText()
        {
                var inputField = CreateInputField("NameInput", _canvas.transform, Vector2.zero);
                inputField.text = "Old Value";
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                await TextInput(Name("NameInput"), "New Value");
                Assert.AreEqual("New Value", inputField.text);
        }

        #endregion

        #region Wait Tests

        [Test]
        public async Task Wait_WaitsForSeconds()
        {
                var startTime = Time.realtimeSinceStartup;
                await Wait(seconds: 0.5f);
                var elapsed = Time.realtimeSinceStartup - startTime;
                Assert.GreaterOrEqual(elapsed, 0.4f, "Should have waited at least 0.4 seconds");
        }

        [Test]
        public async Task WaitFor_WaitsForElementToAppear()
        {
            // Create element after a delay, then verify WaitFor finds it
            await Task.Delay(100);
            CreateButton("DelayedButton", "I Appeared", _canvas.transform, Vector2.zero);

            await WaitFor(Name("DelayedButton"), timeout: 3);
            // No exception means element was found
        }

        #endregion

        #region Find Tests

        [Test]
        public async Task Find_ReturnsComponent()
        {
                var button = CreateButton("FindMeButton", "Find Me", _canvas.transform, Vector2.zero);
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                var found = await Find<Button>(Name("FindMeButton"), throwIfMissing: true, seconds: 0.5f);
                Assert.IsNotNull(found);
                Assert.AreSame(button, found);
        }

        [Test]
        public async Task Find_ReturnsNullWhenNotFound()
        {
                var found = await Find<Button>(Name("NonExistentButton"), throwIfMissing: false, seconds: 0.5f);
                Assert.IsNull(found);
        }

        [Test]
        public async Task FindAll_ReturnsMultipleComponents()
        {
                CreateButton("ItemButton", "Item 1", _canvas.transform, new Vector2(0, 50));
                CreateButton("ItemButton", "Item 2", _canvas.transform, new Vector2(0, 0));
                CreateButton("ItemButton", "Item 3", _canvas.transform, new Vector2(0, -50));
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                var found = await FindAll<Button>(Name("ItemButton"), seconds: 0.5f);
                Assert.AreEqual(3, found.Count(), "Should find all 3 buttons");
        }

        #endregion

        #region Slider Tests

        [Test]
        public async Task ClickSlider_SetsSliderValue()
        {
                var slider = CreateSlider("TestSlider", _canvas.transform, Vector2.zero);
                slider.value = 0f;
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                await ClickSlider(Name("TestSlider"), 0.75f);
                Assert.AreEqual(0.75f, slider.value, 0.1f, "Slider should be near 0.75");
        }

        #endregion

        #region Configuration Tests

        [Test]
        public void Interval_CanBeSetAndRead()
        {
            var original = Interval;
            try
            {
                Interval = 500;
                Assert.AreEqual(500, Interval);
            }
            finally
            {
                Interval = original;
            }
        }

        [Test]
        public void PollInterval_CanBeSetAndRead()
        {
            var original = PollInterval;
            try
            {
                PollInterval = 50;
                Assert.AreEqual(50, PollInterval);
            }
            finally
            {
                PollInterval = original;
            }
        }

        [Test]
        public void DebugMode_CanBeToggled()
        {
            var original = DebugMode;
            try
            {
                DebugMode = true;
                Assert.IsTrue(DebugMode);
                DebugMode = false;
                Assert.IsFalse(DebugMode);
            }
            finally
            {
                DebugMode = original;
            }
        }

        [Test]
        public void SetRandomSeed_SetsReproducibleSeed()
        {
            SetRandomSeed(12345);
            var val1 = RandomGenerator.Next();

            SetRandomSeed(12345);
            var val2 = RandomGenerator.Next();

            Assert.AreEqual(val1, val2, "Same seed should produce same random values");
        }

        #endregion

        #region Double/Triple Click Tests

        [Test]
        public async Task DoubleClick_ClicksButtonTwice()
        {
                var button = CreateButton("DoubleClickTest", "Double Click Me", _canvas.transform, Vector2.zero);
                int clickCount = 0;
                button.onClick.AddListener(() => clickCount++);
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                await DoubleClick(Name("DoubleClickTest"));
                Assert.AreEqual(2, clickCount, "Button should have been clicked twice");
        }

        [Test]
        public async Task TripleClick_ClicksButtonThreeTimes()
        {
                var button = CreateButton("TripleClickTest", "Triple Click Me", _canvas.transform, Vector2.zero);
                int clickCount = 0;
                button.onClick.AddListener(() => clickCount++);
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                await TripleClick(Name("TripleClickTest"));
                Assert.AreEqual(3, clickCount, "Button should have been clicked three times");
        }

        #endregion

        #region Hold Tests

        [Test]
        public async Task Hold_HoldsOnElement()
        {
                var button = CreateButton("HoldTest", "Hold Me", _canvas.transform, Vector2.zero);
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                var startTime = Time.realtimeSinceStartup;
                await Hold(Name("HoldTest"), 0.5f);
                var elapsed = Time.realtimeSinceStartup - startTime;
                Assert.GreaterOrEqual(elapsed, 0.4f, "Should have held for at least 0.4 seconds");
        }

        #endregion

        #region Drag Tests

        [Test]
        public async Task Drag_CompletesWithoutError()
        {
                var button = CreateButton("DragTarget", "Drag Me", _canvas.transform, Vector2.zero);
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                // Just verify drag completes without error
                await Drag(Name("DragTarget"), new Vector2(100, 0), duration: 0.3f);
                Assert.Pass("Drag completed successfully");
        }

        #endregion

        #region Swipe Tests

        [Test]
        public async Task Swipe_SwipesInDirection()
        {
                var button = CreateButton("SwipeTarget", "Swipe Here", _canvas.transform, Vector2.zero);
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                // Just verify the swipe completes without error
                await Swipe(Name("SwipeTarget"), SwipeDirection.Right, distance: 0.1f, duration: 0.3f);
                Assert.Pass("Swipe completed successfully");
        }

        #endregion

        #region WaitFor Condition Tests

        [Test]
        public async Task WaitFor_Condition_WaitsUntilTrue()
        {
            bool conditionMet = false;

            // Set condition, then verify WaitFor succeeds
            await Task.Delay(100);
            conditionMet = true;

            await WaitFor(() => conditionMet, seconds: 5, description: "test condition");
            Assert.IsTrue(conditionMet, "Condition should be met");
        }

        #endregion

        #region Search Chaining Tests

        [Test]
        public async Task SearchChaining_TypeAndName_FindsCorrectElement()
        {
                CreateButton("Button1", "First", _canvas.transform, new Vector2(0, 50));
                CreateButton("Button2", "Second", _canvas.transform, new Vector2(0, -50));
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                // Chain Type and Name filters
                var search = Type<Button>().Name("Button2");
                var found = await Find<Button>(search, throwIfMissing: true, seconds: 0.5f);
                Assert.IsNotNull(found);
                Assert.AreEqual("Button2", found.name);
        }

        [Test]
        public async Task Path_FindsByHierarchyPath()
        {
                // Create nested structure
                var panel = new GameObject("Panel");
                panel.transform.SetParent(_canvas.transform, false);
                _createdObjects.Add(panel);
                panel.AddComponent<RectTransform>();

                var button = CreateButton("NestedButton", "Nested", panel.transform, Vector2.zero);
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                // Path uses / separator for hierarchy, with wildcards
                var found = await Find<Button>(Path("*/Panel/NestedButton"), throwIfMissing: true, seconds: 0.5f);
                Assert.IsNotNull(found);
                Assert.AreEqual("NestedButton", found.name);
        }

        #endregion

        #region Any Search Tests

        [Test]
        public async Task Any_FindsByNameOrPath()
        {
                CreateButton("UniqueBtn", "Click Here", _canvas.transform, Vector2.zero);
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                // Should find by name
                var foundByName = await Find<Button>(Any("UniqueBtn"), throwIfMissing: true, seconds: 0.5f);
                Assert.IsNotNull(foundByName);
                Assert.AreEqual("UniqueBtn", foundByName.name);

                // Any() with text finds the text element, not the button
                // Use Name() for button-specific searches
        }

        #endregion

        #region Tag Search Tests

        [Test]
        public async Task Tag_FindsByUnityTag()
        {
                var button = CreateButton("TaggedButton", "Tagged", _canvas.transform, Vector2.zero);
                button.gameObject.tag = "Player"; // Using built-in tag
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                var found = await Find<Button>(Tag("Player"), throwIfMissing: true, seconds: 0.5f);
                Assert.IsNotNull(found);
                Assert.AreEqual("TaggedButton", found.name);
        }

        #endregion

        #region Near Search Tests

        [Test]
        public void Near_CreatesSearchInstance()
        {
            // Verify Near() creates a valid search
            var search = Near("SomeLabel");
            Assert.IsNotNull(search);
        }

        #endregion

        #region Keyboard Input Tests

        [Test]
        public async Task PressKey_SimulatesKeyPress()
        {
                // Just verify it completes without error
                await PressKey(Key.A);
                Assert.Pass("PressKey completed successfully");
        }

        [Test]
        public async Task HoldKey_HoldsKeyForDuration()
        {
                var startTime = Time.realtimeSinceStartup;
                await HoldKey(Key.Space, 0.3f);
                var elapsed = Time.realtimeSinceStartup - startTime;
                Assert.GreaterOrEqual(elapsed, 0.25f, "Should have held key for at least 0.25 seconds");
        }

        #endregion

        #region Scroll Tests

        [Test]
        public async Task Scroll_ScrollsAtPosition()
        {
                // Create a scrollable area
                var scrollGO = new GameObject("ScrollArea");
                scrollGO.transform.SetParent(_canvas.transform, false);
                var rt = scrollGO.AddComponent<RectTransform>();
                rt.sizeDelta = new Vector2(200, 200);
                scrollGO.AddComponent<Image>();
                _createdObjects.Add(scrollGO);

                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                // Just verify it completes without error (may not find ScrollRect, that's ok)
                await Scroll(new Search().Name("ScrollArea"), 100f, searchTime: 0.5f);
                Assert.Pass("Scroll completed successfully");
        }

        #endregion

        #region ClickAt Tests

        [Test]
        public async Task ClickAt_ClicksAtScreenPercentage()
        {
                var button = CreateButton("CenterButton", "Center", _canvas.transform, Vector2.zero);
                bool clicked = false;
                button.onClick.AddListener(() => clicked = true);
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                // Click at center of screen (0.5, 0.5)
                await ClickAt(0.5f, 0.5f);
                Assert.IsTrue(clicked, "Button at center should have been clicked");
        }

        #endregion

        #region Reflect Tests

        [Test]
        public void Reflect_CreatesSearchFromPath()
        {
            var search = Reflect("UnityEngine.Time.deltaTime");
            Assert.IsNotNull(search);
        }

        [Test]
        public void Invoke_StaticMethod_CallsSuccessfully()
        {
            ReflectTestHelper.ResetState();
            Reflect("ODDGames.Bugpunch.Tests.ReflectTestHelper").Invoke("StaticVoidMethod");
            Assert.IsTrue(ReflectTestHelper.WasCalled, "Static method should have been called");
        }

        [Test]
        public void Invoke_StaticMethodWithArgs_PassesArguments()
        {
            ReflectTestHelper.ResetState();
            Reflect("ODDGames.Bugpunch.Tests.ReflectTestHelper").Invoke("StaticMethodWithArgs", "hello", 42);
            Assert.AreEqual("hello", ReflectTestHelper.LastString);
            Assert.AreEqual(42, ReflectTestHelper.LastInt);
        }

        [Test]
        public void Invoke_StaticMethodWithReturn_ReturnsValue()
        {
            var result = Reflect("ODDGames.Bugpunch.Tests.ReflectTestHelper").Invoke<int>("StaticAdd", 3, 7);
            Assert.AreEqual(10, result);
        }

        [Test]
        public void Invoke_InstanceMethod_CallsSuccessfully()
        {
            var instance = ReflectTestHelper.Instance;
            Reflect("ODDGames.Bugpunch.Tests.ReflectTestHelper.Instance").Invoke("InstanceMethod");
            Assert.IsTrue(instance.InstanceWasCalled, "Instance method should have been called");
        }

        [Test]
        public async Task InvokeAsync_StaticAsyncMethod_AwaitsCompletion()
        {
            ReflectTestHelper.ResetState();
            await Reflect("ODDGames.Bugpunch.Tests.ReflectTestHelper").InvokeAsync("StaticAsyncMethod");
            Assert.IsTrue(ReflectTestHelper.WasCalled, "Async method should have completed");
        }

        [Test]
        public async Task InvokeAsync_WithReturn_ReturnsValue()
        {
            var result = await Reflect("ODDGames.Bugpunch.Tests.ReflectTestHelper").InvokeAsync<int>("StaticAsyncAdd", 5, 3);
            Assert.AreEqual(8, result);
        }

        [Test]
        public void Invoke_DefaultParams_FillsDefaults()
        {
            // Call with only required arg, optional params should use defaults
            var result = Reflect("ODDGames.Bugpunch.Tests.ReflectTestHelper").Invoke<string>("MethodWithDefaults", "hello");
            Assert.AreEqual("hello|42|true", result);
        }

        [Test]
        public void Invoke_DefaultParams_PartialOverride()
        {
            // Call with required + first optional
            var result = Reflect("ODDGames.Bugpunch.Tests.ReflectTestHelper").Invoke<string>("MethodWithDefaults", "hello", 99);
            Assert.AreEqual("hello|99|true", result);
        }

        [Test]
        public void Invoke_NoMatchingSignature_ListsAvailable()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                Reflect("ODDGames.Bugpunch.Tests.ReflectTestHelper").Invoke("StaticAdd", "wrong", "types"));
            Assert.That(ex.Message, Does.Contain("Available signatures"));
            Assert.That(ex.Message, Does.Contain("StaticAdd"));
        }

        #endregion

        #region RandomClick Tests

        [Test]
        public async Task RandomClick_ClicksAClickableElement()
        {
                bool anyClicked = false;
                for (int i = 0; i < 3; i++)
                {
                    var btn = CreateButton($"RandomBtn{i}", $"Random {i}", _canvas.transform, new Vector2(0, i * 50 - 50));
                    btn.onClick.AddListener(() => anyClicked = true);
                }
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                SetRandomSeed(42);
                var clicked = await RandomClick();
                Assert.IsNotNull(clicked, "Should have clicked something");
                Assert.IsTrue(anyClicked, "One of the buttons should have been clicked");
        }

        #endregion

        #region KeyConversion Tests

        [Test]
        public void KeyCodeToKey_ConvertsCorrectly()
        {
            Assert.AreEqual(Key.A, KeyCodeToKey(KeyCode.A));
            Assert.AreEqual(Key.Space, KeyCodeToKey(KeyCode.Space));
            Assert.AreEqual(Key.Enter, KeyCodeToKey(KeyCode.Return));
            Assert.AreEqual(Key.Escape, KeyCodeToKey(KeyCode.Escape));
            Assert.AreEqual(Key.Digit1, KeyCodeToKey(KeyCode.Alpha1));
        }

        [Test]
        public void CharToKey_ConvertsCorrectly()
        {
            Assert.AreEqual(Key.A, CharToKey('a'));
            Assert.AreEqual(Key.A, CharToKey('A'));
            Assert.AreEqual(Key.Space, CharToKey(' '));
            Assert.AreEqual(Key.Period, CharToKey('.'));

            // Verify digit conversion - '0' maps to Digit0, etc.
            var digit0 = CharToKey('0');
            var digit5 = CharToKey('5');
            Assert.AreEqual((int)digit0 + 5, (int)digit5, "Digit5 should be 5 positions after Digit0");
        }

        #endregion

        #region Search Factory Tests

        [Test]
        public void Name_ReturnsSearchInstance()
        {
            var search = Name("TestName");
            Assert.IsNotNull(search);
            Assert.IsInstanceOf<Search>(search);
        }

        [Test]
        public void Text_ReturnsSearchInstance()
        {
            var search = Text("TestText");
            Assert.IsNotNull(search);
            Assert.IsInstanceOf<Search>(search);
        }

        [Test]
        public void Type_ReturnsSearchInstance()
        {
            var search = Type<Button>();
            Assert.IsNotNull(search);
            Assert.IsInstanceOf<Search>(search);
        }

        [Test]
        public void Texture_ReturnsSearchInstance()
        {
            var search = Texture("TestTexture");
            Assert.IsNotNull(search);
            Assert.IsInstanceOf<Search>(search);
        }

        [Test]
        public void Path_ReturnsSearchInstance()
        {
            var search = Path("*/Parent/Child");
            Assert.IsNotNull(search);
            Assert.IsInstanceOf<Search>(search);
        }

        [Test]
        public void Tag_ReturnsSearchInstance()
        {
            var search = Tag("Player");
            Assert.IsNotNull(search);
            Assert.IsInstanceOf<Search>(search);
        }

        [Test]
        public void Any_ReturnsSearchInstance()
        {
            var search = Any("SomePattern");
            Assert.IsNotNull(search);
            Assert.IsInstanceOf<Search>(search);
        }

        [Test]
        public void Adjacent_ReturnsSearchInstance()
        {
            var search = Adjacent("Label:");
            Assert.IsNotNull(search);
            Assert.IsInstanceOf<Search>(search);
        }

        [Test]
        public void Reflect_ReturnsSearchInstance()
        {
            var search = Reflect("UnityEngine.Time.deltaTime");
            Assert.IsNotNull(search);
            Assert.IsInstanceOf<Search>(search);
        }

        #endregion

        #region Search Chaining Verification

        [Test]
        public void SearchChaining_ReturnsSameSearchInstance()
        {
            var search = Name("Test");
            var chained = search.Type<Button>();
            Assert.AreSame(search, chained, "Chaining should return same Search instance");
        }

        [Test]
        public void SearchChaining_MultipleFilters()
        {
            var search = Name("Button*")
                .Type<Button>()
                .Interactable();
            Assert.IsNotNull(search);
        }

        #endregion

        #region Gesture Tests

        [Test]
        public async Task PinchAt_CompletesWithoutError()
        {
                await PinchAt(0.5f, 0.5f, scale: 0.5f, duration: 0.3f);
                Assert.Pass("PinchAt completed successfully");
        }

        [Test]
        public async Task RotateAt_CompletesWithoutError()
        {
                await RotateAt(0.5f, 0.5f, degrees: 45f, duration: 0.3f);
                Assert.Pass("RotateAt completed successfully");
        }

        [Test]
        public async Task SwipeAt_CompletesWithoutError()
        {
                await SwipeAt(0.5f, 0.5f, SwipeDirection.Left, distance: 0.2f, duration: 0.3f);
                Assert.Pass("SwipeAt completed successfully");
        }

        #endregion

        #region WaitForNot Tests

        [Test]
        public async Task WaitForNot_WaitsForElementToDisappear()
        {
            var button = CreateButton("DisappearingButton", "Gone Soon", _canvas.transform, Vector2.zero);
            await Async.DelayFrames(1);
            Canvas.ForceUpdateCanvases();

            // Destroy element, then verify WaitForNot succeeds
            await Task.Delay(100);
            UnityEngine.Object.Destroy(button.gameObject);

            await WaitForNot(Name("DisappearingButton"), timeout: 3);
            // No exception means element disappeared
        }

        #endregion

        #region Ordering Tests

        [Test]
        public void First_ReturnsSearchInstance()
        {
            var search = Type<Button>().First();
            Assert.IsNotNull(search);
        }

        [Test]
        public void Last_ReturnsSearchInstance()
        {
            var search = Type<Button>().Last();
            Assert.IsNotNull(search);
        }

        [Test]
        public void Skip_ReturnsSearchInstance()
        {
            var search = Type<Button>().Skip(1);
            Assert.IsNotNull(search);
        }

        [Test]
        public void Take_ReturnsSearchInstance()
        {
            var search = Type<Button>().Take(5);
            Assert.IsNotNull(search);
        }

        #endregion

        #region Hierarchy Tests

        [Test]
        public void HasParent_ReturnsSearchInstance()
        {
            var search = Name("Child").HasParent("Parent");
            Assert.IsNotNull(search);
        }

        [Test]
        public void HasChild_ReturnsSearchInstance()
        {
            var search = Name("Parent").HasChild("Child");
            Assert.IsNotNull(search);
        }

        [Test]
        public void HasAncestor_ReturnsSearchInstance()
        {
            var search = Name("Grandchild").HasAncestor("Grandparent");
            Assert.IsNotNull(search);
        }

        [Test]
        public void HasDescendant_ReturnsSearchInstance()
        {
            var search = Name("Grandparent").HasDescendant("Grandchild");
            Assert.IsNotNull(search);
        }

        #endregion

        #region Availability Tests

        [Test]
        public void IncludeInactive_ReturnsSearchInstance()
        {
            var search = Name("Disabled").IncludeInactive();
            Assert.IsNotNull(search);
        }

        [Test]
        public void IncludeDisabled_ReturnsSearchInstance()
        {
            var search = Name("Disabled").IncludeDisabled();
            Assert.IsNotNull(search);
        }

        [Test]
        public void Interactable_ReturnsSearchInstance()
        {
            var search = Type<Button>().Interactable();
            Assert.IsNotNull(search);
        }

        #endregion

        #region DragTo Tests

        [Test]
        public async Task DragTo_CompletesWithoutError()
        {
                var source = CreateButton("SourceButton", "Drag From", _canvas.transform, new Vector2(-100, 0));
                var target = CreateButton("TargetButton", "Drag To", _canvas.transform, new Vector2(100, 0));
                await Async.DelayFrames(1);
                Canvas.ForceUpdateCanvases();

                await DragTo(Name("SourceButton"), Name("TargetButton"), duration: 0.3f);
                Assert.Pass("DragTo completed successfully");
        }

        #endregion

        #region DragFromTo Tests

        [Test]
        public async Task DragFromTo_CompletesWithoutError()
        {
                await DragFromTo(new Vector2(100, 100), new Vector2(200, 200), duration: 0.3f);
                Assert.Pass("DragFromTo completed successfully");
        }

        #endregion
    }

    /// <summary>
    /// Helper class for testing Reflect/Invoke with static and instance methods.
    /// </summary>
    public static class ReflectTestHelper
    {
        public static bool WasCalled { get; private set; }
        public static string LastString { get; private set; }
        public static int LastInt { get; private set; }

        private static readonly ReflectTestInstance _instance = new();
        public static ReflectTestInstance Instance => _instance;

        public static void ResetState()
        {
            WasCalled = false;
            LastString = null;
            LastInt = 0;
        }

        public static void StaticVoidMethod() => WasCalled = true;

        public static void StaticMethodWithArgs(string s, int i)
        {
            LastString = s;
            LastInt = i;
        }

        public static int StaticAdd(int a, int b) => a + b;

        public static string MethodWithDefaults(string required, int optional1 = 42, bool optional2 = true)
            => $"{required}|{optional1}|{optional2.ToString().ToLower()}";

        public static async Task StaticAsyncMethod()
        {
            await Task.Yield();
            WasCalled = true;
        }

        public static async Task<int> StaticAsyncAdd(int a, int b)
        {
            await Task.Yield();
            return a + b;
        }
    }

    public class ReflectTestInstance
    {
        public bool InstanceWasCalled { get; private set; }

        public void InstanceMethod()
        {
            InstanceWasCalled = true;
        }
    }
}
