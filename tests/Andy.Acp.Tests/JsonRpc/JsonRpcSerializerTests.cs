using System;
using System.Text.Json;
using Andy.Acp.Core.JsonRpc;
using Xunit;

namespace Andy.Acp.Tests.JsonRpc
{
    public class JsonRpcSerializerTests
    {
        [Fact]
        public void Serialize_Request_ReturnsValidJson()
        {
            // Arrange
            var request = new JsonRpcRequest
            {
                Method = "test",
                Params = new { name = "value" },
                Id = 1
            };

            // Act
            var json = JsonRpcSerializer.Serialize(request);

            // Assert
            Assert.NotNull(json);
            Assert.Contains("\"jsonrpc\":\"2.0\"", json);
            Assert.Contains("\"method\":\"test\"", json);
            Assert.Contains("\"id\":1", json);
        }

        [Fact]
        public void Serialize_Notification_ExcludesId()
        {
            // Arrange
            var notification = new JsonRpcRequest
            {
                Method = "notify",
                Params = "test"
            };

            // Act
            var json = JsonRpcSerializer.Serialize(notification);

            // Assert
            Assert.NotNull(json);
            Assert.Contains("\"method\":\"notify\"", json);
            Assert.DoesNotContain("\"id\"", json);
        }

        [Fact]
        public void Serialize_Response_ReturnsValidJson()
        {
            // Arrange
            var response = new JsonRpcResponse
            {
                Id = "test-id",
                Result = new { success = true }
            };

            // Act
            var json = JsonRpcSerializer.Serialize(response);

            // Assert
            Assert.NotNull(json);
            Assert.Contains("\"jsonrpc\":\"2.0\"", json);
            Assert.Contains("\"id\":\"test-id\"", json);
            Assert.Contains("\"result\"", json);
            Assert.DoesNotContain("\"error\"", json);
        }

        [Fact]
        public void Serialize_ErrorResponse_ReturnsValidJson()
        {
            // Arrange
            var response = new JsonRpcResponse
            {
                Id = 42,
                Error = new JsonRpcError
                {
                    Code = -32601,
                    Message = "Method not found",
                    Data = "additional info"
                }
            };

            // Act
            var json = JsonRpcSerializer.Serialize(response);

            // Assert
            Assert.NotNull(json);
            Assert.Contains("\"error\"", json);
            Assert.Contains("\"code\":-32601", json);
            Assert.Contains("\"message\":\"Method not found\"", json);
            Assert.DoesNotContain("\"result\"", json);
        }

        [Fact]
        public void Serialize_NullMessage_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => JsonRpcSerializer.Serialize(null!));
        }

        [Fact]
        public void Deserialize_ValidRequest_ReturnsRequest()
        {
            // Arrange
            var json = """{"jsonrpc":"2.0","method":"test","params":{"name":"value"},"id":1}""";

            // Act
            var message = JsonRpcSerializer.Deserialize(json);

            // Assert
            Assert.IsType<JsonRpcRequest>(message);
            var request = (JsonRpcRequest)message;
            Assert.Equal("test", request.Method);
            Assert.Equal(1, ((JsonElement)request.Id!).GetInt32());
            Assert.False(request.IsNotification);
        }

        [Fact]
        public void Deserialize_ValidNotification_ReturnsNotification()
        {
            // Arrange
            var json = """{"jsonrpc":"2.0","method":"notify","params":"test"}""";

            // Act
            var message = JsonRpcSerializer.Deserialize(json);

            // Assert
            Assert.IsType<JsonRpcRequest>(message);
            var request = (JsonRpcRequest)message;
            Assert.Equal("notify", request.Method);
            Assert.Null(request.Id);
            Assert.True(request.IsNotification);
        }

        [Fact]
        public void Deserialize_ValidResponse_ReturnsResponse()
        {
            // Arrange
            var json = """{"jsonrpc":"2.0","result":{"success":true},"id":"test"}""";

            // Act
            var message = JsonRpcSerializer.Deserialize(json);

            // Assert
            Assert.IsType<JsonRpcResponse>(message);
            var response = (JsonRpcResponse)message;
            Assert.Equal("test", ((JsonElement)response.Id!).GetString());
            Assert.True(response.IsSuccess);
            Assert.False(response.IsError);
        }

        [Fact]
        public void Deserialize_ValidErrorResponse_ReturnsErrorResponse()
        {
            // Arrange
            var json = """{"jsonrpc":"2.0","error":{"code":-32601,"message":"Method not found"},"id":1}""";

            // Act
            var message = JsonRpcSerializer.Deserialize(json);

            // Assert
            Assert.IsType<JsonRpcResponse>(message);
            var response = (JsonRpcResponse)message;
            Assert.True(response.IsError);
            Assert.False(response.IsSuccess);
            Assert.NotNull(response.Error);
            Assert.Equal(-32601, response.Error.Code);
            Assert.Equal("Method not found", response.Error.Message);
        }

        [Fact]
        public void Deserialize_InvalidJson_ThrowsJsonRpcParseException()
        {
            // Arrange
            var json = "invalid json";

            // Act & Assert
            Assert.Throws<JsonRpcParseException>(() => JsonRpcSerializer.Deserialize(json));
        }

        [Fact]
        public void Deserialize_MissingJsonRpc_ThrowsJsonRpcInvalidRequestException()
        {
            // Arrange
            var json = """{"method":"test","id":1}""";

            // Act & Assert
            Assert.Throws<JsonRpcInvalidRequestException>(() => JsonRpcSerializer.Deserialize(json));
        }

        [Fact]
        public void Deserialize_InvalidJsonRpcVersion_ThrowsJsonRpcInvalidRequestException()
        {
            // Arrange
            var json = """{"jsonrpc":"1.0","method":"test","id":1}""";

            // Act & Assert
            Assert.Throws<JsonRpcInvalidRequestException>(() => JsonRpcSerializer.Deserialize(json));
        }

        [Fact]
        public void Deserialize_MissingMethod_ThrowsJsonRpcInvalidRequestException()
        {
            // Arrange
            var json = """{"jsonrpc":"2.0","id":1}""";

            // Act & Assert
            Assert.Throws<JsonRpcInvalidRequestException>(() => JsonRpcSerializer.Deserialize(json));
        }

        [Fact]
        public void Deserialize_EmptyMethod_ThrowsJsonRpcInvalidRequestException()
        {
            // Arrange
            var json = """{"jsonrpc":"2.0","method":"","id":1}""";

            // Act & Assert
            Assert.Throws<JsonRpcInvalidRequestException>(() => JsonRpcSerializer.Deserialize(json));
        }

        [Fact]
        public void Deserialize_ReservedMethodName_ThrowsJsonRpcInvalidRequestException()
        {
            // Arrange
            var json = """{"jsonrpc":"2.0","method":"rpc.test","id":1}""";

            // Act & Assert
            Assert.Throws<JsonRpcInvalidRequestException>(() => JsonRpcSerializer.Deserialize(json));
        }

        [Fact]
        public void Deserialize_ResponseWithBothResultAndError_ThrowsJsonRpcInvalidRequestException()
        {
            // Arrange
            var json = """{"jsonrpc":"2.0","result":"ok","error":{"code":-1,"message":"error"},"id":1}""";

            // Act & Assert
            Assert.Throws<JsonRpcInvalidRequestException>(() => JsonRpcSerializer.Deserialize(json));
        }

        [Fact]
        public void Deserialize_ResponseWithNeitherResultNorError_ThrowsJsonRpcInvalidRequestException()
        {
            // Arrange
            var json = """{"jsonrpc":"2.0","id":1}""";

            // Act & Assert
            Assert.Throws<JsonRpcInvalidRequestException>(() => JsonRpcSerializer.Deserialize(json));
        }

        [Fact]
        public void TryDeserialize_ValidJson_ReturnsTrue()
        {
            // Arrange
            var json = """{"jsonrpc":"2.0","method":"test","id":1}""";

            // Act
            var result = JsonRpcSerializer.TryDeserialize(json, out var message);

            // Assert
            Assert.True(result);
            Assert.NotNull(message);
            Assert.IsType<JsonRpcRequest>(message);
        }

        [Fact]
        public void TryDeserialize_InvalidJson_ReturnsFalse()
        {
            // Arrange
            var json = "invalid";

            // Act
            var result = JsonRpcSerializer.TryDeserialize(json, out var message);

            // Assert
            Assert.False(result);
            Assert.Null(message);
        }

        [Fact]
        public void CreateSuccessResponse_ReturnsValidResponse()
        {
            // Arrange
            var request = new JsonRpcRequest { Method = "test", Id = "abc" };
            var result = new { data = "success" };

            // Act
            var response = JsonRpcSerializer.CreateSuccessResponse(request, result);

            // Assert
            Assert.Equal(request.Id, response.Id);
            Assert.Equal(result, response.Result);
            Assert.Null(response.Error);
            Assert.True(response.IsSuccess);
        }

        [Fact]
        public void CreateErrorResponse_ReturnsValidErrorResponse()
        {
            // Arrange
            var request = new JsonRpcRequest { Method = "test", Id = 123 };
            var error = new JsonRpcError { Code = -1, Message = "Test error" };

            // Act
            var response = JsonRpcSerializer.CreateErrorResponse(request, error);

            // Assert
            Assert.Equal(request.Id, response.Id);
            Assert.Equal(error, response.Error);
            Assert.Null(response.Result);
            Assert.True(response.IsError);
        }

        [Fact]
        public void CreateErrorResponse_WithErrorCode_ReturnsValidErrorResponse()
        {
            // Arrange
            var request = new JsonRpcRequest { Method = "test", Id = "test" };
            var errorCode = JsonRpcErrorCodes.MethodNotFound;

            // Act
            var response = JsonRpcSerializer.CreateErrorResponse(request, errorCode);

            // Assert
            Assert.Equal(request.Id, response.Id);
            Assert.NotNull(response.Error);
            Assert.Equal(errorCode, response.Error.Code);
            Assert.Equal(JsonRpcErrorCodes.GetMessage(errorCode), response.Error.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Deserialize_NullOrEmptyJson_ThrowsArgumentNullException(string? json)
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => JsonRpcSerializer.Deserialize(json!));
        }
    }
}