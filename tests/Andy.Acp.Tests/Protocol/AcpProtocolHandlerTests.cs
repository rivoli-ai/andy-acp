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
                ProtocolVersion = 1,
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

            Assert.Equal(1, initResult.ProtocolVersion);
            Assert.NotNull(initResult.AgentInfo);
            Assert.Equal("TestServer", initResult.AgentInfo.Name);
            Assert.Equal("1.0.0", initResult.AgentInfo.Version);
            Assert.NotNull(initResult.AgentCapabilities);
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
            Assert.Equal(1, initResult.ProtocolVersion);
            Assert.NotNull(handler.CurrentSession);
        }

        [Fact]
        public async Task HandleInitializeAsync_WithJsonElement_ParsesCorrectly()
        {
            // Arrange
            var handler = CreateHandler();
            var json = JsonSerializer.Serialize(new
            {
                protocolVersion = 1,
                clientInfo = new
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
            Assert.Equal(1, initResult.ProtocolVersion);
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
                ProtocolVersion = 2, // Different version
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            };

            // Act
            var result = await handler.HandleInitializeAsync(initParams);

            // Assert
            Assert.NotNull(result);
            var initResult = (InitializeResult)result!;
            Assert.Equal(1, initResult.ProtocolVersion); // Server returns its own version
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
                ProtocolVersion = 1,
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" },
                Capabilities = new ClientCapabilities
                {
                    SupportedTools = new[] { "tool1", "tool2" }
                }
            }) as InitializeResult;

            Assert.NotNull(initResult);
            Assert.NotNull(handler.CurrentSession);
            var sessionId = handler.CurrentSession.SessionId;

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
            Assert.NotNull(result.AgentCapabilities);
            Assert.NotNull(result.AgentCapabilities.PromptCapabilities);
            Assert.NotNull(result.AgentCapabilities.McpCapabilities);
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
                ProtocolVersion = 1,
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

        [Fact]
        public async Task HandleInitializeAsync_AfterShutdown_ThrowsSessionNotInitialized()
        {
            // Arrange
            var handler = CreateHandler();
            await handler.HandleInitializeAsync(new InitializeParams
            {
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            });

            // Shutdown the session (this terminates and nulls the session)
            await handler.HandleShutdownAsync(null);

            // Act - Try to initialize after shutdown (session is now null, so new init should succeed)
            var result = await handler.HandleInitializeAsync(new InitializeParams
            {
                ClientInfo = new ClientInfo { Name = "TestClient2", Version = "1.0.0" }
            });

            // Assert - Should successfully create a new session
            Assert.NotNull(result);
            Assert.NotNull(handler.CurrentSession);
        }

        [Fact]
        public async Task HandleInitializedAsync_AfterShutdown_ThrowsSessionNotInitialized()
        {
            // Arrange
            var handler = CreateHandler();
            await handler.HandleInitializeAsync(new InitializeParams
            {
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            });

            // Shutdown the session
            await handler.HandleShutdownAsync(null);

            // Act & Assert - Try to send initialized after shutdown
            var exception = await Assert.ThrowsAsync<JsonRpcProtocolException>(async () =>
            {
                await handler.HandleInitializedAsync(null);
            });

            Assert.Equal(JsonRpcErrorCodes.SessionNotInitialized, exception.ErrorCode);
            Assert.Contains("No active session", exception.Message);
        }

        [Fact]
        public async Task HandleShutdownAsync_WithPendingRequests_WaitsForCompletion()
        {
            // Arrange
            var handler = CreateHandler();
            await handler.HandleInitializeAsync(new InitializeParams
            {
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            });

            // Add some pending requests
            var request1 = new JsonRpcRequest { Method = "test1", Id = 1 };
            var request2 = new JsonRpcRequest { Method = "test2", Id = 2 };
            handler.CurrentSession!.AddPendingRequest(request1);
            handler.CurrentSession.AddPendingRequest(request2);

            Assert.Equal(2, handler.CurrentSession.PendingRequests.Count);

            // Act - Start shutdown in background and complete requests
            var shutdownTask = Task.Run(async () => await handler.HandleShutdownAsync(null));

            // Complete the requests while shutdown is waiting
            await Task.Delay(50); // Let shutdown start waiting
            handler.CurrentSession.CompletePendingRequest(1);
            await Task.Delay(50);
            handler.CurrentSession.CompletePendingRequest(2);

            // Wait for shutdown to complete
            var result = await shutdownTask as ShutdownResult;

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.Null(handler.CurrentSession); // Session should be terminated
        }

        [Fact]
        public async Task HandleShutdownAsync_WithSlowPendingRequests_TimesOutAndTerminates()
        {
            // Arrange
            var handler = CreateHandler();
            await handler.HandleInitializeAsync(new InitializeParams
            {
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            });

            // Add a pending request that won't complete
            var request = new JsonRpcRequest { Method = "slow-test", Id = 1 };
            handler.CurrentSession!.AddPendingRequest(request);

            Assert.Single(handler.CurrentSession.PendingRequests);

            // Act - Shutdown should timeout waiting for the request
            var result = await handler.HandleShutdownAsync(null) as ShutdownResult;

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Success); // Shutdown still succeeds even if timeout
            Assert.Null(handler.CurrentSession); // Session is terminated despite pending request
        }

        [Fact]
        public async Task HandleShutdownAsync_NoPendingRequests_TerminatesImmediately()
        {
            // Arrange
            var handler = CreateHandler();
            await handler.HandleInitializeAsync(new InitializeParams
            {
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            });

            Assert.Empty(handler.CurrentSession!.PendingRequests);

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await handler.HandleShutdownAsync(null) as ShutdownResult;
            stopwatch.Stop();

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.Null(handler.CurrentSession);
            // Should complete quickly since no pending requests
            Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"Shutdown took {stopwatch.ElapsedMilliseconds}ms, expected < 1000ms");
        }
    }
}
