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
using Andy.Acp.Core.Client;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Core.Protocol.V2
{
    /// <summary>
    /// ACP v2 (alpha) session method implementations. Instances are invoked by the
    /// version router in <c>AcpServer</c> only on connections that negotiated v2; they
    /// map v2 wire shapes onto the same version-neutral provider abstractions
    /// (<see cref="IAgentProvider"/> and the optional interfaces) used by v1.
    /// Key v2 behaviors handled here: the prompt response is an empty ACK with the stop
    /// reason delivered via <c>state_update</c>; <c>session/resume</c> with
    /// <c>replayFrom: {"type":"start"}</c> takes the place of v1 <c>session/load</c>.
    /// </summary>
    public class AcpSessionHandlerV2
    {
        private readonly IAgentProvider _agentProvider;
        private readonly AcpConnectionState _state;
        private readonly ILogger? _logger;
        private ITransport? _transport;
        private IAcpClient? _client;

        private readonly ConcurrentDictionary<string, CancellationTokenSource> _activePrompts = new();

        public AcpSessionHandlerV2(
            IAgentProvider agentProvider,
            AcpConnectionState state,
            ILogger? logger = null)
        {
            _agentProvider = agentProvider ?? throw new ArgumentNullException(nameof(agentProvider));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _logger = logger;
        }

        public void SetTransport(ITransport transport) => _transport = transport;
        public void SetClient(IAcpClient client) => _client = client;

        private void EnsureReady()
        {
            if (!_state.Initialized)
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.SessionNotInitialized,
                    "Connection is not initialized. Call initialize first.");
            if (!_state.Authenticated)
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.AuthRequired,
                    "Authentication required. Call auth/login first.");
        }

        public async Task<object?> HandleNewSessionAsync(object? parameters, CancellationToken cancellationToken)
        {
            EnsureReady();

            var req = Deserialize<NewSessionRequestV2>(parameters);
            if (string.IsNullOrEmpty(req?.Cwd))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "cwd is required");
            if (!Path.IsPathRooted(req!.Cwd))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "cwd must be an absolute path");

            ValidateMcpServers(req.McpServers);

            var metadata = await _agentProvider.CreateSessionAsync(new NewSessionParams
            {
                Cwd = req.Cwd,
                McpServers = req.McpServers ?? new List<McpServerConfig>(),
                AdditionalDirectories = req.AdditionalDirectories
            }, cancellationToken);

            _logger?.LogInformation("Created new v2 session {SessionId}", metadata.SessionId);

            // v2 NewSessionResponse: sessionId + optional configOptions. No modes.
            return new
            {
                sessionId = metadata.SessionId,
                configOptions = V2Wire.ConfigOptions(metadata.ConfigOptions)
            };
        }

        public async Task<object?> HandleResumeSessionAsync(object? parameters, CancellationToken cancellationToken)
        {
            EnsureReady();

            var req = Deserialize<ResumeSessionRequestV2>(parameters);
            if (string.IsNullOrEmpty(req?.SessionId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "sessionId is required");
            if (string.IsNullOrEmpty(req!.Cwd))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "cwd is required");

            ValidateMcpServers(req.McpServers);

            bool replay = false;
            if (req.ReplayFrom != null)
            {
                if (req.ReplayFrom.Type != "start")
                    throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams,
                        $"Unsupported replayFrom cursor type: {req.ReplayFrom.Type}");
                replay = true;
            }

            var loadParams = new LoadSessionParams
            {
                SessionId = req.SessionId,
                Cwd = req.Cwd,
                McpServers = req.McpServers ?? new List<McpServerConfig>(),
                AdditionalDirectories = req.AdditionalDirectories
            };

            SessionMetadata? metadata;
            if (replay)
            {
                if (!_agentProvider.GetCapabilities().LoadSession)
                    throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams,
                        "This agent does not support history replay");

                var streamer = new SessionUpdateStreamerV2(_transport, req.SessionId, _logger, _client);
                metadata = await _agentProvider.LoadSessionAsync(loadParams, streamer, cancellationToken);
            }
            else if (_agentProvider is ISessionCatalogProvider catalog)
            {
                metadata = await catalog.ResumeSessionAsync(loadParams, cancellationToken);
            }
            else
            {
                // Resume without replay via the load path with a muted streamer, so agents
                // that only implement LoadSessionAsync still support v2 resume.
                var muted = new SessionUpdateStreamerV2(null, req.SessionId, _logger, _client);
                metadata = await _agentProvider.LoadSessionAsync(loadParams, muted, cancellationToken);
            }

            if (metadata == null)
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.ResourceNotFound,
                    $"Session not found: {req.SessionId}");

            return new { configOptions = V2Wire.ConfigOptions(metadata.ConfigOptions) };
        }

        public async Task<object?> HandleListSessionsAsync(object? parameters, CancellationToken cancellationToken)
        {
            EnsureReady();

            var req = Deserialize<ListSessionsRequestV2>(parameters) ?? new ListSessionsRequestV2();

            if (_agentProvider is not ISessionCatalogProvider catalog)
            {
                // session/list is baseline in v2; agents without a catalog list nothing.
                return new { sessions = Array.Empty<object>() };
            }

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

        public async Task<object?> HandleDeleteSessionAsync(object? parameters, CancellationToken cancellationToken)
        {
            EnsureReady();

            if (_agentProvider is not ISessionCatalogProvider catalog)
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.MethodNotFound,
                    "session/delete is not supported by this agent");

            var req = Deserialize<SessionIdRequestV2>(parameters);
            if (string.IsNullOrEmpty(req?.SessionId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "sessionId is required");

            if (!await catalog.DeleteSessionAsync(req!.SessionId, cancellationToken))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.ResourceNotFound,
                    $"Session not found: {req.SessionId}");

            return new { };
        }

        public async Task<object?> HandleCloseSessionAsync(object? parameters, CancellationToken cancellationToken)
        {
            EnsureReady();

            var req = Deserialize<SessionIdRequestV2>(parameters);
            if (string.IsNullOrEmpty(req?.SessionId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "sessionId is required");

            if (_activePrompts.TryGetValue(req!.SessionId, out var cts))
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }

            if (_agentProvider is ISessionCatalogProvider catalog)
                await catalog.CloseSessionAsync(req.SessionId, cancellationToken);
            else
                await _agentProvider.CancelSessionAsync(req.SessionId, cancellationToken);

            return new { };
        }

        public async Task<object?> HandleSetConfigOptionAsync(object? parameters, CancellationToken cancellationToken)
        {
            EnsureReady();

            if (_agentProvider is not ISessionConfigProvider configProvider)
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.MethodNotFound,
                    "session/set_config_option is not supported by this agent");

            var req = Deserialize<SetConfigOptionRequestV2>(parameters);
            if (string.IsNullOrEmpty(req?.SessionId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "sessionId is required");
            if (string.IsNullOrEmpty(req!.ConfigId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "configId is required");
            if (string.IsNullOrEmpty(req.Type))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "type is required");

            // v2 value variants: "id" (select value id) or "boolean".
            var value = new SessionConfigValue();
            switch (req.Type)
            {
                case "id" when req.Value.ValueKind == JsonValueKind.String:
                    value.ValueId = req.Value.GetString();
                    break;
                case "boolean" when req.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    value.BoolValue = req.Value.GetBoolean();
                    break;
                default:
                    throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams,
                        $"Unsupported config value type: {req.Type}");
            }

            IReadOnlyList<SessionConfigOption> options;
            try
            {
                options = await configProvider.SetConfigOptionAsync(req.SessionId, req.ConfigId, value, cancellationToken);
            }
            catch (ArgumentException ex)
            {
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, ex.Message);
            }

            return new { configOptions = V2Wire.ConfigOptions(options) };
        }

        public async Task<object?> HandlePromptAsync(object? parameters, CancellationToken cancellationToken)
        {
            EnsureReady();

            var req = Deserialize<PromptRequestV2>(parameters);
            if (string.IsNullOrEmpty(req?.SessionId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "Session ID is required");
            if (req!.PromptBlocks == null || req.PromptBlocks.Count == 0)
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "Prompt content is required");

            ValidateContentBlocks(req.PromptBlocks);

            var message = new PromptMessage
            {
                Blocks = req.PromptBlocks,
                Text = string.Join("\n", req.PromptBlocks
                    .Where(b => b.Type == "text" && !string.IsNullOrEmpty(b.Text))
                    .Select(b => b.Text!))
            };

            var promptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (!_activePrompts.TryAdd(req.SessionId, promptCts))
            {
                promptCts.Dispose();
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidRequest,
                    $"A prompt is already in progress for session {req.SessionId}");
            }

            var streamer = new SessionUpdateStreamerV2(_transport, req.SessionId, _logger, _client);
            try
            {
                // v2: the turn's lifecycle is reported via state_update; the prompt
                // response itself is an empty ACK sent after the idle state.
                await streamer.SendStateRunningAsync(cancellationToken);

                string stopReason;
                try
                {
                    var response = await _agentProvider.ProcessPromptAsync(
                        req.SessionId, message, streamer, promptCts.Token);
                    stopReason = AcpStopReason.ToWire(response.StopReason);
                }
                catch (OperationCanceledException)
                {
                    stopReason = "cancelled";
                }

                await streamer.SendStateIdleAsync(stopReason, cancellationToken);
                return new { };
            }
            finally
            {
                _activePrompts.TryRemove(req.SessionId, out _);
                promptCts.Dispose();
            }
        }

        public async Task<object?> HandleCancelAsync(object? parameters, CancellationToken cancellationToken)
        {
            var req = Deserialize<SessionIdRequestV2>(parameters);
            if (string.IsNullOrEmpty(req?.SessionId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "Session ID is required");

            if (_activePrompts.TryGetValue(req!.SessionId, out var cts))
            {
                _logger?.LogInformation("Cancelling in-flight v2 prompt for session {SessionId}", req.SessionId);
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }

            await _agentProvider.CancelSessionAsync(req.SessionId, cancellationToken);
            return null; // notification
        }

        /// <summary>
        /// v2 MCP validation: <c>type</c> is required, <c>sse</c> no longer exists,
        /// <c>http</c> requires the capability, and stdio is always accepted.
        /// </summary>
        private void ValidateMcpServers(List<McpServerConfig>? servers)
        {
            if (servers == null || servers.Count == 0)
                return;

            var caps = _agentProvider.GetCapabilities();
            foreach (var server in servers)
            {
                switch (server.Type)
                {
                    case "stdio":
                        break;
                    case "http":
                        if (!caps.McpHttp)
                            throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams,
                                $"HTTP MCP servers are not supported by this agent (server: {server.Name})");
                        break;
                    case null:
                    case "":
                        throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams,
                            "McpServer.type is required in ACP v2");
                    default:
                        throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams,
                            $"Unsupported MCP server type: {server.Type}");
                }
            }
        }

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

        private static T? Deserialize<T>(object? parameters) where T : class
        {
            if (parameters == null)
                return null;
            if (parameters is JsonElement el)
                return JsonSerializer.Deserialize<T>(el.GetRawText(), JsonRpcSerializer.Options);
            var json = JsonSerializer.Serialize(parameters, JsonRpcSerializer.Options);
            return JsonSerializer.Deserialize<T>(json, JsonRpcSerializer.Options);
        }

        private class NewSessionRequestV2
        {
            [JsonPropertyName("cwd")]
            public string? Cwd { get; set; }

            [JsonPropertyName("additionalDirectories")]
            public List<string>? AdditionalDirectories { get; set; }

            [JsonPropertyName("mcpServers")]
            public List<McpServerConfig>? McpServers { get; set; }
        }

        private class ResumeSessionRequestV2 : NewSessionRequestV2
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; } = string.Empty;

            [JsonPropertyName("replayFrom")]
            public ReplayFromV2? ReplayFrom { get; set; }
        }

        private class ReplayFromV2
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = string.Empty;
        }

        private class ListSessionsRequestV2
        {
            [JsonPropertyName("cwd")]
            public string? Cwd { get; set; }

            [JsonPropertyName("cursor")]
            public string? Cursor { get; set; }
        }

        private class SessionIdRequestV2
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; } = string.Empty;
        }

        private class SetConfigOptionRequestV2
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

        private class PromptRequestV2
        {
            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; } = string.Empty;

            [JsonPropertyName("prompt")]
            public List<ContentBlock>? PromptBlocks { get; set; }
        }
    }
}
