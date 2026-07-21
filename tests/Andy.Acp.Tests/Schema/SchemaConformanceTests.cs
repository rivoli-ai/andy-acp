using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Protocol;
using Andy.Acp.Core.Transport;
using Xunit;

namespace Andy.Acp.Tests.Schema
{
    /// <summary>
    /// Validates actual serialized ACP wire output against the pinned ACP v1 JSON Schema
    /// (issue #21). Any schema-invalid output fails these tests.
    /// </summary>
    public class SchemaConformanceTests
    {
        private static string ParamsOf(string notificationJson)
            => JsonDocument.Parse(notificationJson).RootElement.GetProperty("params").GetRawText();

        [Fact]
        public async Task InitializeResponse_IsSchemaValid()
        {
            var state = new AcpConnectionState();
            var handler = new AcpProtocolHandler(state,
                new ServerInfo { Name = "T", Version = "1.0" },
                new AcpAgentCapabilities
                {
                    LoadSession = true,
                    PromptCapabilities = new AcpPromptCapabilities { Image = true },
                    McpCapabilities = new AcpMcpCapabilities()
                });

            var paramsEl = JsonDocument.Parse("{\"protocolVersion\":1,\"clientCapabilities\":{}}").RootElement.Clone();
            var result = await handler.HandleInitializeAsync(paramsEl);
            var json = JsonSerializer.Serialize(result, JsonRpcSerializer.Options);

            AcpSchema.AssertValid("InitializeResponse", json);
        }

        [Fact]
        public void InitializeRequest_IsSchemaValid()
        {
            AcpSchema.AssertValid("InitializeRequest",
                "{\"protocolVersion\":1,\"clientCapabilities\":{\"fs\":{\"readTextFile\":true,\"writeTextFile\":true},\"terminal\":true}}");
        }

        [Fact]
        public void NewSessionRequest_IsSchemaValid()
        {
            AcpSchema.AssertValid("NewSessionRequest", "{\"cwd\":\"/tmp\",\"mcpServers\":[]}");
        }

        [Fact]
        public void PromptRequest_IsSchemaValid()
        {
            AcpSchema.AssertValid("PromptRequest",
                "{\"sessionId\":\"s1\",\"prompt\":[{\"type\":\"text\",\"text\":\"hello\"}]}");
        }

        [Theory]
        [InlineData("{\"type\":\"text\",\"text\":\"hi\"}")]
        [InlineData("{\"type\":\"image\",\"data\":\"AAAA\",\"mimeType\":\"image/png\"}")]
        [InlineData("{\"type\":\"audio\",\"data\":\"AAAA\",\"mimeType\":\"audio/wav\"}")]
        [InlineData("{\"type\":\"resource_link\",\"uri\":\"file:///x\",\"name\":\"x\"}")]
        [InlineData("{\"type\":\"resource\",\"resource\":{\"uri\":\"file:///x\",\"text\":\"c\"}}")]
        public void ContentBlock_Variants_AreSchemaValid(string json)
        {
            AcpSchema.AssertValid("ContentBlock", json);
        }

        [Fact]
        public async Task SessionUpdate_MessageChunk_IsSchemaValid()
        {
            var t = new CapturingTransport();
            var s = new SessionUpdateStreamer(t, "s1");
            await s.SendMessageChunkAsync("hi", CancellationToken.None);
            AcpSchema.AssertValid("SessionNotification", ParamsOf(t.Messages[0]));
        }

        [Fact]
        public async Task SessionUpdate_Thinking_IsSchemaValid()
        {
            var t = new CapturingTransport();
            var s = new SessionUpdateStreamer(t, "s1");
            await s.SendThinkingAsync("pondering", CancellationToken.None);
            AcpSchema.AssertValid("SessionNotification", ParamsOf(t.Messages[0]));
        }

        [Fact]
        public async Task SessionUpdate_ToolCall_IsSchemaValid()
        {
            var t = new CapturingTransport();
            var s = new SessionUpdateStreamer(t, "s1");
            await s.SendToolCallAsync(new ToolCall { Id = "tc1", Name = "read", Kind = "read", Status = "pending" }, CancellationToken.None);
            AcpSchema.AssertValid("SessionNotification", ParamsOf(t.Messages[0]));
        }

        [Fact]
        public async Task SessionUpdate_ToolCallUpdate_IsSchemaValid()
        {
            var t = new CapturingTransport();
            var s = new SessionUpdateStreamer(t, "s1");
            await s.SendToolResultAsync(new ToolResult { CallId = "tc1", Content = "done" }, CancellationToken.None);
            AcpSchema.AssertValid("SessionNotification", ParamsOf(t.Messages[0]));
        }

        [Fact]
        public async Task SessionUpdate_Plan_IsSchemaValid()
        {
            var t = new CapturingTransport();
            var s = new SessionUpdateStreamer(t, "s1");
            await s.SendExecutionPlanAsync(new ExecutionPlan
            {
                Entries = new List<PlanEntry> { new() { Content = "step", Priority = "high", Status = "pending" } }
            }, CancellationToken.None);
            AcpSchema.AssertValid("SessionNotification", ParamsOf(t.Messages[0]));
        }

        [Fact]
        public void InvalidInitializeResponse_FailsValidation()
        {
            // Missing required protocolVersion.
            var result = AcpSchema.Validate("InitializeResponse", "{\"authMethods\":[]}");
            Assert.False(result.IsValid);
        }

        private sealed class CapturingTransport : ITransport
        {
            public List<string> Messages { get; } = new();
            public bool IsConnected => true;
            public Task<string> ReadMessageAsync(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
            public Task WriteMessageAsync(string message, CancellationToken cancellationToken = default)
            {
                Messages.Add(message);
                return Task.CompletedTask;
            }
            public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void Dispose() { }
        }
    }
}
