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
                CreateScreenshotTool(),
                CreatePassTool(),
                CreateFailTool()
            };
        }

        /// <summary>
        /// Creates the search query property schema used by most tools.
        /// </summary>
        private static ToolProperty CreateSearchProperty(string description = null)
        {
            return new ToolProperty
            {
                Type = "object",
                Description = description ?? "Search query to find the target element",
                Properties = new Dictionary<string, ToolProperty>
                {
                    ["base"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Base search method",
                        Enum = new List<string> { "text", "name", "type", "adjacent", "path", "any", "sprite", "tag" }
                    },
                    ["value"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Pattern to search for (text content, name, type name, etc.)"
                    },
                    ["direction"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Direction for adjacent searches",
                        Enum = new List<string> { "right", "left", "above", "below" }
                    },
                    ["chain"] = new ToolProperty
                    {
                        Type = "array",
                        Description = "Chain of filter methods to apply",
                        Items = new ToolProperty
                        {
                            Type = "object",
                            Properties = new Dictionary<string, ToolProperty>
                            {
                                ["method"] = new ToolProperty
                                {
                                    Type = "string",
                                    Description = "Filter method name",
                                    Enum = new List<string> { "near", "hasParent", "hasAncestor", "hasChild", "hasSibling", "includeInactive", "includeDisabled", "visible", "first", "last", "skip", "take", "inRegion" }
                                },
                                ["value"] = new ToolProperty
                                {
                                    Type = "string",
                                    Description = "Value for the filter (e.g., parent name for hasParent)"
                                },
                                ["direction"] = new ToolProperty
                                {
                                    Type = "string",
                                    Description = "Direction for near filter",
                                    Enum = new List<string> { "right", "left", "above", "below" }
                                },
                                ["count"] = new ToolProperty
                                {
                                    Type = "integer",
                                    Description = "Count for skip/take filters"
                                }
                            }
                        }
                    }
                },
                Required = new List<string> { "base", "value" }
            };
        }

        private static ToolDefinition CreateClickTool()
        {
            return new ToolDefinition
            {
                Name = "click",
                Description = "Click on a UI element. Use search to find element, or x/y for coordinates.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["search"] = CreateSearchProperty("Search query to find the element to click"),
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
                        ["search"] = CreateSearchProperty("Search query to find the element to double-click"),
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
                        ["search"] = CreateSearchProperty("Search query to find the element to hold"),
                        ["duration"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "How long to hold in seconds (default: 1.0)"
                        }
                    },
                    Required = new List<string> { "search" }
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
                        ["search"] = CreateSearchProperty("Search query to find the input field"),
                        ["text"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Text to type into the field"
                        },
                        ["clear_first"] = new ToolProperty
                        {
                            Type = "boolean",
                            Description = "Clear existing text before typing (default: true)"
                        },
                        ["press_enter"] = new ToolProperty
                        {
                            Type = "boolean",
                            Description = "Press Enter after typing (default: false)"
                        }
                    },
                    Required = new List<string> { "search", "text" }
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
                        ["from"] = CreateSearchProperty("Search query to find the element to drag from"),
                        ["to"] = CreateSearchProperty("Search query to find the element to drag to (optional)"),
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
                    Required = new List<string> { "from" }
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
                        ["search"] = CreateSearchProperty("Search query to find the scrollable element"),
                        ["direction"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Direction to scroll",
                            Enum = new List<string> { "up", "down", "left", "right" }
                        },
                        ["amount"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Amount to scroll (0.0-1.0 normalized, default: 0.3)"
                        }
                    },
                    Required = new List<string> { "search", "direction" }
                }
            };
        }

        private static ToolDefinition CreateSwipeTool()
        {
            return new ToolDefinition
            {
                Name = "swipe",
                Description = "Swipe gesture on an element or screen",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["search"] = CreateSearchProperty("Search query to find the element to swipe on"),
                        ["direction"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Direction to swipe",
                            Enum = new List<string> { "up", "down", "left", "right" }
                        },
                        ["distance"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Distance to swipe (0.0-1.0 normalized, default: 0.2)"
                        },
                        ["duration"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Duration of swipe in seconds (default: 0.3)"
                        }
                    },
                    Required = new List<string> { "search", "direction" }
                }
            };
        }

        private static ToolDefinition CreateTwoFingerSwipeTool()
        {
            return new ToolDefinition
            {
                Name = "two_finger_swipe",
                Description = "Two-finger swipe gesture (e.g., for map panning)",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["search"] = CreateSearchProperty("Search query to find the element"),
                        ["direction"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Direction to swipe",
                            Enum = new List<string> { "up", "down", "left", "right" }
                        },
                        ["distance"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Distance to swipe (0.0-1.0 normalized, default: 0.2)"
                        },
                        ["duration"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Duration of swipe in seconds (default: 0.3)"
                        },
                        ["finger_spacing"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Distance between fingers (0.0-1.0 normalized, default: 0.03)"
                        }
                    },
                    Required = new List<string> { "search", "direction" }
                }
            };
        }

        private static ToolDefinition CreatePinchTool()
        {
            return new ToolDefinition
            {
                Name = "pinch",
                Description = "Pinch gesture to zoom in or out",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["search"] = CreateSearchProperty("Search query to find the element"),
                        ["scale"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Scale factor (>1.0 = zoom in, <1.0 = zoom out)"
                        },
                        ["duration"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Duration of pinch in seconds (default: 0.5)"
                        }
                    },
                    Required = new List<string> { "search", "scale" }
                }
            };
        }

        private static ToolDefinition CreateRotateTool()
        {
            return new ToolDefinition
            {
                Name = "rotate",
                Description = "Two-finger rotation gesture",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["search"] = CreateSearchProperty("Search query to find the element"),
                        ["degrees"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Rotation in degrees (positive = clockwise, negative = counter-clockwise)"
                        },
                        ["duration"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Duration of rotation in seconds (default: 0.5)"
                        },
                        ["finger_distance"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Distance of fingers from center (0.0-1.0 normalized, default: 0.05)"
                        }
                    },
                    Required = new List<string> { "search", "degrees" }
                }
            };
        }

        private static ToolDefinition CreateSetSliderTool()
        {
            return new ToolDefinition
            {
                Name = "set_slider",
                Description = "Set a slider to a specific value",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["search"] = CreateSearchProperty("Search query to find the slider"),
                        ["value"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Target value (0.0-1.0 normalized)"
                        }
                    },
                    Required = new List<string> { "search", "value" }
                }
            };
        }

        private static ToolDefinition CreateSetScrollbarTool()
        {
            return new ToolDefinition
            {
                Name = "set_scrollbar",
                Description = "Set a scrollbar to a specific position",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["search"] = CreateSearchProperty("Search query to find the scrollbar"),
                        ["value"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Target position (0.0-1.0 normalized)"
                        }
                    },
                    Required = new List<string> { "search", "value" }
                }
            };
        }

        private static ToolDefinition CreateKeyPressTool()
        {
            return new ToolDefinition
            {
                Name = "key_press",
                Description = "Press a keyboard key",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["key"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Key to press (e.g., 'Enter', 'Escape', 'Tab', 'Space', 'A', 'Backspace')"
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
                Description = "Hold one or more keys for a duration (for key combinations or movement controls)",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["keys"] = new ToolProperty
                        {
                            Type = "array",
                            Description = "Keys to hold simultaneously",
                            Items = new ToolProperty { Type = "string" }
                        },
                        ["duration"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Duration to hold in seconds (default: 0.5)"
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
                Description = "Wait for a specified duration (use for animations/loading)",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["seconds"] = new ToolProperty
                        {
                            Type = "number",
                            Description = "Duration to wait in seconds (0.5-5.0)"
                        }
                    },
                    Required = new List<string> { "seconds" }
                }
            };
        }

        private static ToolDefinition CreateScreenshotTool()
        {
            return new ToolDefinition
            {
                Name = "screenshot",
                Description = "Request a screenshot for visual context. Use when element list seems incomplete or you need to verify visual state.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["reason"] = new ToolProperty
                        {
                            Type = "string",
                            Description = "Why you need the screenshot (helps with debugging)"
                        }
                    }
                }
            };
        }

        private static ToolDefinition CreatePassTool()
        {
            return new ToolDefinition
            {
                Name = "pass",
                Description = "Declare the test as PASSED. Only use when confident the test goal has been achieved.",
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
                    },
                    Required = new List<string> { "reason" }
                }
            };
        }

        private static ToolDefinition CreateFailTool()
        {
            return new ToolDefinition
            {
                Name = "fail",
                Description = "Declare the test as FAILED. Use when the test goal cannot be achieved.",
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
