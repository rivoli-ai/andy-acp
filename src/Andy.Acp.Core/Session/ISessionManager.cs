using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Andy.Acp.Core.Session
{
    /// <summary>
    /// Interface for managing ACP sessions
    /// </summary>
    public interface ISessionManager : IDisposable
    {
        /// <summary>
        /// Event raised when a new session is created
        /// </summary>
        event EventHandler<SessionEventArgs>? SessionCreated;

        /// <summary>
        /// Event raised when a session is terminated
        /// </summary>
        event EventHandler<SessionEventArgs>? SessionTerminated;

        /// <summary>
        /// Event raised when a session times out
        /// </summary>
        event EventHandler<SessionTimeoutEventArgs>? SessionTimeout;

        /// <summary>
        /// Gets all active sessions
        /// </summary>
        IReadOnlyDictionary<string, AcpSession> ActiveSessions { get; }

        /// <summary>
        /// Default session timeout duration
        /// </summary>
        TimeSpan DefaultSessionTimeout { get; set; }

        /// <summary>
        /// Default request timeout duration
        /// </summary>
        TimeSpan DefaultRequestTimeout { get; set; }

        /// <summary>
        /// Creates a new session
        /// </summary>
        /// <param name="sessionId">Optional custom session ID</param>
        /// <returns>The created session</returns>
        AcpSession CreateSession(string? sessionId = null);

        /// <summary>
        /// Gets a session by ID
        /// </summary>
        /// <param name="sessionId">The session ID</param>
        /// <returns>The session if found, null otherwise</returns>
        AcpSession? GetSession(string sessionId);

        /// <summary>
        /// Terminates a session
        /// </summary>
        /// <param name="sessionId">The session ID to terminate</param>
        /// <returns>True if the session was found and terminated, false otherwise</returns>
        bool TerminateSession(string sessionId);

        /// <summary>
        /// Checks for timed-out sessions and requests
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the cleanup operation</returns>
        Task CleanupTimedOutSessionsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts the session manager background services
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the background services</returns>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops the session manager and terminates all sessions
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the shutdown operation</returns>
        Task StopAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Event args for session events
    /// </summary>
    public class SessionEventArgs : EventArgs
    {
        public SessionEventArgs(AcpSession session)
        {
            Session = session;
        }

        public AcpSession Session { get; }
    }

    /// <summary>
    /// Event args for session timeout events
    /// </summary>
    public class SessionTimeoutEventArgs : EventArgs
    {
        public SessionTimeoutEventArgs(AcpSession session, TimeSpan timeout)
        {
            Session = session;
            Timeout = timeout;
        }

        public AcpSession Session { get; }
        public TimeSpan Timeout { get; }
    }
}