using System.Text.Json;
using System.Threading.Tasks;
using Andy.Acp.Core.JsonRpc;
using Xunit;

namespace Andy.Acp.Tests.JsonRpc
{
    /// <summary>
    /// Tests for the normative JSON-RPC 2.0 rules addressed by issue #15:
    /// null results, explicit null ids, error.data, batch rejection, and
    /// notification handling.
    /// </summary>
    public class JsonRpcComplianceTests
    {
        [Fact]
        public void Serialize_SuccessWithNullResult_IncludesResultMember()
        {
            var response = new JsonRpcResponse { Id = 1, Result = null };

            var json = JsonRpcSerializer.Serialize(response);

            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("result", out var result));
            Assert.Equal(JsonValueKind.Null, result.ValueKind);
            Assert.False(doc.RootElement.TryGetProperty("error", out _));
        }

        [Fact]
        public void Serialize_ErrorResponse_OmitsResultMember()
        {
            var response = new JsonRpcResponse
            {
                Id = 1,
                Error = new JsonRpcError { Code = JsonRpcErrorCodes.MethodNotFound, Message = "Method not found" }
            };

            var json = JsonRpcSerializer.Serialize(response);

            using var doc = JsonDocument.Parse(json);
            Assert.False(doc.RootElement.TryGetProperty("result", out _));
            Assert.True(doc.RootElement.TryGetProperty("error", out _));
        }

        [Fact]
        public void Deserialize_ExplicitNullId_IsRequestNotNotification()
        {
            var json = """{"jsonrpc":"2.0","method":"ping","id":null}""";

            var message = JsonRpcSerializer.Deserialize(json);

            var request = Assert.IsType<JsonRpcRequest>(message);
            Assert.True(request.HasId);
            Assert.False(request.IsNotification);
        }

        [Fact]
        public void Deserialize_OmittedId_IsNotification()
        {
            var json = """{"jsonrpc":"2.0","method":"ping"}""";

            var message = JsonRpcSerializer.Deserialize(json);

            var request = Assert.IsType<JsonRpcRequest>(message);
            Assert.False(request.HasId);
            Assert.True(request.IsNotification);
        }

        [Fact]
        public void Serialize_ResponseWithNullId_WritesIdNull()
        {
            var response = new JsonRpcResponse { Id = null, Result = "ok" };

            var json = JsonRpcSerializer.Serialize(response);

            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("id", out var id));
            Assert.Equal(JsonValueKind.Null, id.ValueKind);
        }

        [Fact]
        public void ErrorData_RoundTripsThroughErrorMember()
        {
            var data = new { detail = "extra" };
            var response = JsonRpcSerializer.CreateErrorResponse(
                new JsonRpcRequest { Method = "m", Id = 7 },
                JsonRpcErrorCodes.CreateError(JsonRpcErrorCodes.InvalidParams, "bad", data));

            var json = JsonRpcSerializer.Serialize(response);

            using var doc = JsonDocument.Parse(json);
            var error = doc.RootElement.GetProperty("error");
            Assert.Equal("extra", error.GetProperty("data").GetProperty("detail").GetString());
        }

        [Fact]
        public async Task ProtocolException_ErrorData_ReachesResponse()
        {
            var handler = new JsonRpcHandler();
            handler.RegisterMethod("boom", (p, ct) =>
                throw new JsonRpcProtocolException(JsonRpcErrorCodes.InvalidParams, "nope", new { field = "x" }));

            var response = await handler.HandleRequestAsync(new JsonRpcRequest { Method = "boom", Id = 1 });

            Assert.NotNull(response);
            Assert.NotNull(response!.Error!.Data);
            var json = JsonRpcSerializer.Serialize(response);
            Assert.Contains("\"field\"", json);
        }

        [Fact]
        public void Deserialize_BatchArray_IsRejected()
        {
            var json = """[{"jsonrpc":"2.0","method":"a","id":1},{"jsonrpc":"2.0","method":"b","id":2}]""";

            Assert.Throws<JsonRpcInvalidRequestException>(() => JsonRpcSerializer.Deserialize(json));
        }

        [Fact]
        public void Deserialize_PrimitiveParams_IsRejected()
        {
            var json = """{"jsonrpc":"2.0","method":"m","params":42,"id":1}""";

            Assert.Throws<JsonRpcInvalidRequestException>(() => JsonRpcSerializer.Deserialize(json));
        }

        [Fact]
        public async Task HandleRequest_Notification_ReturnsNull()
        {
            var handler = new JsonRpcHandler();
            handler.RegisterMethod("note", (p, ct) => Task.FromResult<object?>("ignored"));

            // A notification has no id member.
            var request = new JsonRpcRequest { Method = "note", HasId = false };
            var response = await handler.HandleRequestAsync(request);

            Assert.Null(response);
        }
    }
}
