using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// Manages conversation history with AI models.
    /// </summary>
    public class ConversationManager
    {
        private readonly List<Message> messages = new List<Message>();

        /// <summary>
        /// Gets the current message history.
        /// </summary>
        public IReadOnlyList<Message> Messages => messages;

        /// <summary>
        /// Gets the message count.
        /// </summary>
        public int MessageCount => messages.Count;

        /// <summary>
        /// Creates a new conversation manager.
        /// </summary>
        public ConversationManager() { }

        /// <summary>
        /// Constructor for backwards compatibility.
        /// </summary>
        public ConversationManager(int maxContextTokens, float compactionThreshold) { }

        /// <summary>
        /// Sets or updates the system message (always first in conversation).
        /// </summary>
        public void SetSystemMessage(string content)
        {
            // Remove existing system message if any
            messages.RemoveAll(m => m.Role == "system");

            // Insert at beginning
            messages.Insert(0, new Message
            {
                Role = "system",
                Content = content
            });
        }

        /// <summary>
        /// Adds a user message with optional screenshot.
        /// </summary>
        public void AddUserMessage(string content, byte[] screenshot = null)
        {
            messages.Add(new Message
            {
                Role = "user",
                Content = content,
                Screenshot = screenshot
            });
        }

        /// <summary>
        /// Adds an assistant response message.
        /// </summary>
        public void AddAssistantMessage(string content, List<ToolCall> toolCalls = null)
        {
            messages.Add(new Message
            {
                Role = "assistant",
                Content = content,
                ToolCalls = toolCalls
            });
        }

        /// <summary>
        /// Adds a tool result message.
        /// </summary>
        public void AddToolResult(string toolCallId, string result)
        {
            messages.Add(new Message
            {
                Role = "tool",
                Content = result,
                ToolCallId = toolCallId
            });
        }

        /// <summary>
        /// Builds a ModelRequest from the current conversation state.
        /// </summary>
        public ModelRequest BuildRequest(byte[] currentScreenshot = null, string elementList = null)
        {
            var request = new ModelRequest
            {
                Messages = messages.ToList(),
                Tools = ToolSchema.GetAllTools()
            };

            if (currentScreenshot != null)
            {
                request.ScreenshotPng = currentScreenshot;
            }

            if (!string.IsNullOrEmpty(elementList))
            {
                request.ElementListJson = elementList;
            }

            return request;
        }

        /// <summary>
        /// Resets the conversation, keeping only the system message.
        /// </summary>
        public void Reset()
        {
            var systemMsg = messages.FirstOrDefault(m => m.Role == "system");
            messages.Clear();

            if (systemMsg != null)
            {
                messages.Add(systemMsg);
            }
        }

        /// <summary>
        /// Gets a summary of actions taken in the conversation.
        /// </summary>
        public string GetActionSummary()
        {
            var actions = new List<string>();

            foreach (var msg in messages.Where(m => m.Role == "assistant"))
            {
                if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                {
                    foreach (var tc in msg.ToolCalls)
                    {
                        var target = tc.GetString("element_id");
                        if (string.IsNullOrEmpty(target))
                        {
                            var x = tc.GetFloat("x", -1f);
                            var y = tc.GetFloat("y", -1f);
                            if (x >= 0 && y >= 0)
                            {
                                target = $"({x:F2},{y:F2})";
                            }
                        }

                        if (!string.IsNullOrEmpty(target))
                        {
                            actions.Add($"{tc.Name}({target})");
                        }
                        else
                        {
                            actions.Add(tc.Name);
                        }
                    }
                }
            }

            if (actions.Count == 0)
            {
                return "No actions taken yet";
            }

            return string.Join(" → ", actions);
        }

        /// <summary>
        /// Gets conversation statistics for debugging.
        /// </summary>
        public ConversationStats GetStats()
        {
            return new ConversationStats
            {
                MessageCount = messages.Count,
                UserMessageCount = messages.Count(m => m.Role == "user"),
                AssistantMessageCount = messages.Count(m => m.Role == "assistant"),
                ToolCallCount = messages.Sum(m => m.ToolCalls?.Count ?? 0)
            };
        }

        /// <summary>
        /// Placeholder event for backwards compatibility.
        /// </summary>
        public event Action<string> OnCompacted;
    }

    /// <summary>
    /// Statistics about the current conversation state.
    /// </summary>
    public class ConversationStats
    {
        public int MessageCount { get; set; }
        public int UserMessageCount { get; set; }
        public int AssistantMessageCount { get; set; }
        public int ToolCallCount { get; set; }
    }
}
