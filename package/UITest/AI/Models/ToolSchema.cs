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
                CreateTypeTool(),
                CreateDragTool(),
                CreateScrollTool(),
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

        /// <summary>
        /// Converts tool definitions to OpenAI-compatible function format.
        /// </summary>
        public static object ToOpenAIFormat(List<ToolDefinition> tools)
        {
            var result = new List<object>();

            foreach (var tool in tools)
            {
                var properties = new Dictionary<string, object>();
                var required = tool.Parameters?.Required ?? new List<string>();

                if (tool.Parameters?.Properties != null)
                {
                    foreach (var prop in tool.Parameters.Properties)
                    {
                        var propDef = new Dictionary<string, object>
                        {
                            ["type"] = prop.Value.Type,
                            ["description"] = prop.Value.Description
                        };

                        if (prop.Value.Enum != null && prop.Value.Enum.Count > 0)
                        {
                            propDef["enum"] = prop.Value.Enum;
                        }

                        properties[prop.Key] = propDef;
                    }
                }

                result.Add(new
                {
                    type = "function",
                    function = new
                    {
                        name = tool.Name,
                        description = tool.Description,
                        parameters = new
                        {
                            type = "object",
                            properties = properties,
                            required = required
                        }
                    }
                });
            }

            return result;
        }
    }
}
