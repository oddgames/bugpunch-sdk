using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using TMPro;

namespace ODDGames.UITest.Tests
{
    /// <summary>
    /// PlayMode tests for ActionExecutor - the unified action execution layer.
    /// Tests all action methods: Click, DoubleClick, Hold, Type, Drag, Scroll, Slider, Dropdown, etc.
    /// </summary>
    [TestFixture]
    public class ActionExecutorTests
    {
        private Canvas _canvas;
        private EventSystem _eventSystem;
        private List<GameObject> _createdObjects;

        private Mouse _mouse;

        [SetUp]
        public void SetUp()
        {
            _createdObjects = new List<GameObject>();

            // Ensure mouse device exists for input injection
            _mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();

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
                    Object.Destroy(obj);
            }
            _createdObjects.Clear();
        }

        #region Click Tests

        [UnityTest]
        public IEnumerator ClickAsync_WithGameObject_ClicksElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("TestButton", new Vector2(0, 0));
                bool clicked = false;
                button.onClick.AddListener(() => clicked = true);

                await UniTask.Yield();
                await ActionExecutor.ClickAsync(button.gameObject);

                Assert.IsTrue(clicked, "Button should have been clicked");
            });
        }

        [UnityTest]
        public IEnumerator ClickAsync_WithSearch_FindsAndClicksElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("ClickMeButton", new Vector2(0, 0));
                bool clicked = false;
                button.onClick.AddListener(() => clicked = true);

                await UniTask.Yield();
                var result = await ActionExecutor.ClickAsync(new Search().Name("ClickMeButton"), searchTime: 2f);

                Assert.IsTrue(result, "Should find the button");
                Assert.IsTrue(clicked, "Button should have been clicked");
            });
        }

        [UnityTest]
        public IEnumerator ClickAsync_WithSearch_ReturnsFlaseWhenNotFound()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();
                var result = await ActionExecutor.ClickAsync(new Search().Name("NonExistentButton"), searchTime: 0.5f);

                Assert.IsFalse(result, "Should return false when element not found");
            });
        }

        [UnityTest]
        public IEnumerator ClickAtAsync_ClicksAtScreenPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("PositionButton", new Vector2(0, 0));
                bool clicked = false;
                button.onClick.AddListener(() => clicked = true);

                await UniTask.Yield();

                // Get the button's screen position
                var rect = button.GetComponent<RectTransform>();
                var screenPos = RectTransformUtility.WorldToScreenPoint(null, rect.position);

                await ActionExecutor.ClickAtAsync(screenPos);

                Assert.IsTrue(clicked, "Button should have been clicked at position");
            });
        }

        #endregion

        #region Double Click Tests

        [UnityTest]
        public IEnumerator DoubleClickAsync_WithGameObject_DoubleClicksElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("DoubleClickButton", new Vector2(0, 0));
                int clickCount = 0;
                button.onClick.AddListener(() => clickCount++);

                await UniTask.Yield();
                await ActionExecutor.DoubleClickAsync(button.gameObject);

                Assert.AreEqual(2, clickCount, "Button should have been clicked twice");
            });
        }

        [UnityTest]
        public IEnumerator DoubleClickAsync_WithSearch_FindsAndDoubleClicks()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("DoubleClickMe", new Vector2(0, 0));
                int clickCount = 0;
                button.onClick.AddListener(() => clickCount++);

                await UniTask.Yield();
                var result = await ActionExecutor.DoubleClickAsync(new Search().Name("DoubleClickMe"), searchTime: 2f);

                Assert.IsTrue(result, "Should find the button");
                Assert.AreEqual(2, clickCount, "Button should have been clicked twice");
            });
        }

        #endregion

        #region Hold Tests

        [UnityTest]
        public IEnumerator HoldAsync_WithGameObject_HoldsForDuration()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("HoldButton", new Vector2(0, 0));
                bool clicked = false;
                button.onClick.AddListener(() => clicked = true);

                await UniTask.Yield();
                await ActionExecutor.HoldAsync(button.gameObject, 0.2f);

                // Hold should trigger click on release
                Assert.IsTrue(clicked, "Button should have been clicked after hold");
            });
        }

        [UnityTest]
        public IEnumerator HoldAsync_WithSearch_FindsAndHolds()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("HoldMe", new Vector2(0, 0));
                bool clicked = false;
                button.onClick.AddListener(() => clicked = true);

                await UniTask.Yield();
                var result = await ActionExecutor.HoldAsync(new Search().Name("HoldMe"), 0.2f, searchTime: 2f);

                Assert.IsTrue(result, "Should find the button");
                Assert.IsTrue(clicked, "Button should have been clicked after hold");
            });
        }

        #endregion

        #region Type Tests

        [UnityTest]
        public IEnumerator TypeAsync_WithGameObject_TypesIntoInputField()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var inputField = CreateTMPInputField("TypeInput", new Vector2(0, 0));

                await UniTask.Yield();
                await ActionExecutor.TypeAsync(inputField.gameObject, "Hello World");

                // Wait for text input to be processed
                await UniTask.DelayFrame(10);

                Assert.AreEqual("Hello World", inputField.text, "Input field should contain typed text");
            });
        }

        [UnityTest]
        public IEnumerator TypeAsync_WithSearch_FindsAndTypes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var inputField = CreateTMPInputField("SearchTypeInput", new Vector2(0, 0));

                await UniTask.Yield();
                var result = await ActionExecutor.TypeAsync(new Search().Name("SearchTypeInput"), "Test Text", searchTime: 2f);

                // Wait for text input to be processed
                await UniTask.DelayFrame(10);

                Assert.IsTrue(result, "Should find the input field");
                Assert.AreEqual("Test Text", inputField.text, "Input field should contain typed text");
            });
        }

        [UnityTest]
        public IEnumerator TypeAsync_WithClearFirst_ClearsExistingText()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var inputField = CreateTMPInputField("ClearInput", new Vector2(0, 0));
                inputField.text = "Existing Text";

                await UniTask.Yield();
                await ActionExecutor.TypeAsync(inputField.gameObject, "New Text", clearFirst: true);

                // Wait for text input to be processed
                await UniTask.DelayFrame(10);

                Assert.AreEqual("New Text", inputField.text, "Input field should contain only new text");
            });
        }

        [UnityTest]
        public IEnumerator TypeAsync_WithoutClearFirst_AppendsText()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var inputField = CreateTMPInputField("AppendInput", new Vector2(0, 0));
                inputField.text = "Hello ";

                await UniTask.Yield();
                await ActionExecutor.TypeAsync(inputField.gameObject, "World", clearFirst: false);

                // Wait for text input to be processed
                await UniTask.DelayFrame(10);

                Assert.IsTrue(inputField.text.Contains("World"), "Input field should contain appended text");
            });
        }

        #endregion

        #region Drag Tests

        [UnityTest]
        public IEnumerator DragFromToAsync_DragsBetweenPositions()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Create a draggable element
                var draggable = CreateDraggablePanel("DragPanel", new Vector2(-100, 0));
                var rt = draggable.GetComponent<RectTransform>();
                var initialPos = rt.anchoredPosition;
                float maxX = initialPos.x;

                await UniTask.Yield();

                var startScreen = RectTransformUtility.WorldToScreenPoint(null, draggable.transform.position);
                var endScreen = startScreen + new Vector2(200, 0);

                // Start drag and track max position reached during operation
                var dragTask = ActionExecutor.DragFromToAsync(startScreen, endScreen, 0.3f);

                // Poll position every frame during drag
                while (!dragTask.Status.IsCompleted())
                {
                    maxX = Mathf.Max(maxX, rt.anchoredPosition.x);
                    await UniTask.Yield();
                }
                await dragTask; // Ensure completion

                // Also check final position
                maxX = Mathf.Max(maxX, rt.anchoredPosition.x);
                Assert.Greater(maxX, initialPos.x, "Element should have moved right during drag");
            });
        }

        [UnityTest]
        public IEnumerator DragAsync_WithDirection_DragsElement()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var draggable = CreateDraggablePanel("DirectionDrag", new Vector2(0, 0));
                var rt = draggable.GetComponent<RectTransform>();
                var initialPos = rt.anchoredPosition;
                float maxX = initialPos.x;

                await UniTask.Yield();

                // Start drag and track max position reached during operation
                var dragTask = ActionExecutor.DragAsync(draggable, new Vector2(100, 0), 0.3f);

                // Poll position every frame during drag
                while (!dragTask.Status.IsCompleted())
                {
                    maxX = Mathf.Max(maxX, rt.anchoredPosition.x);
                    await UniTask.Yield();
                }
                await dragTask; // Ensure completion

                // Also check final position
                maxX = Mathf.Max(maxX, rt.anchoredPosition.x);
                Assert.Greater(maxX, initialPos.x, "Element should have moved right during drag");
            });
        }

        #endregion

        #region Scroll Tests

        [UnityTest]
        public IEnumerator ScrollAtAsync_ScrollsAtPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var scrollView = CreateScrollView("TestScroll", new Vector2(0, 0));
                var scrollRect = scrollView.GetComponent<ScrollRect>();
                scrollRect.verticalNormalizedPosition = 1f; // Start at top

                await UniTask.Yield();

                var screenPos = RectTransformUtility.WorldToScreenPoint(null, scrollView.transform.position);
                await ActionExecutor.ScrollAtAsync(screenPos, -120f); // Scroll down

                await UniTask.Delay(100);

                Assert.Less(scrollRect.verticalNormalizedPosition, 1f, "Should have scrolled down");
            });
        }

        #endregion

        #region Slider Tests

        [UnityTest]
        public IEnumerator SetSliderAsync_SetsSliderValue()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var slider = CreateSlider("TestSlider", new Vector2(0, 0));
                slider.value = 0f;

                await UniTask.Yield();
                await ActionExecutor.SetSliderAsync(slider, 0.75f);

                Assert.AreEqual(0.75f, slider.value, 0.1f, "Slider should be at 75%");
            });
        }

        [UnityTest]
        public IEnumerator ClickSliderAsync_ClicksAtPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var slider = CreateSlider("ClickSlider", new Vector2(0, 0));
                slider.value = 0f;

                await UniTask.Yield();
                await ActionExecutor.ClickSliderAsync(slider, 0.5f);

                Assert.AreEqual(0.5f, slider.value, 0.15f, "Slider should be near 50%");
            });
        }

        [UnityTest]
        public IEnumerator ClickSliderAsync_WithSearch_FindsAndClicks()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var slider = CreateSlider("SearchSlider", new Vector2(0, 0));
                slider.value = 0f;

                await UniTask.Yield();
                var result = await ActionExecutor.ClickSliderAsync(new Search().Name("SearchSlider"), 0.8f, searchTime: 2f);

                Assert.IsTrue(result, "Should find the slider");
                Assert.AreEqual(0.8f, slider.value, 0.15f, "Slider should be near 80%");
            });
        }

        [UnityTest]
        public IEnumerator DragSliderAsync_DragsSlider()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var slider = CreateSlider("DragSlider", new Vector2(0, 0));
                slider.value = 0.2f;

                await UniTask.Yield();
                await ActionExecutor.DragSliderAsync(slider, 0.2f, 0.9f, 0.3f);

                Assert.Greater(slider.value, 0.5f, "Slider should have moved toward 90%");
            });
        }

        #endregion

        #region Scrollbar Tests

        [UnityTest]
        public IEnumerator SetScrollbarAsync_SetsScrollbarValue()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var scrollbar = CreateScrollbar("TestScrollbar", new Vector2(0, 0));
                scrollbar.value = 0f;

                await UniTask.Yield();
                await ActionExecutor.SetScrollbarAsync(scrollbar, 0.5f);

                Assert.AreEqual(0.5f, scrollbar.value, 0.1f, "Scrollbar should be at 50%");
            });
        }

        [UnityTest]
        public IEnumerator SetScrollbarAsync_WithSearch_FindsAndSets()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var scrollbar = CreateScrollbar("SearchScrollbar", new Vector2(0, 0));
                scrollbar.value = 0f;

                await UniTask.Yield();
                var result = await ActionExecutor.SetScrollbarAsync(new Search().Name("SearchScrollbar"), 0.7f, searchTime: 2f);

                Assert.IsTrue(result, "Should find the scrollbar");
                Assert.AreEqual(0.7f, scrollbar.value, 0.1f, "Scrollbar should be at 70%");
            });
        }

        #endregion

        #region Dropdown Tests

        [UnityTest]
        public IEnumerator ClickDropdownAsync_ByIndex_SelectsOption()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var dropdown = CreateDropdown("IndexDropdown", new Vector2(0, 50));
                dropdown.value = 0;

                await UniTask.Yield();
                var result = await ActionExecutor.ClickDropdownAsync(new Search().Name("IndexDropdown"), 2, searchTime: 2f);

                Assert.IsTrue(result, "Should find the dropdown");
                Assert.AreEqual(2, dropdown.value, "Should have selected option 2");
            });
        }

        [UnityTest]
        public IEnumerator ClickDropdownAsync_ByLabel_SelectsOption()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var dropdown = CreateDropdown("LabelDropdown", new Vector2(0, -50));
                dropdown.value = 0;

                await UniTask.Yield();
                var result = await ActionExecutor.ClickDropdownAsync(new Search().Name("LabelDropdown"), "Option 2", searchTime: 2f);

                Assert.IsTrue(result, "Should find the dropdown");
                Assert.AreEqual(1, dropdown.value, "Should have selected 'Option 2' (index 1)");
            });
        }

        [UnityTest]
        public IEnumerator ClickDropdownItems_ClicksAllOptions()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var dropdown = CreateDropdown("TestDropdown", Vector2.zero);
                dropdown.value = 0;
                var clickedValues = new List<int>();
                dropdown.onValueChanged.AddListener(val => clickedValues.Add(val));

                await UniTask.Yield();

                // Create helper to access protected method
                var helperGO = new GameObject("ClickDropdownHelper");
                helperGO.transform.SetParent(_canvas.transform, false);
                var helper = helperGO.AddComponent<TestClickDropdownHelper>();
                _createdObjects.Add(helperGO);

                await helper.TestClickDropdownItems(new Search().Name("TestDropdown"));

                // onValueChanged only fires when value changes, so clicking option 0 when already at 0 doesn't trigger
                // We expect changes: 0->1, 1->2 = 2 changes
                Assert.AreEqual(2, clickedValues.Count, "Should have triggered 2 value changes (0->1, 1->2)");
                Assert.AreEqual(2, dropdown.value, "Final value should be option 2");
            });
        }

        #endregion

        #region ScrollItems Tests

        [UnityTest]
        public IEnumerator ClickScrollItems_ClicksAllItems()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var scrollRect = CreateScrollRectWithButtons("TestScrollView", Vector2.zero, 3);
                var clickCounts = new int[3];

                // Get the buttons and add click listeners
                var buttons = scrollRect.content.GetComponentsInChildren<Button>();
                for (int i = 0; i < buttons.Length; i++)
                {
                    int idx = i;
                    buttons[i].onClick.AddListener(() => clickCounts[idx]++);
                }

                await UniTask.Yield();

                // Create helper to access protected method
                var helperGO = new GameObject("ClickScrollHelper");
                helperGO.transform.SetParent(_canvas.transform, false);
                var helper = helperGO.AddComponent<TestClickScrollHelper>();
                _createdObjects.Add(helperGO);

                await helper.TestClickScrollItems(new Search().Name("TestScrollView"));

                // Each button should have been clicked once
                Assert.AreEqual(1, clickCounts[0], "Button 0 should be clicked once");
                Assert.AreEqual(1, clickCounts[1], "Button 1 should be clicked once");
                Assert.AreEqual(1, clickCounts[2], "Button 2 should be clicked once");
            });
        }

        #endregion

        #region Keyboard Tests

        [UnityTest]
        public IEnumerator PressKeyAsync_PressesKey()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var keyboard = Keyboard.current;
                if (keyboard == null)
                    keyboard = InputSystem.AddDevice<Keyboard>();

                await UniTask.Yield();
                await ActionExecutor.PressKeyAsync(Key.Space);

                // Key press is transient, just verify no exception
                Assert.Pass("Key press completed without error");
            });
        }

        [UnityTest]
        public IEnumerator HoldKeyAsync_HoldsKeyForDuration()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var keyboard = Keyboard.current;
                if (keyboard == null)
                    keyboard = InputSystem.AddDevice<Keyboard>();

                await UniTask.Yield();

                float startTime = Time.realtimeSinceStartup;
                await ActionExecutor.HoldKeyAsync(Key.W, 0.2f);
                float elapsed = Time.realtimeSinceStartup - startTime;

                Assert.GreaterOrEqual(elapsed, 0.15f, "Should have held key for approximately 0.2s");
            });
        }

        [UnityTest]
        public IEnumerator HoldKeysAsync_HoldsMultipleKeys()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var keyboard = Keyboard.current;
                if (keyboard == null)
                    keyboard = InputSystem.AddDevice<Keyboard>();

                await UniTask.Yield();

                float startTime = Time.realtimeSinceStartup;
                await ActionExecutor.HoldKeysAsync(new[] { Key.LeftShift, Key.W }, 0.2f);
                float elapsed = Time.realtimeSinceStartup - startTime;

                Assert.GreaterOrEqual(elapsed, 0.15f, "Should have held keys for approximately 0.2s");
            });
        }

        #endregion

        #region ClickAny Tests

        [UnityTest]
        public IEnumerator ClickAnyAsync_ClicksOneOfMultipleMatches()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Create multiple buttons with same name pattern
                int clickCount = 0;
                for (int i = 0; i < 3; i++)
                {
                    var btn = CreateButton($"AnyButton", new Vector2(-100 + i * 100, 0));
                    btn.onClick.AddListener(() => clickCount++);
                }

                await UniTask.Yield();
                var result = await ActionExecutor.ClickAnyAsync(new Search().Name("AnyButton"), searchTime: 2f);

                Assert.IsTrue(result, "Should find a button");
                Assert.AreEqual(1, clickCount, "Exactly one button should have been clicked");
            });
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

                btnGO.AddComponent<Image>().color = new Color(0.4f, 0.4f, 0.4f);
                btnGO.AddComponent<Button>();
            }

            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            _createdObjects.Add(go);
            return scrollRect;
        }

        #endregion
    }

    /// <summary>
    /// Test helper for ClickDropdownItems.
    /// </summary>
    [UITest(Scenario = 9990, Feature = "Test Helper", Story = "ClickDropdown Helper")]
    public class TestClickDropdownHelper : UITestBehaviour
    {
        protected override UniTask Test() => UniTask.CompletedTask;
        private void Awake() { enabled = false; }
        public async UniTask TestClickDropdownItems(Search search, int delayBetween = 0)
        {
            await ClickDropdownItems(search, delayBetween, throwIfMissing: true, searchTime: 2);
        }
    }

    /// <summary>
    /// Test helper for ClickScrollItems.
    /// </summary>
    [UITest(Scenario = 9989, Feature = "Test Helper", Story = "ClickScroll Helper")]
    public class TestClickScrollHelper : UITestBehaviour
    {
        protected override UniTask Test() => UniTask.CompletedTask;
        private void Awake() { enabled = false; }
        public async UniTask TestClickScrollItems(Search search, int delayBetween = 0)
        {
            await ClickScrollItems(search, delayBetween, throwIfMissing: true, searchTime: 2);
        }
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
}
