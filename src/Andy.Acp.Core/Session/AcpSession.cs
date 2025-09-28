using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
using Andy.Acp.Core.JsonRpc;

namespace Andy.Acp.Core.Session
{
    /// <summary>
    /// Represents an ACP session managing client connection lifecycle and state
    /// </summary>
    public class AcpSession : IDisposable
    {
        private readonly ILogger<AcpSession>? _logger;
        private readonly ConcurrentDictionary<object, PendingRequest> _pendingRequests = new();
        private readonly Dictionary<string, object> _sessionContext = new();
        private readonly object _stateLock = new();
        private SessionState _state = SessionState.Created;
        private bool _disposed;

        /// <summary>
        /// Event raised when the session state changes
        /// </summary>
        public event EventHandler<SessionStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// Event raised when a request times out
        /// </summary>
        public event EventHandler<RequestTimeoutEventArgs>? RequestTimeout;

        /// <summary>
        /// Initializes a new ACP session
        /// </summary>
        /// <param name="sessionId">Unique session identifier</param>
        /// <param name="logger">Optional logger instance</param>
        public AcpSession(string? sessionId = null, ILogger<AcpSession>? logger = null)
        {
            SessionId = sessionId ?? Guid.NewGuid().ToString("N")[..16];
            _logger = logger;
            CreatedAt = DateTime.UtcNow;
            CancellationSource = new CancellationTokenSource();

            _logger?.LogInformation("Created new ACP session: {SessionId}", SessionId);
        }

        /// <summary>
        /// Unique identifier for this session
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        /// Current state of the session
        /// </summary>
        public SessionState State
        {
            get
            {
                lock (_stateLock)
                {
                    return _state;
                }
            }
            private set
            {
                SessionState oldState;
                lock (_stateLock)
                {
                    oldState = _state;
                    _state = value;
                }

                if (oldState != value)
                {
                    _logger?.LogInformation("Session {SessionId} state changed: {OldState} -> {NewState}",
                        SessionId, oldState, value);

                    StateChanged?.Invoke(this, new SessionStateChangedEventArgs(SessionId, oldState, value));
                }
            }
        }

        /// <summary>
        /// When the session was created
        /// </summary>
        public DateTime CreatedAt { get; }

        /// <summary>
        /// When the session was last active (received a request)
        /// </summary>
        public DateTime? LastActivityAt { get; private set; }

        /// <summary>
        /// Client capabilities sent during initialization
        /// </summary>
        public ClientCapabilities? Capabilities { get; private set; }

        /// <summary>
        /// Session-specific context and variables
        /// </summary>
        public IReadOnlyDictionary<string, object> Context => _sessionContext.AsReadOnly();

        /// <summary>
        /// Pending requests awaiting responses
        /// </summary>
        public IReadOnlyDictionary<object, PendingRequest> PendingRequests => _pendingRequests.AsReadOnly();

        /// <summary>
        /// Cancellation token source for the entire session
        /// </summary>
        public CancellationTokenSource CancellationSource { get; }

        /// <summary>
        /// Gets the session's cancellation token
        /// </summary>
        public CancellationToken CancellationToken => CancellationSource.Token;

        /// <summary>
        /// Whether the session is in a healthy state to process requests
        /// </summary>
        public bool IsHealthy => State == SessionState.Initialized || State == SessionState.Active;

        /// <summary>
        /// Whether the session is terminating or terminated
        /// </summary>
        public bool IsTerminating => State == SessionState.ShuttingDown ||
                                    State == SessionState.Terminated ||
                                    State == SessionState.Faulted;

        /// <summary>
        /// Initializes the session with client capabilities
        /// </summary>
        /// <param name="capabilities">Client capabilities</param>
        /// <returns>True if initialization succeeded, false otherwise</returns>
        public bool Initialize(ClientCapabilities capabilities)
        {
            ThrowIfDisposed();

            lock (_stateLock)
            {
                if (_state != SessionState.Created)
                {
                    _logger?.LogWarning("Cannot initialize session {SessionId} in state {State}", SessionId, _state);
                    return false;
                }

                State = SessionState.Initializing;
            }

            try
            {
                Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));

                _logger?.LogInformation("Initializing session {SessionId} with client: {ClientName} {ClientVersion}",
                    SessionId, capabilities.ClientInfo?.Name, capabilities.ClientInfo?.Version);

                // Set initial context
                if (capabilities.ClientInfo != null)
                {
                    SetContextValue("client.name", capabilities.ClientInfo.Name);
                    SetContextValue("client.version", capabilities.ClientInfo.Version);
                }

                State = SessionState.Initialized;
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize session {SessionId}", SessionId);
                State = SessionState.Faulted;
                return false;
            }
        }

        /// <summary>
        /// Marks the session as active (processing requests)
        /// </summary>
        public void MarkActive()
        {
            ThrowIfDisposed();
            LastActivityAt = DateTime.UtcNow;

            if (State == SessionState.Initialized)
            {
                State = SessionState.Active;
            }
        }

        /// <summary>
        /// Adds a pending request to the session
        /// </summary>
        /// <param name="request">The JSON-RPC request</param>
        public void AddPendingRequest(JsonRpcRequest request)
        {
            ThrowIfDisposed();

            if (request.Id == null) return; // Notifications don't need tracking

            var pendingRequest = new PendingRequest(request.Id, request.Method, DateTime.UtcNow);

            if (_pendingRequests.TryAdd(request.Id, pendingRequest))
            {
                _logger?.LogDebug("Added pending request {RequestId} for method {Method} in session {SessionId}",
                    request.Id, request.Method, SessionId);
            }
            else
            {
                _logger?.LogWarning("Duplicate request ID {RequestId} in session {SessionId}", request.Id, SessionId);
                pendingRequest.Dispose();
            }
        }

        /// <summary>
        /// Completes a pending request
        /// </summary>
        /// <param name="requestId">The request ID to complete</param>
        /// <returns>True if the request was found and removed, false otherwise</returns>
        public bool CompletePendingRequest(object requestId)
        {
            ThrowIfDisposed();

            if (_pendingRequests.TryRemove(requestId, out var pendingRequest))
            {
                _logger?.LogDebug("Completed pending request {RequestId} for method {Method} in session {SessionId}",
                    requestId, pendingRequest.Method, SessionId);

                pendingRequest.Dispose();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Sets a value in the session context
        /// </summary>
        /// <param name="key">The context key</param>
        /// <param name="value">The value to set</param>
        public void SetContextValue(string key, object value)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Context key cannot be null or empty", nameof(key));

            lock (_sessionContext)
            {
                _sessionContext[key] = value;
            }

            _logger?.LogDebug("Set context value {Key} in session {SessionId}", key, SessionId);
        }

        /// <summary>
        /// Gets a value from the session context
        /// </summary>
        /// <typeparam name="T">The expected type of the value</typeparam>
        /// <param name="key">The context key</param>
        /// <returns>The value if found and of the correct type, default(T) otherwise</returns>
        public T? GetContextValue<T>(string key)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(key))
                return default;

            lock (_sessionContext)
            {
                if (_sessionContext.TryGetValue(key, out var value) && value is T typedValue)
                {
                    return typedValue;
                }
            }

            return default;
        }

        /// <summary>
        /// Removes a value from the session context
        /// </summary>
        /// <param name="key">The context key to remove</param>
        /// <returns>True if the key was found and removed, false otherwise</returns>
        public bool RemoveContextValue(string key)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(key))
                return false;

            lock (_sessionContext)
            {
                return _sessionContext.Remove(key);
            }
        }

        /// <summary>
        /// Checks for timed-out requests and raises timeout events
        /// </summary>
        /// <param name="timeout">The timeout duration</param>
        /// <returns>Number of requests that timed out</returns>
        public int CheckForTimeouts(TimeSpan timeout)
        {
            ThrowIfDisposed();

            var timedOutRequests = new List<PendingRequest>();

            // Find timed out requests
            foreach (var kvp in _pendingRequests)
            {
                if (kvp.Value.IsTimedOut(timeout))
                {
                    timedOutRequests.Add(kvp.Value);
                }
            }

            // Remove timed out requests and notify
            foreach (var timedOutRequest in timedOutRequests)
            {
                if (_pendingRequests.TryRemove(timedOutRequest.Id, out var removedRequest))
                {
                    _logger?.LogWarning("Request {RequestId} timed out after {Timeout} in session {SessionId}",
                        timedOutRequest.Id, timeout, SessionId);

                    removedRequest.CancellationSource.Cancel();
                    RequestTimeout?.Invoke(this, new RequestTimeoutEventArgs(SessionId, removedRequest));
                    removedRequest.Dispose();
                }
            }

            return timedOutRequests.Count;
        }

        /// <summary>
        /// Initiates graceful shutdown of the session
        /// </summary>
        public void Shutdown()
        {
            if (_disposed) return;

            _logger?.LogInformation("Shutting down session {SessionId}", SessionId);

            State = SessionState.ShuttingDown;

            // Cancel all pending requests
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.CancellationSource.Cancel();
            }

            // Cancel the session
            CancellationSource.Cancel();
        }

        /// <summary>
        /// Terminates the session immediately
        /// </summary>
        public void Terminate()
        {
            if (_disposed) return;

            _logger?.LogInformation("Terminating session {SessionId}", SessionId);

            State = SessionState.Terminated;
            CancellationSource.Cancel();
        }

        /// <summary>
        /// Marks the session as faulted due to an error
        /// </summary>
        /// <param name="error">The error that caused the fault</param>
        public void Fault(Exception error)
        {
            if (_disposed) return;

            _logger?.LogError(error, "Session {SessionId} faulted", SessionId);

            State = SessionState.Faulted;
            CancellationSource.Cancel();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AcpSession));
        }

        /// <summary>
        /// Disposes the session and releases all resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _logger?.LogInformation("Disposing session {SessionId}", SessionId);

                State = SessionState.Terminated;

                // Dispose all pending requests
                foreach (var kvp in _pendingRequests)
                {
                    kvp.Value.Dispose();
                }
                _pendingRequests.Clear();

                // Clear context
                lock (_sessionContext)
                {
                    _sessionContext.Clear();
                }

                CancellationSource?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Event args for session state changes
    /// </summary>
    public class SessionStateChangedEventArgs : EventArgs
    {
        public SessionStateChangedEventArgs(string sessionId, SessionState oldState, SessionState newState)
        {
            SessionId = sessionId;
            OldState = oldState;
            NewState = newState;
        }

        public string SessionId { get; }
        public SessionState OldState { get; }
        public SessionState NewState { get; }
    }

    /// <summary>
    /// Event args for request timeouts
    /// </summary>
    public class RequestTimeoutEventArgs : EventArgs
    {
        public RequestTimeoutEventArgs(string sessionId, PendingRequest request)
        {
            SessionId = sessionId;
            Request = request;
        }

        public string SessionId { get; }
        public PendingRequest Request { get; }
    }
}