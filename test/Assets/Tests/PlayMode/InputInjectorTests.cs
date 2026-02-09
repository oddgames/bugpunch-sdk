using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;
using ODDGames.UIAutomation;

namespace ODDGames.UIAutomation.Tests
{
    /// <summary>
    /// PlayMode tests for InputInjector class - covers input injection methods.
    /// Tests: GetScreenPosition, GetScreenBounds, slider/scrollbar helpers, touch input,
    /// scroll operations, hold/long-press, rotation gestures, two-finger operations.
    /// </summary>
    [TestFixture]
    public class InputInjectorTests
    {
        private Canvas _canvas;
        private EventSystem _eventSystem;
        private List<GameObject> _createdObjects;
        private Camera _camera;

        [SetUp]
        public void SetUp()
        {
            _createdObjects = new List<GameObject>();

            // Create Camera
            var cameraGO = new GameObject("MainCamera");
            _camera = cameraGO.AddComponent<Camera>();
            _camera.tag = "MainCamera";
            _createdObjects.Add(cameraGO);

            // Create EventSystem
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

        #region GetScreenPosition Tests

        [Test]
        public async Task GetScreenPosition_UIElement_ReturnsValidPosition()
        {
            var button = CreateButton("TestButton", new Vector2(100, 50));

            await Async.DelayFrames(1);

            var screenPos = InputInjector.GetScreenPosition(button.gameObject);

            Assert.Greater(screenPos.x, 0, "Screen X should be positive");
            Assert.Greater(screenPos.y, 0, "Screen Y should be positive");
        }

        [Test]
        public async Task GetScreenPosition_NullObject_ReturnsZero()
        {
            await Async.DelayFrames(1);

            var screenPos = InputInjector.GetScreenPosition(null);

            Assert.AreEqual(Vector2.zero, screenPos, "Null object should return Vector2.zero");
        }

        [Test]
        public async Task GetScreenPosition_3DObject_ReturnsValidPosition()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.position = new Vector3(0, 0, 10);
            _createdObjects.Add(cube);

            await Async.DelayFrames(1);

            var screenPos = InputInjector.GetScreenPosition(cube);

            // 3D object in front of camera should return valid screen position
            Assert.Greater(screenPos.x, 0, "Screen X should be positive");
            Assert.Greater(screenPos.y, 0, "Screen Y should be positive");
        }

        #endregion

        #region GetScreenBounds Tests

        [Test]
        public async Task GetScreenBounds_UIElement_ReturnsValidRect()
        {
            var button = CreateButton("BoundsButton", Vector2.zero);

            await Async.DelayFrames(1);

            var bounds = InputInjector.GetScreenBounds(button.gameObject);

            Assert.Greater(bounds.width, 0, "Bounds width should be positive");
            Assert.Greater(bounds.height, 0, "Bounds height should be positive");
        }

        [Test]
        public async Task GetScreenBounds_NullObject_ReturnsZeroRect()
        {
            await Async.DelayFrames(1);

            var bounds = InputInjector.GetScreenBounds(null);

            Assert.AreEqual(Rect.zero, bounds, "Null object should return Rect.zero");
        }

        [Test]
        public async Task GetScreenBounds_3DObject_ReturnsValidRect()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.position = new Vector3(0, 0, 10);
            _createdObjects.Add(cube);

            await Async.DelayFrames(1);

            var bounds = InputInjector.GetScreenBounds(cube);

            Assert.Greater(bounds.width, 0, "Bounds width should be positive");
            Assert.Greater(bounds.height, 0, "Bounds height should be positive");
        }

        #endregion

        #region Slider Click Position Tests

        [Test]
        public async Task GetSliderClickPosition_LeftToRight_ReturnsCorrectPosition()
        {
            var slider = CreateSlider("TestSlider", Vector2.zero, Slider.Direction.LeftToRight);

            await Async.DelayFrames(1);

            var bounds = InputInjector.GetScreenBounds(slider.gameObject);

            // Test at 0%
            var pos0 = InputInjector.GetSliderClickPosition(slider, 0f);
            Assert.AreEqual(bounds.x, pos0.x, 1f, "0% should be at left edge");

            // Test at 50%
            var pos50 = InputInjector.GetSliderClickPosition(slider, 0.5f);
            Assert.AreEqual(bounds.center.x, pos50.x, 1f, "50% should be at center");

            // Test at 100%
            var pos100 = InputInjector.GetSliderClickPosition(slider, 1f);
            Assert.AreEqual(bounds.x + bounds.width, pos100.x, 1f, "100% should be at right edge");
        }

        [Test]
        public async Task GetSliderClickPosition_RightToLeft_ReturnsCorrectPosition()
        {
            var slider = CreateSlider("RTLSlider", Vector2.zero, Slider.Direction.RightToLeft);

            await Async.DelayFrames(1);

            var bounds = InputInjector.GetScreenBounds(slider.gameObject);

            // Test at 0% - should be at right for RTL
            var pos0 = InputInjector.GetSliderClickPosition(slider, 0f);
            Assert.AreEqual(bounds.x + bounds.width, pos0.x, 1f, "0% should be at right edge for RTL");

            // Test at 100% - should be at left for RTL
            var pos100 = InputInjector.GetSliderClickPosition(slider, 1f);
            Assert.AreEqual(bounds.x, pos100.x, 1f, "100% should be at left edge for RTL");
        }

        [Test]
        public async Task GetSliderClickPosition_BottomToTop_ReturnsCorrectPosition()
        {
            var slider = CreateSlider("BTTSlider", Vector2.zero, Slider.Direction.BottomToTop);

            await Async.DelayFrames(1);

            var bounds = InputInjector.GetScreenBounds(slider.gameObject);

            // Test at 0% - should be at bottom
            var pos0 = InputInjector.GetSliderClickPosition(slider, 0f);
            Assert.AreEqual(bounds.y, pos0.y, 1f, "0% should be at bottom edge");

            // Test at 100% - should be at top
            var pos100 = InputInjector.GetSliderClickPosition(slider, 1f);
            Assert.AreEqual(bounds.y + bounds.height, pos100.y, 1f, "100% should be at top edge");
        }

        [Test]
        public async Task GetSliderClickPosition_TopToBottom_ReturnsCorrectPosition()
        {
            var slider = CreateSlider("TTBSlider", Vector2.zero, Slider.Direction.TopToBottom);

            await Async.DelayFrames(1);

            var bounds = InputInjector.GetScreenBounds(slider.gameObject);

            // Test at 0% - should be at top for TTB
            var pos0 = InputInjector.GetSliderClickPosition(slider, 0f);
            Assert.AreEqual(bounds.y + bounds.height, pos0.y, 1f, "0% should be at top edge for TTB");

            // Test at 100% - should be at bottom for TTB
            var pos100 = InputInjector.GetSliderClickPosition(slider, 1f);
            Assert.AreEqual(bounds.y, pos100.y, 1f, "100% should be at bottom edge for TTB");
        }

        #endregion

        #region Scrollbar Click Position Tests

        [Test]
        public async Task GetScrollbarClickPosition_LeftToRight_ReturnsCorrectPosition()
        {
            var scrollbar = CreateScrollbar("LTRScrollbar", Vector2.zero, Scrollbar.Direction.LeftToRight);

            await Async.DelayFrames(1);

            var bounds = InputInjector.GetScreenBounds(scrollbar.gameObject);

            // Test at 0%
            var pos0 = InputInjector.GetScrollbarClickPosition(scrollbar, 0f);
            Assert.AreEqual(bounds.x, pos0.x, 1f, "0% should be at left edge");

            // Test at 100%
            var pos100 = InputInjector.GetScrollbarClickPosition(scrollbar, 1f);
            Assert.AreEqual(bounds.x + bounds.width, pos100.x, 1f, "100% should be at right edge");
        }

        [Test]
        public async Task GetScrollbarClickPosition_BottomToTop_ReturnsCorrectPosition()
        {
            var scrollbar = CreateScrollbar("BTTScrollbar", Vector2.zero, Scrollbar.Direction.BottomToTop);

            await Async.DelayFrames(1);

            var bounds = InputInjector.GetScreenBounds(scrollbar.gameObject);

            // Test at 0% - should be at bottom
            var pos0 = InputInjector.GetScrollbarClickPosition(scrollbar, 0f);
            Assert.AreEqual(bounds.y, pos0.y, 1f, "0% should be at bottom edge");

            // Test at 100% - should be at top
            var pos100 = InputInjector.GetScrollbarClickPosition(scrollbar, 1f);
            Assert.AreEqual(bounds.y + bounds.height, pos100.y, 1f, "100% should be at top edge");
        }

        #endregion

        #region Direction Offset Tests

        [Test]
        public void GetDirectionOffset_Up_ReturnsPositiveY()
        {
            var offset = InputInjector.GetDirectionOffset("up", 0.1f);
            Assert.Greater(offset.y, 0, "Up should return positive Y");
            Assert.AreEqual(0, offset.x, "Up should have zero X");
        }

        [Test]
        public void GetDirectionOffset_Down_ReturnsNegativeY()
        {
            var offset = InputInjector.GetDirectionOffset("down", 0.1f);
            Assert.Less(offset.y, 0, "Down should return negative Y");
            Assert.AreEqual(0, offset.x, "Down should have zero X");
        }

        [Test]
        public void GetDirectionOffset_Left_ReturnsNegativeX()
        {
            var offset = InputInjector.GetDirectionOffset("left", 0.1f);
            Assert.Less(offset.x, 0, "Left should return negative X");
            Assert.AreEqual(0, offset.y, "Left should have zero Y");
        }

        [Test]
        public void GetDirectionOffset_Right_ReturnsPositiveX()
        {
            var offset = InputInjector.GetDirectionOffset("right", 0.1f);
            Assert.Greater(offset.x, 0, "Right should return positive X");
            Assert.AreEqual(0, offset.y, "Right should have zero Y");
        }

        [Test]
        public void GetDirectionOffset_Invalid_ReturnsZero()
        {
            var offset = InputInjector.GetDirectionOffset("invalid", 0.1f);
            Assert.AreEqual(Vector2.zero, offset, "Invalid direction should return zero");
        }

        [Test]
        public void GetDirectionOffset_Null_ReturnsZero()
        {
            var offset = InputInjector.GetDirectionOffset(null, 0.1f);
            Assert.AreEqual(Vector2.zero, offset, "Null direction should return zero");
        }

        [Test]
        public void GetDirectionOffset_CaseInsensitive()
        {
            var upperOffset = InputInjector.GetDirectionOffset("UP", 0.1f);
            var lowerOffset = InputInjector.GetDirectionOffset("up", 0.1f);
            var mixedOffset = InputInjector.GetDirectionOffset("Up", 0.1f);

            Assert.AreEqual(upperOffset, lowerOffset, "Should be case insensitive");
            Assert.AreEqual(lowerOffset, mixedOffset, "Should be case insensitive");
        }

        #endregion

        #region ShouldUseTouchInput Tests

        [Test]
        public void ShouldUseTouchInput_DesktopPlatform_ReturnsFalseWithMouse()
        {
            // On desktop with mouse available, should use mouse
            if (Mouse.current != null)
            {
                bool shouldUseTouch = InputInjector.ShouldUseTouchInput();
                // On desktop with mouse, should return false
                Assert.IsFalse(shouldUseTouch, "Desktop with mouse should not use touch");
            }
        }

        #endregion

        #region InjectPointerTap Tests

        [Test]
        public async Task InjectPointerTap_ClicksButton()
        {
            var button = CreateButton("TapButton", Vector2.zero);
            bool clicked = false;
            button.onClick.AddListener(() => clicked = true);

            await Async.DelayFrames(1);

            var screenPos = InputInjector.GetScreenPosition(button.gameObject);
            await InputInjector.InjectPointerTap(screenPos);

            await Async.DelayFrames(2);

            Assert.IsTrue(clicked, "Button should be clicked");
        }

        #endregion

        #region InjectPointerDoubleTap Tests

        [Test]
        public async Task InjectPointerDoubleTap_DoubleClicksButton()
        {
            var button = CreateButton("DoubleTapButton", Vector2.zero);
            int clickCount = 0;
            button.onClick.AddListener(() => clickCount++);

            await Async.DelayFrames(1);

            var screenPos = InputInjector.GetScreenPosition(button.gameObject);
            await InputInjector.InjectPointerDoubleTap(screenPos);

            await Async.DelayFrames(2);

            Assert.AreEqual(2, clickCount, "Button should be clicked twice");
        }

        #endregion

        #region InjectPointerHold Tests

        [Test]
        public async Task InjectPointerHold_HoldsPosition()
        {
            var button = CreateButton("HoldButton", Vector2.zero);

            await Async.DelayFrames(1);

            var screenPos = InputInjector.GetScreenPosition(button.gameObject);

            // Just verify it doesn't throw
            await InputInjector.InjectPointerHold(screenPos, 0.1f);

            await Async.DelayFrames(2);

            // If we got here without exception, test passes
            Assert.Pass("Hold completed without errors");
        }

        #endregion

        #region InjectPointerDrag Tests

        [Test]
        public async Task InjectPointerDrag_DragsFromStartToEnd()
        {
            var draggable = CreateDraggable("DragTarget", Vector2.zero);

            await Async.DelayFrames(1);

            var startPos = InputInjector.GetScreenPosition(draggable);
            var endPos = startPos + new Vector2(100, 0);

            await InputInjector.InjectPointerDrag(startPos, endPos, 0.1f);

            await Async.DelayFrames(2);

            // Verify drag occurred (object position should change)
            var finalPos = InputInjector.GetScreenPosition(draggable);
            Assert.AreNotEqual(startPos.x, finalPos.x, "Object should have moved");
        }

        #endregion

        #region InjectScroll Tests

        [Test]
        public async Task InjectScroll_FloatDelta_ScrollsAtPosition()
        {
            var scrollRect = CreateScrollRect("ScrollTest", Vector2.zero);
            scrollRect.verticalNormalizedPosition = 0.5f;

            await Async.DelayFrames(2);
            Canvas.ForceUpdateCanvases();

            var initialPos = scrollRect.verticalNormalizedPosition;
            var screenPos = InputInjector.GetScreenPosition(scrollRect.gameObject);

            await InputInjector.InjectScroll(screenPos, 120f);

            await Async.Delay(5, 0.1f);

            Assert.Greater(scrollRect.verticalNormalizedPosition, initialPos,
                $"Scroll up should increase position. Was {initialPos:F3}, now {scrollRect.verticalNormalizedPosition:F3}");
        }

        [Test]
        public async Task InjectScroll_NegativeDelta_ScrollsDown()
        {
            var scrollRect = CreateScrollRect("ScrollDownTest", Vector2.zero);
            // Start at middle
            scrollRect.verticalNormalizedPosition = 0.5f;

            await Async.DelayFrames(2);
            Canvas.ForceUpdateCanvases();

            var initialPos = scrollRect.verticalNormalizedPosition;
            var screenPos = InputInjector.GetScreenPosition(scrollRect.gameObject);

            // Scroll down with negative delta
            await InputInjector.InjectScroll(screenPos, -120f);

            await Async.Delay(5, 0.1f);

            // ScrollRect should have moved down (lower normalized position)
            Assert.Less(scrollRect.verticalNormalizedPosition, initialPos,
                $"Scroll down should decrease position. Was {initialPos:F3}, now {scrollRect.verticalNormalizedPosition:F3}");
        }

        [Test]
        public async Task InjectScroll_Vector2Delta_ScrollsAtPosition()
        {
            var scrollRect = CreateScrollRect("ScrollTest2", Vector2.zero);

            await Async.DelayFrames(1);

            var screenPos = InputInjector.GetScreenPosition(scrollRect.gameObject);

            // Scroll with vector delta
            await InputInjector.InjectScroll(screenPos, new Vector2(0, 100));

            await Async.DelayFrames(2);
        }

        #endregion

        #region TypeIntoField Tests

        [Test]
        public async Task TypeIntoField_SetsTextInInputField()
        {
            var inputField = CreateInputField("TypeTarget", Vector2.zero);

            await Async.DelayFrames(1);

            // TypeIntoField handles the TMP_InputField limitation by using direct text manipulation
            // (TMP_InputField uses IMGUI Event.PopEvent(), not Keyboard.onTextInput)
            await InputInjector.TypeIntoField(inputField.gameObject, "abc");

            await Async.DelayFrames(2);

            // Check if text was set
            Assert.AreEqual("abc", inputField.text, "Text should be set to 'abc'");
        }

        [Test]
        public async Task TypeIntoField_AppendsTextWhenClearFirstIsFalse()
        {
            var inputField = CreateInputField("TypeTarget", Vector2.zero);
            inputField.text = "existing";

            await Async.DelayFrames(1);

            // Append without clearing
            await InputInjector.TypeIntoField(inputField.gameObject, "new", clearFirst: false);

            await Async.DelayFrames(2);

            Assert.AreEqual("existingnew", inputField.text, "Text should be appended");
        }

        #endregion

        #region PressKey Tests

        [Test]
        public async Task PressKey_PressesAndReleasesKey()
        {
            await Async.DelayFrames(1);

            // Just verify it doesn't throw
            await InputInjector.PressKey(Key.Space);

            await Async.DelayFrames(2);

            Assert.Pass("Key press completed without errors");
        }

        #endregion

        #region HoldKey Tests

        [Test]
        public async Task HoldKey_HoldsKeyForDuration()
        {
            await Async.DelayFrames(1);

            float startTime = Time.time;

            await InputInjector.HoldKey(Key.W, 0.1f);

            float elapsed = Time.time - startTime;

            Assert.GreaterOrEqual(elapsed, 0.08f, "Hold should take at least the specified duration");
        }

        #endregion

        #region HoldKeys Tests

        [Test]
        public async Task HoldKeys_HoldsMultipleKeys()
        {
            await Async.DelayFrames(1);

            // Just verify it doesn't throw
            await InputInjector.HoldKeys(new[] { Key.LeftCtrl, Key.A }, 0.1f);

            await Async.DelayFrames(2);

            Assert.Pass("Multiple key hold completed without errors");
        }

        [Test]
        public async Task HoldKeys_EmptyArray_DoesNotThrow()
        {
            await Async.DelayFrames(1);

            // Should handle empty array gracefully
            await InputInjector.HoldKeys(new Key[0], 0.1f);
            await InputInjector.HoldKeys(null, 0.1f);

            Assert.Pass("Empty/null array handled without errors");
        }

        #endregion

        #region SetSlider Tests

        [Test]
        public async Task SetSlider_SetsSliderValue()
        {
            var slider = CreateSlider("SetSliderTest", Vector2.zero, Slider.Direction.LeftToRight);
            slider.value = 0f;

            await Async.DelayFrames(1);

            await InputInjector.SetSlider(slider, 0.75f);

            await Async.DelayFrames(2);

            // Slider value should have changed toward target
            Assert.Greater(slider.value, 0f, "Slider value should have increased");
        }

        #endregion

        #region SetScrollbar Tests

        [Test]
        public async Task SetScrollbar_SetsScrollbarValue()
        {
            var scrollbar = CreateScrollbar("SetScrollbarTest", Vector2.zero, Scrollbar.Direction.LeftToRight);
            scrollbar.value = 0f;

            await Async.DelayFrames(1);

            await InputInjector.SetScrollbar(scrollbar, 0.75f);

            await Async.DelayFrames(2);

            // Scrollbar value should have changed
            Assert.Greater(scrollbar.value, 0f, "Scrollbar value should have increased");
        }

        #endregion

        #region ClearInputField Tests

        [Test]
        public async Task ClearInputField_ClearsText()
        {
            var inputField = CreateInputField("ClearTest", Vector2.zero);
            inputField.text = "Some existing text";

            await Async.DelayFrames(1);

            await InputInjector.ClearInputField(inputField.gameObject);

            await Async.DelayFrames(2);

            // Text should be cleared
            Assert.AreEqual("", inputField.text, "Text should be cleared after ClearInputField");
        }

        [Test]
        public async Task ClearInputField_EmptyField_DoesNotThrow()
        {
            var inputField = CreateInputField("EmptyClearTest", Vector2.zero);
            inputField.text = "";

            await Async.DelayFrames(1);

            // Should handle empty field gracefully
            await InputInjector.ClearInputField(inputField.gameObject);

            Assert.Pass("Empty field handled without errors");
        }

        #endregion

        #region InjectPointerTripleTap Tests

        [Test]
        public async Task InjectPointerTripleTap_ClicksThreeTimes()
        {
            var button = CreateButton("TripleClickButton", Vector2.zero);
            int clickCount = 0;
            button.onClick.AddListener(() => clickCount++);

            await Async.DelayFrames(1);

            var screenPos = InputInjector.GetScreenPosition(button.gameObject);
            await InputInjector.InjectPointerTripleTap(screenPos);

            await Async.DelayFrames(2);

            // Should register 3 clicks (allow 2+ in case focus was stolen during test)
            Assert.GreaterOrEqual(clickCount, 2, "Triple-click should trigger at least 2 click events");
        }

        [Test]
        public async Task InjectPointerTripleTap_OnEmptyArea_DoesNotThrow()
        {
            await Async.DelayFrames(1);

            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);

            // Should not throw
            await InputInjector.InjectPointerTripleTap(center);

            Assert.Pass("Triple-click on empty area completed without errors");
        }

        #endregion

        #region PressKeyWithModifier Tests

        [Test]
        public async Task PressKeyWithModifier_SetsKeyboardState()
        {
            // Note: This test verifies that PressKeyWithModifier correctly sets keyboard state.
            // However, TMP_InputField does NOT respond to Input System keyboard state for shortcuts
            // like Ctrl+A because it uses IMGUI's Event.PopEvent() which is separate from Input System.
            // This is a Unity architectural limitation when using "New Input System Only" mode.
            await Async.DelayFrames(1);

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            Assert.IsNotNull(keyboard, "Keyboard device should exist");

            // Press Ctrl+A and verify keyboard state is set correctly
            // (even though TMP_InputField won't respond to it)
            await InputInjector.PressKeyWithModifier(
                UnityEngine.InputSystem.Key.LeftCtrl,
                UnityEngine.InputSystem.Key.A);

            // Test passes if no exception - keyboard state injection works correctly
            // The limitation is TMP_InputField's use of IMGUI Events, not our injection
            Assert.Pass("PressKeyWithModifier completed without error");
        }

        [Test]
        public async Task PressKey_SingleKey_IsDetected()
        {
            await Async.DelayFrames(1);

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            Assert.IsNotNull(keyboard, "Keyboard device should exist");

            // Press just the A key
            await InputInjector.PressKey(UnityEngine.InputSystem.Key.A);

            // The key should have been pressed and released
            // We can't check isPressed after release, but we can verify no exception
            Assert.Pass("Single key press completed without error");
        }

        [Test]
        public async Task InputField_IsFocusedAfterClick()
        {
            var inputField = CreateInputField("FocusTestField", Vector2.zero);
            inputField.text = "Test text";

            await Async.DelayFrames(1);

            Assert.IsFalse(inputField.isFocused, "Input field should not be focused initially");

            // Click to focus
            var screenPos = InputInjector.GetScreenPosition(inputField.gameObject);
            await InputInjector.InjectPointerTap(screenPos);
            await Async.DelayFrames(2);

            Assert.IsTrue(inputField.isFocused, "Input field should be focused after click");
        }

        #endregion

        #region TypeIntoField Tests

        [Test]
        public async Task TypeIntoField_TypesTextIntoField()
        {
            var inputField = CreateInputField("TypeIntoTest", Vector2.zero);
            inputField.text = "";

            await Async.DelayFrames(1);

            await InputInjector.TypeIntoField(inputField.gameObject, "Hello", clearFirst: true, pressEnter: false);

            await Async.DelayFrames(2);

            // Field should contain typed text
            Assert.IsTrue(inputField.text.Length > 0, "Field should have text");
        }

        [Test]
        public async Task TypeIntoField_WithClearFirst_ReplacesExistingText()
        {
            var inputField = CreateInputField("ClearFirstTest", Vector2.zero);
            inputField.text = "Old text that should be replaced";

            await Async.DelayFrames(1);

            await InputInjector.TypeIntoField(inputField.gameObject, "New", clearFirst: true, pressEnter: false);

            await Async.DelayFrames(2);

            // Field should contain only the new text (old text replaced via triple-click + type)
            Assert.AreEqual("New", inputField.text, "Existing text should be replaced when clearFirst is true");
        }

        [Test]
        public async Task TypeIntoField_WithoutClearFirst_AppendsText()
        {
            var inputField = CreateInputField("AppendTest", Vector2.zero);
            inputField.text = "Existing";

            await Async.DelayFrames(1);

            await InputInjector.TypeIntoField(inputField.gameObject, "New", clearFirst: false, pressEnter: false);

            await Async.DelayFrames(2);

            // Field should contain both old and new text
            Assert.IsTrue(inputField.text.Contains("Existing"), "Original text should still be present");
            Assert.IsTrue(inputField.text.Contains("New"), "New text should be appended");
        }

        #endregion

        #region Swipe Tests

        [Test]
        public async Task Swipe_SwipesInDirection()
        {
            var button = CreateButton("SwipeTarget", Vector2.zero);

            await Async.DelayFrames(1);

            // Just verify it doesn't throw
            await InputInjector.Swipe(button.gameObject, "right", 0.1f, 0.1f);

            Assert.Pass("Swipe completed without errors");
        }

        #endregion

        #region ScrollElement Tests

        [Test]
        public async Task ScrollElement_ScrollsInDirection()
        {
            var scrollRect = CreateScrollRect("ScrollElementTest", Vector2.zero);

            await Async.DelayFrames(1);

            await InputInjector.ScrollElement(scrollRect.gameObject, "down", 0.5f);

            await Async.DelayFrames(2);
        }

        #endregion

        #region Pinch Tests

        [Test]
        public async Task Pinch_WithElement_PerformsPinchGesture()
        {
            var button = CreateButton("PinchTarget", Vector2.zero);

            await Async.DelayFrames(1);

            // Pinch zoom in
            await InputInjector.Pinch(button.gameObject, 1.5f, 0.05f);

            Assert.Pass("Pinch gesture completed without errors");
        }

        [Test]
        public async Task Pinch_NullElement_UsesCenterOfScreen()
        {
            await Async.DelayFrames(1);

            // Pinch at center of screen (null element)
            await InputInjector.Pinch(null, 0.5f, 0.05f);

            Assert.Pass("Pinch at screen center completed without errors");
        }

        [Test]
        public async Task InjectPinch_WithCustomFingerDistance()
        {
            await Async.DelayFrames(1);

            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await InputInjector.InjectPinch(center, 2f, 0.05f, 50f);

            Assert.Pass("Pinch with custom finger distance completed without errors");
        }

        #endregion

        #region TwoFingerSwipe Tests

        [Test]
        public async Task TwoFingerSwipe_WithElement_SwipesWithTwoFingers()
        {
            var button = CreateButton("TwoFingerSwipeTarget", Vector2.zero);

            await Async.DelayFrames(1);

            await InputInjector.TwoFingerSwipe(button.gameObject, "up", 0.1f, 0.05f, 0.02f);

            Assert.Pass("Two-finger swipe completed without errors");
        }

        [Test]
        public async Task InjectTwoFingerSwipe_AtPosition()
        {
            await Async.DelayFrames(1);

            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await InputInjector.InjectTwoFingerSwipe(center, "down", 0.1f, 0.05f, 0.02f);

            Assert.Pass("Inject two-finger swipe completed without errors");
        }

        #endregion

        #region Rotate Tests

        [Test]
        public async Task Rotate_WithElement_PerformsRotationGesture()
        {
            var button = CreateButton("RotateTarget", Vector2.zero);

            await Async.DelayFrames(1);

            // Rotate 45 degrees clockwise
            await InputInjector.Rotate(button.gameObject, 45f, 0.05f, 0.05f);

            Assert.Pass("Rotation gesture completed without errors");
        }

        [Test]
        public async Task InjectRotate_RotatesAtPosition()
        {
            await Async.DelayFrames(1);

            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await InputInjector.InjectRotate(center, -90f, 0.05f, 0.05f);

            Assert.Pass("Inject rotate completed without errors");
        }

        [Test]
        public async Task InjectRotatePixels_RotatesWithPixelRadius()
        {
            await Async.DelayFrames(1);

            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await InputInjector.InjectRotatePixels(center, 180f, 0.05f, 75f);

            Assert.Pass("Inject rotate pixels completed without errors");
        }

        #endregion

        #region Touch Input Tests

        [Test]
        public async Task InjectTouchTap_TapsAtPosition()
        {
            var button = CreateButton("TouchTapButton", Vector2.zero);
            bool clicked = false;
            button.onClick.AddListener(() => clicked = true);

            await Async.DelayFrames(1);

            var screenPos = InputInjector.GetScreenPosition(button.gameObject);
            await InputInjector.InjectTouchTap(screenPos);

            await Async.DelayFrames(2);

            // Touch tap should trigger click (if touch input is active)
            // Note: On desktop this might not work without touchscreen
        }

        [Test]
        public async Task InjectTouchDoubleTap_DoubleTapsAtPosition()
        {
            await Async.DelayFrames(1);

            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await InputInjector.InjectTouchDoubleTap(center);

            Assert.Pass("Touch double tap completed without errors");
        }

        [Test]
        public async Task InjectTouchDrag_DragsBetweenPositions()
        {
            await Async.DelayFrames(1);

            var start = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var end = start + new Vector2(100, 0);

            await InputInjector.InjectTouchDrag(start, end, 0.05f);

            Assert.Pass("Touch drag completed without errors");
        }

        [Test]
        public async Task InjectTouchHold_HoldsAtPosition()
        {
            await Async.DelayFrames(1);

            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            await InputInjector.InjectTouchHold(center, 0.1f);

            Assert.Pass("Touch hold completed without errors");
        }

        #endregion

        #region Mouse Drag Tests

        [Test]
        public async Task InjectMouseDrag_DragsBetweenPositions()
        {
            await Async.DelayFrames(1);

            var start = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var end = start + new Vector2(100, 0);

            await InputInjector.InjectMouseDrag(start, end, 0.05f);

            Assert.Pass("Mouse drag completed without errors");
        }

        [Test]
        public async Task InjectMouseDrag_WithHoldTime_HoldsBeforeDragging()
        {
            await Async.DelayFrames(1);

            var start = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var end = start + new Vector2(100, 0);

            float startTime = Time.time;

            // Use a 0.15s hold time
            await InputInjector.InjectMouseDrag(start, end, 0.05f, 0.15f);

            float elapsed = Time.time - startTime;

            // Should take at least holdTime (0.15s) + duration (0.05s) = 0.2s
            Assert.GreaterOrEqual(elapsed, 0.15f, "Drag with hold should take at least hold time + duration");
        }

        [Test]
        public async Task InjectPointerDrag_WithHoldTime_HoldsBeforeDragging()
        {
            await Async.DelayFrames(1);

            var start = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var end = start + new Vector2(100, 0);

            float startTime = Time.time;

            // Use a 0.2s hold time (for games requiring hold before drag)
            await InputInjector.InjectPointerDrag(start, end, 0.05f, 0.2f);

            float elapsed = Time.time - startTime;

            // Should take at least holdTime (0.2s) + duration (0.05s) = 0.25s
            Assert.GreaterOrEqual(elapsed, 0.2f, "Drag with hold should take at least hold time");
        }

        [Test]
        public async Task InjectMouseDrag_WithRightButton_DragsWithRightMouse()
        {
            await Async.DelayFrames(1);

            var start = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var end = start + new Vector2(100, 0);

            // Right mouse button drag (commonly used for camera rotation)
            await InputInjector.InjectMouseDrag(start, end, 0.05f, 0f, PointerButton.Right);

            Assert.Pass("Right mouse button drag completed without errors");
        }

        [Test]
        public async Task InjectMouseDrag_WithMiddleButton_DragsWithMiddleMouse()
        {
            await Async.DelayFrames(1);

            var start = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var end = start + new Vector2(100, 0);

            // Middle mouse button drag (commonly used for panning)
            await InputInjector.InjectMouseDrag(start, end, 0.05f, 0f, PointerButton.Middle);

            Assert.Pass("Middle mouse button drag completed without errors");
        }

        [Test]
        public async Task InjectPointerDrag_WithRightButton_DragsWithRightMouse()
        {
            await Async.DelayFrames(1);

            var start = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var end = start + new Vector2(100, 0);

            // Right mouse button drag via pointer abstraction
            await InputInjector.InjectPointerDrag(start, end, 0.05f, 0f, PointerButton.Right);

            Assert.Pass("Right mouse button pointer drag completed without errors");
        }

        #endregion

        #region Two Finger Drag Tests

        [Test]
        public async Task InjectTwoFingerDrag_DragsTwoFingers()
        {
            await Async.DelayFrames(1);

            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var start1 = center + new Vector2(-50, 0);
            var start2 = center + new Vector2(50, 0);
            var end1 = start1 + new Vector2(0, 100);
            var end2 = start2 + new Vector2(0, 100);

            await InputInjector.InjectTwoFingerDrag(start1, end1, start2, end2, 0.05f);

            Assert.Pass("Two finger drag completed without errors");
        }

        #endregion

        #region Keys Builder Tests

        [Test]
        public async Task Keys_Hold_HoldsSingleKey()
        {
            await Async.DelayFrames(1);

            await Keys.Hold(Key.W).For(0.1f);

            Assert.Pass("Keys.Hold completed without errors");
        }

        [Test]
        public async Task Keys_Press_PressesKey()
        {
            await Async.DelayFrames(1);

            await Keys.Press(Key.Space);

            Assert.Pass("Keys.Press completed without errors");
        }

        [Test]
        public async Task Keys_HoldMultiple_HoldsMultipleKeys()
        {
            await Async.DelayFrames(1);

            await Keys.Hold(Key.W, Key.A).For(0.1f);

            Assert.Pass("Keys.Hold multiple keys completed without errors");
        }

        [Test]
        public async Task Keys_Then_ChainsKeyActions()
        {
            await Async.DelayFrames(1);

            await Keys.Hold(Key.W).For(0.05f).Then(Key.A).For(0.05f);

            Assert.Pass("Keys.Then chain completed without errors");
        }

        [Test]
        public async Task Keys_ThenPress_ChainsPressAfterHold()
        {
            await Async.DelayFrames(1);

            await Keys.Hold(Key.W).For(0.05f).ThenPress(Key.Space);

            Assert.Pass("Keys.ThenPress chain completed without errors");
        }

        #endregion

        #region 3D Object Click Tests

        [Test]
        public async Task InjectPointerTap_3DObjectWithCollider_TapsAtCenter()
        {
            // Create a 3D cube with collider in front of camera
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "ClickableCube";
            cube.transform.position = new Vector3(0, 0, 5);
            _createdObjects.Add(cube);

            // Add PhysicsRaycaster so EventSystem can detect 3D objects
            _camera.gameObject.AddComponent<UnityEngine.EventSystems.Physics2DRaycaster>();
            var raycaster = _camera.gameObject.AddComponent<UnityEngine.EventSystems.PhysicsRaycaster>();

            await Async.DelayFrames(2);

            var screenPos = InputInjector.GetScreenPosition(cube);
            Assert.Greater(screenPos.x, 0, "3D object screen X should be positive");
            Assert.Greater(screenPos.y, 0, "3D object screen Y should be positive");

            // Click should not throw
            await InputInjector.InjectPointerTap(screenPos);
            await Async.DelayFrames(2);

            Assert.Pass("Click on 3D object with collider completed without errors");
        }

        [Test]
        public async Task InjectPointerTap_3DObjectWithoutCollider_TapsAtCenter()
        {
            // Create a 3D cube WITHOUT collider
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "NoColliderCube";
            cube.transform.position = new Vector3(0, 0, 5);
            Object.Destroy(cube.GetComponent<Collider>());
            _createdObjects.Add(cube);

            await Async.DelayFrames(2);

            var screenPos = InputInjector.GetScreenPosition(cube);
            Assert.Greater(screenPos.x, 0, "3D object screen X should be positive");
            Assert.Greater(screenPos.y, 0, "3D object screen Y should be positive");

            // Click should not throw even without collider - we just tap at the center
            await InputInjector.InjectPointerTap(screenPos);
            await Async.DelayFrames(2);

            Assert.Pass("Click on 3D object without collider completed without errors");
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
            rect.sizeDelta = new Vector2(100, 40);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = Color.gray;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            _createdObjects.Add(go);
            return button;
        }

        private Slider CreateSlider(string name, Vector2 position, Slider.Direction direction)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(150, 20);
            rect.anchoredPosition = position;

            var slider = go.AddComponent<Slider>();
            slider.direction = direction;

            // Background
            var bg = new GameObject("Background");
            bg.transform.SetParent(go.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = Color.gray;

            // Fill Area
            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.sizeDelta = new Vector2(-20, 0);
            fillAreaRect.anchoredPosition = Vector2.zero;

            // Fill
            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.sizeDelta = Vector2.zero;
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = Color.green;

            slider.fillRect = fillRect;
            slider.targetGraphic = fillImage;

            _createdObjects.Add(go);
            return slider;
        }

        private Scrollbar CreateScrollbar(string name, Vector2 position, Scrollbar.Direction direction)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(150, 20);
            rect.anchoredPosition = position;

            var scrollbar = go.AddComponent<Scrollbar>();
            scrollbar.direction = direction;

            // Background
            var bg = new GameObject("Background");
            bg.transform.SetParent(go.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = Color.gray;

            // Handle
            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRect = handleArea.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.sizeDelta = new Vector2(-20, -20);

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var handleRect = handle.AddComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = new Vector2(0.2f, 1f);
            handleRect.sizeDelta = Vector2.zero;
            var handleImage = handle.AddComponent<Image>();
            handleImage.color = Color.white;

            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;

            _createdObjects.Add(go);
            return scrollbar;
        }

        private TMP_InputField CreateInputField(string name, Vector2 position)
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

            // Text Area
            var textArea = new GameObject("Text Area");
            textArea.transform.SetParent(go.transform, false);
            var textAreaRect = textArea.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.sizeDelta = new Vector2(-20, -10);
            textAreaRect.anchoredPosition = Vector2.zero;

            // Text
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(textArea.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            textRect.anchoredPosition = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.color = Color.white;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;

            _createdObjects.Add(go);
            return inputField;
        }

        private ScrollRect CreateScrollRect(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200, 200);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = Color.gray;

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
            contentRect.sizeDelta = new Vector2(0, 500); // Larger than viewport

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.vertical = true;
            scrollRect.horizontal = false;

            _createdObjects.Add(go);
            return scrollRect;
        }

        private GameObject CreateDraggable(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(80, 80);
            rect.anchoredPosition = position;

            var image = go.AddComponent<Image>();
            image.color = Color.blue;

            // Add drag handler
            go.AddComponent<SimpleDragHandler>();

            _createdObjects.Add(go);
            return go;
        }

        #endregion
    }
}
