using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;
using ODDGames.UIAutomation;
using ODDGames.UIAutomation.AI;

namespace ODDGames.UIAutomation.Tests
{
    /// <summary>
    /// Tests for AI tool schema definitions, parsing, and execution.
    /// Verifies that AI tools are correctly defined, parsed, and executed.
    /// </summary>
    [TestFixture]
    public class AIToolTests
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
        }

        #region ToolSchema Tests

        [Test]
        public void GetAllTools_ReturnsExpectedTools()
        {
            var tools = ToolSchema.GetAllTools();

            Assert.IsNotNull(tools, "GetAllTools should not return null");
            Assert.IsNotEmpty(tools, "GetAllTools should return at least one tool");

            // Verify all expected tools are present
            var expectedTools = new[]
            {
                "click", "double_click", "triple_click", "hold", "type",
                "drag", "scroll", "swipe", "two_finger_swipe", "pinch", "rotate",
                "set_slider", "set_scrollbar", "click_dropdown",
                "key_press", "key_hold", "wait", "screenshot", "pass", "fail"
            };

            var toolNames = tools.Select(t => t.Name).ToList();

            foreach (var expected in expectedTools)
            {
                Assert.Contains(expected, toolNames, $"Tool '{expected}' should be present");
            }
        }

        [Test]
        public void GetAllTools_AllToolsHaveRequiredFields()
        {
            var tools = ToolSchema.GetAllTools();

            foreach (var tool in tools)
            {
                Assert.IsFalse(string.IsNullOrEmpty(tool.Name), "Tool name should not be empty");
                Assert.IsFalse(string.IsNullOrEmpty(tool.Description), $"Tool '{tool.Name}' description should not be empty");
                Assert.IsNotNull(tool.Parameters, $"Tool '{tool.Name}' parameters should not be null");
            }
        }

        [Test]
        public void ClickTool_HasCorrectSchema()
        {
            var tools = ToolSchema.GetAllTools();
            var clickTool = tools.FirstOrDefault(t => t.Name == "click");

            Assert.IsNotNull(clickTool, "Click tool should exist");
            Assert.IsNotNull(clickTool.Parameters.Properties, "Click tool should have properties");
            Assert.IsTrue(clickTool.Parameters.Properties.ContainsKey("search"), "Click tool should have 'search' property");
            Assert.IsTrue(clickTool.Parameters.Properties.ContainsKey("x"), "Click tool should have 'x' property");
            Assert.IsTrue(clickTool.Parameters.Properties.ContainsKey("y"), "Click tool should have 'y' property");
        }

        [Test]
        public void TypeTool_HasCorrectSchema()
        {
            var tools = ToolSchema.GetAllTools();
            var typeTool = tools.FirstOrDefault(t => t.Name == "type");

            Assert.IsNotNull(typeTool, "Type tool should exist");
            Assert.IsNotNull(typeTool.Parameters.Properties, "Type tool should have properties");
            Assert.IsTrue(typeTool.Parameters.Properties.ContainsKey("search"), "Type tool should have 'search' property");
            Assert.IsTrue(typeTool.Parameters.Properties.ContainsKey("text"), "Type tool should have 'text' property");
            Assert.IsTrue(typeTool.Parameters.Properties.ContainsKey("clear_first"), "Type tool should have 'clear_first' property");
            Assert.IsTrue(typeTool.Parameters.Properties.ContainsKey("press_enter"), "Type tool should have 'press_enter' property");

            // Verify required fields
            Assert.Contains("search", typeTool.Parameters.Required);
            Assert.Contains("text", typeTool.Parameters.Required);
        }

        [Test]
        public void DragTool_HasCorrectSchema()
        {
            var tools = ToolSchema.GetAllTools();
            var dragTool = tools.FirstOrDefault(t => t.Name == "drag");

            Assert.IsNotNull(dragTool, "Drag tool should exist");
            Assert.IsNotNull(dragTool.Parameters.Properties, "Drag tool should have properties");
            Assert.IsTrue(dragTool.Parameters.Properties.ContainsKey("from"), "Drag tool should have 'from' property");
            Assert.IsTrue(dragTool.Parameters.Properties.ContainsKey("to"), "Drag tool should have 'to' property");
            Assert.IsTrue(dragTool.Parameters.Properties.ContainsKey("direction"), "Drag tool should have 'direction' property");
            Assert.IsTrue(dragTool.Parameters.Properties.ContainsKey("distance"), "Drag tool should have 'distance' property");
            Assert.IsTrue(dragTool.Parameters.Properties.ContainsKey("duration"), "Drag tool should have 'duration' property");

            // Verify required fields
            Assert.Contains("from", dragTool.Parameters.Required);
        }

        [Test]
        public void SearchProperty_HasCorrectStructure()
        {
            var tools = ToolSchema.GetAllTools();
            var clickTool = tools.FirstOrDefault(t => t.Name == "click");
            var searchProp = clickTool.Parameters.Properties["search"];

            Assert.AreEqual("object", searchProp.Type, "Search property should be object type");
            Assert.IsNotNull(searchProp.Properties, "Search property should have nested properties");
            Assert.IsTrue(searchProp.Properties.ContainsKey("base"), "Search should have 'base' property");
            Assert.IsTrue(searchProp.Properties.ContainsKey("value"), "Search should have 'value' property");
            Assert.IsTrue(searchProp.Properties.ContainsKey("chain"), "Search should have 'chain' property");

            // Verify base has correct enum values
            var baseProp = searchProp.Properties["base"];
            Assert.IsNotNull(baseProp.Enum, "Base property should have enum values");
            Assert.Contains("text", baseProp.Enum);
            Assert.Contains("name", baseProp.Enum);
            Assert.Contains("type", baseProp.Enum);
        }

        #endregion

        #region Parse Tests - Click

        [Test]
        public void Parse_ClickWithSearch_CreatesClickAction()
        {
            var call = new ToolCall
            {
                Name = "click",
                Arguments = new Dictionary<string, object>
                {
                    ["search"] = "{\"base\":\"name\",\"value\":\"TestButton\"}"
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<ClickAction>(action, "Should create ClickAction");
            var clickAction = (ClickAction)action;
            Assert.IsNotNull(clickAction.Search, "Search should be set");
            Assert.IsTrue(clickAction.Search.value.Contains("TestButton"), "Search value should contain button name");
        }

        [Test]
        public void Parse_ClickWithCoordinates_CreatesClickAction()
        {
            var call = new ToolCall
            {
                Name = "click",
                Arguments = new Dictionary<string, object>
                {
                    ["x"] = 0.5f,
                    ["y"] = 0.5f
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<ClickAction>(action, "Should create ClickAction");
            var clickAction = (ClickAction)action;
            Assert.IsTrue(clickAction.ScreenPosition.HasValue, "ScreenPosition should be set");
            Assert.AreEqual(0.5f, clickAction.ScreenPosition.Value.x, 0.001f);
            Assert.AreEqual(0.5f, clickAction.ScreenPosition.Value.y, 0.001f);
        }

        #endregion

        #region Parse Tests - Double Click

        [Test]
        public void Parse_DoubleClick_CreatesDoubleClickAction()
        {
            var call = new ToolCall
            {
                Name = "double_click",
                Arguments = new Dictionary<string, object>
                {
                    ["search"] = "{\"base\":\"name\",\"value\":\"DoubleClickButton\"}"
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<DoubleClickAction>(action, "Should create DoubleClickAction");
        }

        #endregion

        #region Parse Tests - Triple Click

        [Test]
        public void Parse_TripleClick_CreatesTripleClickAction()
        {
            var call = new ToolCall
            {
                Name = "triple_click",
                Arguments = new Dictionary<string, object>
                {
                    ["search"] = "{\"base\":\"name\",\"value\":\"TripleClickButton\"}"
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<TripleClickAction>(action, "Should create TripleClickAction");
        }

        #endregion

        #region Parse Tests - Hold

        [Test]
        public void Parse_Hold_CreatesHoldAction()
        {
            var call = new ToolCall
            {
                Name = "hold",
                Arguments = new Dictionary<string, object>
                {
                    ["search"] = "{\"base\":\"name\",\"value\":\"HoldButton\"}",
                    ["duration"] = 2.0f
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<HoldAction>(action, "Should create HoldAction");
            var holdAction = (HoldAction)action;
            Assert.AreEqual(2.0f, holdAction.Duration, 0.001f);
        }

        #endregion

        #region Parse Tests - Type

        [Test]
        public void Parse_Type_CreatesTypeAction()
        {
            var call = new ToolCall
            {
                Name = "type",
                Arguments = new Dictionary<string, object>
                {
                    ["search"] = "{\"base\":\"name\",\"value\":\"InputField\"}",
                    ["text"] = "Hello World",
                    ["clear_first"] = true,
                    ["press_enter"] = false
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<TypeAction>(action, "Should create TypeAction");
            var typeAction = (TypeAction)action;
            Assert.AreEqual("Hello World", typeAction.Text);
            Assert.IsTrue(typeAction.ClearFirst);
            Assert.IsFalse(typeAction.PressEnter);
        }

        #endregion

        #region Parse Tests - Drag

        [Test]
        public void Parse_DragWithDirection_CreatesDragAction()
        {
            var call = new ToolCall
            {
                Name = "drag",
                Arguments = new Dictionary<string, object>
                {
                    ["from"] = "{\"base\":\"name\",\"value\":\"DragElement\"}",
                    ["direction"] = "right",
                    ["distance"] = 100.0f,
                    ["duration"] = 0.5f
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<DragAction>(action, "Should create DragAction");
            var dragAction = (DragAction)action;
            Assert.AreEqual("right", dragAction.Direction);
            Assert.AreEqual(100.0f, dragAction.Distance, 0.001f);
            Assert.AreEqual(0.5f, dragAction.Duration, 0.001f);
        }

        #endregion

        #region Parse Tests - Scroll

        [Test]
        public void Parse_Scroll_CreatesScrollAction()
        {
            var call = new ToolCall
            {
                Name = "scroll",
                Arguments = new Dictionary<string, object>
                {
                    ["search"] = "{\"base\":\"name\",\"value\":\"ScrollView\"}",
                    ["direction"] = "down",
                    ["amount"] = 0.5f
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<ScrollAction>(action, "Should create ScrollAction");
            var scrollAction = (ScrollAction)action;
            Assert.AreEqual("down", scrollAction.Direction);
            Assert.AreEqual(0.5f, scrollAction.Amount, 0.001f);
        }

        #endregion

        #region Parse Tests - Swipe

        [Test]
        public void Parse_Swipe_CreatesSwipeAction()
        {
            var call = new ToolCall
            {
                Name = "swipe",
                Arguments = new Dictionary<string, object>
                {
                    ["search"] = "{\"base\":\"name\",\"value\":\"SwipeArea\"}",
                    ["direction"] = "left",
                    ["distance"] = 0.3f,
                    ["duration"] = 0.4f
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<SwipeAction>(action, "Should create SwipeAction");
            var swipeAction = (SwipeAction)action;
            Assert.AreEqual("left", swipeAction.Direction);
            Assert.AreEqual(0.3f, swipeAction.Distance, 0.001f);
            Assert.AreEqual(0.4f, swipeAction.Duration, 0.001f);
        }

        #endregion

        #region Parse Tests - Two Finger Swipe

        [Test]
        public void Parse_TwoFingerSwipe_CreatesTwoFingerSwipeAction()
        {
            var call = new ToolCall
            {
                Name = "two_finger_swipe",
                Arguments = new Dictionary<string, object>
                {
                    ["search"] = "{\"base\":\"name\",\"value\":\"MapView\"}",
                    ["direction"] = "up",
                    ["distance"] = 0.25f,
                    ["duration"] = 0.3f,
                    ["finger_spacing"] = 0.05f
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<TwoFingerSwipeAction>(action, "Should create TwoFingerSwipeAction");
            var swipeAction = (TwoFingerSwipeAction)action;
            Assert.AreEqual("up", swipeAction.Direction);
            Assert.AreEqual(0.05f, swipeAction.FingerSpacing, 0.001f);
        }

        #endregion

        #region Parse Tests - Pinch

        [Test]
        public void Parse_Pinch_CreatesPinchAction()
        {
            var call = new ToolCall
            {
                Name = "pinch",
                Arguments = new Dictionary<string, object>
                {
                    ["search"] = "{\"base\":\"name\",\"value\":\"ZoomArea\"}",
                    ["scale"] = 1.5f,
                    ["duration"] = 0.5f
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<PinchAction>(action, "Should create PinchAction");
            var pinchAction = (PinchAction)action;
            Assert.AreEqual(1.5f, pinchAction.Scale, 0.001f);
            Assert.AreEqual(0.5f, pinchAction.Duration, 0.001f);
        }

        #endregion

        #region Parse Tests - Rotate

        [Test]
        public void Parse_Rotate_CreatesRotateAction()
        {
            var call = new ToolCall
            {
                Name = "rotate",
                Arguments = new Dictionary<string, object>
                {
                    ["search"] = "{\"base\":\"name\",\"value\":\"RotateArea\"}",
                    ["degrees"] = 45.0f,
                    ["duration"] = 0.5f,
                    ["finger_distance"] = 0.1f
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<RotateAction>(action, "Should create RotateAction");
            var rotateAction = (RotateAction)action;
            Assert.AreEqual(45.0f, rotateAction.Degrees, 0.001f);
            Assert.AreEqual(0.1f, rotateAction.FingerDistance, 0.001f);
        }

        #endregion

        #region Parse Tests - Set Slider

        [Test]
        public void Parse_SetSlider_CreatesSetSliderAction()
        {
            var call = new ToolCall
            {
                Name = "set_slider",
                Arguments = new Dictionary<string, object>
                {
                    ["search"] = "{\"base\":\"name\",\"value\":\"VolumeSlider\"}",
                    ["value"] = 0.75f
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<SetSliderAction>(action, "Should create SetSliderAction");
            var sliderAction = (SetSliderAction)action;
            Assert.AreEqual(0.75f, sliderAction.Value, 0.001f);
        }

        #endregion

        #region Parse Tests - Set Scrollbar

        [Test]
        public void Parse_SetScrollbar_CreatesSetScrollbarAction()
        {
            var call = new ToolCall
            {
                Name = "set_scrollbar",
                Arguments = new Dictionary<string, object>
                {
                    ["search"] = "{\"base\":\"name\",\"value\":\"ContentScrollbar\"}",
                    ["value"] = 0.5f
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<SetScrollbarAction>(action, "Should create SetScrollbarAction");
            var scrollbarAction = (SetScrollbarAction)action;
            Assert.AreEqual(0.5f, scrollbarAction.Value, 0.001f);
        }

        #endregion

        #region Parse Tests - Click Dropdown

        [Test]
        public void Parse_ClickDropdownByIndex_CreatesClickDropdownAction()
        {
            var call = new ToolCall
            {
                Name = "click_dropdown",
                Arguments = new Dictionary<string, object>
                {
                    ["search"] = "{\"base\":\"name\",\"value\":\"OptionsDropdown\"}",
                    ["index"] = 2
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<ClickDropdownAction>(action, "Should create ClickDropdownAction");
            var dropdownAction = (ClickDropdownAction)action;
            Assert.AreEqual(2, dropdownAction.OptionIndex);
        }

        [Test]
        public void Parse_ClickDropdownByLabel_CreatesClickDropdownAction()
        {
            var call = new ToolCall
            {
                Name = "click_dropdown",
                Arguments = new Dictionary<string, object>
                {
                    ["search"] = "{\"base\":\"name\",\"value\":\"OptionsDropdown\"}",
                    ["label"] = "Option 3"
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<ClickDropdownAction>(action, "Should create ClickDropdownAction");
            var dropdownAction = (ClickDropdownAction)action;
            Assert.AreEqual("Option 3", dropdownAction.OptionLabel);
        }

        #endregion

        #region Parse Tests - Key Press

        [Test]
        public void Parse_KeyPress_CreatesKeyPressAction()
        {
            var call = new ToolCall
            {
                Name = "key_press",
                Arguments = new Dictionary<string, object>
                {
                    ["key"] = "Enter"
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<KeyPressAction>(action, "Should create KeyPressAction");
            var keyAction = (KeyPressAction)action;
            Assert.AreEqual("Enter", keyAction.Key);
        }

        #endregion

        #region Parse Tests - Key Hold

        [Test]
        public void Parse_KeyHold_CreatesKeyHoldAction()
        {
            var call = new ToolCall
            {
                Name = "key_hold",
                Arguments = new Dictionary<string, object>
                {
                    ["keys"] = new List<object> { "W", "LeftShift" },
                    ["duration"] = 1.0f
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<KeyHoldAction>(action, "Should create KeyHoldAction");
            var keyAction = (KeyHoldAction)action;
            Assert.AreEqual(2, keyAction.Keys.Length);
            Assert.AreEqual("W", keyAction.Keys[0]);
            Assert.AreEqual("LeftShift", keyAction.Keys[1]);
            Assert.AreEqual(1.0f, keyAction.Duration, 0.001f);
        }

        #endregion

        #region Parse Tests - Wait

        [Test]
        public void Parse_Wait_CreatesWaitAction()
        {
            var call = new ToolCall
            {
                Name = "wait",
                Arguments = new Dictionary<string, object>
                {
                    ["seconds"] = 2.5f
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<WaitAction>(action, "Should create WaitAction");
            var waitAction = (WaitAction)action;
            Assert.AreEqual(2.5f, waitAction.Seconds, 0.001f);
        }

        [Test]
        public void Parse_Wait_ClampsValueToMax10Seconds()
        {
            var call = new ToolCall
            {
                Name = "wait",
                Arguments = new Dictionary<string, object>
                {
                    ["seconds"] = 30.0f
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<WaitAction>(action, "Should create WaitAction");
            var waitAction = (WaitAction)action;
            Assert.AreEqual(10.0f, waitAction.Seconds, 0.001f, "Wait should be clamped to 10 seconds max");
        }

        #endregion

        #region Parse Tests - Pass/Fail

        [Test]
        public void Parse_Pass_CreatesPassAction()
        {
            var call = new ToolCall
            {
                Name = "pass",
                Arguments = new Dictionary<string, object>
                {
                    ["reason"] = "Test completed successfully"
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<PassAction>(action, "Should create PassAction");
            var passAction = (PassAction)action;
            Assert.AreEqual("Test completed successfully", passAction.Reason);
        }

        [Test]
        public void Parse_Fail_CreatesFailAction()
        {
            var call = new ToolCall
            {
                Name = "fail",
                Arguments = new Dictionary<string, object>
                {
                    ["reason"] = "Could not find login button"
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<FailAction>(action, "Should create FailAction");
            var failAction = (FailAction)action;
            Assert.AreEqual("Could not find login button", failAction.Reason);
        }

        #endregion

        #region Parse Tests - Screenshot

        [Test]
        public void Parse_Screenshot_CreatesScreenshotAction()
        {
            var call = new ToolCall
            {
                Name = "screenshot",
                Arguments = new Dictionary<string, object>
                {
                    ["reason"] = "Need to verify UI state"
                }
            };

            var action = AIActionExecutor.Parse(call, new ScreenState());

            Assert.IsInstanceOf<ScreenshotAction>(action, "Should create ScreenshotAction");
            var screenshotAction = (ScreenshotAction)action;
            Assert.AreEqual("Need to verify UI state", screenshotAction.Reason);
        }

        #endregion

        #region Parse Tests - Unknown Tool

        [Test]
        public void Parse_UnknownTool_ThrowsArgumentException()
        {
            var call = new ToolCall
            {
                Name = "unknown_tool",
                Arguments = new Dictionary<string, object>()
            };

            Assert.Throws<ArgumentException>(() => AIActionExecutor.Parse(call, new ScreenState()));
        }

        #endregion

        #region Execute Tests - Click

        [Test]
        public async Task Execute_ClickWithElement_ClicksElement()
        {
            var button = CreateButton("ClickTestButton", new Vector2(0, 0));
            bool clicked = false;
            button.onClick.AddListener(() => clicked = true);

            await Async.DelayFrames(1);

            var action = new ClickAction
            {
                Search = new SearchQuery { searchBase = "name", value = "ClickTestButton" }
            };

            var result = await AIActionExecutor.ExecuteAsync(action);

            Assert.IsTrue(result.Success, "Action should succeed");
            Assert.IsTrue(clicked, "Button should have been clicked");
        }

        [Test]
        public async Task Execute_ClickWithScreenPosition_ClicksAtPosition()
        {
            var button = CreateButton("PositionTestButton", new Vector2(0, 0));
            bool clicked = false;
            button.onClick.AddListener(() => clicked = true);

            await Async.DelayFrames(1);

            // Get the button's screen position and convert to normalized (0-1)
            var rect = button.GetComponent<RectTransform>();
            var screenPos = RectTransformUtility.WorldToScreenPoint(null, rect.position);
            var normalizedPos = new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);

            var action = new ClickAction
            {
                ScreenPosition = normalizedPos
            };

            var result = await AIActionExecutor.ExecuteAsync(action);

            Assert.IsTrue(result.Success, "Action should succeed");
            Assert.IsTrue(clicked, "Button should have been clicked at position");
        }

        #endregion

        #region Execute Tests - Double Click

        [Test]
        public async Task Execute_DoubleClick_DoubleClicksElement()
        {
            var button = CreateButton("DoubleClickTestButton", new Vector2(0, 0));
            int clickCount = 0;
            button.onClick.AddListener(() => clickCount++);

            await Async.DelayFrames(1);

            var action = new DoubleClickAction
            {
                Search = new SearchQuery { searchBase = "name", value = "DoubleClickTestButton" }
            };

            var result = await AIActionExecutor.ExecuteAsync(action);

            Assert.IsTrue(result.Success, "Action should succeed");
            Assert.AreEqual(2, clickCount, "Button should have been clicked twice");
        }

        #endregion

        #region Execute Tests - Type

        [Test]
        public async Task Execute_Type_TypesIntoInputField()
        {
            var inputField = CreateTMPInputField("TypeTestInput", new Vector2(0, 0));

            await Async.DelayFrames(1);

            var action = new TypeAction
            {
                Search = new SearchQuery { searchBase = "name", value = "TypeTestInput" },
                Text = "Test Input",
                ClearFirst = true,
                PressEnter = false
            };

            var result = await AIActionExecutor.ExecuteAsync(action);

            // Wait for text input to be processed
            await Async.DelayFrames(10);

            Assert.IsTrue(result.Success, "Action should succeed");
            Assert.AreEqual("Test Input", inputField.text, "Input field should contain typed text");
        }

        #endregion

        #region Execute Tests - Wait

        [Test]
        public async Task Execute_Wait_WaitsForDuration()
        {
            var action = new WaitAction { Seconds = 0.2f };

            var startTime = Time.realtimeSinceStartup;
            var result = await AIActionExecutor.ExecuteAsync(action);
            var elapsed = Time.realtimeSinceStartup - startTime;

            Assert.IsTrue(result.Success, "Action should succeed");
            Assert.GreaterOrEqual(elapsed, 0.15f, "Should have waited at least 0.15 seconds");
        }

        #endregion

        #region Execute Tests - Set Slider

        [Test]
        public async Task Execute_SetSlider_SetsSliderValue()
        {
            var slider = CreateSlider("SliderTestSlider", new Vector2(0, 0));
            slider.value = 0f;

            await Async.DelayFrames(1);

            var action = new SetSliderAction
            {
                Search = new SearchQuery { searchBase = "name", value = "SliderTestSlider" },
                Value = 0.75f
            };

            var result = await AIActionExecutor.ExecuteAsync(action);

            Assert.IsTrue(result.Success, "Action should succeed");
            Assert.AreEqual(0.75f, slider.value, 0.1f, "Slider should be at 75%");
        }

        #endregion

        #region Execute Tests - Key Press

        [Test]
        public async Task Execute_KeyPress_PressesKey()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                keyboard = InputSystem.AddDevice<Keyboard>();

            await Async.DelayFrames(1);

            var action = new KeyPressAction { Key = "Space" };

            var result = await AIActionExecutor.ExecuteAsync(action);

            Assert.IsTrue(result.Success, "Action should succeed");
        }

        #endregion

        #region Execute Tests - Key Hold

        [Test]
        public async Task Execute_KeyHold_HoldsKeysForDuration()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                keyboard = InputSystem.AddDevice<Keyboard>();

            await Async.DelayFrames(1);

            var action = new KeyHoldAction
            {
                Keys = new[] { "W" },
                Duration = 0.2f
            };

            var startTime = Time.realtimeSinceStartup;
            var result = await AIActionExecutor.ExecuteAsync(action);
            var elapsed = Time.realtimeSinceStartup - startTime;

            Assert.IsTrue(result.Success, "Action should succeed");
            Assert.GreaterOrEqual(elapsed, 0.15f, "Should have held key for approximately 0.2s");
        }

        #endregion

        #region Execute Tests - Scrollbar

        [Test]
        public async Task Execute_SetScrollbar_SetsScrollbarValue()
        {
            var scrollbar = CreateScrollbar("ScrollbarTest", new Vector2(0, 0));
            scrollbar.value = 0f;

            await Async.DelayFrames(1);

            var action = new SetScrollbarAction
            {
                Search = new SearchQuery { searchBase = "name", value = "ScrollbarTest" },
                Value = 0.5f
            };

            var result = await AIActionExecutor.ExecuteAsync(action);

            Assert.IsTrue(result.Success, "Action should succeed");
            Assert.AreEqual(0.5f, scrollbar.value, 0.1f, "Scrollbar should be at 50%");
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

        #endregion
    }
}
