using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Andy.Acp.Core.JsonRpc;

namespace Andy.Acp.Core.Protocol
{
    /// <summary>
    /// Handles the ACP v1 <c>initialize</c> handshake and protocol-version negotiation.
    /// Unlike earlier revisions, this does not register <c>initialized</c> or
    /// <c>shutdown</c> (neither is part of ACP v1), and it does not create a conversation
    /// session — that is the job of <c>session/new</c>. It records the outcome in the shared
    /// <see cref="AcpConnectionState"/> so session methods can enforce initialize-first ordering.
    /// </summary>
    public class AcpProtocolHandler
    {
        private readonly AcpConnectionState _state;
        private readonly ServerInfo _serverInfo;
        private readonly AcpAgentCapabilities _agentCapabilities;
        private readonly ILogger<AcpProtocolHandler>? _logger;

        /// <summary>The highest ACP protocol version this agent supports.</summary>
        public const int ProtocolVersion = 1;

        /// <summary>The lowest ACP protocol version this agent supports.</summary>
        public const int MinProtocolVersion = 1;

        public AcpProtocolHandler(
            AcpConnectionState state,
            ServerInfo serverInfo,
            AcpAgentCapabilities agentCapabilities,
            ILogger<AcpProtocolHandler>? logger = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _serverInfo = serverInfo ?? throw new ArgumentNullException(nameof(serverInfo));
            _agentCapabilities = agentCapabilities ?? throw new ArgumentNullException(nameof(agentCapabilities));
            _logger = logger;
        }

        /// <summary>
        /// Handles the <c>initialize</c> request: negotiates the protocol version, records the
        /// client's capabilities, and advertises this agent's capabilities.
        /// </summary>
        public Task<object?> HandleInitializeAsync(object? parameters, CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("Processing initialize request");

            if (_state.Initialized)
            {
                throw new JsonRpcProtocolException(
                    JsonRpcErrorCodes.SessionAlreadyInitialized,
                    "Connection already initialized");
            }

            AcpInitializeParams initParams;
            try
            {
                initParams = DeserializeParams<AcpInitializeParams>(parameters) ?? new AcpInitializeParams();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to parse initialize parameters");
                throw new ArgumentException("Invalid initialize parameters", nameof(parameters), ex);
            }

            int negotiated = NegotiateVersion(initParams.ProtocolVersion);

            _state.ClientCapabilities = initParams.ClientCapabilities ?? new AcpClientCapabilities();
            _state.ProtocolVersion = negotiated;
            _state.Initialized = true;

            _logger?.LogInformation(
                "Initialized: negotiated protocol version {Version} (client requested {Requested})",
                negotiated, initParams.ProtocolVersion);

            var result = new AcpInitializeResult
            {
                ProtocolVersion = negotiated,
                AgentInfo = new Implementation
                {
                    Name = _serverInfo.Name,
                    Version = _serverInfo.Version
                },
                AgentCapabilities = _agentCapabilities,
                AuthMethods = new()
            };

            return Task.FromResult<object?>(result);
        }

        /// <summary>
        /// Applies the ACP version-negotiation rule: if the client's requested version is
        /// supported it is used; a higher request is clamped down to this agent's maximum; a
        /// lower unsupported request falls back to the agent's maximum so the client can decide.
        /// </summary>
        private static int NegotiateVersion(int? requested)
        {
            if (!requested.HasValue)
                return ProtocolVersion;

            int v = requested.Value;
            if (v > ProtocolVersion)
                return ProtocolVersion;
            if (v < MinProtocolVersion)
                return ProtocolVersion;
            return v;
        }

        /// <summary>
        /// Registers the ACP lifecycle methods. Only <c>initialize</c> is part of ACP v1.
        /// </summary>
        public void RegisterMethods(JsonRpcHandler jsonRpcHandler)
        {
            if (jsonRpcHandler == null)
                throw new ArgumentNullException(nameof(jsonRpcHandler));

            jsonRpcHandler.RegisterMethod("initialize", HandleInitializeAsync);
            _logger?.LogInformation("Registered ACP protocol method: initialize");
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
    }
}
