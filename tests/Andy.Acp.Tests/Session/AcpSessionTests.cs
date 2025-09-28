using System;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Session;
using Andy.Acp.Core.JsonRpc;
using Xunit;

namespace Andy.Acp.Tests.Session
{
    public class AcpSessionTests : IDisposable
    {
        private readonly AcpSession _session;

        public AcpSessionTests()
        {
            _session = new AcpSession();
        }

        public void Dispose()
        {
            _session?.Dispose();
        }

        [Fact]
        public void Constructor_GeneratesUniqueSessionId()
        {
            // Arrange & Act
            using var session1 = new AcpSession();
            using var session2 = new AcpSession();

            // Assert
            Assert.NotNull(session1.SessionId);
            Assert.NotNull(session2.SessionId);
            Assert.NotEqual(session1.SessionId, session2.SessionId);
        }

        [Fact]
        public void Constructor_WithCustomSessionId_UsesProvidedId()
        {
            // Arrange
            var customId = "custom-session-123";

            // Act
            using var session = new AcpSession(customId);

            // Assert
            Assert.Equal(customId, session.SessionId);
        }

        [Fact]
        public void InitialState_IsCreated()
        {
            // Assert
            Assert.Equal(SessionState.Created, _session.State);
            Assert.False(_session.IsHealthy);
            Assert.False(_session.IsTerminating);
        }

        [Fact]
        public void Initialize_WithValidCapabilities_ReturnsTrue()
        {
            // Arrange
            var capabilities = new ClientCapabilities
            {
                ClientInfo = new ClientInfo { Name = "Test Client", Version = "1.0.0" }
            };

            // Act
            var result = _session.Initialize(capabilities);

            // Assert
            Assert.True(result);
            Assert.Equal(SessionState.Initialized, _session.State);
            Assert.Equal(capabilities, _session.Capabilities);
            Assert.True(_session.IsHealthy);
        }

        [Fact]
        public void Initialize_WithNullCapabilities_ReturnsFalseAndFaults()
        {
            // Act
            var result = _session.Initialize(null!);

            // Assert
            Assert.False(result);
            Assert.Equal(SessionState.Faulted, _session.State);
        }

        [Fact]
        public void Initialize_WhenAlreadyInitialized_ReturnsFalse()
        {
            // Arrange
            var capabilities = new ClientCapabilities
            {
                ClientInfo = new ClientInfo { Name = "Test Client", Version = "1.0.0" }
            };
            _session.Initialize(capabilities);

            // Act
            var result = _session.Initialize(capabilities);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void MarkActive_WhenInitialized_ChangesToActive()
        {
            // Arrange
            var capabilities = new ClientCapabilities
            {
                ClientInfo = new ClientInfo { Name = "Test Client", Version = "1.0.0" }
            };
            _session.Initialize(capabilities);

            // Act
            _session.MarkActive();

            // Assert
            Assert.Equal(SessionState.Active, _session.State);
            Assert.NotNull(_session.LastActivityAt);
            Assert.True(_session.IsHealthy);
        }

        [Fact]
        public void MarkActive_UpdatesLastActivityTime()
        {
            // Arrange
            var capabilities = new ClientCapabilities
            {
                ClientInfo = new ClientInfo { Name = "Test Client", Version = "1.0.0" }
            };
            _session.Initialize(capabilities);
            var initialTime = _session.LastActivityAt;

            // Act
            Thread.Sleep(10); // Small delay to ensure time difference
            _session.MarkActive();

            // Assert
            Assert.NotEqual(initialTime, _session.LastActivityAt);
        }

        [Fact]
        public void AddPendingRequest_WithValidRequest_TracksRequest()
        {
            // Arrange
            var request = new JsonRpcRequest { Method = "test", Id = 123 };

            // Act
            _session.AddPendingRequest(request);

            // Assert
            Assert.Single(_session.PendingRequests);
            Assert.True(_session.PendingRequests.ContainsKey(123));
            Assert.Equal("test", _session.PendingRequests[123].Method);
        }

        [Fact]
        public void AddPendingRequest_WithNotification_DoesNotTrack()
        {
            // Arrange
            var notification = new JsonRpcRequest { Method = "notify" }; // No Id = notification

            // Act
            _session.AddPendingRequest(notification);

            // Assert
            Assert.Empty(_session.PendingRequests);
        }

        [Fact]
        public void CompletePendingRequest_WithExistingRequest_ReturnsTrue()
        {
            // Arrange
            var request = new JsonRpcRequest { Method = "test", Id = 123 };
            _session.AddPendingRequest(request);

            // Act
            var result = _session.CompletePendingRequest(123);

            // Assert
            Assert.True(result);
            Assert.Empty(_session.PendingRequests);
        }

        [Fact]
        public void CompletePendingRequest_WithNonExistentRequest_ReturnsFalse()
        {
            // Act
            var result = _session.CompletePendingRequest(999);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void SetContextValue_StoresValue()
        {
            // Act
            _session.SetContextValue("test.key", "test value");

            // Assert
            Assert.Equal("test value", _session.GetContextValue<string>("test.key"));
        }

        [Fact]
        public void SetContextValue_WithNullKey_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => _session.SetContextValue(null!, "value"));
            Assert.Throws<ArgumentException>(() => _session.SetContextValue("", "value"));
            Assert.Throws<ArgumentException>(() => _session.SetContextValue("   ", "value"));
        }

        [Fact]
        public void GetContextValue_WithNonExistentKey_ReturnsDefault()
        {
            // Act
            var result = _session.GetContextValue<string>("nonexistent");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetContextValue_WithWrongType_ReturnsDefault()
        {
            // Arrange
            _session.SetContextValue("test.key", "string value");

            // Act
            var result = _session.GetContextValue<int>("test.key");

            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public void RemoveContextValue_WithExistingKey_ReturnsTrue()
        {
            // Arrange
            _session.SetContextValue("test.key", "value");

            // Act
            var result = _session.RemoveContextValue("test.key");

            // Assert
            Assert.True(result);
            Assert.Null(_session.GetContextValue<string>("test.key"));
        }

        [Fact]
        public void RemoveContextValue_WithNonExistentKey_ReturnsFalse()
        {
            // Act
            var result = _session.RemoveContextValue("nonexistent");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void CheckForTimeouts_WithTimedOutRequests_RemovesAndReturnsCount()
        {
            // Arrange
            var request1 = new JsonRpcRequest { Method = "test1", Id = 1 };
            var request2 = new JsonRpcRequest { Method = "test2", Id = 2 };

            _session.AddPendingRequest(request1);
            _session.AddPendingRequest(request2);

            // Act
            var timeoutCount = _session.CheckForTimeouts(TimeSpan.FromMilliseconds(-1)); // Negative timeout = everything times out

            // Assert
            Assert.Equal(2, timeoutCount);
            Assert.Empty(_session.PendingRequests);
        }

        [Fact]
        public void CheckForTimeouts_WithRecentRequests_ReturnsZero()
        {
            // Arrange
            var request = new JsonRpcRequest { Method = "test", Id = 1 };
            _session.AddPendingRequest(request);

            // Act
            var timeoutCount = _session.CheckForTimeouts(TimeSpan.FromHours(1)); // 1 hour timeout

            // Assert
            Assert.Equal(0, timeoutCount);
            Assert.Single(_session.PendingRequests);
        }

        [Fact]
        public void Shutdown_ChangesToShuttingDownState()
        {
            // Act
            _session.Shutdown();

            // Assert
            Assert.Equal(SessionState.ShuttingDown, _session.State);
            Assert.True(_session.IsTerminating);
            Assert.True(_session.CancellationToken.IsCancellationRequested);
        }

        [Fact]
        public void Terminate_ChangesToTerminatedState()
        {
            // Act
            _session.Terminate();

            // Assert
            Assert.Equal(SessionState.Terminated, _session.State);
            Assert.True(_session.IsTerminating);
            Assert.True(_session.CancellationToken.IsCancellationRequested);
        }

        [Fact]
        public void Fault_WithException_ChangesToFaultedState()
        {
            // Arrange
            var exception = new InvalidOperationException("Test error");

            // Act
            _session.Fault(exception);

            // Assert
            Assert.Equal(SessionState.Faulted, _session.State);
            Assert.True(_session.IsTerminating);
            Assert.True(_session.CancellationToken.IsCancellationRequested);
        }

        [Fact]
        public void StateChanged_Event_IsFired()
        {
            // Arrange
            var stateChanges = new List<SessionStateChangedEventArgs>();
            _session.StateChanged += (sender, args) => stateChanges.Add(args);

            var capabilities = new ClientCapabilities
            {
                ClientInfo = new ClientInfo { Name = "Test Client", Version = "1.0.0" }
            };

            // Act
            _session.Initialize(capabilities);

            // Assert
            Assert.Equal(2, stateChanges.Count); // Created -> Initializing -> Initialized

            var firstChange = stateChanges[0];
            Assert.Equal(_session.SessionId, firstChange.SessionId);
            Assert.Equal(SessionState.Created, firstChange.OldState);
            Assert.Equal(SessionState.Initializing, firstChange.NewState);

            var secondChange = stateChanges[1];
            Assert.Equal(_session.SessionId, secondChange.SessionId);
            Assert.Equal(SessionState.Initializing, secondChange.OldState);
            Assert.Equal(SessionState.Initialized, secondChange.NewState);
        }

        [Fact]
        public void Context_IsReadOnly()
        {
            // Arrange
            _session.SetContextValue("test", "value");

            // Act
            var context = _session.Context;

            // Assert
            Assert.Single(context);
            Assert.Equal("value", context["test"]);

            // Verify it's read-only by checking type
            Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(context);
        }

        [Fact]
        public void CreatedAt_IsSetCorrectly()
        {
            // Arrange
            var beforeCreation = DateTime.UtcNow.AddSeconds(-1);
            var afterCreation = DateTime.UtcNow.AddSeconds(1);

            // Act
            using var session = new AcpSession();

            // Assert
            Assert.True(session.CreatedAt > beforeCreation);
            Assert.True(session.CreatedAt < afterCreation);
        }

        [Fact]
        public void Dispose_TerminatesSession()
        {
            // Arrange
            var capabilities = new ClientCapabilities
            {
                ClientInfo = new ClientInfo { Name = "Test Client", Version = "1.0.0" }
            };
            _session.Initialize(capabilities);

            // Act
            _session.Dispose();

            // Assert
            Assert.Equal(SessionState.Terminated, _session.State);
        }

        [Fact]
        public void Dispose_ThrowsObjectDisposedExceptionOnSubsequentOperations()
        {
            // Arrange
            _session.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => _session.Initialize(new ClientCapabilities()));
            Assert.Throws<ObjectDisposedException>(() => _session.MarkActive());
            Assert.Throws<ObjectDisposedException>(() => _session.SetContextValue("key", "value"));
        }
    }
}