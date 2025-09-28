using System;
using Andy.Acp.Core.Session;
using Xunit;

namespace Andy.Acp.Tests.Session
{
    public class SessionStateTests
    {
        [Fact]
        public void ClientCapabilities_CanBeInitialized()
        {
            // Act
            var capabilities = new ClientCapabilities
            {
                ClientInfo = new ClientInfo
                {
                    Name = "Test Client",
                    Version = "1.0.0",
                    Description = "Test Description",
                    Homepage = "https://example.com",
                    Contact = "test@example.com"
                },
                SupportedTools = new[] { "tool1", "tool2" },
                SupportedResources = new[] { "file://", "http://" },
                MaxConcurrentTools = 5,
                ToolTimeoutMs = 30000,
                Extensions = new Dictionary<string, object>
                {
                    { "custom.feature", true },
                    { "custom.limit", 100 }
                }
            };

            // Assert
            Assert.NotNull(capabilities.ClientInfo);
            Assert.Equal("Test Client", capabilities.ClientInfo.Name);
            Assert.Equal("1.0.0", capabilities.ClientInfo.Version);
            Assert.Equal("Test Description", capabilities.ClientInfo.Description);
            Assert.Equal("https://example.com", capabilities.ClientInfo.Homepage);
            Assert.Equal("test@example.com", capabilities.ClientInfo.Contact);

            Assert.Equal(2, capabilities.SupportedTools!.Length);
            Assert.Contains("tool1", capabilities.SupportedTools);
            Assert.Contains("tool2", capabilities.SupportedTools);

            Assert.Equal(2, capabilities.SupportedResources!.Length);
            Assert.Contains("file://", capabilities.SupportedResources);
            Assert.Contains("http://", capabilities.SupportedResources);

            Assert.Equal(5, capabilities.MaxConcurrentTools);
            Assert.Equal(30000, capabilities.ToolTimeoutMs);

            Assert.Equal(2, capabilities.Extensions!.Count);
            Assert.Equal(true, capabilities.Extensions["custom.feature"]);
            Assert.Equal(100, capabilities.Extensions["custom.limit"]);
        }

        [Fact]
        public void ClientInfo_RequiredProperties_CanBeSet()
        {
            // Act
            var clientInfo = new ClientInfo
            {
                Name = "Required Name",
                Version = "Required Version"
            };

            // Assert
            Assert.Equal("Required Name", clientInfo.Name);
            Assert.Equal("Required Version", clientInfo.Version);
            Assert.Null(clientInfo.Description);
            Assert.Null(clientInfo.Homepage);
            Assert.Null(clientInfo.Contact);
        }

        [Fact]
        public void PendingRequest_Constructor_InitializesCorrectly()
        {
            // Arrange
            var id = "test-id";
            var method = "test.method";
            var timestamp = DateTime.UtcNow;

            // Act
            var pendingRequest = new PendingRequest(id, method, timestamp);

            // Assert
            Assert.Equal(id, pendingRequest.Id);
            Assert.Equal(method, pendingRequest.Method);
            Assert.Equal(timestamp, pendingRequest.Timestamp);
            Assert.NotNull(pendingRequest.CancellationSource);
            Assert.False(pendingRequest.CancellationSource.Token.IsCancellationRequested);
        }

        [Fact]
        public void PendingRequest_IsTimedOut_WithOldTimestamp_ReturnsTrue()
        {
            // Arrange
            var oldTimestamp = DateTime.UtcNow.AddMinutes(-10);
            var pendingRequest = new PendingRequest("id", "method", oldTimestamp);
            var timeout = TimeSpan.FromMinutes(5);

            // Act
            var isTimedOut = pendingRequest.IsTimedOut(timeout);

            // Assert
            Assert.True(isTimedOut);
        }

        [Fact]
        public void PendingRequest_IsTimedOut_WithRecentTimestamp_ReturnsFalse()
        {
            // Arrange
            var recentTimestamp = DateTime.UtcNow.AddSeconds(-10);
            var pendingRequest = new PendingRequest("id", "method", recentTimestamp);
            var timeout = TimeSpan.FromMinutes(5);

            // Act
            var isTimedOut = pendingRequest.IsTimedOut(timeout);

            // Assert
            Assert.False(isTimedOut);
        }

        [Fact]
        public void PendingRequest_Dispose_DisposesResources()
        {
            // Arrange
            var pendingRequest = new PendingRequest("id", "method", DateTime.UtcNow);

            // Act
            pendingRequest.Dispose();

            // Assert
            Assert.Throws<ObjectDisposedException>(() => pendingRequest.CancellationSource.Token);
        }

        [Theory]
        [InlineData(SessionState.Created)]
        [InlineData(SessionState.Initializing)]
        [InlineData(SessionState.Initialized)]
        [InlineData(SessionState.Active)]
        [InlineData(SessionState.ShuttingDown)]
        [InlineData(SessionState.Terminated)]
        [InlineData(SessionState.Faulted)]
        public void SessionState_AllValues_AreValid(SessionState state)
        {
            // Act & Assert
            Assert.True(Enum.IsDefined(typeof(SessionState), state));
        }

        [Fact]
        public void ClientCapabilities_DefaultValues_AreNull()
        {
            // Act
            var capabilities = new ClientCapabilities();

            // Assert
            Assert.Null(capabilities.ClientInfo);
            Assert.Null(capabilities.SupportedTools);
            Assert.Null(capabilities.SupportedResources);
            Assert.Null(capabilities.MaxConcurrentTools);
            Assert.Null(capabilities.ToolTimeoutMs);
            Assert.Null(capabilities.Extensions);
        }

        [Fact]
        public void PendingRequest_WithVariousIdTypes_WorksCorrectly()
        {
            // Arrange & Act
            var stringRequest = new PendingRequest("string-id", "method", DateTime.UtcNow);
            var intRequest = new PendingRequest(123, "method", DateTime.UtcNow);
            var guidRequest = new PendingRequest(Guid.NewGuid(), "method", DateTime.UtcNow);

            // Assert
            Assert.Equal("string-id", stringRequest.Id);
            Assert.Equal(123, intRequest.Id);
            Assert.IsType<Guid>(guidRequest.Id);
        }

        [Fact]
        public void ClientCapabilities_WithEmptyCollections_WorksCorrectly()
        {
            // Act
            var capabilities = new ClientCapabilities
            {
                SupportedTools = Array.Empty<string>(),
                SupportedResources = Array.Empty<string>(),
                Extensions = new Dictionary<string, object>()
            };

            // Assert
            Assert.Empty(capabilities.SupportedTools);
            Assert.Empty(capabilities.SupportedResources);
            Assert.Empty(capabilities.Extensions);
        }
    }
}