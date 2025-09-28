using System;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.JsonRpc;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Andy.Acp.Tests.JsonRpc
{
    public class JsonRpcHandlerTests
    {
        private readonly JsonRpcHandler _handler;

        public JsonRpcHandlerTests()
        {
            _handler = new JsonRpcHandler();
        }

        [Fact]
        public async Task HandleRequestAsync_WithRegisteredMethod_ReturnsSuccessResponse()
        {
            // Arrange
            _handler.RegisterMethod("test", (parameters, ct) => Task.FromResult<object?>("success"));
            var request = new JsonRpcRequest { Method = "test", Id = 1 };

            // Act
            var response = await _handler.HandleRequestAsync(request);

            // Assert
            Assert.NotNull(response);
            Assert.Equal(request.Id, response.Id);
            Assert.True(response.IsSuccess);
            Assert.Equal("success", response.Result);
        }

        [Fact]
        public async Task HandleRequestAsync_WithUnregisteredMethod_ReturnsMethodNotFoundError()
        {
            // Arrange
            var request = new JsonRpcRequest { Method = "unknown", Id = 1 };

            // Act
            var response = await _handler.HandleRequestAsync(request);

            // Assert
            Assert.NotNull(response);
            Assert.Equal(request.Id, response.Id);
            Assert.True(response.IsError);
            Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
        }

        [Fact]
        public async Task HandleRequestAsync_WithNotification_ReturnsNull()
        {
            // Arrange
            _handler.RegisterMethod("notify", (parameters, ct) => Task.FromResult<object?>("result"));
            var notification = new JsonRpcRequest { Method = "notify" }; // No Id = notification

            // Act
            var response = await _handler.HandleRequestAsync(notification);

            // Assert
            Assert.Null(response);
        }

        [Fact]
        public async Task HandleRequestAsync_WithNotificationAndUnknownMethod_ReturnsNull()
        {
            // Arrange
            var notification = new JsonRpcRequest { Method = "unknown" }; // No Id = notification

            // Act
            var response = await _handler.HandleRequestAsync(notification);

            // Assert
            Assert.Null(response);
        }

        [Fact]
        public async Task HandleRequestAsync_WithMethodException_ReturnsInternalError()
        {
            // Arrange
            _handler.RegisterMethod("error", (parameters, ct) => throw new InvalidOperationException("Test error"));
            var request = new JsonRpcRequest { Method = "error", Id = 1 };

            // Act
            var response = await _handler.HandleRequestAsync(request);

            // Assert
            Assert.NotNull(response);
            Assert.True(response.IsError);
            Assert.Equal(JsonRpcErrorCodes.InternalError, response.Error!.Code);
            Assert.Contains("Test error", response.Error.Message);
        }

        [Fact]
        public async Task HandleRequestAsync_WithArgumentException_ReturnsInvalidParamsError()
        {
            // Arrange
            _handler.RegisterMethod("invalid", (parameters, ct) => throw new ArgumentException("Invalid parameters"));
            var request = new JsonRpcRequest { Method = "invalid", Id = 1 };

            // Act
            var response = await _handler.HandleRequestAsync(request);

            // Assert
            Assert.NotNull(response);
            Assert.True(response.IsError);
            Assert.Equal(JsonRpcErrorCodes.InvalidParams, response.Error!.Code);
            Assert.Contains("Invalid parameters", response.Error.Message);
        }

        [Fact]
        public async Task HandleRequestAsync_WithCancellation_ReturnsCancelledError()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            _handler.RegisterMethod("cancel", async (parameters, ct) =>
            {
                await Task.Delay(1000, ct);
                return "never reached";
            });
            var request = new JsonRpcRequest { Method = "cancel", Id = 1 };

            // Act
            cts.Cancel();
            var response = await _handler.HandleRequestAsync(request, cts.Token);

            // Assert
            Assert.NotNull(response);
            Assert.True(response.IsError);
            Assert.Equal(JsonRpcErrorCodes.Cancelled, response.Error!.Code);
        }

        [Fact]
        public async Task HandleRequestAsync_WithMethodHandler_CallsHandler()
        {
            // Arrange
            var methodHandler = new TestMethodHandler("test", "handler result");
            _handler.RegisterMethodHandler(methodHandler);
            var request = new JsonRpcRequest { Method = "test", Id = 1, Params = "test params" };

            // Act
            var response = await _handler.HandleRequestAsync(request);

            // Assert
            Assert.NotNull(response);
            Assert.True(response.IsSuccess);
            Assert.Equal("handler result", response.Result);
            Assert.True(methodHandler.WasCalled);
            Assert.Equal("test params", methodHandler.ReceivedParameters);
        }

        [Fact]
        public void SupportsMethod_WithRegisteredMethod_ReturnsTrue()
        {
            // Arrange
            _handler.RegisterMethod("test", (p, ct) => Task.FromResult<object?>(null));

            // Act & Assert
            Assert.True(_handler.SupportsMethod("test"));
        }

        [Fact]
        public void SupportsMethod_WithUnregisteredMethod_ReturnsFalse()
        {
            // Act & Assert
            Assert.False(_handler.SupportsMethod("unknown"));
        }

        [Fact]
        public void SupportsMethod_WithNullOrEmpty_ReturnsFalse()
        {
            // Act & Assert
            Assert.False(_handler.SupportsMethod(null!));
            Assert.False(_handler.SupportsMethod(""));
            Assert.False(_handler.SupportsMethod("   "));
        }

        [Fact]
        public void GetSupportedMethods_ReturnsRegisteredMethods()
        {
            // Arrange
            _handler.RegisterMethod("method1", (p, ct) => Task.FromResult<object?>(null));
            _handler.RegisterMethod("method2", (p, ct) => Task.FromResult<object?>(null));
            _handler.RegisterMethodHandler(new TestMethodHandler("method3", null));

            // Act
            var methods = _handler.GetSupportedMethods();

            // Assert
            Assert.Equal(3, methods.Length);
            Assert.Contains("method1", methods);
            Assert.Contains("method2", methods);
            Assert.Contains("method3", methods);
        }

        [Fact]
        public void RegisterMethod_WithNullMethod_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => _handler.RegisterMethod(null!, (p, ct) => Task.FromResult<object?>(null)));
            Assert.Throws<ArgumentException>(() => _handler.RegisterMethod("", (p, ct) => Task.FromResult<object?>(null)));
        }

        [Fact]
        public void RegisterMethod_WithNullDelegate_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _handler.RegisterMethod("test", null!));
        }

        [Fact]
        public void RegisterMethodHandler_WithNullHandler_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _handler.RegisterMethodHandler(null!));
        }

        [Fact]
        public void RegisterMethodHandler_WithEmptyMethod_ThrowsArgumentException()
        {
            // Arrange
            var handler = new TestMethodHandler("", null);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => _handler.RegisterMethodHandler(handler));
        }

        [Fact]
        public void UnregisterMethod_WithRegisteredMethod_ReturnsTrue()
        {
            // Arrange
            _handler.RegisterMethod("test", (p, ct) => Task.FromResult<object?>(null));

            // Act
            var result = _handler.UnregisterMethod("test");

            // Assert
            Assert.True(result);
            Assert.False(_handler.SupportsMethod("test"));
        }

        [Fact]
        public void UnregisterMethod_WithUnregisteredMethod_ReturnsFalse()
        {
            // Act
            var result = _handler.UnregisterMethod("unknown");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Clear_RemovesAllMethods()
        {
            // Arrange
            _handler.RegisterMethod("method1", (p, ct) => Task.FromResult<object?>(null));
            _handler.RegisterMethod("method2", (p, ct) => Task.FromResult<object?>(null));

            // Act
            _handler.Clear();

            // Assert
            Assert.Empty(_handler.GetSupportedMethods());
            Assert.False(_handler.SupportsMethod("method1"));
            Assert.False(_handler.SupportsMethod("method2"));
        }

        [Fact]
        public async Task HandleRequestAsync_NullRequest_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => _handler.HandleRequestAsync(null!));
        }

        [Fact]
        public async Task HandleRequestAsync_RequestReceivedEvent_IsFired()
        {
            // Arrange
            JsonRpcRequestEventArgs? eventArgs = null;
            _handler.RequestReceived += (sender, args) => eventArgs = args;
            var request = new JsonRpcRequest { Method = "test", Id = 1 };

            // Act
            await _handler.HandleRequestAsync(request);

            // Assert
            Assert.NotNull(eventArgs);
            Assert.Equal(request, eventArgs.Request);
        }

        [Fact]
        public async Task HandleRequestAsync_RequestProcessedEvent_IsFired()
        {
            // Arrange
            JsonRpcRequestEventArgs? eventArgs = null;
            _handler.RequestProcessed += (sender, args) => eventArgs = args;
            var request = new JsonRpcRequest { Method = "test", Id = 1 };

            // Act
            await _handler.HandleRequestAsync(request);

            // Assert
            Assert.NotNull(eventArgs);
            Assert.Equal(request, eventArgs.Request);
            Assert.True(eventArgs.Handled);
        }

        [Fact]
        public async Task HandleRequestAsync_EventHandlerSetsResponse_UsesEventResponse()
        {
            // Arrange
            var customResponse = new JsonRpcResponse { Id = 1, Result = "custom" };
            _handler.RequestReceived += (sender, args) =>
            {
                args.Response = customResponse;
                args.Handled = true;
            };
            var request = new JsonRpcRequest { Method = "test", Id = 1 };

            // Act
            var response = await _handler.HandleRequestAsync(request);

            // Assert
            Assert.Equal(customResponse, response);
        }

        private class TestMethodHandler : IJsonRpcMethodHandler
        {
            public TestMethodHandler(string method, object? result)
            {
                Method = method;
                Result = result;
            }

            public string Method { get; }
            public object? Result { get; }
            public bool WasCalled { get; private set; }
            public object? ReceivedParameters { get; private set; }

            public Task<object?> ExecuteAsync(object? parameters, CancellationToken cancellationToken = default)
            {
                WasCalled = true;
                ReceivedParameters = parameters;
                return Task.FromResult(Result);
            }
        }
    }
}