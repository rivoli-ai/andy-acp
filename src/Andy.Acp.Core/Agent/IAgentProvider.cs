using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Andy.Acp.Core.Agent
{
    /// <summary>
    /// Core interface that agent implementations must provide to handle conversation and reasoning.
    /// This is the main interface that connects an LLM-based agent to the ACP protocol.
    /// </summary>
    public interface IAgentProvider
    {
        /// <summary>
        /// Process a user prompt and generate an agent response.
        /// This is the core method where the agent's LLM processes the user's message
        /// and generates a response, potentially using tools.
        /// </summary>
        /// <param name="sessionId">The session ID for context tracking</param>
        /// <param name="prompt">The user's prompt message</param>
        /// <param name="streamer">Callback interface to stream updates back to the client</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The final agent response after processing completes</returns>
        Task<AgentResponse> ProcessPromptAsync(
            string sessionId,
            PromptMessage prompt,
            IResponseStreamer streamer,
            CancellationToken cancellationToken);

        /// <summary>
        /// Create a new conversation session with optional initial context.
        /// </summary>
        /// <param name="parameters">Session creation parameters (optional)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Metadata about the created session</returns>
        Task<SessionMetadata> CreateSessionAsync(
            NewSessionParams? parameters,
            CancellationToken cancellationToken);

        /// <summary>
        /// Load an existing session to resume a previous conversation.
        /// Implementations should return null or throw if session doesn't exist.
        /// </summary>
        /// <param name="sessionId">The session ID to load</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Metadata about the loaded session, or null if not found</returns>
        Task<SessionMetadata?> LoadSessionAsync(
            string sessionId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Cancel any ongoing operations in the specified session.
        /// This should gracefully stop LLM generation and tool execution.
        /// </summary>
        /// <param name="sessionId">The session ID to cancel</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task CancelSessionAsync(string sessionId, CancellationToken cancellationToken);

        /// <summary>
        /// Set the operating mode for a session (e.g., "code", "chat", "architect").
        /// Optional - implementations can return false if not supported.
        /// </summary>
        /// <param name="sessionId">The session ID</param>
        /// <param name="mode">The mode to set</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if mode was set successfully</returns>
        Task<bool> SetSessionModeAsync(
            string sessionId,
            string mode,
            CancellationToken cancellationToken);

        /// <summary>
        /// Set the model variant for a session.
        /// Optional - implementations can return false if not supported.
        /// </summary>
        /// <param name="sessionId">The session ID</param>
        /// <param name="model">The model identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if model was set successfully</returns>
        Task<bool> SetSessionModelAsync(
            string sessionId,
            string model,
            CancellationToken cancellationToken);

        /// <summary>
        /// Get the capabilities supported by this agent.
        /// </summary>
        AgentCapabilities GetCapabilities();
    }

    /// <summary>
    /// Capabilities that an agent supports
    /// </summary>
    public class AgentCapabilities
    {
        /// <summary>
        /// Whether the agent supports loading existing sessions
        /// </summary>
        public bool LoadSession { get; set; } = false;

        /// <summary>
        /// Whether the agent supports audio in prompts
        /// </summary>
        public bool AudioPrompts { get; set; } = false;

        /// <summary>
        /// Whether the agent supports images in prompts
        /// </summary>
        public bool ImagePrompts { get; set; } = false;

        /// <summary>
        /// Whether the agent supports embedded context in prompts
        /// </summary>
        public bool EmbeddedContext { get; set; } = false;

        /// <summary>
        /// Custom capabilities (extensibility)
        /// </summary>
        public Dictionary<string, object>? Extensions { get; set; }
    }
}
