using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.JsonRpc;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Core.Protocol
{
    /// <summary>
    /// Handles ACP session-related protocol methods (session/new, session/prompt, etc.)
    /// </summary>
    public class AcpSessionHandler
    {
        private readonly IAgentProvider _agentProvider;
        private readonly ILogger<AcpSessionHandler>? _logger;
        private readonly JsonRpcHandler _jsonRpcHandler;

        public AcpSessionHandler(
            IAgentProvider agentProvider,
            JsonRpcHandler jsonRpcHandler,
            ILogger<AcpSessionHandler>? logger = null)
        {
            _agentProvider = agentProvider ?? throw new ArgumentNullException(nameof(agentProvider));
            _jsonRpcHandler = jsonRpcHandler ?? throw new ArgumentNullException(nameof(jsonRpcHandler));
            _logger = logger;
        }

        /// <summary>
        /// Register all session/* methods with the JSON-RPC handler
        /// </summary>
        public void RegisterMethods()
        {
            _jsonRpcHandler.RegisterMethod("session/new", HandleNewSessionAsync);
            _jsonRpcHandler.RegisterMethod("session/load", HandleLoadSessionAsync);
            _jsonRpcHandler.RegisterMethod("session/prompt", HandlePromptAsync);
            _jsonRpcHandler.RegisterMethod("session/set_mode", HandleSetModeAsync);
            _jsonRpcHandler.RegisterMethod("session/set_model", HandleSetModelAsync);
            _jsonRpcHandler.RegisterMethod("session/cancel", HandleCancelAsync);

            _logger?.LogInformation("Registered ACP session methods: session/new, session/load, session/prompt, session/set_mode, session/set_model, session/cancel");
        }

        private async Task<object?> HandleNewSessionAsync(object? parameters, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Handling session/new request");

            try
            {
                var sessionParams = DeserializeParams<NewSessionParams>(parameters);
                var metadata = await _agentProvider.CreateSessionAsync(sessionParams, cancellationToken);

                _logger?.LogInformation("Created new session: {SessionId}", metadata.SessionId);

                return new
                {
                    sessionId = metadata.SessionId,
                    createdAt = metadata.CreatedAt,
                    mode = metadata.Mode,
                    model = metadata.Model,
                    metadata = metadata.Metadata
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error creating new session");
                throw;
            }
        }

        private async Task<object?> HandleLoadSessionAsync(object? parameters, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Handling session/load request");

            try
            {
                var loadParams = DeserializeParams<LoadSessionRequest>(parameters);

                if (string.IsNullOrEmpty(loadParams?.SessionId))
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "Session ID is required");
                }

                var metadata = await _agentProvider.LoadSessionAsync(loadParams.SessionId, cancellationToken);

                if (metadata == null)
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        $"Session not found: {loadParams.SessionId}");
                }

                _logger?.LogInformation("Loaded session: {SessionId}", metadata.SessionId);

                return new
                {
                    sessionId = metadata.SessionId,
                    createdAt = metadata.CreatedAt,
                    lastAccessedAt = metadata.LastAccessedAt,
                    messageCount = metadata.MessageCount,
                    mode = metadata.Mode,
                    model = metadata.Model,
                    metadata = metadata.Metadata
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading session");
                throw;
            }
        }

        private async Task<object?> HandlePromptAsync(object? parameters, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Handling session/prompt request");

            try
            {
                var promptParams = DeserializeParams<PromptRequest>(parameters);

                if (string.IsNullOrEmpty(promptParams?.SessionId))
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "Session ID is required");
                }

                if (promptParams.Prompt == null || string.IsNullOrEmpty(promptParams.Prompt.Text))
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "Prompt text is required");
                }

                _logger?.LogInformation("Processing prompt for session {SessionId}: {PromptPreview}",
                    promptParams.SessionId,
                    promptParams.Prompt.Text.Substring(0, Math.Min(50, promptParams.Prompt.Text.Length)));

                // Create a response streamer that sends session/update notifications
                var streamer = new SessionUpdateStreamer(_jsonRpcHandler, promptParams.SessionId, _logger);

                // Process the prompt through the agent
                var response = await _agentProvider.ProcessPromptAsync(
                    promptParams.SessionId,
                    promptParams.Prompt,
                    streamer,
                    cancellationToken);

                _logger?.LogInformation("Prompt processing completed for session {SessionId}, stop reason: {StopReason}",
                    promptParams.SessionId, response.StopReason);

                // Return the final response
                return new
                {
                    message = response.Message,
                    stopReason = response.StopReason.ToString().ToLowerInvariant(),
                    toolCalls = response.ToolCalls,
                    error = response.Error,
                    usage = response.Usage != null ? new
                    {
                        inputTokens = response.Usage.InputTokens,
                        outputTokens = response.Usage.OutputTokens,
                        totalTokens = response.Usage.TotalTokens
                    } : null
                };
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("Prompt processing was cancelled");
                return new
                {
                    message = "",
                    stopReason = "cancelled"
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing prompt");
                throw;
            }
        }

        private async Task<object?> HandleSetModeAsync(object? parameters, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Handling session/set_mode request");

            try
            {
                var setModeParams = DeserializeParams<SetModeRequest>(parameters);

                if (string.IsNullOrEmpty(setModeParams?.SessionId))
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "Session ID is required");
                }

                if (string.IsNullOrEmpty(setModeParams.Mode))
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "Mode is required");
                }

                var success = await _agentProvider.SetSessionModeAsync(
                    setModeParams.SessionId,
                    setModeParams.Mode,
                    cancellationToken);

                _logger?.LogInformation("Set mode for session {SessionId} to {Mode}: {Success}",
                    setModeParams.SessionId, setModeParams.Mode, success);

                return new { success };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error setting session mode");
                throw;
            }
        }

        private async Task<object?> HandleSetModelAsync(object? parameters, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Handling session/set_model request");

            try
            {
                var setModelParams = DeserializeParams<SetModelRequest>(parameters);

                if (string.IsNullOrEmpty(setModelParams?.SessionId))
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "Session ID is required");
                }

                if (string.IsNullOrEmpty(setModelParams.Model))
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "Model is required");
                }

                var success = await _agentProvider.SetSessionModelAsync(
                    setModelParams.SessionId,
                    setModelParams.Model,
                    cancellationToken);

                _logger?.LogInformation("Set model for session {SessionId} to {Model}: {Success}",
                    setModelParams.SessionId, setModelParams.Model, success);

                return new { success };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error setting session model");
                throw;
            }
        }

        private async Task<object?> HandleCancelAsync(object? parameters, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Handling session/cancel request");

            try
            {
                var cancelParams = DeserializeParams<CancelRequest>(parameters);

                if (string.IsNullOrEmpty(cancelParams?.SessionId))
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "Session ID is required");
                }

                await _agentProvider.CancelSessionAsync(cancelParams.SessionId, cancellationToken);

                _logger?.LogInformation("Cancelled session {SessionId}", cancelParams.SessionId);

                return new { success = true };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error cancelling session");
                throw;
            }
        }

        private T? DeserializeParams<T>(object? parameters) where T : class
        {
            if (parameters == null)
                return null;

            if (parameters is JsonElement jsonElement)
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
            }

            var json = JsonSerializer.Serialize(parameters);
            return JsonSerializer.Deserialize<T>(json);
        }

        // Request parameter classes
        private class LoadSessionRequest
        {
            public string SessionId { get; set; } = string.Empty;
        }

        private class PromptRequest
        {
            public string SessionId { get; set; } = string.Empty;
            public PromptMessage Prompt { get; set; } = new();
        }

        private class SetModeRequest
        {
            public string SessionId { get; set; } = string.Empty;
            public string Mode { get; set; } = string.Empty;
        }

        private class SetModelRequest
        {
            public string SessionId { get; set; } = string.Empty;
            public string Model { get; set; } = string.Empty;
        }

        private class CancelRequest
        {
            public string SessionId { get; set; } = string.Empty;
        }
    }
}
