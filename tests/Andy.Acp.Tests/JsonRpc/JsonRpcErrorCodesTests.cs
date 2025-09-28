using Andy.Acp.Core.JsonRpc;
using Xunit;

namespace Andy.Acp.Tests.JsonRpc
{
    public class JsonRpcErrorCodesTests
    {
        [Theory]
        [InlineData(JsonRpcErrorCodes.ParseError, "Parse error")]
        [InlineData(JsonRpcErrorCodes.InvalidRequest, "Invalid Request")]
        [InlineData(JsonRpcErrorCodes.MethodNotFound, "Method not found")]
        [InlineData(JsonRpcErrorCodes.InvalidParams, "Invalid params")]
        [InlineData(JsonRpcErrorCodes.InternalError, "Internal error")]
        [InlineData(JsonRpcErrorCodes.SessionNotInitialized, "Session not initialized")]
        [InlineData(JsonRpcErrorCodes.SessionAlreadyInitialized, "Session already initialized")]
        [InlineData(JsonRpcErrorCodes.InvalidProtocolVersion, "Invalid protocol version")]
        [InlineData(JsonRpcErrorCodes.ToolNotFound, "Tool not found")]
        [InlineData(JsonRpcErrorCodes.ToolExecutionFailed, "Tool execution failed")]
        [InlineData(JsonRpcErrorCodes.ResourceNotFound, "Resource not found")]
        [InlineData(JsonRpcErrorCodes.ResourceAccessDenied, "Resource access denied")]
        [InlineData(JsonRpcErrorCodes.Timeout, "Operation timeout")]
        [InlineData(JsonRpcErrorCodes.Cancelled, "Operation cancelled")]
        public void GetMessage_WithKnownErrorCode_ReturnsCorrectMessage(int errorCode, string expectedMessage)
        {
            // Act
            var message = JsonRpcErrorCodes.GetMessage(errorCode);

            // Assert
            Assert.Equal(expectedMessage, message);
        }

        [Fact]
        public void GetMessage_WithUnknownErrorCode_ReturnsUnknownError()
        {
            // Arrange
            var unknownCode = -99999;

            // Act
            var message = JsonRpcErrorCodes.GetMessage(unknownCode);

            // Assert
            Assert.Equal("Unknown error", message);
        }

        [Fact]
        public void CreateError_WithCodeOnly_ReturnsErrorWithDefaultMessage()
        {
            // Arrange
            var code = JsonRpcErrorCodes.MethodNotFound;

            // Act
            var error = JsonRpcErrorCodes.CreateError(code);

            // Assert
            Assert.Equal(code, error.Code);
            Assert.Equal("Method not found", error.Message);
            Assert.Null(error.Data);
        }

        [Fact]
        public void CreateError_WithCodeAndData_ReturnsErrorWithData()
        {
            // Arrange
            var code = JsonRpcErrorCodes.InvalidParams;
            var data = new { parameter = "missing" };

            // Act
            var error = JsonRpcErrorCodes.CreateError(code, data);

            // Assert
            Assert.Equal(code, error.Code);
            Assert.Equal("Invalid params", error.Message);
            Assert.Equal(data, error.Data);
        }

        [Fact]
        public void CreateError_WithCustomMessage_ReturnsErrorWithCustomMessage()
        {
            // Arrange
            var code = JsonRpcErrorCodes.InternalError;
            var customMessage = "Custom error message";
            var data = "additional info";

            // Act
            var error = JsonRpcErrorCodes.CreateError(code, customMessage, data);

            // Assert
            Assert.Equal(code, error.Code);
            Assert.Equal(customMessage, error.Message);
            Assert.Equal(data, error.Data);
        }

        [Fact]
        public void StandardErrorCodes_HaveCorrectValues()
        {
            // Assert standard JSON-RPC 2.0 error codes
            Assert.Equal(-32700, JsonRpcErrorCodes.ParseError);
            Assert.Equal(-32600, JsonRpcErrorCodes.InvalidRequest);
            Assert.Equal(-32601, JsonRpcErrorCodes.MethodNotFound);
            Assert.Equal(-32602, JsonRpcErrorCodes.InvalidParams);
            Assert.Equal(-32603, JsonRpcErrorCodes.InternalError);
        }

        [Fact]
        public void AcpErrorCodes_AreInCorrectRange()
        {
            // Assert ACP-specific error codes are in the -32000 to -32099 range
            Assert.InRange(JsonRpcErrorCodes.SessionNotInitialized, -32099, -32000);
            Assert.InRange(JsonRpcErrorCodes.SessionAlreadyInitialized, -32099, -32000);
            Assert.InRange(JsonRpcErrorCodes.InvalidProtocolVersion, -32099, -32000);
            Assert.InRange(JsonRpcErrorCodes.ToolNotFound, -32099, -32000);
            Assert.InRange(JsonRpcErrorCodes.ToolExecutionFailed, -32099, -32000);
            Assert.InRange(JsonRpcErrorCodes.ResourceNotFound, -32099, -32000);
            Assert.InRange(JsonRpcErrorCodes.ResourceAccessDenied, -32099, -32000);
            Assert.InRange(JsonRpcErrorCodes.Timeout, -32099, -32000);
            Assert.InRange(JsonRpcErrorCodes.Cancelled, -32099, -32000);
        }
    }
}