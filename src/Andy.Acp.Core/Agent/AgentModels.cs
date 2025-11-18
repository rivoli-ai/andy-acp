using System;
using System.Collections.Generic;

namespace Andy.Acp.Core.Agent
{
    /// <summary>
    /// A message from the user to the agent
    /// </summary>
    public class PromptMessage
    {
        /// <summary>
        /// The text content of the message
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Optional image attachments (URLs or base64)
        /// </summary>
        public List<ImageAttachment>? Images { get; set; }

        /// <summary>
        /// Optional audio attachments
        /// </summary>
        public List<AudioAttachment>? Audio { get; set; }

        /// <summary>
        /// Optional embedded context (file contents, etc.)
        /// </summary>
        public List<ContextItem>? Context { get; set; }

        /// <summary>
        /// Additional metadata
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }
    }

    /// <summary>
    /// Image attachment in a prompt
    /// </summary>
    public class ImageAttachment
    {
        /// <summary>
        /// URL or base64-encoded image data
        /// </summary>
        public string Data { get; set; } = string.Empty;

        /// <summary>
        /// MIME type (e.g., "image/png")
        /// </summary>
        public string? MimeType { get; set; }
    }

    /// <summary>
    /// Audio attachment in a prompt
    /// </summary>
    public class AudioAttachment
    {
        /// <summary>
        /// URL or base64-encoded audio data
        /// </summary>
        public string Data { get; set; } = string.Empty;

        /// <summary>
        /// MIME type (e.g., "audio/wav")
        /// </summary>
        public string? MimeType { get; set; }
    }

    /// <summary>
    /// Context item embedded in a prompt
    /// </summary>
    public class ContextItem
    {
        /// <summary>
        /// Type of context (e.g., "file", "selection")
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// The context content
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Optional metadata (e.g., file path, line numbers)
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }
    }

    /// <summary>
    /// The agent's response to a prompt
    /// </summary>
    public class AgentResponse
    {
        /// <summary>
        /// The final message text from the agent
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Why the agent stopped (e.g., "completed", "cancelled", "error")
        /// </summary>
        public StopReason StopReason { get; set; } = StopReason.Completed;

        /// <summary>
        /// Tool calls that were made during processing
        /// </summary>
        public List<ToolCall>? ToolCalls { get; set; }

        /// <summary>
        /// Any error that occurred
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Usage statistics (tokens, etc.)
        /// </summary>
        public UsageStats? Usage { get; set; }
    }

    /// <summary>
    /// Reason why the agent stopped generating
    /// </summary>
    public enum StopReason
    {
        /// <summary>
        /// Agent completed the response normally
        /// </summary>
        Completed,

        /// <summary>
        /// User cancelled the operation
        /// </summary>
        Cancelled,

        /// <summary>
        /// An error occurred
        /// </summary>
        Error,

        /// <summary>
        /// Reached token limit
        /// </summary>
        TokenLimit,

        /// <summary>
        /// Reached time limit
        /// </summary>
        TimeLimit
    }

    /// <summary>
    /// Usage statistics for a response
    /// </summary>
    public class UsageStats
    {
        /// <summary>
        /// Input tokens consumed
        /// </summary>
        public int InputTokens { get; set; }

        /// <summary>
        /// Output tokens generated
        /// </summary>
        public int OutputTokens { get; set; }

        /// <summary>
        /// Total tokens
        /// </summary>
        public int TotalTokens => InputTokens + OutputTokens;
    }

    /// <summary>
    /// Parameters for creating a new session
    /// </summary>
    public class NewSessionParams
    {
        /// <summary>
        /// Optional session ID (if not provided, one will be generated)
        /// </summary>
        public string? SessionId { get; set; }

        /// <summary>
        /// Initial system prompt or instructions
        /// </summary>
        public string? SystemPrompt { get; set; }

        /// <summary>
        /// Initial mode (e.g., "code", "chat")
        /// </summary>
        public string? Mode { get; set; }

        /// <summary>
        /// Preferred model
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Additional metadata
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }
    }

    /// <summary>
    /// Metadata about a session
    /// </summary>
    public class SessionMetadata
    {
        /// <summary>
        /// The session ID
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// When the session was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// When the session was last accessed
        /// </summary>
        public DateTime LastAccessedAt { get; set; }

        /// <summary>
        /// Current mode
        /// </summary>
        public string? Mode { get; set; }

        /// <summary>
        /// Current model
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Number of messages in the conversation
        /// </summary>
        public int MessageCount { get; set; }

        /// <summary>
        /// Additional metadata
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
