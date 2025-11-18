using System;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.JsonRpc;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Core.Protocol
{
    /// <summary>
    /// Implementation of IResponseStreamer that sends session/update notifications
    /// to the client as the agent generates responses.
    /// </summary>
    public class SessionUpdateStreamer : IResponseStreamer
    {
        private readonly JsonRpcHandler _jsonRpcHandler;
        private readonly string _sessionId;
        private readonly ILogger? _logger;

        public SessionUpdateStreamer(
            JsonRpcHandler jsonRpcHandler,
            string sessionId,
            ILogger? logger = null)
        {
            _jsonRpcHandler = jsonRpcHandler ?? throw new ArgumentNullException(nameof(jsonRpcHandler));
            _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            _logger = logger;
        }

        public async Task SendMessageChunkAsync(string text, CancellationToken cancellationToken)
        {
            try
            {
                await SendNotificationAsync(new
                {
                    sessionId = _sessionId,
                    type = "message_chunk",
                    data = new { text }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to send message chunk notification");
            }
        }

        public async Task SendToolCallAsync(ToolCall toolCall, CancellationToken cancellationToken)
        {
            try
            {
                await SendNotificationAsync(new
                {
                    sessionId = _sessionId,
                    type = "tool_call",
                    data = new
                    {
                        id = toolCall.Id,
                        name = toolCall.Name,
                        input = toolCall.Input
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to send tool call notification");
            }
        }

        public async Task SendToolResultAsync(ToolResult result, CancellationToken cancellationToken)
        {
            try
            {
                await SendNotificationAsync(new
                {
                    sessionId = _sessionId,
                    type = "tool_result",
                    data = new
                    {
                        callId = result.CallId,
                        isError = result.IsError,
                        content = result.Content
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to send tool result notification");
            }
        }

        public async Task SendThinkingAsync(string thinking, CancellationToken cancellationToken)
        {
            try
            {
                await SendNotificationAsync(new
                {
                    sessionId = _sessionId,
                    type = "thinking",
                    data = new { text = thinking }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to send thinking notification");
            }
        }

        public async Task SendExecutionPlanAsync(ExecutionPlan plan, CancellationToken cancellationToken)
        {
            try
            {
                await SendNotificationAsync(new
                {
                    sessionId = _sessionId,
                    type = "execution_plan",
                    data = new
                    {
                        description = plan.Description,
                        steps = plan.Steps
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to send execution plan notification");
            }
        }

        private Task SendNotificationAsync(object data, CancellationToken cancellationToken)
        {
            // TODO: Implement notification sending through transport
            // For now, we just log the notification
            // In a full implementation, this would create a JSON-RPC notification message
            // and send it through the transport layer

            _logger?.LogTrace("Session update notification for session {SessionId}: {Data}", _sessionId, data);

            return Task.CompletedTask;
        }
    }
}
