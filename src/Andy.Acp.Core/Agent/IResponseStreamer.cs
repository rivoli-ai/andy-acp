using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Client;

namespace Andy.Acp.Core.Agent
{
    /// <summary>
    /// Interface for streaming agent responses back to the client in real-time.
    /// This allows progressive updates as the agent generates responses and executes tools.
    /// It also exposes <see cref="Client"/>, the handle for agent → client requests
    /// (filesystem, terminal, permission) over the same connection.
    /// </summary>
    public interface IResponseStreamer
    {
        /// <summary>
        /// The handle for issuing ACP requests to the client (filesystem, terminal,
        /// permission). Throws if no client is available (e.g. in non-connected contexts).
        /// </summary>
        IAcpClient Client { get; }

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

        /// <summary>
        /// Send the current set of slash-commands available in the session
        /// (ACP <c>available_commands_update</c>).
        /// </summary>
        Task SendAvailableCommandsAsync(IReadOnlyList<AvailableCommand> commands, CancellationToken cancellationToken);

        /// <summary>
        /// Notify the client that the session's current mode changed
        /// (ACP <c>current_mode_update</c>).
        /// </summary>
        Task SendCurrentModeAsync(string modeId, CancellationToken cancellationToken);

        /// <summary>
        /// Notify the client that the session's config options changed
        /// (ACP <c>config_option_update</c>).
        /// </summary>
        Task SendConfigOptionsAsync(IReadOnlyList<SessionConfigOption> configOptions, CancellationToken cancellationToken);

        /// <summary>
        /// Send updated session info such as a generated title
        /// (ACP <c>session_info_update</c>).
        /// </summary>
        Task SendSessionInfoAsync(string? title, System.DateTimeOffset? updatedAt, CancellationToken cancellationToken);

        /// <summary>
        /// Send context-window usage (ACP <c>usage_update</c>): <paramref name="used"/>
        /// tokens consumed out of a window of <paramref name="size"/>.
        /// </summary>
        Task SendUsageAsync(long used, long size, UsageCost? cost, CancellationToken cancellationToken);
    }

    /// <summary>A slash-command the session currently offers (ACP <c>AvailableCommand</c>).</summary>
    public class AvailableCommand
    {
        /// <summary>Command name (e.g. "web", without the leading slash).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Human-readable description.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Optional hint describing the free-form input the command accepts.</summary>
        public string? InputHint { get; set; }
    }

    /// <summary>Monetary cost carried by a usage update (ACP <c>Cost</c>).</summary>
    public class UsageCost
    {
        public double Amount { get; set; }

        /// <summary>ISO 4217 currency code (e.g. "USD").</summary>
        public string Currency { get; set; } = "USD";
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

        /// <summary>
        /// File locations this tool call affects (ACP <c>locations</c>), enabling
        /// follow-along in the client.
        /// </summary>
        public List<ToolCallLocation>? Locations { get; set; }

        /// <summary>
        /// Structured content produced or previewed by the call (ACP <c>content</c>).
        /// </summary>
        public List<ToolCallContent>? ContentItems { get; set; }
    }

    /// <summary>A file location a tool call affects (ACP <c>ToolCallLocation</c>).</summary>
    public class ToolCallLocation
    {
        /// <summary>Absolute file path.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>Optional 1-based line number.</summary>
        public int? Line { get; set; }
    }

    /// <summary>
    /// Content attached to a tool call or tool result (ACP <c>ToolCallContent</c>).
    /// <see cref="Type"/> selects the variant:
    /// <list type="bullet">
    /// <item><c>content</c>: <see cref="Text"/> (or a full <see cref="Content"/> block)</item>
    /// <item><c>diff</c>: <see cref="Path"/>, <see cref="NewText"/>, optional <see cref="OldText"/></item>
    /// <item><c>terminal</c>: <see cref="TerminalId"/></item>
    /// </list>
    /// </summary>
    public class ToolCallContent
    {
        /// <summary>Variant discriminator: content, diff, or terminal.</summary>
        public string Type { get; set; } = "content";

        /// <summary>Text convenience for the content variant.</summary>
        public string? Text { get; set; }

        /// <summary>Full content block for the content variant (overrides <see cref="Text"/>).</summary>
        public ContentBlock? Content { get; set; }

        /// <summary>Absolute file path (diff variant).</summary>
        public string? Path { get; set; }

        /// <summary>Original file text (diff variant; null for a new file).</summary>
        public string? OldText { get; set; }

        /// <summary>Replacement file text (diff variant).</summary>
        public string? NewText { get; set; }

        /// <summary>Terminal id (terminal variant), from a client-created terminal.</summary>
        public string? TerminalId { get; set; }
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

        /// <summary>
        /// Structured content for the update (ACP <c>content</c>). When set, takes
        /// precedence over the plain <see cref="Content"/> text.
        /// </summary>
        public List<ToolCallContent>? ContentItems { get; set; }

        /// <summary>Raw tool output (ACP <c>rawOutput</c>).</summary>
        public object? RawOutput { get; set; }

        /// <summary>Updated file locations for the call (ACP <c>locations</c>).</summary>
        public List<ToolCallLocation>? Locations { get; set; }
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
