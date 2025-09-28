using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Session;
using Xunit;

namespace Andy.Acp.Tests.Session
{
    public class SessionManagerTests : IDisposable
    {
        private readonly SessionManager _sessionManager;

        public SessionManagerTests()
        {
            _sessionManager = new SessionManager();
        }

        public void Dispose()
        {
            _sessionManager?.Dispose();
        }

        [Fact]
        public void Constructor_SetsDefaultTimeouts()
        {
            // Assert
            Assert.Equal(TimeSpan.FromMinutes(30), _sessionManager.DefaultSessionTimeout);
            Assert.Equal(TimeSpan.FromMinutes(5), _sessionManager.DefaultRequestTimeout);
        }

        [Fact]
        public void CreateSession_ReturnsNewSession()
        {
            // Act
            var session = _sessionManager.CreateSession();

            // Assert
            Assert.NotNull(session);
            Assert.NotNull(session.SessionId);
            Assert.Equal(SessionState.Created, session.State);
            Assert.Single(_sessionManager.ActiveSessions);
        }

        [Fact]
        public void CreateSession_WithCustomId_UsesProvidedId()
        {
            // Arrange
            var customId = "custom-session-123";

            // Act
            var session = _sessionManager.CreateSession(customId);

            // Assert
            Assert.Equal(customId, session.SessionId);
            Assert.Single(_sessionManager.ActiveSessions);
            Assert.True(_sessionManager.ActiveSessions.ContainsKey(customId));
        }

        [Fact]
        public void CreateSession_WithDuplicateId_ThrowsInvalidOperationException()
        {
            // Arrange
            var sessionId = "duplicate-id";
            _sessionManager.CreateSession(sessionId);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => _sessionManager.CreateSession(sessionId));
        }

        [Fact]
        public void CreateSession_FiresSessionCreatedEvent()
        {
            // Arrange
            SessionEventArgs? eventArgs = null;
            _sessionManager.SessionCreated += (sender, args) => eventArgs = args;

            // Act
            var session = _sessionManager.CreateSession();

            // Assert
            Assert.NotNull(eventArgs);
            Assert.Equal(session, eventArgs.Session);
        }

        [Fact]
        public void GetSession_WithExistingId_ReturnsSession()
        {
            // Arrange
            var session = _sessionManager.CreateSession();

            // Act
            var retrievedSession = _sessionManager.GetSession(session.SessionId);

            // Assert
            Assert.Equal(session, retrievedSession);
        }

        [Fact]
        public void GetSession_WithNonExistentId_ReturnsNull()
        {
            // Act
            var session = _sessionManager.GetSession("nonexistent");

            // Assert
            Assert.Null(session);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void GetSession_WithInvalidId_ReturnsNull(string? sessionId)
        {
            // Act
            var session = _sessionManager.GetSession(sessionId!);

            // Assert
            Assert.Null(session);
        }

        [Fact]
        public void TerminateSession_WithExistingSession_ReturnsTrue()
        {
            // Arrange
            var session = _sessionManager.CreateSession();
            var sessionId = session.SessionId;

            // Act
            var result = _sessionManager.TerminateSession(sessionId);

            // Assert
            Assert.True(result);
            Assert.Empty(_sessionManager.ActiveSessions);
            Assert.Equal(SessionState.Terminated, session.State);
        }

        [Fact]
        public void TerminateSession_WithNonExistentSession_ReturnsFalse()
        {
            // Act
            var result = _sessionManager.TerminateSession("nonexistent");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void TerminateSession_FiresSessionTerminatedEvent()
        {
            // Arrange
            var session = _sessionManager.CreateSession();
            SessionEventArgs? eventArgs = null;
            _sessionManager.SessionTerminated += (sender, args) => eventArgs = args;

            // Act
            _sessionManager.TerminateSession(session.SessionId);

            // Assert
            Assert.NotNull(eventArgs);
            Assert.Equal(session, eventArgs.Session);
        }

        [Fact]
        public async Task CleanupTimedOutSessionsAsync_WithTimedOutSession_RemovesSession()
        {
            // Arrange
            _sessionManager.DefaultSessionTimeout = TimeSpan.FromMilliseconds(1);
            var session = _sessionManager.CreateSession();

            // Wait for session to timeout
            await Task.Delay(10);

            // Act
            await _sessionManager.CleanupTimedOutSessionsAsync();

            // Assert
            Assert.Empty(_sessionManager.ActiveSessions);
        }

        [Fact]
        public async Task CleanupTimedOutSessionsAsync_WithActiveSession_KeepsSession()
        {
            // Arrange
            _sessionManager.DefaultSessionTimeout = TimeSpan.FromHours(1);
            var session = _sessionManager.CreateSession();
            session.MarkActive(); // Mark as recently active

            // Act
            await _sessionManager.CleanupTimedOutSessionsAsync();

            // Assert
            Assert.Single(_sessionManager.ActiveSessions);
        }

        [Fact]
        public async Task CleanupTimedOutSessionsAsync_WithTerminatedSession_RemovesSession()
        {
            // Arrange
            var session = _sessionManager.CreateSession();
            session.Terminate();

            // Act
            await _sessionManager.CleanupTimedOutSessionsAsync();

            // Assert
            Assert.Empty(_sessionManager.ActiveSessions);
        }

        [Fact]
        public async Task StartAsync_CanBeCalledMultipleTimes()
        {
            // Act & Assert
            await _sessionManager.StartAsync();
            await _sessionManager.StartAsync(); // Should not throw
        }

        [Fact]
        public async Task StopAsync_TerminatesAllSessions()
        {
            // Arrange
            var session1 = _sessionManager.CreateSession();
            var session2 = _sessionManager.CreateSession();
            Assert.Equal(2, _sessionManager.ActiveSessions.Count); // Verify setup
            await _sessionManager.StartAsync(); // Need to start before stopping

            // Act
            await _sessionManager.StopAsync();

            // Assert - sessions should be terminated and removed from active sessions
            Assert.Empty(_sessionManager.ActiveSessions);
            Assert.Equal(SessionState.Terminated, session1.State);
            Assert.Equal(SessionState.Terminated, session2.State);
        }

        [Fact]
        public async Task StopAsync_AfterStart_CompletesSuccessfully()
        {
            // Arrange
            await _sessionManager.StartAsync();

            // Act & Assert
            await _sessionManager.StopAsync(); // Should complete without hanging
        }

        [Fact]
        public void ActiveSessions_IsReadOnly()
        {
            // Arrange
            _sessionManager.CreateSession();

            // Act
            var activeSessions = _sessionManager.ActiveSessions;

            // Assert
            Assert.Single(activeSessions);
            Assert.IsAssignableFrom<IReadOnlyDictionary<string, AcpSession>>(activeSessions);
        }

        [Fact]
        public void DefaultTimeouts_CanBeModified()
        {
            // Arrange
            var newSessionTimeout = TimeSpan.FromMinutes(60);
            var newRequestTimeout = TimeSpan.FromMinutes(10);

            // Act
            _sessionManager.DefaultSessionTimeout = newSessionTimeout;
            _sessionManager.DefaultRequestTimeout = newRequestTimeout;

            // Assert
            Assert.Equal(newSessionTimeout, _sessionManager.DefaultSessionTimeout);
            Assert.Equal(newRequestTimeout, _sessionManager.DefaultRequestTimeout);
        }

        [Fact]
        public async Task SessionTimeout_Event_IsFired()
        {
            // Arrange
            _sessionManager.DefaultSessionTimeout = TimeSpan.FromMilliseconds(1);
            SessionTimeoutEventArgs? eventArgs = null;
            _sessionManager.SessionTimeout += (sender, args) => eventArgs = args;

            var session = _sessionManager.CreateSession();

            // Act
            await Task.Delay(50); // Wait for timeout
            await _sessionManager.CleanupTimedOutSessionsAsync();

            // Assert
            Assert.NotNull(eventArgs);
            Assert.Equal(session, eventArgs.Session);
            Assert.Equal(_sessionManager.DefaultSessionTimeout, eventArgs.Timeout);
        }

        [Fact]
        public async Task Dispose_TerminatesAllSessions()
        {
            // Arrange
            var session1 = _sessionManager.CreateSession();
            var session2 = _sessionManager.CreateSession();
            await _sessionManager.StartAsync(); // Need to start before stopping

            // Act
            _sessionManager.Dispose();

            // Small delay to allow disposal to complete
            await Task.Delay(100);

            // Assert
            Assert.Equal(SessionState.Terminated, session1.State);
            Assert.Equal(SessionState.Terminated, session2.State);
        }

        [Fact]
        public void Dispose_ThrowsObjectDisposedExceptionOnSubsequentOperations()
        {
            // Arrange
            _sessionManager.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => _sessionManager.CreateSession());
            Assert.Throws<ObjectDisposedException>(() => _sessionManager.GetSession("test"));
            Assert.Throws<ObjectDisposedException>(() => _sessionManager.TerminateSession("test"));
        }

        [Fact]
        public async Task AutomaticCleanup_RemovesTerminatedSessions()
        {
            // Arrange
            var session = _sessionManager.CreateSession();

            // Act
            session.Terminate();

            // Wait a bit for automatic cleanup
            await Task.Delay(200);

            // Assert
            Assert.Empty(_sessionManager.ActiveSessions);
        }

        [Fact]
        public async Task AutomaticCleanup_RemovesFaultedSessions()
        {
            // Arrange
            var session = _sessionManager.CreateSession();

            // Act
            session.Fault(new InvalidOperationException("Test error"));

            // Wait a bit for automatic cleanup
            await Task.Delay(200);

            // Assert
            Assert.Empty(_sessionManager.ActiveSessions);
        }

        [Fact]
        public void CreateMultipleSessions_AllAreTracked()
        {
            // Act
            var session1 = _sessionManager.CreateSession();
            var session2 = _sessionManager.CreateSession();
            var session3 = _sessionManager.CreateSession();

            // Assert
            Assert.Equal(3, _sessionManager.ActiveSessions.Count);
            Assert.Contains(session1.SessionId, _sessionManager.ActiveSessions.Keys);
            Assert.Contains(session2.SessionId, _sessionManager.ActiveSessions.Keys);
            Assert.Contains(session3.SessionId, _sessionManager.ActiveSessions.Keys);
        }
    }
}