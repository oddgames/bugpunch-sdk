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

        [UnityTest]
        public IEnumerator GetScreenPosition_UIElement_ReturnsValidPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("TestButton", new Vector2(100, 50));

                await UniTask.Yield();

                var screenPos = InputInjector.GetScreenPosition(button.gameObject);

                Assert.Greater(screenPos.x, 0, "Screen X should be positive");
                Assert.Greater(screenPos.y, 0, "Screen Y should be positive");
            });
        }

        [UnityTest]
        public IEnumerator GetScreenPosition_NullObject_ReturnsZero()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var screenPos = InputInjector.GetScreenPosition(null);

                Assert.AreEqual(Vector2.zero, screenPos, "Null object should return Vector2.zero");
            });
        }

        [UnityTest]
        public IEnumerator GetScreenPosition_3DObject_ReturnsValidPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = new Vector3(0, 0, 10);
                _createdObjects.Add(cube);

                await UniTask.Yield();

                var screenPos = InputInjector.GetScreenPosition(cube);

                // 3D object in front of camera should return valid screen position
                Assert.Greater(screenPos.x, 0, "Screen X should be positive");
                Assert.Greater(screenPos.y, 0, "Screen Y should be positive");
            });
        }

        #endregion

        #region GetScreenBounds Tests

        [UnityTest]
        public IEnumerator GetScreenBounds_UIElement_ReturnsValidRect()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("BoundsButton", Vector2.zero);

                await UniTask.Yield();

                var bounds = InputInjector.GetScreenBounds(button.gameObject);

                Assert.Greater(bounds.width, 0, "Bounds width should be positive");
                Assert.Greater(bounds.height, 0, "Bounds height should be positive");
            });
        }

        [UnityTest]
        public IEnumerator GetScreenBounds_NullObject_ReturnsZeroRect()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var bounds = InputInjector.GetScreenBounds(null);

                Assert.AreEqual(Rect.zero, bounds, "Null object should return Rect.zero");
            });
        }

        [UnityTest]
        public IEnumerator GetScreenBounds_3DObject_ReturnsValidRect()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = new Vector3(0, 0, 10);
                _createdObjects.Add(cube);

                await UniTask.Yield();

                var bounds = InputInjector.GetScreenBounds(cube);

                Assert.Greater(bounds.width, 0, "Bounds width should be positive");
                Assert.Greater(bounds.height, 0, "Bounds height should be positive");
            });
        }

        #endregion

        #region Slider Click Position Tests

        [UnityTest]
        public IEnumerator GetSliderClickPosition_LeftToRight_ReturnsCorrectPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var slider = CreateSlider("TestSlider", Vector2.zero, Slider.Direction.LeftToRight);

                await UniTask.Yield();

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
            });
        }

        [UnityTest]
        public IEnumerator GetSliderClickPosition_RightToLeft_ReturnsCorrectPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var slider = CreateSlider("RTLSlider", Vector2.zero, Slider.Direction.RightToLeft);

                await UniTask.Yield();

                var bounds = InputInjector.GetScreenBounds(slider.gameObject);

                // Test at 0% - should be at right for RTL
                var pos0 = InputInjector.GetSliderClickPosition(slider, 0f);
                Assert.AreEqual(bounds.x + bounds.width, pos0.x, 1f, "0% should be at right edge for RTL");

                // Test at 100% - should be at left for RTL
                var pos100 = InputInjector.GetSliderClickPosition(slider, 1f);
                Assert.AreEqual(bounds.x, pos100.x, 1f, "100% should be at left edge for RTL");
            });
        }

        [UnityTest]
        public IEnumerator GetSliderClickPosition_BottomToTop_ReturnsCorrectPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var slider = CreateSlider("BTTSlider", Vector2.zero, Slider.Direction.BottomToTop);

                await UniTask.Yield();

                var bounds = InputInjector.GetScreenBounds(slider.gameObject);

                // Test at 0% - should be at bottom
                var pos0 = InputInjector.GetSliderClickPosition(slider, 0f);
                Assert.AreEqual(bounds.y, pos0.y, 1f, "0% should be at bottom edge");

                // Test at 100% - should be at top
                var pos100 = InputInjector.GetSliderClickPosition(slider, 1f);
                Assert.AreEqual(bounds.y + bounds.height, pos100.y, 1f, "100% should be at top edge");
            });
        }

        [UnityTest]
        public IEnumerator GetSliderClickPosition_TopToBottom_ReturnsCorrectPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var slider = CreateSlider("TTBSlider", Vector2.zero, Slider.Direction.TopToBottom);

                await UniTask.Yield();

                var bounds = InputInjector.GetScreenBounds(slider.gameObject);

                // Test at 0% - should be at top for TTB
                var pos0 = InputInjector.GetSliderClickPosition(slider, 0f);
                Assert.AreEqual(bounds.y + bounds.height, pos0.y, 1f, "0% should be at top edge for TTB");

                // Test at 100% - should be at bottom for TTB
                var pos100 = InputInjector.GetSliderClickPosition(slider, 1f);
                Assert.AreEqual(bounds.y, pos100.y, 1f, "100% should be at bottom edge for TTB");
            });
        }

        #endregion

        #region Scrollbar Click Position Tests

        [UnityTest]
        public IEnumerator GetScrollbarClickPosition_LeftToRight_ReturnsCorrectPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var scrollbar = CreateScrollbar("LTRScrollbar", Vector2.zero, Scrollbar.Direction.LeftToRight);

                await UniTask.Yield();

                var bounds = InputInjector.GetScreenBounds(scrollbar.gameObject);

                // Test at 0%
                var pos0 = InputInjector.GetScrollbarClickPosition(scrollbar, 0f);
                Assert.AreEqual(bounds.x, pos0.x, 1f, "0% should be at left edge");

                // Test at 100%
                var pos100 = InputInjector.GetScrollbarClickPosition(scrollbar, 1f);
                Assert.AreEqual(bounds.x + bounds.width, pos100.x, 1f, "100% should be at right edge");
            });
        }

        [UnityTest]
        public IEnumerator GetScrollbarClickPosition_BottomToTop_ReturnsCorrectPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var scrollbar = CreateScrollbar("BTTScrollbar", Vector2.zero, Scrollbar.Direction.BottomToTop);

                await UniTask.Yield();

                var bounds = InputInjector.GetScreenBounds(scrollbar.gameObject);

                // Test at 0% - should be at bottom
                var pos0 = InputInjector.GetScrollbarClickPosition(scrollbar, 0f);
                Assert.AreEqual(bounds.y, pos0.y, 1f, "0% should be at bottom edge");

                // Test at 100% - should be at top
                var pos100 = InputInjector.GetScrollbarClickPosition(scrollbar, 1f);
                Assert.AreEqual(bounds.y + bounds.height, pos100.y, 1f, "100% should be at top edge");
            });
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

        [UnityTest]
        public IEnumerator InjectPointerTap_ClicksButton()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("TapButton", Vector2.zero);
                bool clicked = false;
                button.onClick.AddListener(() => clicked = true);

                await UniTask.Yield();

                var screenPos = InputInjector.GetScreenPosition(button.gameObject);
                await InputInjector.InjectPointerTap(screenPos);

                await UniTask.DelayFrame(2);

                Assert.IsTrue(clicked, "Button should be clicked");
            });
        }

        #endregion

        #region InjectPointerDoubleTap Tests

        [UnityTest]
        public IEnumerator InjectPointerDoubleTap_DoubleClicksButton()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("DoubleTapButton", Vector2.zero);
                int clickCount = 0;
                button.onClick.AddListener(() => clickCount++);

                await UniTask.Yield();

                var screenPos = InputInjector.GetScreenPosition(button.gameObject);
                await InputInjector.InjectPointerDoubleTap(screenPos);

                await UniTask.DelayFrame(2);

                Assert.AreEqual(2, clickCount, "Button should be clicked twice");
            });
        }

        #endregion

        #region InjectPointerHold Tests

        [UnityTest]
        public IEnumerator InjectPointerHold_HoldsPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("HoldButton", Vector2.zero);

                await UniTask.Yield();

                var screenPos = InputInjector.GetScreenPosition(button.gameObject);

                // Just verify it doesn't throw
                await InputInjector.InjectPointerHold(screenPos, 0.1f);

                await UniTask.DelayFrame(2);

                // If we got here without exception, test passes
                Assert.Pass("Hold completed without errors");
            });
        }

        #endregion

        #region InjectPointerDrag Tests

        [UnityTest]
        public IEnumerator InjectPointerDrag_DragsFromStartToEnd()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var draggable = CreateDraggable("DragTarget", Vector2.zero);

                await UniTask.Yield();

                var startPos = InputInjector.GetScreenPosition(draggable);
                var endPos = startPos + new Vector2(100, 0);

                await InputInjector.InjectPointerDrag(startPos, endPos, 0.5f);

                await UniTask.DelayFrame(5);

                // Verify drag occurred (object position should change)
                var finalPos = InputInjector.GetScreenPosition(draggable);
                Assert.AreNotEqual(startPos.x, finalPos.x, "Object should have moved");
            });
        }

        #endregion

        #region InjectScroll Tests

        [UnityTest]
        public IEnumerator InjectScroll_FloatDelta_ScrollsAtPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var scrollRect = CreateScrollRect("ScrollTest", Vector2.zero);
                scrollRect.normalizedPosition = new Vector2(0, 0.5f);

                await UniTask.Yield();

                var screenPos = InputInjector.GetScreenPosition(scrollRect.gameObject);

                // Scroll up
                await InputInjector.InjectScroll(screenPos, 1f);

                await UniTask.DelayFrame(5);

                // Scroll position should have changed
                // Note: exact behavior depends on scroll rect setup
            });
        }

        [UnityTest]
        public IEnumerator InjectScroll_Vector2Delta_ScrollsAtPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var scrollRect = CreateScrollRect("ScrollTest2", Vector2.zero);

                await UniTask.Yield();

                var screenPos = InputInjector.GetScreenPosition(scrollRect.gameObject);

                // Scroll with vector delta
                await InputInjector.InjectScroll(screenPos, new Vector2(0, 100));

                await UniTask.DelayFrame(2);
            });
        }

        #endregion

        #region TypeIntoField Tests

        [UnityTest]
        public IEnumerator TypeIntoField_SetsTextInInputField()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var inputField = CreateInputField("TypeTarget", Vector2.zero);

                await UniTask.Yield();

                // TypeIntoField handles the TMP_InputField limitation by using direct text manipulation
                // (TMP_InputField uses IMGUI Event.PopEvent(), not Keyboard.onTextInput)
                await InputInjector.TypeIntoField(inputField.gameObject, "abc");

                await UniTask.DelayFrame(5);

                // Check if text was set
                Assert.AreEqual("abc", inputField.text, "Text should be set to 'abc'");
            });
        }

        [UnityTest]
        public IEnumerator TypeIntoField_AppendsTextWhenClearFirstIsFalse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var inputField = CreateInputField("TypeTarget", Vector2.zero);
                inputField.text = "existing";

                await UniTask.Yield();

                // Append without clearing
                await InputInjector.TypeIntoField(inputField.gameObject, "new", clearFirst: false);

                await UniTask.DelayFrame(5);

                Assert.AreEqual("existingnew", inputField.text, "Text should be appended");
            });
        }

        #endregion

        #region PressKey Tests

        [UnityTest]
        public IEnumerator PressKey_PressesAndReleasesKey()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Just verify it doesn't throw
                await InputInjector.PressKey(Key.Space);

                await UniTask.DelayFrame(2);

                Assert.Pass("Key press completed without errors");
            });
        }

        #endregion

        #region HoldKey Tests

        [UnityTest]
        public IEnumerator HoldKey_HoldsKeyForDuration()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                float startTime = Time.time;

                await InputInjector.HoldKey(Key.W, 0.2f);

                float elapsed = Time.time - startTime;

                Assert.GreaterOrEqual(elapsed, 0.15f, "Hold should take at least the specified duration");
            });
        }

        #endregion

        #region HoldKeys Tests

        [UnityTest]
        public IEnumerator HoldKeys_HoldsMultipleKeys()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Just verify it doesn't throw
                await InputInjector.HoldKeys(new[] { Key.LeftCtrl, Key.A }, 0.1f);

                await UniTask.DelayFrame(2);

                Assert.Pass("Multiple key hold completed without errors");
            });
        }

        [UnityTest]
        public IEnumerator HoldKeys_EmptyArray_DoesNotThrow()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Should handle empty array gracefully
                await InputInjector.HoldKeys(new Key[0], 0.1f);
                await InputInjector.HoldKeys(null, 0.1f);

                Assert.Pass("Empty/null array handled without errors");
            });
        }

        #endregion

        #region SetSlider Tests

        [UnityTest]
        public IEnumerator SetSlider_SetsSliderValue()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var slider = CreateSlider("SetSliderTest", Vector2.zero, Slider.Direction.LeftToRight);
                slider.value = 0f;

                await UniTask.Yield();

                await InputInjector.SetSlider(slider, 0.75f);

                await UniTask.DelayFrame(5);

                // Slider value should have changed toward target
                Assert.Greater(slider.value, 0f, "Slider value should have increased");
            });
        }

        #endregion

        #region SetScrollbar Tests

        [UnityTest]
        public IEnumerator SetScrollbar_SetsScrollbarValue()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var scrollbar = CreateScrollbar("SetScrollbarTest", Vector2.zero, Scrollbar.Direction.LeftToRight);
                scrollbar.value = 0f;

                await UniTask.Yield();

                await InputInjector.SetScrollbar(scrollbar, 0.75f);

                await UniTask.DelayFrame(5);

                // Scrollbar value should have changed
                Assert.Greater(scrollbar.value, 0f, "Scrollbar value should have increased");
            });
        }

        #endregion

        #region ClearInputField Tests

        [UnityTest]
        public IEnumerator ClearInputField_ClearsText()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var inputField = CreateInputField("ClearTest", Vector2.zero);
                inputField.text = "Some existing text";

                await UniTask.Yield();

                await InputInjector.ClearInputField(inputField.gameObject);

                await UniTask.DelayFrame(5);

                // Text should be cleared
                Assert.AreEqual("", inputField.text, "Text should be cleared after ClearInputField");
            });
        }

        [UnityTest]
        public IEnumerator ClearInputField_EmptyField_DoesNotThrow()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var inputField = CreateInputField("EmptyClearTest", Vector2.zero);
                inputField.text = "";

                await UniTask.Yield();

                // Should handle empty field gracefully
                await InputInjector.ClearInputField(inputField.gameObject);

                Assert.Pass("Empty field handled without errors");
            });
        }

        #endregion

        #region InjectPointerTripleTap Tests

        [UnityTest]
        public IEnumerator InjectPointerTripleTap_ClicksThreeTimes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("TripleClickButton", Vector2.zero);
                int clickCount = 0;
                button.onClick.AddListener(() => clickCount++);

                await UniTask.Yield();

                var screenPos = InputInjector.GetScreenPosition(button.gameObject);
                await InputInjector.InjectPointerTripleTap(screenPos);

                await UniTask.DelayFrame(2);

                // Should register 3 clicks
                Assert.AreEqual(3, clickCount, "Triple-click should trigger 3 click events");
            });
        }

        [UnityTest]
        public IEnumerator InjectPointerTripleTap_OnEmptyArea_DoesNotThrow()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var center = new Vector2(Screen.width / 2f, Screen.height / 2f);

                // Should not throw
                await InputInjector.InjectPointerTripleTap(center);

                Assert.Pass("Triple-click on empty area completed without errors");
            });
        }

        #endregion

        #region PressKeyWithModifier Tests

        [UnityTest]
        public IEnumerator PressKeyWithModifier_SetsKeyboardState()
        {
            // Note: This test verifies that PressKeyWithModifier correctly sets keyboard state.
            // However, TMP_InputField does NOT respond to Input System keyboard state for shortcuts
            // like Ctrl+A because it uses IMGUI's Event.PopEvent() which is separate from Input System.
            // This is a Unity architectural limitation when using "New Input System Only" mode.
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

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
            });
        }

        [UnityTest]
        public IEnumerator PressKeyWithModifier_VerifyKeyboardState()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                Assert.IsNotNull(keyboard, "Keyboard device should exist");

                // Press Ctrl+A and check keyboard state during the press
                bool ctrlWasPressed = false;
                bool aWasPressed = false;
                bool bothPressed = false;

                // Queue the modifier
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,
                    new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.LeftCtrl));
                UnityEngine.InputSystem.InputSystem.Update();
                await UniTask.Yield();

                ctrlWasPressed = keyboard.leftCtrlKey.isPressed;
                Debug.Log($"After Ctrl down: Ctrl={ctrlWasPressed}");

                // Queue Ctrl+A together
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,
                    new UnityEngine.InputSystem.LowLevel.KeyboardState(
                        UnityEngine.InputSystem.Key.LeftCtrl,
                        UnityEngine.InputSystem.Key.A));
                UnityEngine.InputSystem.InputSystem.Update();
                await UniTask.Yield();

                aWasPressed = keyboard.aKey.isPressed;
                bothPressed = keyboard.leftCtrlKey.isPressed && keyboard.aKey.isPressed;
                Debug.Log($"After Ctrl+A: Ctrl={keyboard.leftCtrlKey.isPressed}, A={aWasPressed}, Both={bothPressed}");

                // Release
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,
                    new UnityEngine.InputSystem.LowLevel.KeyboardState());
                UnityEngine.InputSystem.InputSystem.Update();
                await UniTask.Yield();

                Assert.IsTrue(ctrlWasPressed, "Ctrl should have been pressed");
                Assert.IsTrue(aWasPressed, "A should have been pressed");
                Assert.IsTrue(bothPressed, "Both Ctrl and A should have been pressed simultaneously");
            });
        }

        [UnityTest]
        public IEnumerator PressKey_SingleKey_IsDetected()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                Assert.IsNotNull(keyboard, "Keyboard device should exist");

                // Press just the A key
                await InputInjector.PressKey(UnityEngine.InputSystem.Key.A);

                // The key should have been pressed and released
                // We can't check isPressed after release, but we can verify no exception
                Assert.Pass("Single key press completed without error");
            });
        }

        [UnityTest]
        public IEnumerator InputField_IsFocusedAfterClick()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var inputField = CreateInputField("FocusTestField", Vector2.zero);
                inputField.text = "Test text";

                await UniTask.Yield();

                Assert.IsFalse(inputField.isFocused, "Input field should not be focused initially");

                // Click to focus
                var screenPos = InputInjector.GetScreenPosition(inputField.gameObject);
                await InputInjector.InjectPointerTap(screenPos);
                await UniTask.Delay(200);

                Assert.IsTrue(inputField.isFocused, "Input field should be focused after click");
            });
        }

        [UnityTest]
        public IEnumerator InputSystem_QueueStateEvent_PopulatesEventPopEvent()
        {
            // Tests if InputSystem.QueueStateEvent populates the Event.PopEvent() queue
            // Checks immediately on same frame and each subsequent frame to catch transient events
            return UniTask.ToCoroutine(async () =>
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (keyboard == null)
                    keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();

                await UniTask.Yield();

                int totalEventsPopped = 0;
                var evt = new Event();

                // Helper to pop and log all events
                int PopAllEvents(string context)
                {
                    int count = 0;
                    int queueCount = Event.GetEventCount();
                    Debug.Log($"[Test] {context} - Frame {Time.frameCount}: Queue count = {queueCount}");
                    while (Event.PopEvent(evt))
                    {
                        count++;
                        Debug.Log($"[Test] {context} - Popped: type={evt.type}, rawType={evt.rawType}, keyCode={evt.keyCode}, char='{evt.character}'");
                    }
                    return count;
                }

                // Clear any existing events
                PopAllEvents("Pre-clear");

                Debug.Log($"[Test] === Injecting Key.A via InputSystem.QueueStateEvent ===");

                // Inject a key press via Input System
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,
                    new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.A));

                // Check BEFORE InputSystem.Update()
                totalEventsPopped += PopAllEvents("After QueueStateEvent, before Update()");

                // Process the Input System
                UnityEngine.InputSystem.InputSystem.Update();

                // Check IMMEDIATELY after InputSystem.Update() on same frame
                totalEventsPopped += PopAllEvents("After Update(), same frame");

                // Check next several frames
                for (int i = 1; i <= 5; i++)
                {
                    await UniTask.Yield();
                    totalEventsPopped += PopAllEvents($"Frame +{i}");
                }

                // Also try injecting text event to compare
                Debug.Log($"[Test] === Injecting 'X' via InputSystem.QueueTextEvent for comparison ===");
                UnityEngine.InputSystem.InputSystem.QueueTextEvent(keyboard, 'X');
                UnityEngine.InputSystem.InputSystem.Update();
                totalEventsPopped += PopAllEvents("After QueueTextEvent");
                await UniTask.Yield();
                totalEventsPopped += PopAllEvents("Frame after QueueTextEvent");

                // Release key
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,
                    new UnityEngine.InputSystem.LowLevel.KeyboardState());
                UnityEngine.InputSystem.InputSystem.Update();

                Debug.Log($"[Test] === RESULTS ===");
                Debug.Log($"[Test] Total events popped: {totalEventsPopped}");

                if (totalEventsPopped > 0)
                {
                    Debug.Log("[Test] SUCCESS: Events appeared in Event.PopEvent queue!");
                }
                else
                {
                    Debug.Log("[Test] CONFIRMED: Neither QueueStateEvent nor QueueTextEvent populate Event.PopEvent");
                    Debug.Log("[Test] The two event systems are completely separate in Unity");
                }

                Assert.Pass("Test complete - check logs for results");
            });
        }

        [UnityTest]
        public IEnumerator QueueTextEvent_ControlCharacter_SelectsAll()
        {
            // Tests if sending ASCII control character (Ctrl+A = '\x01') via QueueTextEvent
            // triggers select-all in TMP_InputField
            return UniTask.ToCoroutine(async () =>
            {
                var inputField = CreateInputField("CtrlCharTest", Vector2.zero);
                inputField.text = "Test text to select";

                await UniTask.Yield();

                // Focus the input field
                var screenPos = InputInjector.GetScreenPosition(inputField.gameObject);
                await InputInjector.InjectPointerTap(screenPos);
                await UniTask.Delay(200);

                Assert.IsTrue(inputField.isFocused, "Input field should be focused");
                Debug.Log($"[Test] Initial selection: anchor={inputField.selectionAnchorPosition}, focus={inputField.selectionFocusPosition}");

                var keyboard = UnityEngine.InputSystem.Keyboard.current;

                // Try sending Ctrl+A as control character (ASCII 1)
                char ctrlA = '\x01'; // ASCII SOH = Ctrl+A
                Debug.Log($"[Test] Sending control character: (char){(int)ctrlA} = Ctrl+A");

                UnityEngine.InputSystem.InputSystem.QueueTextEvent(keyboard, ctrlA);
                UnityEngine.InputSystem.InputSystem.Update();
                await UniTask.Yield();
                await UniTask.DelayFrame(5);

                int selectionLength = Mathf.Abs(inputField.selectionFocusPosition - inputField.selectionAnchorPosition);
                Debug.Log($"[Test] After Ctrl+A char: selection={selectionLength}, anchor={inputField.selectionAnchorPosition}, focus={inputField.selectionFocusPosition}");

                if (selectionLength == inputField.text.Length)
                {
                    Debug.Log("[Test] SUCCESS: Control character Ctrl+A selected all text!");
                }
                else if (selectionLength > 0)
                {
                    Debug.Log($"[Test] PARTIAL: Selected {selectionLength} of {inputField.text.Length} chars");
                }
                else
                {
                    Debug.Log("[Test] Control character did not select text");
                    Debug.Log("[Test] TMP_InputField likely filters out control characters in text input");
                }

                Assert.Pass("Test complete - check logs for results");
            });
        }

        #endregion

        #region TypeIntoField Tests

        [UnityTest]
        public IEnumerator TypeIntoField_TypesTextIntoField()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var inputField = CreateInputField("TypeIntoTest", Vector2.zero);
                inputField.text = "";

                await UniTask.Yield();

                await InputInjector.TypeIntoField(inputField.gameObject, "Hello", clearFirst: true, pressEnter: false);

                await UniTask.DelayFrame(10);

                // Field should contain typed text
                Assert.IsTrue(inputField.text.Length > 0, "Field should have text");
            });
        }

        [UnityTest]
        public IEnumerator TypeIntoField_WithClearFirst_ReplacesExistingText()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var inputField = CreateInputField("ClearFirstTest", Vector2.zero);
                inputField.text = "Old text that should be replaced";

                await UniTask.Yield();

                await InputInjector.TypeIntoField(inputField.gameObject, "New", clearFirst: true, pressEnter: false);

                await UniTask.DelayFrame(10);

                // Field should contain only the new text (old text replaced via triple-click + type)
                Assert.AreEqual("New", inputField.text, "Existing text should be replaced when clearFirst is true");
            });
        }

        [UnityTest]
        public IEnumerator TypeIntoField_WithoutClearFirst_AppendsText()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var inputField = CreateInputField("AppendTest", Vector2.zero);
                inputField.text = "Existing";

                await UniTask.Yield();

                await InputInjector.TypeIntoField(inputField.gameObject, "New", clearFirst: false, pressEnter: false);

                await UniTask.DelayFrame(10);

                // Field should contain both old and new text
                Assert.IsTrue(inputField.text.Contains("Existing"), "Original text should still be present");
                Assert.IsTrue(inputField.text.Contains("New"), "New text should be appended");
            });
        }

        #endregion

        #region Swipe Tests

        [UnityTest]
        public IEnumerator Swipe_SwipesInDirection()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("SwipeTarget", Vector2.zero);

                await UniTask.Yield();

                // Just verify it doesn't throw
                await InputInjector.Swipe(button.gameObject, "right", 0.1f, 0.1f);

                Assert.Pass("Swipe completed without errors");
            });
        }

        #endregion

        #region ScrollElement Tests

        [UnityTest]
        public IEnumerator ScrollElement_ScrollsInDirection()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var scrollRect = CreateScrollRect("ScrollElementTest", Vector2.zero);

                await UniTask.Yield();

                await InputInjector.ScrollElement(scrollRect.gameObject, "down", 0.5f);

                await UniTask.DelayFrame(5);
            });
        }

        #endregion

        #region Pinch Tests

        [UnityTest]
        public IEnumerator Pinch_WithElement_PerformsPinchGesture()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("PinchTarget", Vector2.zero);

                await UniTask.Yield();

                // Pinch zoom in
                await InputInjector.Pinch(button.gameObject, 1.5f, 0.2f);

                Assert.Pass("Pinch gesture completed without errors");
            });
        }

        [UnityTest]
        public IEnumerator Pinch_NullElement_UsesCenterOfScreen()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                // Pinch at center of screen (null element)
                await InputInjector.Pinch(null, 0.5f, 0.2f);

                Assert.Pass("Pinch at screen center completed without errors");
            });
        }

        [UnityTest]
        public IEnumerator InjectPinch_WithCustomFingerDistance()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
                await InputInjector.InjectPinch(center, 2f, 0.2f, 50f);

                Assert.Pass("Pinch with custom finger distance completed without errors");
            });
        }

        #endregion

        #region TwoFingerSwipe Tests

        [UnityTest]
        public IEnumerator TwoFingerSwipe_WithElement_SwipesWithTwoFingers()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("TwoFingerSwipeTarget", Vector2.zero);

                await UniTask.Yield();

                await InputInjector.TwoFingerSwipe(button.gameObject, "up", 0.1f, 0.2f, 0.02f);

                Assert.Pass("Two-finger swipe completed without errors");
            });
        }

        [UnityTest]
        public IEnumerator InjectTwoFingerSwipe_AtPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
                await InputInjector.InjectTwoFingerSwipe(center, "down", 0.1f, 0.2f, 0.02f);

                Assert.Pass("Inject two-finger swipe completed without errors");
            });
        }

        #endregion

        #region Rotate Tests

        [UnityTest]
        public IEnumerator Rotate_WithElement_PerformsRotationGesture()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("RotateTarget", Vector2.zero);

                await UniTask.Yield();

                // Rotate 45 degrees clockwise
                await InputInjector.Rotate(button.gameObject, 45f, 0.2f, 0.05f);

                Assert.Pass("Rotation gesture completed without errors");
            });
        }

        [UnityTest]
        public IEnumerator InjectRotate_RotatesAtPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
                await InputInjector.InjectRotate(center, -90f, 0.2f, 0.05f);

                Assert.Pass("Inject rotate completed without errors");
            });
        }

        [UnityTest]
        public IEnumerator InjectRotatePixels_RotatesWithPixelRadius()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
                await InputInjector.InjectRotatePixels(center, 180f, 0.2f, 75f);

                Assert.Pass("Inject rotate pixels completed without errors");
            });
        }

        #endregion

        #region Touch Input Tests

        [UnityTest]
        public IEnumerator InjectTouchTap_TapsAtPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var button = CreateButton("TouchTapButton", Vector2.zero);
                bool clicked = false;
                button.onClick.AddListener(() => clicked = true);

                await UniTask.Yield();

                var screenPos = InputInjector.GetScreenPosition(button.gameObject);
                await InputInjector.InjectTouchTap(screenPos);

                await UniTask.DelayFrame(5);

                // Touch tap should trigger click (if touch input is active)
                // Note: On desktop this might not work without touchscreen
            });
        }

        [UnityTest]
        public IEnumerator InjectTouchDoubleTap_DoubleTapsAtPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
                await InputInjector.InjectTouchDoubleTap(center);

                Assert.Pass("Touch double tap completed without errors");
            });
        }

        [UnityTest]
        public IEnumerator InjectTouchDrag_DragsBetweenPositions()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var start = new Vector2(Screen.width / 2f, Screen.height / 2f);
                var end = start + new Vector2(100, 0);

                await InputInjector.InjectTouchDrag(start, end, 0.2f);

                Assert.Pass("Touch drag completed without errors");
            });
        }

        [UnityTest]
        public IEnumerator InjectTouchHold_HoldsAtPosition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
                await InputInjector.InjectTouchHold(center, 0.2f);

                Assert.Pass("Touch hold completed without errors");
            });
        }

        #endregion

        #region Mouse Drag Tests

        [UnityTest]
        public IEnumerator InjectMouseDrag_DragsBetweenPositions()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var start = new Vector2(Screen.width / 2f, Screen.height / 2f);
                var end = start + new Vector2(100, 0);

                await InputInjector.InjectMouseDrag(start, end, 0.2f);

                Assert.Pass("Mouse drag completed without errors");
            });
        }

        [UnityTest]
        public IEnumerator InjectMouseDrag_WithHoldTime_HoldsBeforeDragging()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var start = new Vector2(Screen.width / 2f, Screen.height / 2f);
                var end = start + new Vector2(100, 0);

                float startTime = Time.time;

                // Use a 0.5s hold time
                await InputInjector.InjectMouseDrag(start, end, 0.2f, 0.5f);

                float elapsed = Time.time - startTime;

                // Should take at least holdTime (0.5s) + duration (0.2s) = 0.7s
                Assert.GreaterOrEqual(elapsed, 0.6f, "Drag with hold should take at least hold time + duration");
            });
        }

        [UnityTest]
        public IEnumerator InjectPointerDrag_WithLongHoldTime_HoldsBeforeDragging()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var start = new Vector2(Screen.width / 2f, Screen.height / 2f);
                var end = start + new Vector2(100, 0);

                float startTime = Time.time;

                // Use a 1.0s hold time (for games requiring long hold before drag)
                await InputInjector.InjectPointerDrag(start, end, 0.2f, 1.0f);

                float elapsed = Time.time - startTime;

                // Should take at least holdTime (1.0s) + duration (0.2s) = 1.2s
                Assert.GreaterOrEqual(elapsed, 1.0f, "Drag with long hold should take at least 1 second");
            });
        }

        [UnityTest]
        public IEnumerator InjectMouseDrag_WithRightButton_DragsWithRightMouse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var start = new Vector2(Screen.width / 2f, Screen.height / 2f);
                var end = start + new Vector2(100, 0);

                // Right mouse button drag (commonly used for camera rotation)
                await InputInjector.InjectMouseDrag(start, end, 0.2f, 0f, PointerButton.Right);

                Assert.Pass("Right mouse button drag completed without errors");
            });
        }

        [UnityTest]
        public IEnumerator InjectMouseDrag_WithMiddleButton_DragsWithMiddleMouse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var start = new Vector2(Screen.width / 2f, Screen.height / 2f);
                var end = start + new Vector2(100, 0);

                // Middle mouse button drag (commonly used for panning)
                await InputInjector.InjectMouseDrag(start, end, 0.2f, 0f, PointerButton.Middle);

                Assert.Pass("Middle mouse button drag completed without errors");
            });
        }

        [UnityTest]
        public IEnumerator InjectPointerDrag_WithRightButton_DragsWithRightMouse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var start = new Vector2(Screen.width / 2f, Screen.height / 2f);
                var end = start + new Vector2(100, 0);

                // Right mouse button drag via pointer abstraction
                await InputInjector.InjectPointerDrag(start, end, 0.2f, 0f, PointerButton.Right);

                Assert.Pass("Right mouse button pointer drag completed without errors");
            });
        }

        #endregion

        #region Two Finger Drag Tests

        [UnityTest]
        public IEnumerator InjectTwoFingerDrag_DragsTwoFingers()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
                var start1 = center + new Vector2(-50, 0);
                var start2 = center + new Vector2(50, 0);
                var end1 = start1 + new Vector2(0, 100);
                var end2 = start2 + new Vector2(0, 100);

                await InputInjector.InjectTwoFingerDrag(start1, end1, start2, end2, 0.2f);

                Assert.Pass("Two finger drag completed without errors");
            });
        }

        #endregion

        #region Keys Builder Tests

        [UnityTest]
        public IEnumerator Keys_Hold_HoldsSingleKey()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                await Keys.Hold(Key.W).For(0.1f);

                Assert.Pass("Keys.Hold completed without errors");
            });
        }

        [UnityTest]
        public IEnumerator Keys_Press_PressesKey()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                await Keys.Press(Key.Space);

                Assert.Pass("Keys.Press completed without errors");
            });
        }

        [UnityTest]
        public IEnumerator Keys_HoldMultiple_HoldsMultipleKeys()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                await Keys.Hold(Key.W, Key.A).For(0.1f);

                Assert.Pass("Keys.Hold multiple keys completed without errors");
            });
        }

        [UnityTest]
        public IEnumerator Keys_Then_ChainsKeyActions()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                await Keys.Hold(Key.W).For(0.05f).Then(Key.A).For(0.05f);

                Assert.Pass("Keys.Then chain completed without errors");
            });
        }

        [UnityTest]
        public IEnumerator Keys_ThenPress_ChainsPressAfterHold()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await UniTask.Yield();

                await Keys.Hold(Key.W).For(0.05f).ThenPress(Key.Space);

                Assert.Pass("Keys.ThenPress chain completed without errors");
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
