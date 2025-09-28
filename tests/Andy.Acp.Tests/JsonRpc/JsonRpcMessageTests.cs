using Andy.Acp.Core.JsonRpc;
using Xunit;

namespace Andy.Acp.Tests.JsonRpc
{
    public class JsonRpcMessageTests
    {
        [Fact]
        public void JsonRpcRequest_WithId_IsNotNotification()
        {
            // Arrange
            var request = new JsonRpcRequest
            {
                Method = "test",
                Id = 1
            };

            // Act & Assert
            Assert.False(request.IsNotification);
        }

        [Fact]
        public void JsonRpcRequest_WithoutId_IsNotification()
        {
            // Arrange
            var request = new JsonRpcRequest
            {
                Method = "test"
            };

            // Act & Assert
            Assert.True(request.IsNotification);
            Assert.Null(request.Id);
        }

        [Fact]
        public void JsonRpcRequest_DefaultJsonRpcVersion_Is2_0()
        {
            // Arrange
            var request = new JsonRpcRequest { Method = "test" };

            // Act & Assert
            Assert.Equal("2.0", request.JsonRpc);
        }

        [Fact]
        public void JsonRpcResponse_WithResult_IsSuccess()
        {
            // Arrange
            var response = new JsonRpcResponse
            {
                Id = 1,
                Result = "success"
            };

            // Act & Assert
            Assert.True(response.IsSuccess);
            Assert.False(response.IsError);
        }

        [Fact]
        public void JsonRpcResponse_WithError_IsError()
        {
            // Arrange
            var response = new JsonRpcResponse
            {
                Id = 1,
                Error = new JsonRpcError
                {
                    Code = -1,
                    Message = "Error"
                }
            };

            // Act & Assert
            Assert.True(response.IsError);
            Assert.False(response.IsSuccess);
        }

        [Fact]
        public void JsonRpcResponse_WithNeitherResultNorError_IsSuccess()
        {
            // Arrange
            var response = new JsonRpcResponse
            {
                Id = 1
            };

            // Act & Assert
            Assert.True(response.IsSuccess);
            Assert.False(response.IsError);
        }

        [Fact]
        public void JsonRpcResponse_DefaultJsonRpcVersion_Is2_0()
        {
            // Arrange
            var response = new JsonRpcResponse { Id = 1 };

            // Act & Assert
            Assert.Equal("2.0", response.JsonRpc);
        }

        [Fact]
        public void JsonRpcError_RequiredProperties_CanBeSet()
        {
            // Arrange & Act
            var error = new JsonRpcError
            {
                Code = -32601,
                Message = "Method not found"
            };

            // Assert
            Assert.Equal(-32601, error.Code);
            Assert.Equal("Method not found", error.Message);
            Assert.Null(error.Data);
        }

        [Fact]
        public void JsonRpcError_WithData_StoresData()
        {
            // Arrange
            var data = new { method = "unknown_method" };
            var error = new JsonRpcError
            {
                Code = -32601,
                Message = "Method not found",
                Data = data
            };

            // Act & Assert
            Assert.Equal(data, error.Data);
        }

        [Fact]
        public void JsonRpcRequest_WithParams_StoresParams()
        {
            // Arrange
            var parameters = new { name = "value", count = 42 };
            var request = new JsonRpcRequest
            {
                Method = "test",
                Params = parameters,
                Id = 1
            };

            // Act & Assert
            Assert.Equal(parameters, request.Params);
        }

        [Fact]
        public void JsonRpcRequest_WithoutParams_ParamsIsNull()
        {
            // Arrange
            var request = new JsonRpcRequest
            {
                Method = "test",
                Id = 1
            };

            // Act & Assert
            Assert.Null(request.Params);
        }

        [Fact]
        public void JsonRpcResponse_WithNullId_StoresNullId()
        {
            // Arrange
            var response = new JsonRpcResponse
            {
                Id = null,
                Result = "ok"
            };

            // Act & Assert
            Assert.Null(response.Id);
        }

        [Theory]
        [InlineData(1)]
        [InlineData("string-id")]
        [InlineData(null)]
        public void JsonRpcMessage_SupportsVariousIdTypes(object? id)
        {
            // Arrange
            var request = new JsonRpcRequest
            {
                Method = "test",
                Id = id
            };

            var response = new JsonRpcResponse
            {
                Id = id,
                Result = "ok"
            };

            // Act & Assert
            Assert.Equal(id, request.Id);
            Assert.Equal(id, response.Id);
        }
    }
}