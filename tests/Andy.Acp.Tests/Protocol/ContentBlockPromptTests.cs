using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Protocol;
using Xunit;

namespace Andy.Acp.Tests.Protocol
{
    /// <summary>
    /// Tests that prompt content blocks are preserved and validated against
    /// capabilities, and that non-text prompts are supported (issue #16).
    /// </summary>
    public class ContentBlockPromptTests
    {
        private static (JsonRpcHandler handler, CapturingAgent agent) Setup(AgentCapabilities caps)
        {
            var handler = new JsonRpcHandler();
            var agent = new CapturingAgent { Caps = caps };
            var sessionHandler = new AcpSessionHandler(agent, handler);
            sessionHandler.RegisterMethods();
            return (handler, agent);
        }

        private static JsonRpcRequest Prompt(string promptArrayJson)
        {
            var json = "{\"sessionId\":\"s1\",\"prompt\":" + promptArrayJson + "}";
            var el = JsonDocument.Parse(json).RootElement.Clone();
            return new JsonRpcRequest { Method = "session/prompt", Id = 1, Params = el };
        }

        [Fact]
        public async Task MixedTextAndImage_PreservesAllBlocks()
        {
            var (handler, agent) = Setup(new AgentCapabilities { ImagePrompts = true });

            var resp = await handler.HandleRequestAsync(Prompt(
                "[{\"type\":\"text\",\"text\":\"hi\"},{\"type\":\"image\",\"data\":\"AAAA\",\"mimeType\":\"image/png\"}]"));

            Assert.True(resp!.IsSuccess);
            Assert.NotNull(agent.Received);
            Assert.Equal(2, agent.Received!.Blocks.Count);
            Assert.Equal("text", agent.Received.Blocks[0].Type);
            Assert.Equal("image", agent.Received.Blocks[1].Type);
            Assert.Equal("AAAA", agent.Received.Blocks[1].Data);
            Assert.Equal("image/png", agent.Received.Blocks[1].MimeType);
            Assert.Equal("hi", agent.Received.Text);
        }

        [Fact]
        public async Task NonTextPrompt_IsAccepted()
        {
            var (handler, agent) = Setup(new AgentCapabilities { ImagePrompts = true });

            var resp = await handler.HandleRequestAsync(Prompt(
                "[{\"type\":\"image\",\"data\":\"AAAA\",\"mimeType\":\"image/png\"}]"));

            Assert.True(resp!.IsSuccess);
            Assert.Single(agent.Received!.Blocks);
            Assert.Equal(string.Empty, agent.Received.Text);
        }

        [Fact]
        public async Task ImageWithoutCapability_ReturnsInvalidParams()
        {
            var (handler, agent) = Setup(new AgentCapabilities()); // image not supported

            var resp = await handler.HandleRequestAsync(Prompt(
                "[{\"type\":\"image\",\"data\":\"AAAA\",\"mimeType\":\"image/png\"}]"));

            Assert.True(resp!.IsError);
            Assert.Equal(JsonRpcErrorCodes.InvalidParams, resp.Error!.Code);
            Assert.Null(agent.Received);
        }

        [Fact]
        public async Task ResourceLink_AcceptedWithoutEmbeddedCapability()
        {
            var (handler, agent) = Setup(new AgentCapabilities()); // embeddedContext false

            var resp = await handler.HandleRequestAsync(Prompt(
                "[{\"type\":\"resource_link\",\"uri\":\"file:///x\",\"name\":\"x\"}]"));

            Assert.True(resp!.IsSuccess);
            Assert.Equal("resource_link", agent.Received!.Blocks[0].Type);
            Assert.Equal("file:///x", agent.Received.Blocks[0].Uri);
            Assert.Equal("x", agent.Received.Blocks[0].Name);
        }

        [Fact]
        public async Task EmbeddedResource_PreservedWhenCapable()
        {
            var (handler, agent) = Setup(new AgentCapabilities { EmbeddedContext = true });

            var resp = await handler.HandleRequestAsync(Prompt(
                "[{\"type\":\"resource\",\"resource\":{\"uri\":\"file:///x\",\"text\":\"content\"}}]"));

            Assert.True(resp!.IsSuccess);
            Assert.NotNull(agent.Received!.Blocks[0].Resource);
            Assert.Equal("content", agent.Received.Blocks[0].Resource!.Text);
            Assert.Equal("file:///x", agent.Received.Blocks[0].Resource!.Uri);
        }

        private sealed class CapturingAgent : IAgentProvider
        {
            public PromptMessage? Received;
            public AgentCapabilities Caps = new();

            public Task<AgentResponse> ProcessPromptAsync(string sessionId, PromptMessage prompt, IResponseStreamer streamer, CancellationToken cancellationToken)
            {
                Received = prompt;
                return Task.FromResult(new AgentResponse { StopReason = StopReason.Completed });
            }

            public Task<SessionMetadata> CreateSessionAsync(NewSessionParams? parameters, CancellationToken cancellationToken)
                => Task.FromResult(new SessionMetadata { SessionId = "s1" });
            public Task<SessionMetadata?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken)
                => Task.FromResult<SessionMetadata?>(null);
            public Task CancelSessionAsync(string sessionId, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<bool> SetSessionModeAsync(string sessionId, string mode, CancellationToken cancellationToken) => Task.FromResult(true);
            public Task<bool> SetSessionModelAsync(string sessionId, string model, CancellationToken cancellationToken) => Task.FromResult(true);
            public AgentCapabilities GetCapabilities() => Caps;
        }
    }
}
