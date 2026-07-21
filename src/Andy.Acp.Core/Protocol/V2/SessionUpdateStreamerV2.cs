using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.Client;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Core.Protocol.V2
{
    /// <summary>
    /// v2 implementation of <see cref="IResponseStreamer"/>: emits ACP v2 (alpha)
    /// <c>session/update</c> variants. Differences from v1 handled here:
    /// message chunks carry a required <c>messageId</c>; tool calls are upserts via
    /// <c>tool_call_update</c> (no separate creation variant); plans are
    /// <c>plan_update</c> with a <c>planId</c>; v1 diffs are mapped to the reworked v2
    /// diff shape; turn state is reported via <c>state_update</c>.
    /// </summary>
    public class SessionUpdateStreamerV2 : IResponseStreamer
    {
        private readonly ITransport? _transport;
        private readonly string _sessionId;
        private readonly ILogger? _logger;
        private readonly IAcpClient? _client;

        // v2 requires a messageId on every content chunk. One id per role per turn keeps
        // consecutive chunks appending to the same message.
        private readonly string _messageId = Guid.NewGuid().ToString("N");
        private readonly string _thoughtMessageId = Guid.NewGuid().ToString("N");
        private readonly string _planId = Guid.NewGuid().ToString("N");

        public SessionUpdateStreamerV2(
            ITransport? transport,
            string sessionId,
            ILogger? logger = null,
            IAcpClient? client = null)
        {
            _transport = transport;
            _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            _logger = logger;
            _client = client;
        }

        /// <inheritdoc />
        public IAcpClient Client =>
            _client ?? throw new InvalidOperationException(
                "No ACP client is available in this context (agent-to-client requests require a live connection).");

        public Task SendMessageChunkAsync(string text, CancellationToken cancellationToken)
            => SendUpdateAsync(new
            {
                sessionUpdate = "agent_message_chunk",
                messageId = _messageId,
                content = new { type = "text", text }
            }, cancellationToken);

        public Task SendThinkingAsync(string thinking, CancellationToken cancellationToken)
            => SendUpdateAsync(new
            {
                sessionUpdate = "agent_thought_chunk",
                messageId = _thoughtMessageId,
                content = new { type = "text", text = thinking }
            }, cancellationToken);

        public Task SendToolCallAsync(ToolCall toolCall, CancellationToken cancellationToken)
            // v2 has no separate creation variant: the first upsert creates the call.
            => SendUpdateAsync(new
            {
                sessionUpdate = "tool_call_update",
                toolCallId = toolCall.Id,
                title = string.IsNullOrEmpty(toolCall.Title) ? toolCall.Name : toolCall.Title,
                kind = string.IsNullOrEmpty(toolCall.Kind) ? "other" : toolCall.Kind,
                status = string.IsNullOrEmpty(toolCall.Status) ? "pending" : toolCall.Status,
                rawInput = toolCall.Input,
                locations = MapLocations(toolCall.Locations),
                content = MapToolContent(toolCall.ContentItems)
            }, cancellationToken);

        public Task SendToolResultAsync(ToolResult result, CancellationToken cancellationToken)
        {
            object? content = MapToolContent(result.ContentItems);
            if (content == null && result.Content != null)
            {
                content = new object[]
                {
                    new { type = "content", content = new { type = "text", text = result.Content } }
                };
            }

            return SendUpdateAsync(new
            {
                sessionUpdate = "tool_call_update",
                toolCallId = result.CallId,
                status = result.IsError ? "failed" : "completed",
                content,
                rawOutput = result.RawOutput,
                locations = MapLocations(result.Locations)
            }, cancellationToken);
        }

        public Task SendExecutionPlanAsync(ExecutionPlan plan, CancellationToken cancellationToken)
            => SendUpdateAsync(new
            {
                sessionUpdate = "plan_update",
                plan = new
                {
                    type = "items",
                    planId = _planId,
                    entries = plan.ResolveEntries().Select(e => new
                    {
                        content = e.Content,
                        priority = string.IsNullOrEmpty(e.Priority) ? "medium" : e.Priority,
                        status = string.IsNullOrEmpty(e.Status) ? "pending" : e.Status
                    }).ToArray()
                }
            }, cancellationToken);

        public Task SendAvailableCommandsAsync(IReadOnlyList<AvailableCommand> commands, CancellationToken cancellationToken)
            => SendUpdateAsync(new
            {
                sessionUpdate = "available_commands_update",
                availableCommands = commands.Select(c => new
                {
                    name = c.Name,
                    description = c.Description,
                    input = c.InputHint == null ? null : new { hint = c.InputHint }
                }).ToArray()
            }, cancellationToken);

        public Task SendCurrentModeAsync(string modeId, CancellationToken cancellationToken)
            // v2 removed current_mode_update: modes are config options (category "mode").
            => throw new NotSupportedException(
                "ACP v2 has no current_mode_update; report mode changes via SendConfigOptionsAsync " +
                "with a config option of category \"mode\".");

        public Task SendConfigOptionsAsync(IReadOnlyList<SessionConfigOption> configOptions, CancellationToken cancellationToken)
            => SendUpdateAsync(new
            {
                sessionUpdate = "config_option_update",
                configOptions = V2Wire.ConfigOptions(configOptions)
            }, cancellationToken);

        public Task SendSessionInfoAsync(string? title, DateTimeOffset? updatedAt, CancellationToken cancellationToken)
            => SendUpdateAsync(new
            {
                sessionUpdate = "session_info_update",
                title,
                updatedAt = updatedAt?.ToString("o")
            }, cancellationToken);

        public Task SendUsageAsync(long used, long size, UsageCost? cost, CancellationToken cancellationToken)
            => SendUpdateAsync(new
            {
                sessionUpdate = "usage_update",
                used,
                size,
                cost = cost == null ? null : new { amount = cost.Amount, currency = cost.Currency }
            }, cancellationToken);

        /// <summary>Emits <c>state_update</c> with <c>state: "running"</c>.</summary>
        public Task SendStateRunningAsync(CancellationToken cancellationToken)
            => SendUpdateAsync(new { sessionUpdate = "state_update", state = "running" }, cancellationToken);

        /// <summary>
        /// Emits <c>state_update</c> with <c>state: "idle"</c> carrying the stop reason —
        /// in v2 this replaces the v1 PromptResponse stopReason.
        /// </summary>
        public Task SendStateIdleAsync(string stopReason, CancellationToken cancellationToken)
            => SendUpdateAsync(new { sessionUpdate = "state_update", state = "idle", stopReason }, cancellationToken);

        private static object? MapLocations(List<ToolCallLocation>? locations)
        {
            if (locations == null || locations.Count == 0)
                return null;
            return locations.Select(l => new { path = l.Path, line = l.Line }).ToArray();
        }

        /// <summary>
        /// Maps provider tool content to v2 <c>ToolCallContent</c>. The v1-style
        /// path/oldText/newText diff maps onto the v2 shape as a single change
        /// (<c>add</c> when there was no old text, else <c>modify</c>); no patch text is
        /// synthesized.
        /// </summary>
        private static object? MapToolContent(List<ToolCallContent>? items)
        {
            if (items == null || items.Count == 0)
                return null;

            return items.Select<ToolCallContent, object>(item => item.Type switch
            {
                "diff" => new
                {
                    type = "diff",
                    changes = new object[]
                    {
                        new
                        {
                            operation = item.OldText == null ? "add" : "modify",
                            path = item.Path ?? string.Empty
                        }
                    }
                },
                "terminal" => new
                {
                    type = "terminal",
                    terminalId = item.TerminalId ?? string.Empty
                },
                _ => new
                {
                    type = "content",
                    content = (object?)item.Content ?? new { type = "text", text = item.Text ?? string.Empty }
                }
            }).ToArray();
        }

        private async Task SendUpdateAsync(object update, CancellationToken cancellationToken)
        {
            if (_transport == null)
            {
                _logger?.LogWarning("Transport not set; dropping session/update notification");
                return;
            }

            var notification = new
            {
                jsonrpc = "2.0",
                method = "session/update",
                @params = new { sessionId = _sessionId, update }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(notification, JsonRpcSerializer.Options);
            _logger?.LogTrace("Sending v2 session/update: {Notification}", json);

            try
            {
                await _transport.WriteMessageAsync(json, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to write session/update notification");
                throw;
            }
        }
    }
}
