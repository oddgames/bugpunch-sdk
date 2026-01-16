using System;
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
                    UnityEngine.Object.Destroy(obj);
            }
            _createdObjects.Clear();

            // Clean up static test data
            TestTrucks.PlayerTruck = null;
            TestTrucks.PlayerTrucks = null;
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
                var dragTask = ActionExecutor.DragFromToAsync(startScreen, endScreen, 1.0f, 0f); // Skip hold for faster test

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
                var dragTask = ActionExecutor.DragAsync(draggable, new Vector2(100, 0), 1.0f, 0f); // Skip hold for faster test

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
                await ActionExecutor.DragSliderAsync(slider, 0.2f, 0.9f, 1.0f, 0f); // Skip hold for faster test

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
                    buttons[i].onClick.AddListener(() =>
                    {
                        clickCounts[idx]++;
                        Debug.Log($"[TEST] Button {idx} clicked, count={clickCounts[idx]}");
                    });
                }

                // Force layout rebuild and wait for it to settle
                Canvas.ForceUpdateCanvases();
                await UniTask.DelayFrame(5);

                // Log button positions for debugging
                foreach (var btn in buttons)
                {
                    var pos = InputInjector.GetScreenPosition(btn.gameObject);
                    Debug.Log($"[TEST] Button '{btn.name}' at screen pos ({pos.x:F0}, {pos.y:F0})");
                }

                // Create helper to access protected method
                var helperGO = new GameObject("ClickScrollHelper");
                helperGO.transform.SetParent(_canvas.transform, false);
                var helper = helperGO.AddComponent<TestClickScrollHelper>();
                _createdObjects.Add(helperGO);

                await helper.TestClickScrollItems(new Search().Name("TestScrollView"));

                // Wait for any pending events
                await UniTask.DelayFrame(2);

                // Log final counts
                Debug.Log($"[TEST] Final counts: {clickCounts[0]}, {clickCounts[1]}, {clickCounts[2]}");

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

        #region Assertion Tests

        [UnityTest]
        public IEnumerator AssertExists_WithExistingElement_Passes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("ExistingButton", new Vector2(0, 0));
                await UniTask.Yield();

                ActionExecutor.AssertExists(new Search().Name("ExistingButton"));
                Assert.Pass("AssertExists passed for existing element");
            });
        }

        [UnityTest]
        public IEnumerator AssertExists_WithMissingElement_Throws()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                Assert.Throws<InvalidOperationException>(() =>
                {
                    ActionExecutor.AssertExists(new Search().Name("NonExistentButton"));
                });
            });
        }

        [UnityTest]
        public IEnumerator AssertNotExists_WithMissingElement_Passes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                ActionExecutor.AssertNotExists(new Search().Name("NonExistentButton"));
                Assert.Pass("AssertNotExists passed for missing element");
            });
        }

        [UnityTest]
        public IEnumerator AssertNotExists_WithExistingElement_Throws()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("ExistingButton", new Vector2(0, 0));
                await UniTask.Yield();

                Assert.Throws<InvalidOperationException>(() =>
                {
                    ActionExecutor.AssertNotExists(new Search().Name("ExistingButton"));
                });
            });
        }

        [UnityTest]
        public IEnumerator AssertText_WithMatchingText_Passes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var go = CreateTextElement("TestText", "Hello World", new Vector2(0, 0));
                await UniTask.Yield();

                ActionExecutor.AssertText(new Search().Name("TestText"), "Hello World");
                Assert.Pass("AssertText passed for matching text");
            });
        }

        [UnityTest]
        public IEnumerator AssertText_WithMismatchedText_Throws()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var go = CreateTextElement("TestText", "Hello World", new Vector2(0, 0));
                await UniTask.Yield();

                Assert.Throws<InvalidOperationException>(() =>
                {
                    ActionExecutor.AssertText(new Search().Name("TestText"), "Wrong Text");
                });
            });
        }

        [UnityTest]
        public IEnumerator AssertToggle_WithMatchingState_Passes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var toggle = CreateToggle("TestToggle", new Vector2(0, 0));
                toggle.isOn = true;
                await UniTask.Yield();

                ActionExecutor.AssertToggle(new Search().Name("TestToggle"), true);
                Assert.Pass("AssertToggle passed for matching state");
            });
        }

        [UnityTest]
        public IEnumerator AssertToggle_WithMismatchedState_Throws()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var toggle = CreateToggle("TestToggle", new Vector2(0, 0));
                toggle.isOn = false;
                await UniTask.Yield();

                Assert.Throws<InvalidOperationException>(() =>
                {
                    ActionExecutor.AssertToggle(new Search().Name("TestToggle"), true);
                });
            });
        }

        [UnityTest]
        public IEnumerator AssertSlider_WithMatchingValue_Passes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var slider = CreateSlider("TestSlider", new Vector2(0, 0));
                slider.value = 0.5f;
                await UniTask.Yield();

                ActionExecutor.AssertSlider(new Search().Name("TestSlider"), 0.5f);
                Assert.Pass("AssertSlider passed for matching value");
            });
        }

        [UnityTest]
        public IEnumerator AssertSlider_WithMismatchedValue_Throws()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var slider = CreateSlider("TestSlider", new Vector2(0, 0));
                slider.value = 0.5f;
                await UniTask.Yield();

                Assert.Throws<InvalidOperationException>(() =>
                {
                    ActionExecutor.AssertSlider(new Search().Name("TestSlider"), 0.9f);
                });
            });
        }

        [UnityTest]
        public IEnumerator AssertInteractable_WithInteractableElement_Passes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("InteractableButton", new Vector2(0, 0));
                button.interactable = true;
                await UniTask.Yield();

                ActionExecutor.AssertInteractable(new Search().Name("InteractableButton"), true);
                Assert.Pass("AssertInteractable passed for interactable element");
            });
        }

        [UnityTest]
        public IEnumerator AssertInteractable_WithNonInteractableElement_Throws()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("NonInteractableButton", new Vector2(0, 0));
                button.interactable = false;
                await UniTask.Yield();

                Assert.Throws<InvalidOperationException>(() =>
                {
                    ActionExecutor.AssertInteractable(new Search().Name("NonInteractableButton"), true);
                });
            });
        }

        #endregion

        #region Static Path Assertion Tests

        [UnityTest]
        public IEnumerator Assert_WithTruthyStaticPath_Passes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Use Application.isPlaying which is always true in play mode tests
                await UniTask.Yield();

                ActionExecutor.Assert("Application.isPlaying");
                Assert.Pass("Assert passed for truthy static path");
            });
        }

        [UnityTest]
        public IEnumerator Assert_WithFalsyStaticPath_Throws()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Use Application.isBatchMode which should be false in editor tests
                await UniTask.Yield();

                // We need a falsy path - using Screen.fullScreen which is likely false in editor
                // Note: This test depends on editor state, may need adjustment
                try
                {
                    ActionExecutor.Assert("Screen.fullScreen");
                    // If we get here, fullScreen was true - skip the test
                    if (Screen.fullScreen)
                        Assert.Pass("Screen was full screen, test skipped");
                    else
                        Assert.Fail("Assert should have thrown for falsy path");
                }
                catch (InvalidOperationException)
                {
                    Assert.Pass("Assert correctly threw for falsy static path");
                }
            });
        }

        [UnityTest]
        public IEnumerator AssertGeneric_WithMatchingValue_Passes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Use Application.platform with expected value
                ActionExecutor.Assert("Application.isPlaying", true);
                Assert.Pass("Assert<T> passed for matching value");
            });
        }

        [UnityTest]
        public IEnumerator AssertGreater_WithValidComparison_Passes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Screen.width should be > 0
                ActionExecutor.AssertGreater("Screen.width", 0);
                Assert.Pass("AssertGreater passed");
            });
        }

        [UnityTest]
        public IEnumerator AssertGreater_WithInvalidComparison_Throws()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                Assert.Throws<InvalidOperationException>(() =>
                {
                    // Screen.width should not be > 100000
                    ActionExecutor.AssertGreater("Screen.width", 100000);
                });
            });
        }

        [UnityTest]
        public IEnumerator AssertLess_WithValidComparison_Passes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Screen.width should be < 100000
                ActionExecutor.AssertLess("Screen.width", 100000);
                Assert.Pass("AssertLess passed");
            });
        }

        [UnityTest]
        public IEnumerator AssertLess_WithInvalidComparison_Throws()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                Assert.Throws<InvalidOperationException>(() =>
                {
                    // Screen.width should not be < 0
                    ActionExecutor.AssertLess("Screen.width", 0);
                });
            });
        }

        #endregion

        #region WaitFor Tests

        [UnityTest]
        public IEnumerator WaitFor_WithExistingElement_ReturnsTrue()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("WaitForButton", new Vector2(0, 0));
                await UniTask.Yield();

                var result = await ActionExecutor.WaitFor(new Search().Name("WaitForButton"), timeout: 1f);
                Assert.IsTrue(result, "WaitFor should return true for existing element");
            });
        }

        [UnityTest]
        public IEnumerator WaitFor_WithMissingElement_ReturnsFalse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var result = await ActionExecutor.WaitFor(new Search().Name("NonExistent"), timeout: 0.3f);
                Assert.IsFalse(result, "WaitFor should return false for missing element after timeout");
            });
        }

        [UnityTest]
        public IEnumerator WaitFor_WithDelayedElement_WaitsAndReturnsTrue()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Start wait and create button after a delay
                var waitTask = ActionExecutor.WaitFor(new Search().Name("DelayedButton"), timeout: 2f);

                // Create button after 200ms
                await UniTask.Delay(200);
                CreateButton("DelayedButton", new Vector2(0, 0));

                var result = await waitTask;
                Assert.IsTrue(result, "WaitFor should return true when element appears before timeout");
            });
        }

        [UnityTest]
        public IEnumerator WaitForText_WithMatchingText_ReturnsTrue()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateTextElement("WaitText", "Expected Text", new Vector2(0, 0));
                await UniTask.Yield();

                var result = await ActionExecutor.WaitFor(new Search().Name("WaitText"), "Expected Text", timeout: 1f);
                Assert.IsTrue(result, "WaitFor should return true for matching text");
            });
        }

        [UnityTest]
        public IEnumerator WaitForText_WithMismatchedText_ReturnsFalse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateTextElement("WaitText", "Actual Text", new Vector2(0, 0));
                await UniTask.Yield();

                var result = await ActionExecutor.WaitFor(new Search().Name("WaitText"), "Wrong Text", timeout: 0.3f);
                Assert.IsFalse(result, "WaitFor should return false for mismatched text");
            });
        }

        [UnityTest]
        public IEnumerator WaitForToggle_WithMatchingState_ReturnsTrue()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var toggle = CreateToggle("WaitToggle", new Vector2(0, 0));
                toggle.isOn = true;
                await UniTask.Yield();

                var result = await ActionExecutor.WaitFor(new Search().Name("WaitToggle"), true, timeout: 1f);
                Assert.IsTrue(result, "WaitFor should return true for matching toggle state");
            });
        }

        [UnityTest]
        public IEnumerator WaitForNot_WithMissingElement_ReturnsTrue()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var result = await ActionExecutor.WaitForNot(new Search().Name("NonExistent"), timeout: 1f);
                Assert.IsTrue(result, "WaitForNot should return true when element doesn't exist");
            });
        }

        [UnityTest]
        public IEnumerator WaitForNot_WithExistingElement_ReturnsFalse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateButton("ExistingButton", new Vector2(0, 0));
                await UniTask.Yield();

                var result = await ActionExecutor.WaitForNot(new Search().Name("ExistingButton"), timeout: 0.3f);
                Assert.IsFalse(result, "WaitForNot should return false when element exists");
            });
        }

        [UnityTest]
        public IEnumerator WaitForNot_WithRemovedElement_ReturnsTrue()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("ToBeRemoved", new Vector2(0, 0));
                await UniTask.Yield();

                // Start wait and destroy button after a delay
                var waitTask = ActionExecutor.WaitForNot(new Search().Name("ToBeRemoved"), timeout: 2f);

                // Destroy after 200ms
                await UniTask.Delay(200);
                UnityEngine.Object.Destroy(button.gameObject);

                var result = await waitTask;
                Assert.IsTrue(result, "WaitForNot should return true when element is removed");
            });
        }

        #endregion

        #region StaticPath - Basic Resolution

        [UnityTest]
        public IEnumerator StaticPath_ResolvesObject()
        {
            return UniTask.ToCoroutine(async () =>
            {
                TestTrucks.PlayerTruck = new TruckController { Name = "MainTruck", Health = 100f };
                await UniTask.Yield();

                var path = Search.Static("TestTrucks.PlayerTruck");
                Assert.IsNotNull(path.Value);
                Assert.AreEqual("MainTruck", path.GetValue<string>("Name"));
                Assert.AreEqual(100f, path.GetValue<float>("Health"));
            });
        }

        [UnityTest]
        public IEnumerator StaticPath_ResolvesNestedProperty()
        {
            return UniTask.ToCoroutine(async () =>
            {
                TestTrucks.PlayerTruck = new TruckController
                {
                    Name = "MainTruck",
                    DamageController = new DamageController { MaxHealth = 150f, IsDamaged = true }
                };
                await UniTask.Yield();

                var damage = Search.Static("TestTrucks.PlayerTruck").Property("DamageController");
                Assert.AreEqual(150f, damage.GetValue<float>("MaxHealth"));
                Assert.IsTrue(damage.GetValue<bool>("IsDamaged"));
            });
        }

        #endregion

        #region StaticPath - Value Properties

        [UnityTest]
        public IEnumerator StaticPath_AllBasicTypes()
        {
            return UniTask.ToCoroutine(async () =>
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
                await UniTask.Yield();

                var truck = Search.Static("TestTrucks.PlayerTruck");

                // String
                Assert.AreEqual("TestTruck", truck.Property("Name").StringValue);

                // Bool
                Assert.IsTrue(truck.Property("IsActive").BoolValue);

                // Int
                Assert.AreEqual(42, truck.Property("Count").IntValue);

                // Float
                Assert.AreEqual(85.5f, truck.Property("Health").FloatValue, 0.01f);

                // Double via FloatValue
                Assert.AreEqual(3.14f, truck.Property("Precision").FloatValue, 0.01f);

                // Long via IntValue (truncates)
                Assert.AreEqual(9876543210L, truck.Property("BigNumber").GetValue<long>());
            });
        }

        [UnityTest]
        public IEnumerator StaticPath_ArrayProperties()
        {
            return UniTask.ToCoroutine(async () =>
            {
                TestTrucks.PlayerTruck = new TruckController
                {
                    Tags = new[] { "fast", "red", "turbo" },
                    Scores = new[] { 100, 200, 300 },
                    Multipliers = new[] { 1.5f, 2.0f, 2.5f }
                };
                await UniTask.Yield();

                var truck = Search.Static("TestTrucks.PlayerTruck");

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
            });
        }

        [UnityTest]
        public IEnumerator StaticPath_BoolValue_OnNonBool_ThrowsException()
        {
            return UniTask.ToCoroutine(async () =>
            {
                TestTrucks.PlayerTruck = new TruckController { Name = "TestTruck" };
                await UniTask.Yield();

                // BoolValue on a string should throw InvalidOperationException
                Assert.Throws<System.InvalidOperationException>(() =>
                {
                    var _ = Search.Static("TestTrucks.PlayerTruck").Property("Name").BoolValue;
                });
            });
        }

        [UnityTest]
        public IEnumerator StaticPath_AccessesPrivateField()
        {
            return UniTask.ToCoroutine(async () =>
            {
                TestTrucks.PlayerTruck = new TruckController();
                TestTrucks.PlayerTruck.SetSecretCode("SECRET");
                await UniTask.Yield();

                Assert.AreEqual("SECRET", Search.Static("TestTrucks.PlayerTruck").Property("_secretCode").StringValue);
            });
        }

        #endregion

        #region StaticPath - Array Iteration

        [UnityTest]
        public IEnumerator StaticPath_IteratesArray()
        {
            return UniTask.ToCoroutine(async () =>
            {
                TestTrucks.PlayerTrucks = new[]
                {
                    new TruckController { Name = "Truck1" },
                    new TruckController { Name = "Truck2" },
                    new TruckController { Name = "Truck3" }
                };
                await UniTask.Yield();

                var names = new List<string>();
                foreach (var truck in Search.Static("TestTrucks.PlayerTrucks"))
                {
                    names.Add(truck.GetValue<string>("Name"));
                }

                Assert.AreEqual(3, names.Count);
                Assert.Contains("Truck1", names);
                Assert.Contains("Truck2", names);
                Assert.Contains("Truck3", names);
            });
        }

        [UnityTest]
        public IEnumerator StaticPath_IteratesWithNestedAccess()
        {
            return UniTask.ToCoroutine(async () =>
            {
                TestTrucks.PlayerTrucks = new[]
                {
                    new TruckController { DamageController = new DamageController { MaxHealth = 100f } },
                    new TruckController { DamageController = new DamageController { MaxHealth = 200f } }
                };
                await UniTask.Yield();

                float total = 0f;
                foreach (var truck in Search.Static("TestTrucks.PlayerTrucks"))
                {
                    total += truck.Property("DamageController").GetValue<float>("MaxHealth");
                }

                Assert.AreEqual(300f, total, 0.01f);
            });
        }

        #endregion

        #region GetValue - Static Paths

        [UnityTest]
        public IEnumerator GetValue_ResolvesStaticPath()
        {
            return UniTask.ToCoroutine(async () =>
            {
                TestTrucks.PlayerTruck = new TruckController { Name = "GetValueTruck", Health = 42f };
                await UniTask.Yield();

                Assert.AreEqual("GetValueTruck", ActionExecutor.GetValue<string>("TestTrucks.PlayerTruck.Name"));
                Assert.AreEqual(42f, ActionExecutor.GetValue<float>("TestTrucks.PlayerTruck.Health"), 0.01f);
            });
        }

        #endregion

        #region Static Path WaitFor Tests

        [UnityTest]
        public IEnumerator WaitForStaticPath_WithTruthyValue_ReturnsTrue()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var result = await ActionExecutor.WaitFor("Application.isPlaying", timeout: 1f);
                Assert.IsTrue(result, "WaitFor should return true for truthy static path");
            });
        }

        [UnityTest]
        public IEnumerator WaitForStaticPathGeneric_WithMatchingValue_ReturnsTrue()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var result = await ActionExecutor.WaitFor("Application.isPlaying", true, timeout: 1f);
                Assert.IsTrue(result, "WaitFor<T> should return true for matching value");
            });
        }

        [UnityTest]
        public IEnumerator WaitForStaticPathGeneric_WithMismatchedValue_ReturnsFalse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var result = await ActionExecutor.WaitFor("Application.isPlaying", false, timeout: 0.3f);
                Assert.IsFalse(result, "WaitFor<T> should return false for mismatched value");
            });
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
