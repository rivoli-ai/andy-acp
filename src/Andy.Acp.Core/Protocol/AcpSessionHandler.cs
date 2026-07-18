using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Transport;
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
        private ITransport? _transport;

        // Tracks the in-flight prompt operation per session so that session/cancel
        // can interrupt exactly the prompt it targets. At most one prompt per session
        // is allowed to run concurrently.
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _activePrompts = new();

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
        /// Sets the transport for sending notifications
        /// </summary>
        public void SetTransport(ITransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
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

                if (promptParams.PromptBlocks == null || promptParams.PromptBlocks.Count == 0)
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "Prompt content is required");
                }

                // Validate content types against the agent's negotiated capabilities.
                // text and resource_link are baseline-supported; image/audio/embedded
                // resource are gated on capabilities. Non-text prompts are permitted.
                ValidateContentBlocks(promptParams.PromptBlocks);

                // Convert content blocks to PromptMessage without losing non-text content.
                var promptMessage = promptParams.ToPromptMessage();

                _logger?.LogInformation("Processing prompt for session {SessionId}: {BlockCount} block(s)",
                    promptParams.SessionId, promptParams.PromptBlocks.Count);

                // Link a per-prompt cancellation source to the connection token so that a
                // session/cancel targeting this session cancels exactly this prompt.
                var promptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (!_activePrompts.TryAdd(promptParams.SessionId, promptCts))
                {
                    promptCts.Dispose();
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidRequest,
                        $"A prompt is already in progress for session {promptParams.SessionId}");
                }

                try
                {
                    // Create a response streamer that sends session/update notifications
                    var streamer = new SessionUpdateStreamer(_transport, promptParams.SessionId, _logger);

                    // Process the prompt through the agent using the linked token.
                    var response = await _agentProvider.ProcessPromptAsync(
                        promptParams.SessionId,
                        promptMessage,
                        streamer,
                        promptCts.Token);

                    _logger?.LogInformation("Prompt processing completed for session {SessionId}, stop reason: {StopReason}",
                        promptParams.SessionId, response.StopReason);

                    // Return only stopReason. The message content was already streamed via
                    // session/update notifications, which are written before this response.
                    return new
                    {
                        stopReason = MapStopReason(response.StopReason)
                    };
                }
                catch (OperationCanceledException)
                {
                    _logger?.LogInformation("Prompt processing was cancelled for session {SessionId}", promptParams.SessionId);
                    return new
                    {
                        stopReason = "cancelled"
                    };
                }
                finally
                {
                    _activePrompts.TryRemove(promptParams.SessionId, out _);
                    promptCts.Dispose();
                }
            }
            catch (JsonRpcProtocolException)
            {
                throw;
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
            _logger?.LogDebug("Handling session/cancel notification");

            try
            {
                var cancelParams = DeserializeParams<CancelRequest>(parameters);

                if (string.IsNullOrEmpty(cancelParams?.SessionId))
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "Session ID is required");
                }

                // Cancel the in-flight prompt for this session, if any. Duplicate or late
                // cancellations are harmless: there is simply no active prompt to cancel.
                if (_activePrompts.TryGetValue(cancelParams.SessionId, out var promptCts))
                {
                    _logger?.LogInformation("Cancelling in-flight prompt for session {SessionId}", cancelParams.SessionId);
                    try { promptCts.Cancel(); } catch (ObjectDisposedException) { }
                }

                // Also notify the agent so it can stop any session-scoped background work.
                await _agentProvider.CancelSessionAsync(cancelParams.SessionId, cancellationToken);

                // session/cancel is a notification; the server sends no response for it.
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling session/cancel");
                throw;
            }
        }

        private T? DeserializeParams<T>(object? parameters) where T : class
        {
            if (parameters == null)
                return null;

            // Use the shared JSON-RPC options (camelCase, case-insensitive) so ACP wire
            // field names bind correctly across every parameter type.
            if (parameters is JsonElement jsonElement)
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText(), JsonRpcSerializer.Options);
            }

            var json = JsonSerializer.Serialize(parameters, JsonRpcSerializer.Options);
            return JsonSerializer.Deserialize<T>(json, JsonRpcSerializer.Options);
        }

        /// <summary>
        /// Maps our StopReason enum to ACP protocol values
        /// </summary>
        private static string MapStopReason(StopReason stopReason)
        {
            return stopReason switch
            {
                StopReason.Completed => "end_turn",
                StopReason.Cancelled => "cancelled",
                StopReason.TokenLimit => "max_tokens",
                StopReason.Error => "refusal",
                StopReason.TimeLimit => "max_tokens",
                _ => "end_turn"
            };
        }

        // Request parameter classes
        private class LoadSessionRequest
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; } = string.Empty;
        }

        /// <summary>
        /// Validates every prompt content block against the agent's negotiated
        /// capabilities. text and resource_link are always accepted; image, audio, and
        /// embedded resource content require the corresponding capability. Unsupported or
        /// unknown content produces an InvalidParams error.
        /// </summary>
        private void ValidateContentBlocks(List<ContentBlock> blocks)
        {
            var caps = _agentProvider.GetCapabilities();

            foreach (var block in blocks)
            {
                switch (block.Type)
                {
                    case "text":
                    case "resource_link":
                        // Baseline-supported regardless of capabilities.
                        break;
                    case "image":
                        if (!caps.ImagePrompts)
                            throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams,
                                "Image content is not supported by this agent");
                        break;
                    case "audio":
                        if (!caps.AudioPrompts)
                            throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams,
                                "Audio content is not supported by this agent");
                        break;
                    case "resource":
                        if (!caps.EmbeddedContext)
                            throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams,
                                "Embedded resource content is not supported by this agent");
                        break;
                    default:
                        throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams,
                            $"Unsupported content block type: {block.Type}");
                }
            }
        }

        private class PromptRequest
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; } = string.Empty;

            [JsonPropertyName("prompt")]
            public List<ContentBlock>? PromptBlocks { get; set; }

            /// <summary>
            /// Converts the request to a <see cref="PromptMessage"/> without discarding
            /// non-text content: all blocks are preserved in order, and the text blocks
            /// are additionally joined into <see cref="PromptMessage.Text"/>.
            /// </summary>
            public PromptMessage ToPromptMessage()
            {
                var message = new PromptMessage();

                if (PromptBlocks == null || PromptBlocks.Count == 0)
                    return message;

                message.Blocks = PromptBlocks;

                var textParts = PromptBlocks
                    .Where(b => b.Type == "text" && !string.IsNullOrEmpty(b.Text))
                    .Select(b => b.Text!);
                message.Text = string.Join("\n", textParts);

                return message;
            }
        }

        private class SetModeRequest
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; } = string.Empty;

            [JsonPropertyName("mode")]
            public string Mode { get; set; } = string.Empty;
        }

        private class SetModelRequest
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; } = string.Empty;

            [JsonPropertyName("model")]
            public string Model { get; set; } = string.Empty;
        }

        private class CancelRequest
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; } = string.Empty;
        }
    }
}
