using System;
using System.Collections.Generic;

namespace Andy.Acp.Core.Agent
{
    /// <summary>
    /// A message from the user to the agent. The canonical representation is
    /// <see cref="Blocks"/>, which preserves every ACP content block (text, image,
    /// audio, resource, resource_link) in order. <see cref="Text"/> is a convenience
    /// join of the text blocks and may be empty for a valid non-text prompt.
    /// </summary>
    public class PromptMessage
    {
        /// <summary>
        /// The ordered ACP content blocks that make up this prompt. This is the
        /// lossless representation: image/audio/resource/resource_link payloads are
        /// preserved here even when they have no text.
        /// </summary>
        public List<ContentBlock> Blocks { get; set; } = new();

        /// <summary>
        /// Convenience concatenation of the text blocks (newline-joined). May be empty
        /// when the prompt contains only non-text content.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Additional metadata
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }
    }

    /// <summary>
    /// An ACP content block. A single flexible shape covers every variant; the
    /// populated fields depend on <see cref="Type"/>:
    /// <list type="bullet">
    /// <item><c>text</c>: <see cref="Text"/></item>
    /// <item><c>image</c>/<c>audio</c>: <see cref="Data"/> (base64) + <see cref="MimeType"/></item>
    /// <item><c>resource_link</c>: <see cref="Uri"/> + <see cref="Name"/> (+ optional metadata)</item>
    /// <item><c>resource</c>: <see cref="Resource"/> (embedded text or blob)</item>
    /// </list>
    /// </summary>
    public class ContentBlock
    {
        /// <summary>Discriminator: text, image, audio, resource_link, or resource.</summary>
        public string Type { get; set; } = "text";

        /// <summary>Text payload (type = text).</summary>
        public string? Text { get; set; }

        /// <summary>Base64-encoded payload (type = image or audio).</summary>
        public string? Data { get; set; }

        /// <summary>MIME type (image/audio/resource_link).</summary>
        public string? MimeType { get; set; }

        /// <summary>URI (resource_link, or optional source uri for image).</summary>
        public string? Uri { get; set; }

        /// <summary>Human-readable name (resource_link).</summary>
        public string? Name { get; set; }

        /// <summary>Optional description (resource_link).</summary>
        public string? Description { get; set; }

        /// <summary>Optional title (resource_link).</summary>
        public string? Title { get; set; }

        /// <summary>Optional size in bytes (resource_link).</summary>
        public long? Size { get; set; }

        /// <summary>Embedded resource contents (type = resource).</summary>
        public EmbeddedResource? Resource { get; set; }

        /// <summary>Optional ACP annotations, preserved as raw JSON.</summary>
        public object? Annotations { get; set; }
    }

    /// <summary>
    /// Embedded resource contents for a <c>resource</c> content block: either text
    /// (<see cref="Text"/>) or binary (<see cref="Blob"/>, base64).
    /// </summary>
    public class EmbeddedResource
    {
        /// <summary>Resource URI.</summary>
        public string? Uri { get; set; }

        /// <summary>MIME type of the resource.</summary>
        public string? MimeType { get; set; }

        /// <summary>Text contents (TextResourceContents).</summary>
        public string? Text { get; set; }

        /// <summary>Base64 binary contents (BlobResourceContents).</summary>
        public string? Blob { get; set; }
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
