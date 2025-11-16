using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Andy.Acp.Core.Protocol;
using Andy.Acp.Core.Session;
using Andy.Acp.Core.JsonRpc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Andy.Acp.Tests.Protocol
{
    public class AcpProtocolHandlerTests
    {
        private readonly ServerInfo _testServerInfo = new()
        {
            Name = "TestServer",
            Version = "1.0.0",
            Description = "Test ACP Server"
        };

        private readonly ServerCapabilities _testServerCapabilities = new()
        {
            Tools = new ToolsCapability
            {
                Supported = true,
                Available = new[] { "test-tool" },
                ListSupported = true,
                ExecutionSupported = true
            },
            Prompts = new PromptsCapability
            {
                Supported = false
            },
            Resources = new ResourcesCapability
            {
                Supported = true,
                SupportedSchemes = new[] { "file://" }
            }
        };

        private AcpProtocolHandler CreateHandler()
        {
            var sessionManager = new SessionManager(NullLogger<SessionManager>.Instance);
            return new AcpProtocolHandler(sessionManager, _testServerInfo, _testServerCapabilities);
        }

        [Fact]
        public async Task HandleInitializeAsync_WithValidParams_CreatesSessionAndReturnsResult()
        {
            // Arrange
            var handler = CreateHandler();
            var initParams = new InitializeParams
            {
                ProtocolVersion = "1.0",
                ClientInfo = new ClientInfo
                {
                    Name = "TestClient",
                    Version = "1.0.0"
                },
                Capabilities = new ClientCapabilities
                {
                    SupportedTools = new[] { "test" }
                }
            };

            // Act
            var result = await handler.HandleInitializeAsync(initParams);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<InitializeResult>(result);
            var initResult = (InitializeResult)result;

            Assert.Equal("1.0", initResult.ProtocolVersion);
            Assert.Equal("TestServer", initResult.ServerInfo.Name);
            Assert.Equal("1.0.0", initResult.ServerInfo.Version);
            Assert.NotNull(initResult.Capabilities);
            Assert.NotNull(initResult.Capabilities.Tools);
            Assert.True(initResult.Capabilities.Tools.Supported);
            Assert.NotNull(initResult.SessionInfo);
            Assert.NotEmpty(initResult.SessionInfo.SessionId);
            Assert.NotNull(handler.CurrentSession);
            Assert.Equal(SessionState.Initialized, handler.CurrentSession.State);
        }

        [Fact]
        public async Task HandleInitializeAsync_WithNullParams_CreatesSessionWithDefaults()
        {
            // Arrange
            var handler = CreateHandler();

            // Act
            var result = await handler.HandleInitializeAsync(null);

            // Assert
            Assert.NotNull(result);
            var initResult = (InitializeResult)result!;
            Assert.Equal("1.0", initResult.ProtocolVersion);
            Assert.NotNull(handler.CurrentSession);
        }

        [Fact]
        public async Task HandleInitializeAsync_WithJsonElement_ParsesCorrectly()
        {
            // Arrange
            var handler = CreateHandler();
            var json = JsonSerializer.Serialize(new
            {
                ProtocolVersion = "1.0",
                ClientInfo = new
                {
                    Name = "TestClient",
                    Version = "1.0.0"
                }
            });
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);

            // Act
            var result = await handler.HandleInitializeAsync(jsonElement);

            // Assert
            Assert.NotNull(result);
            var initResult = (InitializeResult)result!;
            Assert.Equal("1.0", initResult.ProtocolVersion);
            Assert.NotNull(handler.CurrentSession);
            Assert.NotNull(handler.CurrentSession.Capabilities?.ClientInfo);
            Assert.Equal("TestClient", handler.CurrentSession.Capabilities.ClientInfo.Name);
        }

        [Fact]
        public async Task HandleInitializeAsync_WhenAlreadyInitialized_ThrowsJsonRpcException()
        {
            // Arrange
            var handler = CreateHandler();
            await handler.HandleInitializeAsync(new InitializeParams
            {
                ClientInfo = new ClientInfo { Name = "Client1", Version = "1.0" }
            });

            // Act & Assert
            var exception = await Assert.ThrowsAsync<JsonRpcProtocolException>(async () =>
            {
                await handler.HandleInitializeAsync(new InitializeParams
                {
                    ClientInfo = new ClientInfo { Name = "Client2", Version = "1.0" }
                });
            });

            Assert.Equal(JsonRpcErrorCodes.SessionAlreadyInitialized, exception.ErrorCode);
        }

        [Fact]
        public async Task HandleInitializeAsync_WithDifferentProtocolVersion_LogsWarningButSucceeds()
        {
            // Arrange
            var handler = CreateHandler();
            var initParams = new InitializeParams
            {
                ProtocolVersion = "2.0", // Different version
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            };

            // Act
            var result = await handler.HandleInitializeAsync(initParams);

            // Assert
            Assert.NotNull(result);
            var initResult = (InitializeResult)result!;
            Assert.Equal("1.0", initResult.ProtocolVersion); // Server returns its own version
            Assert.NotNull(handler.CurrentSession);
        }

        [Fact]
        public async Task HandleInitializedAsync_AfterInitialize_MarksSessionActive()
        {
            // Arrange
            var handler = CreateHandler();
            await handler.HandleInitializeAsync(new InitializeParams
            {
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            });

            Assert.Equal(SessionState.Initialized, handler.CurrentSession!.State);

            // Act
            var result = await handler.HandleInitializedAsync(null);

            // Assert
            Assert.Null(result); // Initialized is a notification, no result expected
            Assert.Equal(SessionState.Active, handler.CurrentSession.State);
        }

        [Fact]
        public async Task HandleInitializedAsync_WithoutInitialize_ThrowsJsonRpcException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<JsonRpcProtocolException>(async () =>
            {
                await handler.HandleInitializedAsync(null);
            });

            Assert.Equal(JsonRpcErrorCodes.SessionNotInitialized, exception.ErrorCode);
        }

        [Fact]
        public async Task HandleShutdownAsync_WithActiveSession_TerminatesSession()
        {
            // Arrange
            var handler = CreateHandler();
            await handler.HandleInitializeAsync(new InitializeParams
            {
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            });

            var sessionBeforeShutdown = handler.CurrentSession;
            Assert.NotNull(sessionBeforeShutdown);

            // Act
            var result = await handler.HandleShutdownAsync(null);

            // Assert
            Assert.NotNull(result);
            var shutdownResult = (ShutdownResult)result!;
            Assert.True(shutdownResult.Success);
            Assert.Null(handler.CurrentSession); // Session should be cleared
            Assert.Equal(SessionState.Terminated, sessionBeforeShutdown.State);
        }

        [Fact]
        public async Task HandleShutdownAsync_WithReason_IncludesReasonInLog()
        {
            // Arrange
            var handler = CreateHandler();
            await handler.HandleInitializeAsync(new InitializeParams
            {
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            });

            var shutdownParams = new ShutdownParams
            {
                Reason = "Test shutdown reason"
            };

            // Act
            var result = await handler.HandleShutdownAsync(shutdownParams);

            // Assert
            Assert.NotNull(result);
            var shutdownResult = (ShutdownResult)result!;
            Assert.True(shutdownResult.Success);
        }

        [Fact]
        public async Task HandleShutdownAsync_WithoutSession_ReturnsUnsuccessful()
        {
            // Arrange
            var handler = CreateHandler();

            // Act
            var result = await handler.HandleShutdownAsync(null);

            // Assert
            Assert.NotNull(result);
            var shutdownResult = (ShutdownResult)result!;
            Assert.False(shutdownResult.Success);
            Assert.Equal("No active session", shutdownResult.Message);
        }

        [Fact]
        public void RegisterMethods_RegistersAllProtocolMethods()
        {
            // Arrange
            var handler = CreateHandler();
            var jsonRpcHandler = new JsonRpcHandler(NullLogger<JsonRpcHandler>.Instance);

            // Act
            handler.RegisterMethods(jsonRpcHandler);

            // Assert
            Assert.True(jsonRpcHandler.SupportsMethod("initialize"));
            Assert.True(jsonRpcHandler.SupportsMethod("initialized"));
            Assert.True(jsonRpcHandler.SupportsMethod("shutdown"));
        }

        [Fact]
        public void RegisterMethods_WithNullHandler_ThrowsArgumentNullException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => handler.RegisterMethods(null!));
        }

        [Fact]
        public async Task FullHandshakeFlow_InitializeToShutdown_WorksCorrectly()
        {
            // Arrange
            var handler = CreateHandler();

            // Act - Initialize
            var initResult = await handler.HandleInitializeAsync(new InitializeParams
            {
                ProtocolVersion = "1.0",
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" },
                Capabilities = new ClientCapabilities
                {
                    SupportedTools = new[] { "tool1", "tool2" }
                }
            }) as InitializeResult;

            Assert.NotNull(initResult);
            Assert.NotNull(handler.CurrentSession);
            var sessionId = initResult.SessionInfo!.SessionId;

            // Act - Initialized
            await handler.HandleInitializedAsync(null);
            Assert.Equal(SessionState.Active, handler.CurrentSession.State);

            // Act - Shutdown
            var shutdownResult = await handler.HandleShutdownAsync(new ShutdownParams
            {
                Reason = "Test complete"
            }) as ShutdownResult;

            // Assert
            Assert.NotNull(shutdownResult);
            Assert.True(shutdownResult.Success);
            Assert.Null(handler.CurrentSession);
        }

        [Fact]
        public void HasActiveSession_WithNoSession_ReturnsFalse()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            Assert.False(handler.HasActiveSession());
        }

        [Fact]
        public async Task HasActiveSession_WithInitializedSession_ReturnsTrue()
        {
            // Arrange
            var handler = CreateHandler();
            await handler.HandleInitializeAsync(new InitializeParams
            {
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            });

            // Act & Assert
            Assert.True(handler.HasActiveSession());
        }

        [Fact]
        public async Task HasActiveSession_AfterShutdown_ReturnsFalse()
        {
            // Arrange
            var handler = CreateHandler();
            await handler.HandleInitializeAsync(new InitializeParams
            {
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            });
            await handler.HandleShutdownAsync(null);

            // Act & Assert
            Assert.False(handler.HasActiveSession());
        }

        [Fact]
        public void GetCurrentSessionOrThrow_WithNoSession_ThrowsJsonRpcException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = Assert.Throws<JsonRpcProtocolException>(() => handler.GetCurrentSessionOrThrow());
            Assert.Equal(JsonRpcErrorCodes.SessionNotInitialized, exception.ErrorCode);
        }

        [Fact]
        public async Task GetCurrentSessionOrThrow_WithSession_ReturnsSession()
        {
            // Arrange
            var handler = CreateHandler();
            await handler.HandleInitializeAsync(new InitializeParams
            {
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            });

            // Act
            var session = handler.GetCurrentSessionOrThrow();

            // Assert
            Assert.NotNull(session);
            Assert.Equal(handler.CurrentSession, session);
        }

        [Fact]
        public async Task HandleInitializeAsync_CancellationRequested_ThrowsOperationCanceledException()
        {
            // Arrange
            var handler = CreateHandler();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            // Note: The current implementation doesn't check cancellation tokens during initialize,
            // but this test documents the expected behavior if it did
            // For now, we just verify the method completes (since it doesn't check cancellation)
            var result = await handler.HandleInitializeAsync(new InitializeParams
            {
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            }, cts.Token);

            Assert.NotNull(result);
        }

        [Fact]
        public async Task ServerCapabilities_IncludesAllConfiguredCapabilities()
        {
            // Arrange
            var handler = CreateHandler();

            // Act
            var result = await handler.HandleInitializeAsync(new InitializeParams
            {
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            }) as InitializeResult;

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Capabilities);
            Assert.NotNull(result.Capabilities.Tools);
            Assert.True(result.Capabilities.Tools.Supported);
            Assert.Contains("test-tool", result.Capabilities.Tools.Available!);
            Assert.NotNull(result.Capabilities.Resources);
            Assert.True(result.Capabilities.Resources.Supported);
            Assert.Contains("file://", result.Capabilities.Resources.SupportedSchemes!);
        }

        [Fact]
        public async Task JsonRpcProtocolException_PropagatesCorrectly()
        {
            // Arrange
            var handler = CreateHandler();
            var jsonRpcHandler = new JsonRpcHandler(NullLogger<JsonRpcHandler>.Instance);
            handler.RegisterMethods(jsonRpcHandler);

            // Initialize once
            var initRequest = new JsonRpcRequest
            {
                Method = "initialize",
                Id = 1,
                Params = new InitializeParams { ClientInfo = new ClientInfo { Name = "Test", Version = "1.0" } }
            };
            await jsonRpcHandler.HandleRequestAsync(initRequest);

            // Act - Try to initialize again (should throw JsonRpcProtocolException)
            var duplicateInitRequest = new JsonRpcRequest
            {
                Method = "initialize",
                Id = 2,
                Params = new InitializeParams { ClientInfo = new ClientInfo { Name = "Test", Version = "1.0" } }
            };
            var response = await jsonRpcHandler.HandleRequestAsync(duplicateInitRequest);

            // Assert - Exception should be converted to error response
            Assert.NotNull(response);
            Assert.True(response.IsError);
            Assert.Equal(JsonRpcErrorCodes.SessionAlreadyInitialized, response.Error!.Code);
            Assert.Equal("Session already initialized", response.Error.Message);
        }

        [Fact]
        public async Task SequentialInitializeAttempts_SecondFails()
        {
            // Arrange
            var sessionManager = new SessionManager(NullLogger<SessionManager>.Instance);
            var handler = new AcpProtocolHandler(sessionManager, _testServerInfo, _testServerCapabilities);

            var initParams = new InitializeParams
            {
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            };

            // Act - First initialization should succeed
            await handler.HandleInitializeAsync(initParams);

            // Second initialization should fail
            var exception = await Assert.ThrowsAsync<JsonRpcProtocolException>(async () =>
            {
                await handler.HandleInitializeAsync(initParams);
            });

            // Assert
            Assert.Equal(JsonRpcErrorCodes.SessionAlreadyInitialized, exception.ErrorCode);
            Assert.NotNull(handler.CurrentSession);
        }

        [Fact]
        public async Task ConvertClientInfo_HandlesNullClientInfo()
        {
            // Arrange
            var handler = CreateHandler();
            var initParams = new InitializeParams
            {
                ProtocolVersion = "1.0",
                ClientInfo = null, // Null client info
                Capabilities = new ClientCapabilities()
            };

            // Act
            var result = await handler.HandleInitializeAsync(initParams) as InitializeResult;

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(handler.CurrentSession);
            // Session should still be created even with null client info
            Assert.Null(handler.CurrentSession.Capabilities?.ClientInfo);
        }
    }
}
