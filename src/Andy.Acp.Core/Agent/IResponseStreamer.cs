using System.Threading;
using System.Threading.Tasks;

namespace Andy.Acp.Core.Agent
{
    /// <summary>
    /// Interface for streaming agent responses back to the client in real-time.
    /// This allows progressive updates as the agent generates responses and executes tools.
    /// </summary>
    public interface IResponseStreamer
    {
        /// <summary>
        /// Send a chunk of the agent's message text.
        /// Called multiple times as the LLM generates tokens.
        /// </summary>
        /// <param name="text">The text chunk to send</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SendMessageChunkAsync(string text, CancellationToken cancellationToken);

        /// <summary>
        /// Send notification that the agent is about to call a tool.
        /// </summary>
        /// <param name="toolCall">Information about the tool being called</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SendToolCallAsync(ToolCall toolCall, CancellationToken cancellationToken);

        /// <summary>
        /// Send the result of a tool execution.
        /// </summary>
        /// <param name="result">The tool execution result</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SendToolResultAsync(ToolResult result, CancellationToken cancellationToken);

        /// <summary>
        /// Send a thinking/reasoning update (for agents that expose reasoning).
        /// </summary>
        /// <param name="thinking">The thinking text</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SendThinkingAsync(string thinking, CancellationToken cancellationToken);

        /// <summary>
        /// Send notification about an execution plan (for agents that plan ahead).
        /// </summary>
        /// <param name="plan">The execution plan</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SendExecutionPlanAsync(ExecutionPlan plan, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Information about a tool call
    /// </summary>
    public class ToolCall
    {
        /// <summary>
        /// Unique identifier for this tool call
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Name of the tool being called
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Input parameters for the tool
        /// </summary>
        public object? Input { get; set; }
    }

    /// <summary>
    /// Result of a tool execution
    /// </summary>
    public class ToolResult
    {
        /// <summary>
        /// Tool call ID this result corresponds to
        /// </summary>
        public string CallId { get; set; } = string.Empty;

        /// <summary>
        /// Whether the tool executed successfully
        /// </summary>
        public bool IsError { get; set; }

        /// <summary>
        /// The tool's output or error message
        /// </summary>
        public string? Content { get; set; }
    }

    /// <summary>
    /// An execution plan describing what the agent intends to do
    /// </summary>
    public class ExecutionPlan
    {
        /// <summary>
        /// Description of the plan
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Steps in the plan
        /// </summary>
        public string[]? Steps { get; set; }
    }
}
