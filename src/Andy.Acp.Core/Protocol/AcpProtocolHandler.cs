using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Session;

namespace Andy.Acp.Core.Protocol
{
    /// <summary>
    /// Handles ACP protocol initialization and lifecycle methods
    /// </summary>
    public class AcpProtocolHandler
    {
        private readonly ISessionManager _sessionManager;
        private readonly ILogger<AcpProtocolHandler>? _logger;
        private readonly ServerInfo _serverInfo;
        private readonly ServerCapabilities _serverCapabilities;
        private AcpSession? _currentSession;

        /// <summary>
        /// The supported ACP protocol version
        /// </summary>
        public const string ProtocolVersion = "1.0";

        /// <summary>
        /// Initializes a new ACP protocol handler
        /// </summary>
        /// <param name="sessionManager">The session manager</param>
        /// <param name="serverInfo">Server information</param>
        /// <param name="serverCapabilities">Server capabilities</param>
        /// <param name="logger">Optional logger</param>
        public AcpProtocolHandler(
            ISessionManager sessionManager,
            ServerInfo serverInfo,
            ServerCapabilities serverCapabilities,
            ILogger<AcpProtocolHandler>? logger = null)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _serverInfo = serverInfo ?? throw new ArgumentNullException(nameof(serverInfo));
            _serverCapabilities = serverCapabilities ?? throw new ArgumentNullException(nameof(serverCapabilities));
            _logger = logger;
        }

        /// <summary>
        /// Gets the current active session
        /// </summary>
        public AcpSession? CurrentSession => _currentSession;

        /// <summary>
        /// Handles the initialize request
        /// </summary>
        public async Task<object?> HandleInitializeAsync(object? parameters, CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("Processing initialize request");

            // Check if already initialized
            if (_currentSession != null)
            {
                // Check if session is shutting down or terminated
                if (_currentSession.State == SessionState.ShuttingDown ||
                    _currentSession.State == SessionState.Terminated)
                {
                    _logger?.LogWarning("Initialize called after shutdown");
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidRequest,
                        "Cannot initialize after shutdown. Session is terminating."
                    );
                }

                _logger?.LogWarning("Initialize called but session already exists: {SessionId}", _currentSession.SessionId);
                throw new JsonRpcProtocolException(
                    JsonRpcErrorCodes.SessionAlreadyInitialized,
                    "Session already initialized"
                );
            }

            // Parse initialize parameters
            InitializeParams? initParams = null;
            try
            {
                if (parameters is JsonElement jsonElement)
                {
                    initParams = JsonSerializer.Deserialize<InitializeParams>(jsonElement.GetRawText());
                }
                else if (parameters != null)
                {
                    var json = JsonSerializer.Serialize(parameters);
                    initParams = JsonSerializer.Deserialize<InitializeParams>(json);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to parse initialize parameters");
                throw new ArgumentException("Invalid initialize parameters", nameof(parameters), ex);
            }

            initParams ??= new InitializeParams();

            // Validate protocol version
            if (!string.IsNullOrEmpty(initParams.ProtocolVersion) &&
                initParams.ProtocolVersion != ProtocolVersion)
            {
                _logger?.LogWarning("Protocol version mismatch: client={ClientVersion}, server={ServerVersion}",
                    initParams.ProtocolVersion, ProtocolVersion);

                // For now, we'll accept different versions but log a warning
                // In a stricter implementation, you might reject incompatible versions
            }

            // Create session with client capabilities
            _currentSession = _sessionManager.CreateSession();
            _logger?.LogInformation("Created session {SessionId} for initialize request", _currentSession.SessionId);

            // Build client capabilities from initialize params
            var clientCapabilities = new Andy.Acp.Core.Session.ClientCapabilities
            {
                ClientInfo = ConvertClientInfo(initParams.ClientInfo),
                SupportedTools = initParams.Capabilities?.SupportedTools,
                SupportedResources = initParams.Capabilities?.SupportedResources,
                MaxConcurrentTools = initParams.Capabilities?.MaxConcurrentTools,
                ToolTimeoutMs = initParams.Capabilities?.ToolTimeoutMs,
                Extensions = initParams.Capabilities?.Extensions
            };

            // Initialize the session
            if (!_currentSession.Initialize(clientCapabilities))
            {
                _logger?.LogError("Failed to initialize session {SessionId}", _currentSession.SessionId);
                var failedSession = _currentSession;
                _currentSession = null;
                _sessionManager.TerminateSession(failedSession.SessionId);

                throw new JsonRpcProtocolException(
                    JsonRpcErrorCodes.InternalError,
                    "Failed to initialize session"
                );
            }

            _logger?.LogInformation("Session {SessionId} initialized successfully", _currentSession.SessionId);

            // Return initialize result
            var result = new InitializeResult
            {
                ProtocolVersion = ProtocolVersion,
                ServerInfo = _serverInfo,
                Capabilities = _serverCapabilities,
                SessionInfo = new SessionInfo
                {
                    SessionId = _currentSession.SessionId,
                    TimeoutMs = (int)_sessionManager.DefaultSessionTimeout.TotalMilliseconds,
                    Metadata = new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["createdAt"] = _currentSession.CreatedAt.ToString("O")
                    }
                }
            };

            return await Task.FromResult(result);
        }

        /// <summary>
        /// Handles the initialized notification
        /// </summary>
        public async Task<object?> HandleInitializedAsync(object? parameters, CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("Processing initialized notification");

            if (_currentSession == null)
            {
                _logger?.LogWarning("Initialized notification received but no session exists");
                throw new JsonRpcProtocolException(
                    JsonRpcErrorCodes.SessionNotInitialized,
                    "No active session. Call initialize first."
                );
            }

            // Reject if session is shutting down or terminated
            if (_currentSession.State == SessionState.ShuttingDown ||
                _currentSession.State == SessionState.Terminated)
            {
                _logger?.LogWarning("Initialized notification received after shutdown");
                throw new JsonRpcProtocolException(
                    JsonRpcErrorCodes.InvalidRequest,
                    "Cannot process initialized notification. Session is shutting down."
                );
            }

            if (_currentSession.State != SessionState.Initialized &&
                _currentSession.State != SessionState.Active)
            {
                _logger?.LogWarning("Initialized notification received but session is in state: {State}",
                    _currentSession.State);
            }

            // Mark session as active
            _currentSession.MarkActive();

            _logger?.LogInformation("Session {SessionId} marked as active", _currentSession.SessionId);

            // Initialized is a notification, so no response is expected
            return await Task.FromResult<object?>(null);
        }

        /// <summary>
        /// Handles the shutdown request
        /// </summary>
        public async Task<object?> HandleShutdownAsync(object? parameters, CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("Processing shutdown request");

            if (_currentSession == null)
            {
                _logger?.LogWarning("Shutdown called but no session exists");
                return await Task.FromResult(new ShutdownResult
                {
                    Success = false,
                    Message = "No active session"
                });
            }

            // Parse shutdown parameters
            ShutdownParams? shutdownParams = null;
            try
            {
                if (parameters is JsonElement jsonElement)
                {
                    shutdownParams = JsonSerializer.Deserialize<ShutdownParams>(jsonElement.GetRawText());
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to parse shutdown parameters");
            }

            var reason = shutdownParams?.Reason ?? "Client requested shutdown";
            _logger?.LogInformation("Shutting down session {SessionId}: {Reason}",
                _currentSession.SessionId, reason);

            // Initiate graceful shutdown
            _currentSession.Shutdown();

            // Wait for pending operations to complete (with 5 second timeout)
            var pendingCount = _currentSession.PendingRequests.Count;
            if (pendingCount > 0)
            {
                _logger?.LogInformation("Waiting for {Count} pending requests to complete", pendingCount);

                var timeout = TimeSpan.FromSeconds(5);
                var startTime = DateTime.UtcNow;

                while (_currentSession.PendingRequests.Count > 0 &&
                       DateTime.UtcNow - startTime < timeout &&
                       !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken);
                }

                var remainingCount = _currentSession.PendingRequests.Count;
                if (remainingCount > 0)
                {
                    _logger?.LogWarning("{Count} requests did not complete within timeout, forcing termination",
                        remainingCount);
                }
                else
                {
                    _logger?.LogInformation("All pending requests completed successfully");
                }
            }

            // Terminate the session
            var sessionId = _currentSession.SessionId;
            _sessionManager.TerminateSession(sessionId);
            _currentSession = null;

            _logger?.LogInformation("Session {SessionId} terminated successfully", sessionId);

            return await Task.FromResult(new ShutdownResult
            {
                Success = true,
                Message = "Session terminated successfully"
            });
        }

        /// <summary>
        /// Registers the ACP protocol methods with a JSON-RPC handler
        /// </summary>
        public void RegisterMethods(JsonRpcHandler jsonRpcHandler)
        {
            if (jsonRpcHandler == null)
                throw new ArgumentNullException(nameof(jsonRpcHandler));

            jsonRpcHandler.RegisterMethod("initialize", HandleInitializeAsync);
            jsonRpcHandler.RegisterMethod("initialized", HandleInitializedAsync);
            jsonRpcHandler.RegisterMethod("shutdown", HandleShutdownAsync);

            _logger?.LogInformation("Registered ACP protocol methods: initialize, initialized, shutdown");
        }

        /// <summary>
        /// Converts protocol ClientInfo to session ClientInfo
        /// </summary>
        private static Andy.Acp.Core.Session.ClientInfo? ConvertClientInfo(ClientInfo? clientInfo)
        {
            if (clientInfo == null)
                return null;

            return new Andy.Acp.Core.Session.ClientInfo
            {
                Name = clientInfo.Name,
                Version = clientInfo.Version,
                Description = clientInfo.Description,
                Homepage = clientInfo.Homepage,
                Contact = clientInfo.Contact
            };
        }

        /// <summary>
        /// Checks if a session is currently active
        /// </summary>
        public bool HasActiveSession() => _currentSession != null && _currentSession.IsHealthy;

        /// <summary>
        /// Gets the current session or throws if no session exists
        /// </summary>
        public AcpSession GetCurrentSessionOrThrow()
        {
            if (_currentSession == null)
            {
                throw new JsonRpcProtocolException(
                    JsonRpcErrorCodes.SessionNotInitialized,
                    "No active session. Call initialize first."
                );
            }

            return _currentSession;
        }
    }
}
