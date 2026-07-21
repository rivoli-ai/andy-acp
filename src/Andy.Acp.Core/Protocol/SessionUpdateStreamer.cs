using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.Client;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Core.Protocol
{
    /// <summary>
    /// Implementation of <see cref="IResponseStreamer"/> that sends ACP v1
    /// <c>session/update</c> notifications as the agent generates a response.
    /// <para>
    /// Every notification has the shape
    /// <c>{ "jsonrpc":"2.0", "method":"session/update",
    ///      "params": { "sessionId": ..., "update": { "sessionUpdate": &lt;variant&gt;, ... } } }</c>
    /// where <c>update.sessionUpdate</c> selects a schema-defined variant.
    /// </para>
    /// <para>
    /// Write-failure behavior: cancellation (<see cref="OperationCanceledException"/>) always
    /// propagates so an in-flight prompt stops promptly. Other transport write failures are
    /// logged and rethrown, because a broken connection makes further streaming pointless and
    /// the agent should observe the failure rather than continue silently.
    /// </para>
    /// </summary>
    public class SessionUpdateStreamer : IResponseStreamer
    {
        private readonly ITransport? _transport;
        private readonly string _sessionId;
        private readonly ILogger? _logger;
        private readonly IAcpClient? _client;

        public SessionUpdateStreamer(
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
        {
            return SendUpdateAsync(new
            {
                sessionUpdate = "agent_message_chunk",
                content = new { type = "text", text }
            }, cancellationToken);
        }

        public Task SendThinkingAsync(string thinking, CancellationToken cancellationToken)
        {
            return SendUpdateAsync(new
            {
                sessionUpdate = "agent_thought_chunk",
                content = new { type = "text", text = thinking }
            }, cancellationToken);
        }

        public Task SendToolCallAsync(ToolCall toolCall, CancellationToken cancellationToken)
        {
            return SendUpdateAsync(new
            {
                sessionUpdate = "tool_call",
                toolCallId = toolCall.Id,
                title = string.IsNullOrEmpty(toolCall.Title) ? toolCall.Name : toolCall.Title,
                kind = string.IsNullOrEmpty(toolCall.Kind) ? "other" : toolCall.Kind,
                status = string.IsNullOrEmpty(toolCall.Status) ? "pending" : toolCall.Status,
                rawInput = toolCall.Input,
                locations = MapLocations(toolCall.Locations),
                content = MapToolContent(toolCall.ContentItems)
            }, cancellationToken);
        }

        public Task SendToolResultAsync(ToolResult result, CancellationToken cancellationToken)
        {
            // Structured content takes precedence; otherwise wrap the plain text.
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
        {
            var entries = plan.ResolveEntries().Select(e => new
            {
                content = e.Content,
                priority = string.IsNullOrEmpty(e.Priority) ? "medium" : e.Priority,
                status = string.IsNullOrEmpty(e.Status) ? "pending" : e.Status
            }).ToArray();

            return SendUpdateAsync(new
            {
                sessionUpdate = "plan",
                entries
            }, cancellationToken);
        }

        public Task SendAvailableCommandsAsync(System.Collections.Generic.IReadOnlyList<AvailableCommand> commands, CancellationToken cancellationToken)
        {
            return SendUpdateAsync(new
            {
                sessionUpdate = "available_commands_update",
                availableCommands = commands.Select(c => new
                {
                    name = c.Name,
                    description = c.Description,
                    input = c.InputHint == null ? null : new { hint = c.InputHint }
                }).ToArray()
            }, cancellationToken);
        }

        public Task SendCurrentModeAsync(string modeId, CancellationToken cancellationToken)
        {
            return SendUpdateAsync(new
            {
                sessionUpdate = "current_mode_update",
                currentModeId = modeId
            }, cancellationToken);
        }

        public Task SendConfigOptionsAsync(System.Collections.Generic.IReadOnlyList<SessionConfigOption> configOptions, CancellationToken cancellationToken)
        {
            return SendUpdateAsync(new
            {
                sessionUpdate = "config_option_update",
                configOptions
            }, cancellationToken);
        }

        public Task SendSessionInfoAsync(string? title, DateTimeOffset? updatedAt, CancellationToken cancellationToken)
        {
            return SendUpdateAsync(new
            {
                sessionUpdate = "session_info_update",
                title,
                updatedAt = updatedAt?.ToString("o")
            }, cancellationToken);
        }

        public Task SendUsageAsync(long used, long size, UsageCost? cost, CancellationToken cancellationToken)
        {
            return SendUpdateAsync(new
            {
                sessionUpdate = "usage_update",
                used,
                size,
                cost = cost == null ? null : new { amount = cost.Amount, currency = cost.Currency }
            }, cancellationToken);
        }

        /// <summary>Maps tool-call locations to the ACP wire shape.</summary>
        private static object? MapLocations(System.Collections.Generic.List<ToolCallLocation>? locations)
        {
            if (locations == null || locations.Count == 0)
                return null;

            return locations.Select(l => new { path = l.Path, line = l.Line }).ToArray();
        }

        /// <summary>
        /// Maps <see cref="ToolCallContent"/> items to the ACP <c>ToolCallContent</c>
        /// wire variants (content, diff, terminal).
        /// </summary>
        private static object? MapToolContent(System.Collections.Generic.List<ToolCallContent>? items)
        {
            if (items == null || items.Count == 0)
                return null;

            return items.Select<ToolCallContent, object>(item => item.Type switch
            {
                "diff" => new
                {
                    type = "diff",
                    path = item.Path ?? string.Empty,
                    oldText = item.OldText,
                    newText = item.NewText ?? string.Empty
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

        /// <summary>
        /// Wraps a session-update variant in the ACP notification envelope and writes it.
        /// The <paramref name="update"/> object must already include a valid
        /// <c>sessionUpdate</c> discriminator.
        /// </summary>
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
                @params = new
                {
                    sessionId = _sessionId,
                    update
                }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(notification, JsonRpcSerializer.Options);
            _logger?.LogTrace("Sending session/update: {Notification}", json);

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
