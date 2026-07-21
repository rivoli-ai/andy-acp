using System.Text.Json;
using System.Threading.Tasks;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Protocol;
using Xunit;

namespace Andy.Acp.Tests.Protocol
{
    /// <summary>
    /// Tests for the ACP v1 initialize handshake (issue #12): capability shapes,
    /// integer protocol-version negotiation, initialize-once, and connection-state tracking.
    /// </summary>
    public class AcpProtocolHandlerTests
    {
        private readonly ServerInfo _serverInfo = new() { Name = "TestServer", Version = "1.0.0" };

        private readonly AcpAgentCapabilities _agentCaps = new()
        {
            LoadSession = true,
            PromptCapabilities = new AcpPromptCapabilities { Image = true },
            McpCapabilities = new AcpMcpCapabilities()
        };

        private (AcpProtocolHandler handler, AcpConnectionState state) Create()
        {
            var state = new AcpConnectionState();
            return (new AcpProtocolHandler(state, _serverInfo, _agentCaps), state);
        }

        private static JsonElement Params(object o) =>
            JsonDocument.Parse(JsonSerializer.Serialize(o)).RootElement.Clone();

        [Fact]
        public async Task Initialize_ReturnsNegotiatedVersionAndCapabilities()
        {
            var (handler, state) = Create();

            var result = (AcpInitializeResult)(await handler.HandleInitializeAsync(
                Params(new { protocolVersion = 1, clientCapabilities = new { } }))!)!;

            Assert.Equal(1, result.ProtocolVersion);
            Assert.Equal("TestServer", result.AgentInfo!.Name);
            Assert.True(result.AgentCapabilities!.LoadSession);
            Assert.True(result.AgentCapabilities.PromptCapabilities!.Image);
            Assert.NotNull(result.AuthMethods);
            Assert.True(state.Initialized);
            Assert.Equal(1, state.ProtocolVersion);
        }

        [Fact]
        public async Task Initialize_WithNullParams_UsesDefaults()
        {
            var (handler, state) = Create();

            var result = (AcpInitializeResult)(await handler.HandleInitializeAsync(null))!;

            Assert.Equal(1, result.ProtocolVersion);
            Assert.True(state.Initialized);
        }

        [Fact]
        public async Task Initialize_HigherRequestedVersion_ClampsToAgentMax()
        {
            var (handler, _) = Create();

            var result = (AcpInitializeResult)(await handler.HandleInitializeAsync(
                Params(new { protocolVersion = 99 }))!)!;

            Assert.Equal(AcpProtocolHandler.ProtocolVersion, result.ProtocolVersion);
        }

        [Fact]
        public async Task Initialize_CapturesClientCapabilities()
        {
            var (handler, state) = Create();

            await handler.HandleInitializeAsync(Params(new
            {
                protocolVersion = 1,
                clientCapabilities = new { fs = new { readTextFile = true, writeTextFile = true }, terminal = true }
            }));

            Assert.True(state.ClientCapabilities.Terminal);
            Assert.True(state.ClientCapabilities.Fs!.ReadTextFile);
            Assert.True(state.ClientCapabilities.Fs.WriteTextFile);
        }

        [Fact]
        public async Task Initialize_Twice_ThrowsAlreadyInitialized()
        {
            var (handler, _) = Create();
            await handler.HandleInitializeAsync(Params(new { protocolVersion = 1 }));

            var ex = await Assert.ThrowsAsync<JsonRpcProtocolException>(
                async () => await handler.HandleInitializeAsync(Params(new { protocolVersion = 1 })));
            Assert.Equal(JsonRpcErrorCodes.SessionAlreadyInitialized, ex.ErrorCode);
        }

        [Fact]
        public void RegisterMethods_RegistersOnlyInitialize()
        {
            var (handler, _) = Create();
            var rpc = new JsonRpcHandler();

            handler.RegisterMethods(rpc);

            Assert.True(rpc.SupportsMethod("initialize"));
            Assert.False(rpc.SupportsMethod("initialized"));
            Assert.False(rpc.SupportsMethod("shutdown"));
        }
    }
}
