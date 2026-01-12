using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// Manages conversation history with AI models, handling context limits via smart compaction.
    /// </summary>
    public class ConversationManager
    {
        private readonly List<Message> messages = new List<Message>();
        private readonly int maxContextTokens;
        private readonly float compactionThreshold;

        private int estimatedTokens;
        private bool wasCompacted;

        /// <summary>
        /// Gets the current message history.
        /// </summary>
        public IReadOnlyList<Message> Messages => messages;

        /// <summary>
        /// Gets the estimated token count for the conversation.
        /// </summary>
        public int EstimatedTokens => estimatedTokens;

        /// <summary>
        /// Gets whether the conversation was recently compacted.
        /// </summary>
        public bool WasCompacted => wasCompacted;

        /// <summary>
        /// Event fired when conversation is compacted.
        /// </summary>
        public event Action<string> OnCompacted;

        /// <summary>
        /// Creates a new conversation manager.
        /// </summary>
        /// <param name="maxContextTokens">Maximum tokens before compaction (default 8000)</param>
        /// <param name="compactionThreshold">Threshold (0-1) at which to trigger compaction (default 0.8)</param>
        public ConversationManager(int maxContextTokens = 8000, float compactionThreshold = 0.8f)
        {
            this.maxContextTokens = maxContextTokens;
            this.compactionThreshold = compactionThreshold;
        }

        /// <summary>
        /// Sets or updates the system message (always first in conversation).
        /// </summary>
        public void SetSystemMessage(string content)
        {
            // Remove existing system message if any
            messages.RemoveAll(m => m.Role == "system" && !m.Content.StartsWith("[Previous"));

            // Insert at beginning
            messages.Insert(0, new Message
            {
                Role = "system",
                Content = content
            });

            UpdateTokenEstimate();
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

            CheckAndCompact();
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

            CheckAndCompact();
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

            CheckAndCompact();
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

            // Add current screenshot if provided
            if (currentScreenshot != null)
            {
                request.ScreenshotPng = currentScreenshot;
            }

            // Add element list if provided
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
            var systemMsg = messages.FirstOrDefault(m => m.Role == "system" && !m.Content.StartsWith("[Previous"));
            messages.Clear();

            if (systemMsg != null)
            {
                messages.Add(systemMsg);
            }

            wasCompacted = false;
            UpdateTokenEstimate();
        }

        /// <summary>
        /// Gets a summary of actions taken in the conversation.
        /// </summary>
        public string GetActionSummary()
        {
            return SummarizeActions(messages.Where(m => m.Role != "system").ToList());
        }

        private void CheckAndCompact()
        {
            UpdateTokenEstimate();

            if (estimatedTokens > maxContextTokens * compactionThreshold)
            {
                CompactHistory();
            }
        }

        private void CompactHistory()
        {
            // Find system message (always keep)
            var systemMsg = messages.FirstOrDefault(m => m.Role == "system" && !m.Content.StartsWith("[Previous"));

            // Keep the last 3 exchanges (6 messages: user + assistant pairs)
            var nonSystemMessages = messages.Where(m => m.Role != "system" || m.Content.StartsWith("[Previous")).ToList();
            var recentMessages = nonSystemMessages.TakeLast(6).ToList();
            var olderMessages = nonSystemMessages.Take(nonSystemMessages.Count - 6).ToList();

            if (olderMessages.Count == 0)
            {
                // Not enough to compact
                return;
            }

            // Create summary of older conversation
            var summary = SummarizeActions(olderMessages);

            // Clear and rebuild
            messages.Clear();

            if (systemMsg != null)
            {
                messages.Add(systemMsg);
            }

            // Add summary as a system note
            messages.Add(new Message
            {
                Role = "system",
                Content = $"[Previous actions summary: {summary}]"
            });

            // Add recent messages
            messages.AddRange(recentMessages);

            wasCompacted = true;
            UpdateTokenEstimate();

            OnCompacted?.Invoke(summary);
        }

        private string SummarizeActions(List<Message> messagesToSummarize)
        {
            var actions = new List<string>();

            foreach (var msg in messagesToSummarize.Where(m => m.Role == "assistant"))
            {
                // Extract tool calls from message
                if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                {
                    foreach (var tc in msg.ToolCalls)
                    {
                        var target = tc.GetString("element_id");
                        if (string.IsNullOrEmpty(target))
                        {
                            // Check for coordinate-based actions
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

        private void UpdateTokenEstimate()
        {
            // Rough estimation: ~4 characters per token for text
            // Screenshots add significant tokens (assume ~1000 tokens per image for vision models)
            int tokens = 0;

            foreach (var msg in messages)
            {
                // Text content
                if (!string.IsNullOrEmpty(msg.Content))
                {
                    tokens += msg.Content.Length / 4;
                }

                // Screenshot (if present)
                if (msg.Screenshot != null && msg.Screenshot.Length > 0)
                {
                    tokens += 1000; // Rough estimate for image tokens
                }

                // Tool calls
                if (msg.ToolCalls != null)
                {
                    foreach (var tc in msg.ToolCalls)
                    {
                        tokens += 20; // Base cost for tool call structure
                        if (tc.Arguments != null)
                        {
                            tokens += tc.Arguments.Count * 10;
                        }
                    }
                }
            }

            estimatedTokens = tokens;
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
                ToolCallCount = messages.Sum(m => m.ToolCalls?.Count ?? 0),
                EstimatedTokens = estimatedTokens,
                MaxTokens = maxContextTokens,
                UtilizationPercent = (float)estimatedTokens / maxContextTokens * 100f,
                WasCompacted = wasCompacted
            };
        }
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
        public int EstimatedTokens { get; set; }
        public int MaxTokens { get; set; }
        public float UtilizationPercent { get; set; }
        public bool WasCompacted { get; set; }
    }
}
