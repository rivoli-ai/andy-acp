using System.Collections.Generic;
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
    /// Information about a tool call. Maps to the ACP <c>tool_call</c> session update.
    /// </summary>
    public class ToolCall
    {
        /// <summary>
        /// Unique identifier for this tool call (ACP <c>toolCallId</c>).
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Name of the tool being called. Used as the <c>title</c> when <see cref="Title"/> is not set.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable title for the call. Falls back to <see cref="Name"/> when null.
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// ACP tool kind: read, edit, delete, move, search, execute, think, fetch,
        /// switch_mode, or other. Defaults to "other".
        /// </summary>
        public string Kind { get; set; } = "other";

        /// <summary>
        /// ACP tool call status: pending, in_progress, completed, or failed. Defaults to "pending".
        /// </summary>
        public string Status { get; set; } = "pending";

        /// <summary>
        /// Raw input parameters for the tool (ACP <c>rawInput</c>).
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
    /// An execution plan describing what the agent intends to do. Maps to the ACP
    /// <c>plan</c> session update, whose entries each carry a priority and status.
    /// </summary>
    public class ExecutionPlan
    {
        /// <summary>
        /// Description of the plan (informational; not part of the ACP plan wire shape).
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Optional simple step descriptions. When <see cref="Entries"/> is not set, each
        /// step is mapped to a plan entry with medium priority and pending status.
        /// </summary>
        public string[]? Steps { get; set; }

        /// <summary>
        /// Structured plan entries. Preferred over <see cref="Steps"/>.
        /// </summary>
        public List<PlanEntry>? Entries { get; set; }

        /// <summary>
        /// Returns the effective, schema-valid plan entries, deriving them from
        /// <see cref="Steps"/> when <see cref="Entries"/> is not provided.
        /// </summary>
        public List<PlanEntry> ResolveEntries()
        {
            if (Entries != null && Entries.Count > 0)
                return Entries;

            var derived = new List<PlanEntry>();
            if (Steps != null)
            {
                foreach (var step in Steps)
                    derived.Add(new PlanEntry { Content = step });
            }
            return derived;
        }
    }

    /// <summary>
    /// A single entry in an execution plan (ACP <c>PlanEntry</c>).
    /// </summary>
    public class PlanEntry
    {
        /// <summary>Human-readable description of the plan item.</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>Priority: high, medium, or low. Defaults to "medium".</summary>
        public string Priority { get; set; } = "medium";

        /// <summary>Status: pending, in_progress, or completed. Defaults to "pending".</summary>
        public string Status { get; set; } = "pending";
    }
}
