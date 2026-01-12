using System.Collections.Generic;

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// Defines the tools available to the AI for interacting with the UI.
    /// </summary>
    public static class ToolSchema
    {
        /// <summary>
        /// Gets all available tools for AI test execution.
        /// </summary>
        public static List<ToolDefinition> GetAllTools()
        {
            return new List<ToolDefinition>
            {
                CreateClickTool(),
                CreateDoubleClickTool(),
                CreateHoldTool(),
                CreateTypeTool(),
                CreateDragTool(),
                CreateScrollTool(),
                CreateSwipeTool(),
                CreateTwoFingerSwipeTool(),
                CreatePinchTool(),
                CreateRotateTool(),
                CreateSetSliderTool(),
                CreateSetScrollbarTool(),
                CreateKeyPressTool(),
                CreateKeyHoldTool(),
                CreateWaitTool(),
                CreatePassTool(),
                CreateFailTool()
            };
        }

        private static ToolDefinition CreateClickTool()
        {
            return new ToolDefinition
            {
                Name = "click",
                Description = "Click on a UI element by ID, or click at screen coordinates if you see something clickable that isn't in the element list",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["element_id"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Element ID from the list (e.g., 'e1', 'e2'). Preferred when element is in the list."
                        },
                        ["x"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Screen X coordinate (0.0-1.0 normalized). Use when target not in element list."
                        },
                        ["y"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Screen Y coordinate (0.0-1.0 normalized). Use when target not in element list."
                        }
                    }
                    // Note: Either element_id OR (x,y) required
                }
            };
        }

        private static ToolDefinition CreateDoubleClickTool()
        {
            return new ToolDefinition
            {
                Name = "double_click",
                Description = "Double-click on a UI element. Use for elements that require double-click activation.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["element_id"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Element ID from the list (e.g., 'e1', 'e2')"
                        },
                        ["x"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Screen X coordinate (0.0-1.0 normalized)"
                        },
                        ["y"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Screen Y coordinate (0.0-1.0 normalized)"
                        }
                    }
                }
            };
        }

        private static ToolDefinition CreateHoldTool()
        {
            return new ToolDefinition
            {
                Name = "hold",
                Description = "Long press/hold on a UI element. Use for context menus or hold-activated actions.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["element_id"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Element ID to hold"
                        },
                        ["duration"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "How long to hold in seconds (default: 1.0)"
                        }
                    },
                    Required = new List<string> { "element_id" }
                }
            };
        }

        private static ToolDefinition CreateTypeTool()
        {
            return new ToolDefinition
            {
                Name = "type",
                Description = "Type text into an input field. The field should be clicked/focused first.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["element_id"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "The ID of the input field to type into"
                        },
                        ["text"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "The text to type"
                        },
                        ["clear_first"] = new ToolProperty
                        {
                            Type = "boolean",
                            Description = "Whether to clear existing text before typing (default: true)"
                        },
                        ["press_enter"] = new ToolProperty
                        {
                            Type = "boolean",
                            Description = "Whether to press Enter after typing (default: false)"
                        }
                    },
                    Required = new List<string> { "element_id", "text" }
                }
            };
        }

        private static ToolDefinition CreateDragTool()
        {
            return new ToolDefinition
            {
                Name = "drag",
                Description = "Drag from one element/position to another, or drag in a direction",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["from_element_id"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Element ID to drag from"
                        },
                        ["to_element_id"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Element ID to drag to (optional)"
                        },
                        ["direction"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Direction to drag if no target element",
                            Enum = new List<string> { "up", "down", "left", "right" }
                        },
                        ["distance"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Distance to drag in pixels (default: 200)"
                        },
                        ["duration"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Duration of drag in seconds (default: 0.3)"
                        }
                    },
                    Required = new List<string> { "from_element_id" }
                }
            };
        }

        private static ToolDefinition CreateScrollTool()
        {
            return new ToolDefinition
            {
                Name = "scroll",
                Description = "Scroll a scrollable area up or down",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["element_id"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "The scrollable element ID (ScrollRect, ScrollView)"
                        },
                        ["direction"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Direction to scroll",
                            Enum = new List<string> { "up", "down", "left", "right" }
                        },
                        ["amount"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Scroll amount (0.0-1.0, default: 0.3)"
                        }
                    },
                    Required = new List<string> { "element_id", "direction" }
                }
            };
        }

        private static ToolDefinition CreateSwipeTool()
        {
            return new ToolDefinition
            {
                Name = "swipe",
                Description = "Swipe gesture on an element (touch gesture). Use for mobile-style swipe interactions.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["element_id"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Element ID to swipe on"
                        },
                        ["direction"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Direction to swipe",
                            Enum = new List<string> { "up", "down", "left", "right" }
                        },
                        ["distance"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Swipe distance as fraction of screen (0.0-1.0, default: 0.2)"
                        },
                        ["duration"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Swipe duration in seconds (default: 0.3)"
                        }
                    },
                    Required = new List<string> { "element_id", "direction" }
                }
            };
        }

        private static ToolDefinition CreatePinchTool()
        {
            return new ToolDefinition
            {
                Name = "pinch",
                Description = "Pinch gesture for zoom in/out (touch gesture). Scale > 1 zooms in, < 1 zooms out.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["element_id"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Element ID to pinch on"
                        },
                        ["scale"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Scale factor: >1 = zoom in, <1 = zoom out (default: 1.5)"
                        },
                        ["duration"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Pinch duration in seconds (default: 0.5)"
                        }
                    },
                    Required = new List<string> { "element_id", "scale" }
                }
            };
        }

        private static ToolDefinition CreateTwoFingerSwipeTool()
        {
            return new ToolDefinition
            {
                Name = "two_finger_swipe",
                Description = "Two-finger swipe gesture (e.g., for map panning). Both fingers move in the same direction.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["element_id"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Element ID to swipe on"
                        },
                        ["direction"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Swipe direction: up, down, left, right"
                        },
                        ["distance"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Swipe distance (0-1 normalized, default: 0.2)"
                        },
                        ["duration"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Swipe duration in seconds (default: 0.3)"
                        },
                        ["finger_spacing"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Distance between fingers (0-1 normalized, default: 0.03)"
                        }
                    },
                    Required = new List<string> { "element_id", "direction" }
                }
            };
        }

        private static ToolDefinition CreateRotateTool()
        {
            return new ToolDefinition
            {
                Name = "rotate",
                Description = "Two-finger rotation gesture. Positive degrees = clockwise, negative = counter-clockwise.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["element_id"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Element ID to rotate on"
                        },
                        ["degrees"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Rotation angle in degrees (positive = clockwise, default: 90)"
                        },
                        ["duration"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Rotation duration in seconds (default: 0.5)"
                        },
                        ["finger_distance"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Distance of fingers from center (0-1 normalized, default: 0.05)"
                        }
                    },
                    Required = new List<string> { "element_id", "degrees" }
                }
            };
        }

        private static ToolDefinition CreateSetSliderTool()
        {
            return new ToolDefinition
            {
                Name = "set_slider",
                Description = "Set a slider to a specific value (0-1 normalized)",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["element_id"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "The slider element ID"
                        },
                        ["value"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Target value (0.0-1.0)"
                        }
                    },
                    Required = new List<string> { "element_id", "value" }
                }
            };
        }

        private static ToolDefinition CreateSetScrollbarTool()
        {
            return new ToolDefinition
            {
                Name = "set_scrollbar",
                Description = "Set a scrollbar to a specific position (0-1 normalized)",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["element_id"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "The scrollbar element ID"
                        },
                        ["value"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Target position (0.0-1.0)"
                        }
                    },
                    Required = new List<string> { "element_id", "value" }
                }
            };
        }

        private static ToolDefinition CreateKeyPressTool()
        {
            return new ToolDefinition
            {
                Name = "key_press",
                Description = "Press a keyboard key. Use for keyboard shortcuts or special keys like Enter, Escape, Tab.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["key"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Key to press (e.g., 'Enter', 'Escape', 'Tab', 'Space', 'A', 'F1')"
                        }
                    },
                    Required = new List<string> { "key" }
                }
            };
        }

        private static ToolDefinition CreateKeyHoldTool()
        {
            return new ToolDefinition
            {
                Name = "key_hold",
                Description = "Hold one or more keys for a duration. Use for key combinations like Ctrl+C.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["keys"] = new ToolProperty
                        {
                            Type = "array",
                            Description = "Array of keys to hold together (e.g., ['LeftControl', 'C'])",
                            Items = new ToolPropertyItems { Type = "string" }
                        },
                        ["duration"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "How long to hold in seconds (default: 0.5)"
                        }
                    },
                    Required = new List<string> { "keys" }
                }
            };
        }

        private static ToolDefinition CreateWaitTool()
        {
            return new ToolDefinition
            {
                Name = "wait",
                Description = "Wait for a specified duration. Use when expecting animations, loading, or transitions.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["seconds"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Seconds to wait (0.5-5.0)"
                        }
                    },
                    Required = new List<string> { "seconds" }
                }
            };
        }

        private static ToolDefinition CreatePassTool()
        {
            return new ToolDefinition
            {
                Name = "pass",
                Description = "Declare the test as PASSED when the pass condition is met. Only call this when you are confident the test goal has been achieved.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["reason"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Explanation of why the test passed"
                        }
                    }
                }
            };
        }

        private static ToolDefinition CreateFailTool()
        {
            return new ToolDefinition
            {
                Name = "fail",
                Description = "Declare the test as FAILED when the fail condition is met, or when the test cannot proceed. Use this when you're certain the test goal cannot be achieved.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["reason"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Explanation of why the test failed"
                        }
                    },
                    Required = new List<string> { "reason" }
                }
            };
        }
    }
}
