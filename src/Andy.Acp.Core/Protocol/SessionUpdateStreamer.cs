using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
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

        public SessionUpdateStreamer(
            ITransport? transport,
            string sessionId,
            ILogger? logger = null)
        {
            _transport = transport;
            _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            _logger = logger;
        }

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
                rawInput = toolCall.Input
            }, cancellationToken);
        }

        public Task SendToolResultAsync(ToolResult result, CancellationToken cancellationToken)
        {
            object? content = result.Content == null
                ? null
                : new object[]
                {
                    new { type = "content", content = new { type = "text", text = result.Content } }
                };

            return SendUpdateAsync(new
            {
                sessionUpdate = "tool_call_update",
                toolCallId = result.CallId,
                status = result.IsError ? "failed" : "completed",
                content
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
