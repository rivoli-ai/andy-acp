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
        /// Load an existing session to resume a previous conversation. Before returning,
        /// the implementation should replay the conversation history (user messages, agent
        /// messages, tool calls, plans, mode/config state) through <paramref name="streamer"/>
        /// as session/update notifications, as required by ACP session/load.
        /// Implementations should return null (or throw) if the session doesn't exist.
        /// </summary>
        /// <param name="parameters">Load parameters (sessionId, cwd, mcpServers).</param>
        /// <param name="streamer">Streamer used to replay session history to the client.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Metadata about the loaded session, or null if not found</returns>
        Task<SessionMetadata?> LoadSessionAsync(
            LoadSessionParams parameters,
            IResponseStreamer streamer,
            CancellationToken cancellationToken);

        /// <summary>
        /// Cancel any ongoing operations in the specified session.
        /// This should gracefully stop LLM generation and tool execution.
        /// </summary>
        /// <param name="sessionId">The session ID to cancel</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task CancelSessionAsync(string sessionId, CancellationToken cancellationToken);

        /// <summary>
        /// Set the operating mode for a session by ACP mode id (e.g., "code", "chat").
        /// Optional - implementations can return false if not supported.
        /// </summary>
        /// <param name="sessionId">The session ID</param>
        /// <param name="modeId">The ACP mode id to set</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if the mode was set successfully</returns>
        Task<bool> SetSessionModeAsync(
            string sessionId,
            string modeId,
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
        /// Whether the agent supports additional readable directories on session
        /// requests. Advertised as the <c>sessionCapabilities.additionalDirectories</c>
        /// marker.
        /// </summary>
        public bool AdditionalDirectories { get; set; } = false;

        /// <summary>
        /// Whether the agent accepts HTTP MCP server configurations. Advertised as
        /// <c>mcpCapabilities.http</c>; http configs are rejected with invalid-params
        /// when false.
        /// </summary>
        public bool McpHttp { get; set; } = false;

        /// <summary>
        /// Whether the agent accepts SSE MCP server configurations. Advertised as
        /// <c>mcpCapabilities.sse</c>; sse configs are rejected with invalid-params
        /// when false.
        /// </summary>
        public bool McpSse { get; set; } = false;

        /// <summary>
        /// Custom capabilities (extensibility)
        /// </summary>
        public Dictionary<string, object>? Extensions { get; set; }
    }
}
