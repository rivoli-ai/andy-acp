using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Core.Session
{
    /// <summary>
    /// Manages ACP sessions and their lifecycle
    /// </summary>
    public class SessionManager : ISessionManager
    {
        private readonly ILogger<SessionManager>? _logger;
        private readonly ConcurrentDictionary<string, AcpSession> _sessions = new();
        private readonly Timer? _cleanupTimer;
        private readonly CancellationTokenSource _shutdownTokenSource = new();
        private bool _disposed;
        private bool _started;

        /// <summary>
        /// Initializes a new session manager
        /// </summary>
        /// <param name="logger">Optional logger instance</param>
        public SessionManager(ILogger<SessionManager>? logger = null)
        {
            _logger = logger;
            DefaultSessionTimeout = TimeSpan.FromMinutes(30); // 30 minutes default session timeout
            DefaultRequestTimeout = TimeSpan.FromMinutes(5);  // 5 minutes default request timeout

            // Start cleanup timer (runs every minute)
            _cleanupTimer = new Timer(async _ => await CleanupTimedOutSessionsAsync(_shutdownTokenSource.Token),
                null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

            _logger?.LogInformation("Session manager initialized with timeouts: Session={SessionTimeout}, Request={RequestTimeout}",
                DefaultSessionTimeout, DefaultRequestTimeout);
        }

        /// <inheritdoc/>
        public event EventHandler<SessionEventArgs>? SessionCreated;

        /// <inheritdoc/>
        public event EventHandler<SessionEventArgs>? SessionTerminated;

        /// <inheritdoc/>
        public event EventHandler<SessionTimeoutEventArgs>? SessionTimeout;

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, AcpSession> ActiveSessions => _sessions.AsReadOnly();

        /// <inheritdoc/>
        public TimeSpan DefaultSessionTimeout { get; set; }

        /// <inheritdoc/>
        public TimeSpan DefaultRequestTimeout { get; set; }

        /// <inheritdoc/>
        public AcpSession CreateSession(string? sessionId = null)
        {
            ThrowIfDisposed();

            var session = new AcpSession(sessionId, null); // Session will create its own logger

            if (!_sessions.TryAdd(session.SessionId, session))
            {
                session.Dispose();
                throw new InvalidOperationException($"Session with ID '{session.SessionId}' already exists");
            }

            // Subscribe to session events
            session.StateChanged += OnSessionStateChanged;

            _logger?.LogInformation("Created session {SessionId} (Total sessions: {SessionCount})",
                session.SessionId, _sessions.Count);

            SessionCreated?.Invoke(this, new SessionEventArgs(session));

            return session;
        }

        /// <inheritdoc/>
        public AcpSession? GetSession(string sessionId)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(sessionId))
                return null;

            return _sessions.TryGetValue(sessionId, out var session) ? session : null;
        }

        /// <inheritdoc/>
        public bool TerminateSession(string sessionId)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(sessionId))
                return false;

            if (_sessions.TryRemove(sessionId, out var session))
            {
                _logger?.LogInformation("Terminating session {SessionId}", sessionId);

                session.StateChanged -= OnSessionStateChanged;
                session.Terminate();

                SessionTerminated?.Invoke(this, new SessionEventArgs(session));

                session.Dispose();
                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public async Task CleanupTimedOutSessionsAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed || cancellationToken.IsCancellationRequested)
                return;

            var sessionsToRemove = new List<AcpSession>();
            var now = DateTime.UtcNow;

            // Find timed-out sessions
            foreach (var kvp in _sessions)
            {
                var session = kvp.Value;

                // Check session timeout
                var sessionAge = now - session.CreatedAt;
                var sessionInactive = session.LastActivityAt.HasValue
                    ? now - session.LastActivityAt.Value
                    : sessionAge;

                bool sessionTimedOut = sessionInactive > DefaultSessionTimeout;

                // Check for terminated sessions
                bool sessionTerminated = session.IsTerminating;

                if (sessionTimedOut || sessionTerminated)
                {
                    sessionsToRemove.Add(session);
                    continue;
                }

                // Check request timeouts within the session
                try
                {
                    var timedOutRequests = session.CheckForTimeouts(DefaultRequestTimeout);
                    if (timedOutRequests > 0)
                    {
                        _logger?.LogInformation("Session {SessionId} had {Count} timed-out requests",
                            session.SessionId, timedOutRequests);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error checking timeouts for session {SessionId}", session.SessionId);
                }
            }

            // Remove timed-out and terminated sessions
            foreach (var session in sessionsToRemove)
            {
                if (_sessions.TryRemove(session.SessionId, out var removedSession))
                {
                    var reason = session.IsTerminating ? "terminated" : "timed out";
                    _logger?.LogInformation("Removing session {SessionId} ({Reason})", session.SessionId, reason);

                    removedSession.StateChanged -= OnSessionStateChanged;

                    if (!session.IsTerminating)
                    {
                        SessionTimeout?.Invoke(this, new SessionTimeoutEventArgs(removedSession, DefaultSessionTimeout));
                        removedSession.Shutdown();
                    }

                    SessionTerminated?.Invoke(this, new SessionEventArgs(removedSession));
                    removedSession.Dispose();
                }
            }

            if (sessionsToRemove.Count > 0)
            {
                _logger?.LogDebug("Cleaned up {Count} sessions (Remaining: {Remaining})",
                    sessionsToRemove.Count, _sessions.Count);
            }

            await Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (_started)
                return Task.CompletedTask;

            _started = true;
            _logger?.LogInformation("Session manager started");

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed || !_started)
                return;

            _logger?.LogInformation("Stopping session manager and terminating {Count} sessions", _sessions.Count);

            // Signal shutdown
            _shutdownTokenSource.Cancel();

            // Terminate all sessions synchronously
            var sessionIds = _sessions.Keys.ToList();
            foreach (var sessionId in sessionIds)
            {
                TerminateSession(sessionId);
            }

            // Wait for all sessions to be removed
            var maxWait = TimeSpan.FromSeconds(5);
            var start = DateTime.UtcNow;
            while (_sessions.Count > 0 && DateTime.UtcNow - start < maxWait)
            {
                await Task.Delay(10, cancellationToken);
            }

            _started = false;
            _logger?.LogInformation("Session manager stopped");
        }

        private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs e)
        {
            if (sender is AcpSession session)
            {
                _logger?.LogDebug("Session {SessionId} state changed: {OldState} -> {NewState}",
                    e.SessionId, e.OldState, e.NewState);

                // Automatically remove terminated or faulted sessions
                if (e.NewState == SessionState.Terminated || e.NewState == SessionState.Faulted)
                {
                    // Remove immediately without delay
                    if (_sessions.TryRemove(e.SessionId, out var removedSession))
                    {
                        _logger?.LogDebug("Automatically removed terminated session {SessionId}", e.SessionId);
                        removedSession.StateChanged -= OnSessionStateChanged;
                        SessionTerminated?.Invoke(this, new SessionEventArgs(removedSession));
                    }
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SessionManager));
        }

        /// <summary>
        /// Disposes the session manager and all sessions
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
                _logger?.LogInformation("Disposing session manager");

                // Stop the manager synchronously
                try
                {
                    StopAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error stopping session manager during disposal");
                }

                // Dispose timer
                _cleanupTimer?.Dispose();

                // Dispose shutdown token source
                _shutdownTokenSource?.Dispose();

                _disposed = true;
            }
        }
    }
}