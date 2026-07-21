using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
    /// Handles ACP session methods (session/new, session/load, session/prompt,
    /// session/set_mode, session/cancel). Session methods require a completed
    /// <c>initialize</c> handshake, tracked via <see cref="AcpConnectionState"/>.
    /// </summary>
    public class AcpSessionHandler
    {
        private readonly IAgentProvider _agentProvider;
        private readonly ILogger<AcpSessionHandler>? _logger;
        private readonly JsonRpcHandler _jsonRpcHandler;
        private readonly AcpConnectionState _state;
        private ITransport? _transport;
        private Andy.Acp.Core.Client.IAcpClient? _client;

        // Tracks the in-flight prompt operation per session so that session/cancel can
        // interrupt exactly the prompt it targets. At most one prompt per session runs at a time.
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _activePrompts = new();

        public AcpSessionHandler(
            IAgentProvider agentProvider,
            JsonRpcHandler jsonRpcHandler,
            AcpConnectionState state,
            ILogger<AcpSessionHandler>? logger = null)
        {
            _agentProvider = agentProvider ?? throw new ArgumentNullException(nameof(agentProvider));
            _jsonRpcHandler = jsonRpcHandler ?? throw new ArgumentNullException(nameof(jsonRpcHandler));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _logger = logger;
        }

        /// <summary>Sets the transport used to send session/update notifications.</summary>
        public void SetTransport(ITransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        /// <summary>Sets the client handle used for agent → client requests during prompts/loads.</summary>
        public void SetClient(Andy.Acp.Core.Client.IAcpClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>Registers all ACP session methods with the JSON-RPC handler.</summary>
        public void RegisterMethods()
        {
            _jsonRpcHandler.RegisterMethod("session/new", HandleNewSessionAsync);
            _jsonRpcHandler.RegisterMethod("session/load", HandleLoadSessionAsync);
            _jsonRpcHandler.RegisterMethod("session/prompt", HandlePromptAsync);
            _jsonRpcHandler.RegisterMethod("session/set_mode", HandleSetModeAsync);
            _jsonRpcHandler.RegisterMethod("session/cancel", HandleCancelAsync);

            // Optional ACP surface, registered only when the agent implements the
            // corresponding provider interface (capabilities are advertised to match).
            if (_agentProvider is ISessionConfigProvider)
                _jsonRpcHandler.RegisterMethod("session/set_config_option", HandleSetConfigOptionAsync);

            if (_agentProvider is ISessionCatalogProvider)
            {
                _jsonRpcHandler.RegisterMethod("session/list", HandleListSessionsAsync);
                _jsonRpcHandler.RegisterMethod("session/delete", HandleDeleteSessionAsync);
                _jsonRpcHandler.RegisterMethod("session/resume", HandleResumeSessionAsync);
                _jsonRpcHandler.RegisterMethod("session/close", HandleCloseSessionAsync);
            }

            _logger?.LogInformation("Registered ACP session methods (config: {Config}, catalog: {Catalog})",
                _agentProvider is ISessionConfigProvider, _agentProvider is ISessionCatalogProvider);
        }

        /// <summary>
        /// Throws if the connection has not completed the initialize handshake, or if the
        /// agent requires authentication that has not happened yet (ACP error -32000).
        /// </summary>
        private void EnsureInitialized()
        {
            if (!_state.Initialized)
            {
                throw new JsonRpcProtocolException(
                    JsonRpcErrorCodes.SessionNotInitialized,
                    "Connection is not initialized. Call initialize first.");
            }

            if (!_state.Authenticated)
            {
                throw new JsonRpcProtocolException(
                    JsonRpcErrorCodes.AuthRequired,
                    "Authentication required. Call authenticate first.");
            }
        }

        private async Task<object?> HandleNewSessionAsync(object? parameters, CancellationToken cancellationToken)
        {
            EnsureInitialized();

            var req = DeserializeParams<NewSessionRequest>(parameters);

            if (string.IsNullOrEmpty(req?.Cwd))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "cwd is required");
            if (!Path.IsPathRooted(req!.Cwd))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "cwd must be an absolute path");

            var sessionParams = new NewSessionParams
            {
                Cwd = req.Cwd,
                McpServers = req.McpServers ?? new List<McpServerConfig>(),
                AdditionalDirectories = req.AdditionalDirectories
            };

            var metadata = await _agentProvider.CreateSessionAsync(sessionParams, cancellationToken);
            _logger?.LogInformation("Created new session {SessionId}", metadata.SessionId);

            // ACP NewSessionResponse: sessionId (required) + optional modes/configOptions.
            return new
            {
                sessionId = metadata.SessionId,
                modes = metadata.Modes,
                configOptions = metadata.ConfigOptions
            };
        }

        private async Task<object?> HandleLoadSessionAsync(object? parameters, CancellationToken cancellationToken)
        {
            EnsureInitialized();

            if (!_agentProvider.GetCapabilities().LoadSession)
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidRequest,
                    "This agent does not support session/load");

            var req = DeserializeParams<LoadSessionRequest>(parameters);

            if (string.IsNullOrEmpty(req?.SessionId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "sessionId is required");
            if (string.IsNullOrEmpty(req!.Cwd))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "cwd is required");

            var loadParams = new LoadSessionParams
            {
                SessionId = req.SessionId,
                Cwd = req.Cwd,
                McpServers = req.McpServers ?? new List<McpServerConfig>(),
                AdditionalDirectories = req.AdditionalDirectories
            };

            // The provider replays conversation history through this streamer before returning.
            var streamer = new SessionUpdateStreamer(_transport, req.SessionId, _logger, _client);

            var metadata = await _agentProvider.LoadSessionAsync(loadParams, streamer, cancellationToken);
            if (metadata == null)
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams,
                    $"Session not found: {req.SessionId}");

            _logger?.LogInformation("Loaded session {SessionId}", req.SessionId);

            // ACP LoadSessionResponse: optional modes/configOptions, no sessionId.
            return new
            {
                modes = metadata.Modes,
                configOptions = metadata.ConfigOptions
            };
        }

        private async Task<object?> HandleSetConfigOptionAsync(object? parameters, CancellationToken cancellationToken)
        {
            EnsureInitialized();

            var configProvider = (ISessionConfigProvider)_agentProvider;
            var req = DeserializeParams<SetConfigOptionRequest>(parameters);

            if (string.IsNullOrEmpty(req?.SessionId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "sessionId is required");
            if (string.IsNullOrEmpty(req!.ConfigId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "configId is required");

            // The value variant is discriminated by JSON kind: boolean for the "boolean"
            // variant, string for the default "value_id" variant.
            var value = new SessionConfigValue();
            if (req.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                value.BoolValue = req.Value.GetBoolean();
            else if (req.Value.ValueKind is JsonValueKind.String)
                value.ValueId = req.Value.GetString();
            else
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams,
                    "value must be a string (value id) or a boolean");

            IReadOnlyList<SessionConfigOption> options;
            try
            {
                options = await configProvider.SetConfigOptionAsync(req.SessionId, req.ConfigId, value, cancellationToken);
            }
            catch (ArgumentException ex)
            {
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, ex.Message);
            }

            _logger?.LogInformation("Set config option {ConfigId} for session {SessionId}", req.ConfigId, req.SessionId);

            // ACP SetSessionConfigOptionResponse: the full updated option list (required).
            return new { configOptions = options };
        }

        private async Task<object?> HandleListSessionsAsync(object? parameters, CancellationToken cancellationToken)
        {
            EnsureInitialized();

            var catalog = (ISessionCatalogProvider)_agentProvider;
            var req = DeserializeParams<ListSessionsRequest>(parameters) ?? new ListSessionsRequest();

            var result = await catalog.ListSessionsAsync(req.Cwd, req.Cursor, cancellationToken);

            return new
            {
                sessions = result.Sessions.Select(s => new
                {
                    sessionId = s.SessionId,
                    cwd = s.Cwd,
                    additionalDirectories = s.AdditionalDirectories,
                    title = s.Title,
                    updatedAt = s.UpdatedAt?.ToString("o")
                }).ToArray(),
                nextCursor = result.NextCursor
            };
        }

        private async Task<object?> HandleDeleteSessionAsync(object? parameters, CancellationToken cancellationToken)
        {
            EnsureInitialized();

            var catalog = (ISessionCatalogProvider)_agentProvider;
            var req = DeserializeParams<SessionIdRequest>(parameters);

            if (string.IsNullOrEmpty(req?.SessionId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "sessionId is required");

            var deleted = await catalog.DeleteSessionAsync(req!.SessionId, cancellationToken);
            if (!deleted)
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.ResourceNotFound,
                    $"Session not found: {req.SessionId}");

            _logger?.LogInformation("Deleted session {SessionId}", req.SessionId);
            return new { };
        }

        private async Task<object?> HandleResumeSessionAsync(object? parameters, CancellationToken cancellationToken)
        {
            EnsureInitialized();

            var catalog = (ISessionCatalogProvider)_agentProvider;
            var req = DeserializeParams<LoadSessionRequest>(parameters);

            if (string.IsNullOrEmpty(req?.SessionId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "sessionId is required");
            if (string.IsNullOrEmpty(req!.Cwd))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "cwd is required");

            var resumeParams = new LoadSessionParams
            {
                SessionId = req.SessionId,
                Cwd = req.Cwd,
                McpServers = req.McpServers ?? new List<McpServerConfig>(),
                AdditionalDirectories = req.AdditionalDirectories
            };

            var metadata = await catalog.ResumeSessionAsync(resumeParams, cancellationToken);
            if (metadata == null)
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.ResourceNotFound,
                    $"Session not found: {req.SessionId}");

            _logger?.LogInformation("Resumed session {SessionId}", req.SessionId);

            // ACP ResumeSessionResponse: optional modes/configOptions (no history replay).
            return new
            {
                modes = metadata.Modes,
                configOptions = metadata.ConfigOptions
            };
        }

        private async Task<object?> HandleCloseSessionAsync(object? parameters, CancellationToken cancellationToken)
        {
            EnsureInitialized();

            var catalog = (ISessionCatalogProvider)_agentProvider;
            var req = DeserializeParams<SessionIdRequest>(parameters);

            if (string.IsNullOrEmpty(req?.SessionId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "sessionId is required");

            // Closing a session also cancels any prompt still in flight for it.
            if (_activePrompts.TryGetValue(req!.SessionId, out var promptCts))
            {
                try { promptCts.Cancel(); } catch (ObjectDisposedException) { }
            }

            await catalog.CloseSessionAsync(req.SessionId, cancellationToken);

            _logger?.LogInformation("Closed session {SessionId}", req.SessionId);
            return new { };
        }

        private async Task<object?> HandlePromptAsync(object? parameters, CancellationToken cancellationToken)
        {
            EnsureInitialized();

            try
            {
                var promptParams = DeserializeParams<PromptRequest>(parameters);

                if (string.IsNullOrEmpty(promptParams?.SessionId))
                    throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "Session ID is required");

                if (promptParams!.PromptBlocks == null || promptParams.PromptBlocks.Count == 0)
                    throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "Prompt content is required");

                // Validate content types against negotiated capabilities; non-text prompts allowed.
                ValidateContentBlocks(promptParams.PromptBlocks);

                var promptMessage = promptParams.ToPromptMessage();

                _logger?.LogInformation("Processing prompt for session {SessionId}: {BlockCount} block(s)",
                    promptParams.SessionId, promptParams.PromptBlocks.Count);

                // Link a per-prompt cancellation source so session/cancel cancels exactly this prompt.
                var promptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (!_activePrompts.TryAdd(promptParams.SessionId, promptCts))
                {
                    promptCts.Dispose();
                    throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidRequest,
                        $"A prompt is already in progress for session {promptParams.SessionId}");
                }

                try
                {
                    var streamer = new SessionUpdateStreamer(_transport, promptParams.SessionId, _logger, _client);

                    var response = await _agentProvider.ProcessPromptAsync(
                        promptParams.SessionId, promptMessage, streamer, promptCts.Token);

                    _logger?.LogInformation("Prompt completed for session {SessionId}, stopReason {StopReason}",
                        promptParams.SessionId, response.StopReason);

                    return new { stopReason = MapStopReason(response.StopReason) };
                }
                catch (OperationCanceledException)
                {
                    _logger?.LogInformation("Prompt cancelled for session {SessionId}", promptParams.SessionId);
                    return new { stopReason = "cancelled" };
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
            EnsureInitialized();

            var req = DeserializeParams<SetSessionModeRequest>(parameters);

            if (string.IsNullOrEmpty(req?.SessionId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "Session ID is required");
            if (string.IsNullOrEmpty(req!.ModeId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "modeId is required");

            var success = await _agentProvider.SetSessionModeAsync(req.SessionId, req.ModeId, cancellationToken);
            if (!success)
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams,
                    $"Unknown or unsupported mode: {req.ModeId}");

            _logger?.LogInformation("Set mode {ModeId} for session {SessionId}", req.ModeId, req.SessionId);

            // ACP SetSessionModeResponse is an empty object.
            return new { };
        }

        private async Task<object?> HandleCancelAsync(object? parameters, CancellationToken cancellationToken)
        {
            var req = DeserializeParams<CancelRequest>(parameters);

            if (string.IsNullOrEmpty(req?.SessionId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "Session ID is required");

            // Cancel the in-flight prompt for this session, if any. Duplicate/late cancels are harmless.
            if (_activePrompts.TryGetValue(req!.SessionId, out var promptCts))
            {
                _logger?.LogInformation("Cancelling in-flight prompt for session {SessionId}", req.SessionId);
                try { promptCts.Cancel(); } catch (ObjectDisposedException) { }
            }

            await _agentProvider.CancelSessionAsync(req.SessionId, cancellationToken);

            // session/cancel is a notification; no response is sent.
            return null;
        }

        /// <summary>
        /// Validates each prompt content block against the agent's negotiated capabilities.
        /// text and resource_link are baseline; image/audio/embedded resource require the
        /// matching capability. Unknown or unsupported content yields InvalidParams.
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

        private static T? DeserializeParams<T>(object? parameters) where T : class
        {
            if (parameters == null)
                return null;

            if (parameters is JsonElement jsonElement)
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText(), JsonRpcSerializer.Options);

            var json = JsonSerializer.Serialize(parameters, JsonRpcSerializer.Options);
            return JsonSerializer.Deserialize<T>(json, JsonRpcSerializer.Options);
        }

        private static string MapStopReason(StopReason stopReason) => stopReason switch
        {
            StopReason.Completed => "end_turn",
            StopReason.Cancelled => "cancelled",
            StopReason.TokenLimit => "max_tokens",
            StopReason.Error => "refusal",
            StopReason.TimeLimit => "max_tokens",
            _ => "end_turn"
        };

        private class NewSessionRequest
        {
            [JsonPropertyName("cwd")]
            public string? Cwd { get; set; }

            [JsonPropertyName("mcpServers")]
            public List<McpServerConfig>? McpServers { get; set; }

            [JsonPropertyName("additionalDirectories")]
            public List<string>? AdditionalDirectories { get; set; }
        }

        private class LoadSessionRequest
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; } = string.Empty;

            [JsonPropertyName("cwd")]
            public string? Cwd { get; set; }

            [JsonPropertyName("mcpServers")]
            public List<McpServerConfig>? McpServers { get; set; }

            [JsonPropertyName("additionalDirectories")]
            public List<string>? AdditionalDirectories { get; set; }
        }

        private class PromptRequest
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; } = string.Empty;

            [JsonPropertyName("prompt")]
            public List<ContentBlock>? PromptBlocks { get; set; }

            public PromptMessage ToPromptMessage()
            {
                var message = new PromptMessage();
                if (PromptBlocks == null || PromptBlocks.Count == 0)
                    return message;

                message.Blocks = PromptBlocks;
                message.Text = string.Join("\n", PromptBlocks
                    .Where(b => b.Type == "text" && !string.IsNullOrEmpty(b.Text))
                    .Select(b => b.Text!));
                return message;
            }
        }

        private class SetSessionModeRequest
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; } = string.Empty;

            [JsonPropertyName("modeId")]
            public string ModeId { get; set; } = string.Empty;
        }

        private class CancelRequest
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; } = string.Empty;
        }

        private class SessionIdRequest
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; } = string.Empty;
        }

        private class SetConfigOptionRequest
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; } = string.Empty;

            [JsonPropertyName("configId")]
            public string ConfigId { get; set; } = string.Empty;

            [JsonPropertyName("type")]
            public string? Type { get; set; }

            [JsonPropertyName("value")]
            public JsonElement Value { get; set; }
        }

        private class ListSessionsRequest
        {
            [JsonPropertyName("cwd")]
            public string? Cwd { get; set; }

            [JsonPropertyName("cursor")]
            public string? Cursor { get; set; }
        }
    }
}
