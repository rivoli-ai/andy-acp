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

        // ---- optional surface -------------------------------------------------------

        [Fact]
        public void InitializeResponse_WithAuthAndSessionCapabilities_IsSchemaValid()
        {
            var result = new AcpInitializeResult
            {
                ProtocolVersion = 1,
                AgentInfo = new Implementation { Name = "T", Version = "1.0" },
                AgentCapabilities = new AcpAgentCapabilities
                {
                    LoadSession = true,
                    SessionCapabilities = new AcpSessionCapabilities
                    {
                        List = new CapabilityMarker(),
                        Delete = new CapabilityMarker(),
                        Resume = new CapabilityMarker(),
                        Close = new CapabilityMarker()
                    },
                    Auth = new AcpAgentAuthCapabilities { Logout = new CapabilityMarker() }
                },
                AuthMethods = new List<AuthMethodDescription>
                {
                    new() { Id = "api-key", Name = "API key", Description = "Use an API key" }
                }
            };

            AcpSchema.AssertValid("InitializeResponse", JsonSerializer.Serialize(result, JsonRpcSerializer.Options));
        }

        [Fact]
        public void SetSessionConfigOptionResponse_IsSchemaValid()
        {
            var options = new List<SessionConfigOption>
            {
                new()
                {
                    Type = "select",
                    Id = "model",
                    Name = "Model",
                    Category = "model",
                    CurrentValueId = "claude-fable-5",
                    Options = new List<SessionConfigSelectOption>
                    {
                        new() { Value = "claude-fable-5", Name = "Fable 5" },
                        new() { Value = "claude-opus-4-8", Name = "Opus 4.8" }
                    }
                },
                new()
                {
                    Type = "boolean",
                    Id = "thinking",
                    Name = "Extended thinking",
                    Category = "thought_level",
                    CurrentBoolValue = true
                }
            };

            var json = JsonSerializer.Serialize(new { configOptions = options }, JsonRpcSerializer.Options);
            AcpSchema.AssertValid("SetSessionConfigOptionResponse", json);
        }

        [Fact]
        public void ListSessionsResponse_IsSchemaValid()
        {
            AcpSchema.AssertValid("ListSessionsResponse",
                "{\"sessions\":[{\"sessionId\":\"s1\",\"cwd\":\"/tmp\",\"title\":\"First\",\"updatedAt\":\"2026-07-01T00:00:00.0000000+00:00\"}],\"nextCursor\":\"n\"}");
        }

        [Fact]
        public void ResumeSessionResponse_IsSchemaValid()
        {
            AcpSchema.AssertValid("ResumeSessionResponse",
                "{\"modes\":{\"currentModeId\":\"chat\",\"availableModes\":[{\"id\":\"chat\",\"name\":\"Chat\"}]}}");
        }

        [Fact]
        public async Task SessionUpdate_AvailableCommands_IsSchemaValid()
        {
            var t = new CapturingTransport();
            var s = new SessionUpdateStreamer(t, "s1");
            await s.SendAvailableCommandsAsync(new List<AvailableCommand>
            {
                new() { Name = "web", Description = "Search the web", InputHint = "query" },
                new() { Name = "clear", Description = "Clear the conversation" }
            }, CancellationToken.None);
            AcpSchema.AssertValid("SessionNotification", ParamsOf(t.Messages[0]));
        }

        [Fact]
        public async Task SessionUpdate_CurrentMode_IsSchemaValid()
        {
            var t = new CapturingTransport();
            var s = new SessionUpdateStreamer(t, "s1");
            await s.SendCurrentModeAsync("architect", CancellationToken.None);
            AcpSchema.AssertValid("SessionNotification", ParamsOf(t.Messages[0]));
        }

        [Fact]
        public async Task SessionUpdate_ConfigOptions_IsSchemaValid()
        {
            var t = new CapturingTransport();
            var s = new SessionUpdateStreamer(t, "s1");
            await s.SendConfigOptionsAsync(new List<SessionConfigOption>
            {
                new()
                {
                    Type = "select",
                    Id = "model",
                    Name = "Model",
                    CurrentValueId = "m1",
                    Options = new List<SessionConfigSelectOption> { new() { Value = "m1", Name = "M1" } }
                }
            }, CancellationToken.None);
            AcpSchema.AssertValid("SessionNotification", ParamsOf(t.Messages[0]));
        }

        [Fact]
        public async Task SessionUpdate_SessionInfo_IsSchemaValid()
        {
            var t = new CapturingTransport();
            var s = new SessionUpdateStreamer(t, "s1");
            await s.SendSessionInfoAsync("My session", new System.DateTimeOffset(2026, 7, 21, 0, 0, 0, System.TimeSpan.Zero), CancellationToken.None);
            AcpSchema.AssertValid("SessionNotification", ParamsOf(t.Messages[0]));
        }

        [Fact]
        public async Task SessionUpdate_Usage_IsSchemaValid()
        {
            var t = new CapturingTransport();
            var s = new SessionUpdateStreamer(t, "s1");
            await s.SendUsageAsync(1200, 200000, new UsageCost { Amount = 0.42, Currency = "USD" }, CancellationToken.None);
            AcpSchema.AssertValid("SessionNotification", ParamsOf(t.Messages[0]));
        }

        [Fact]
        public async Task SessionUpdate_ToolCallWithDiffTerminalAndLocations_IsSchemaValid()
        {
            var t = new CapturingTransport();
            var s = new SessionUpdateStreamer(t, "s1");
            await s.SendToolCallAsync(new ToolCall
            {
                Id = "tc1",
                Name = "edit_file",
                Kind = "edit",
                Status = "in_progress",
                Locations = new List<ToolCallLocation> { new() { Path = "/src/a.cs", Line = 12 } },
                ContentItems = new List<ToolCallContent>
                {
                    new() { Type = "diff", Path = "/src/a.cs", OldText = "old", NewText = "new" },
                    new() { Type = "terminal", TerminalId = "term-1" },
                    new() { Type = "content", Text = "preview" }
                }
            }, CancellationToken.None);
            AcpSchema.AssertValid("SessionNotification", ParamsOf(t.Messages[0]));
        }

        [Fact]
        public async Task SessionUpdate_ToolCallUpdateWithRawOutput_IsSchemaValid()
        {
            var t = new CapturingTransport();
            var s = new SessionUpdateStreamer(t, "s1");
            await s.SendToolResultAsync(new ToolResult
            {
                CallId = "tc1",
                Content = "done",
                RawOutput = new { exitCode = 0 },
                Locations = new List<ToolCallLocation> { new() { Path = "/src/a.cs" } }
            }, CancellationToken.None);
            AcpSchema.AssertValid("SessionNotification", ParamsOf(t.Messages[0]));
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
