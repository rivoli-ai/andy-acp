using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.JsonRpc;

namespace Andy.Acp.Core.Protocol
{
    /// <summary>
    /// Handles the ACP <c>initialize</c> handshake and protocol-version negotiation for
    /// both stable v1 and (when enabled) v2 alpha. It does not register <c>initialized</c>
    /// or <c>shutdown</c> (neither is part of ACP), and it does not create a conversation
    /// session — that is the job of <c>session/new</c>. It records the outcome in the shared
    /// <see cref="AcpConnectionState"/> so session methods can enforce initialize-first ordering.
    /// </summary>
    public class AcpProtocolHandler
    {
        private readonly AcpConnectionState _state;
        private readonly ServerInfo _serverInfo;
        private readonly AcpAgentCapabilities _agentCapabilities;
        private readonly IAuthenticationProvider? _authProvider;
        private readonly System.Collections.Generic.IReadOnlySet<int> _supportedVersions;
        private readonly V2.V2AgentCapabilities? _v2Capabilities;
        private readonly ILogger<AcpProtocolHandler>? _logger;

        /// <summary>The default ACP protocol version (see <see cref="AcpVersions"/>).</summary>
        public const int ProtocolVersion = AcpVersions.V1;

        /// <summary>The lowest ACP protocol version this agent supports (see <see cref="AcpVersions"/>).</summary>
        public const int MinProtocolVersion = AcpVersions.V1;

        public AcpProtocolHandler(
            AcpConnectionState state,
            ServerInfo serverInfo,
            AcpAgentCapabilities agentCapabilities,
            ILogger<AcpProtocolHandler>? logger = null,
            IAuthenticationProvider? authProvider = null,
            System.Collections.Generic.IReadOnlySet<int>? supportedVersions = null,
            V2.V2AgentCapabilities? v2Capabilities = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _serverInfo = serverInfo ?? throw new ArgumentNullException(nameof(serverInfo));
            _agentCapabilities = agentCapabilities ?? throw new ArgumentNullException(nameof(agentCapabilities));
            _authProvider = authProvider;
            _supportedVersions = supportedVersions ?? AcpVersions.Default;
            _v2Capabilities = v2Capabilities;
            _logger = logger;

            if (_supportedVersions.Contains(AcpVersions.V2Alpha) && _v2Capabilities == null)
                throw new ArgumentException(
                    "v2 alpha is in the supported-version set but no v2 capabilities were supplied",
                    nameof(v2Capabilities));
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
            // Agents that don't require authentication are implicitly authenticated.
            _state.Authenticated = _authProvider?.RequiresAuthentication != true;

            _logger?.LogInformation(
                "Initialized: negotiated protocol version {Version} (client requested {Requested})",
                negotiated, initParams.ProtocolVersion);

            if (negotiated == AcpVersions.V2Alpha)
            {
                // v2 shape: required `info`, marker-object `capabilities`, `methodId` auth methods.
                var v2Result = new V2.V2InitializeResult
                {
                    ProtocolVersion = negotiated,
                    Info = new Implementation
                    {
                        Name = _serverInfo.Name,
                        Version = _serverInfo.Version
                    },
                    Capabilities = _v2Capabilities,
                    AuthMethods = _authProvider?.GetAuthMethods()
                        .Select(m => new V2.V2AuthMethod { MethodId = m.Id, Name = m.Name, Description = m.Description })
                        .ToList()
                };
                return Task.FromResult<object?>(v2Result);
            }

            var result = new AcpInitializeResult
            {
                ProtocolVersion = negotiated,
                AgentInfo = new Implementation
                {
                    Name = _serverInfo.Name,
                    Version = _serverInfo.Version
                },
                AgentCapabilities = _agentCapabilities,
                AuthMethods = _authProvider?.GetAuthMethods()
                    .Select(m => new AuthMethodDescription { Id = m.Id, Name = m.Name, Description = m.Description })
                    .ToList() ?? new()
            };

            return Task.FromResult<object?>(result);
        }

        /// <summary>
        /// Applies the ACP version-negotiation rule against the configured supported set:
        /// a supported requested version is used as-is; anything else falls back to stable
        /// v1 when served, so alpha versions are only negotiated on an explicit client
        /// request. (Without v1 in the set, the highest supported version is used.)
        /// </summary>
        private int NegotiateVersion(int? requested)
        {
            if (requested.HasValue && _supportedVersions.Contains(requested.Value))
                return requested.Value;

            if (_supportedVersions.Contains(AcpVersions.V1))
                return AcpVersions.V1;

            return _supportedVersions.Max();
        }

        /// <summary>
        /// Handles the ACP <c>authenticate</c> request. On success the connection is marked
        /// authenticated and session methods become available.
        /// </summary>
        public async Task<object?> HandleAuthenticateAsync(object? parameters, CancellationToken cancellationToken = default)
        {
            if (_authProvider == null)
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.MethodNotFound, "Authentication is not supported");

            if (!_state.Initialized)
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.SessionNotInitialized,
                    "Connection is not initialized. Call initialize first.");

            var req = DeserializeParams<AuthenticateParams>(parameters);
            if (string.IsNullOrEmpty(req?.MethodId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "methodId is required");

            if (_authProvider.GetAuthMethods().All(m => m.Id != req!.MethodId))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams,
                    $"Unknown auth method: {req!.MethodId}");

            await _authProvider.AuthenticateAsync(req!.MethodId, cancellationToken).ConfigureAwait(false);
            _state.Authenticated = true;

            _logger?.LogInformation("Authenticated via method {MethodId}", req.MethodId);

            // ACP AuthenticateResponse is an empty object.
            return new { };
        }

        /// <summary>
        /// Handles the ACP <c>logout</c> (v1) / <c>auth/logout</c> (v2) request. On v2,
        /// non-empty authMethods imply mandatory logout support, so the SupportsLogout
        /// opt-out only applies to v1 connections.
        /// </summary>
        public async Task<object?> HandleLogoutAsync(object? parameters, CancellationToken cancellationToken = default)
        {
            if (_authProvider == null ||
                (!_authProvider.SupportsLogout && _state.ProtocolVersion != AcpVersions.V2Alpha))
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.MethodNotFound, "Logout is not supported");

            await _authProvider.LogoutAsync(cancellationToken).ConfigureAwait(false);
            _state.Authenticated = !_authProvider.RequiresAuthentication;

            _logger?.LogInformation("Logged out");
            return new { };
        }

        /// <summary>
        /// Registers the ACP lifecycle methods: <c>initialize</c>, plus
        /// <c>authenticate</c>/<c>logout</c> when an authentication provider is present.
        /// </summary>
        public void RegisterMethods(JsonRpcHandler jsonRpcHandler)
        {
            if (jsonRpcHandler == null)
                throw new ArgumentNullException(nameof(jsonRpcHandler));

            jsonRpcHandler.RegisterMethod("initialize", HandleInitializeAsync);

            if (_authProvider != null)
            {
                // v1 names. The method registry prevents these on v2-negotiated connections.
                jsonRpcHandler.RegisterMethod("authenticate", HandleAuthenticateAsync);
                if (_authProvider.SupportsLogout)
                    jsonRpcHandler.RegisterMethod("logout", HandleLogoutAsync);

                // v2 names (same params: LoginAuthRequest.methodId matches, logout is empty).
                // Only reachable on v2-negotiated connections via the method registry.
                if (_supportedVersions.Contains(AcpVersions.V2Alpha))
                {
                    jsonRpcHandler.RegisterMethod("auth/login", HandleAuthenticateAsync);
                    jsonRpcHandler.RegisterMethod("auth/logout", HandleLogoutAsync);
                }
            }

            _logger?.LogInformation("Registered ACP protocol methods: initialize{Auth}",
                _authProvider != null ? ", authenticate" + (_authProvider.SupportsLogout ? ", logout" : "") : "");
        }

        private class AuthenticateParams
        {
            [JsonPropertyName("methodId")]
            public string MethodId { get; set; } = string.Empty;
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
