using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Protocol;
using Andy.Acp.Core.Protocol.V2;
using Andy.Acp.Core.Server;
using Andy.Acp.Tests.Schema;
using Andy.Acp.Tests.Server;
using Xunit;

namespace Andy.Acp.Tests.Protocol.V2
{
    /// <summary>
    /// Epic #30 tests: version negotiation matrix (#27), the v2 alpha method surface
    /// (#28), and v2 schema validation plus cross-version behavior (#29).
    /// </summary>
    public class V2ProtocolTests
    {
        // ---- negotiation matrix (#27) ----------------------------------------------

        private static (AcpProtocolHandler handler, AcpConnectionState state) Handler(bool v2)
        {
            var state = new AcpConnectionState();
            var handler = new AcpProtocolHandler(
                state,
                new ServerInfo { Name = "T", Version = "1.0" },
                new AcpAgentCapabilities(),
                supportedVersions: v2 ? AcpVersions.All : AcpVersions.Default,
                v2Capabilities: v2 ? new V2AgentCapabilities { Session = new V2SessionCapabilities() } : null);
            return (handler, state);
        }

        private static JsonElement Params(object o) =>
            JsonDocument.Parse(JsonSerializer.Serialize(o, JsonRpcSerializer.Options)).RootElement.Clone();

        [Theory]
        [InlineData(false, 1, 1)] // v1-only server, v1 request -> v1
        [InlineData(false, 2, 1)] // v1-only server, v2 request -> negotiated down to v1
        [InlineData(true, 1, 1)]  // v1+v2 server, v1 request -> v1
        [InlineData(true, 2, 2)]  // v1+v2 server, explicit v2 request -> v2
        [InlineData(true, 99, 1)] // unsupported request -> stable fallback, not alpha
        [InlineData(true, 0, 1)]  // unsupported low request -> stable fallback, not alpha
        public async Task Negotiation_Matrix(bool v2Enabled, int requested, int expected)
        {
            var (handler, state) = Handler(v2Enabled);

            var result = await handler.HandleInitializeAsync(Params(new { protocolVersion = requested }));

            Assert.Equal(expected, state.ProtocolVersion);
            var json = JsonSerializer.Serialize(result, result!.GetType(), JsonRpcSerializer.Options);
            Assert.Equal(expected, JsonDocument.Parse(json).RootElement.GetProperty("protocolVersion").GetInt32());
        }

        [Fact]
        public async Task V2Initialize_ResponseIsV2ShapedAndSchemaValid()
        {
            var (handler, _) = Handler(v2: true);

            var result = await handler.HandleInitializeAsync(Params(new
            {
                protocolVersion = 2,
                info = new { name = "client", version = "1.0" }
            }));

            var json = JsonSerializer.Serialize(result, result!.GetType(), JsonRpcSerializer.Options);
            AcpSchema.AssertValidV2("InitializeResponse", json);

            var root = JsonDocument.Parse(json).RootElement;
            // v2 shape: `info` required, no v1 `agentInfo`/`agentCapabilities` members.
            Assert.True(root.TryGetProperty("info", out _));
            Assert.False(root.TryGetProperty("agentInfo", out _));
            Assert.False(root.TryGetProperty("agentCapabilities", out _));
        }

        [Fact]
        public async Task V1Initialize_OnV2EnabledServer_KeepsV1Shape()
        {
            var (handler, _) = Handler(v2: true);

            var result = await handler.HandleInitializeAsync(Params(new { protocolVersion = 1 }));

            var json = JsonSerializer.Serialize(result, result!.GetType(), JsonRpcSerializer.Options);
            AcpSchema.AssertValid("InitializeResponse", json);
            Assert.True(JsonDocument.Parse(json).RootElement.TryGetProperty("agentInfo", out _));
        }

        // ---- e2e v2 flow + version gating (#28, #29) --------------------------------

        [Fact]
        public async Task V2PromptFlow_StateUpdatesAndEmptyAck_AllSchemaValid()
        {
            var harness = new InMemoryDuplex();
            var server = new AcpServer(new V2Agent(), options: new AcpServerOptions { EnableV2Alpha = true });

            using var cts = new CancellationTokenSource();
            var serverTask = server.RunAsync(harness.ServerTransport, cts.Token);

            await harness.SendAsync("""{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":2,"info":{"name":"c","version":"1"}}}""");
            var initJson = await harness.ReadMessageAsync();
            AcpSchema.AssertValidV2("InitializeResponse",
                JsonDocument.Parse(initJson).RootElement.GetProperty("result").GetRawText());

            // v2: mcpServers is optional on session/new.
            await harness.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"session/new","params":{"cwd":"/tmp"}}""");
            var newDoc = JsonDocument.Parse(await harness.ReadMessageAsync());
            AcpSchema.AssertValidV2("NewSessionResponse", newDoc.RootElement.GetProperty("result").GetRawText());
            var sid = newDoc.RootElement.GetProperty("result").GetProperty("sessionId").GetString();

            await harness.SendAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/prompt\",\"params\":{\"sessionId\":\"" +
                sid + "\",\"prompt\":[{\"type\":\"text\",\"text\":\"hi\"}]}}");

            var states = new List<string>();
            string? chunkMessageId = null;
            string? idleStopReason = null;
            JsonElement? promptResult = null;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (promptResult == null)
            {
                var root = JsonDocument.Parse(await harness.ReadMessageAsync(timeout.Token)).RootElement.Clone();
                if (root.TryGetProperty("method", out var m) && m.GetString() == "session/update")
                {
                    AcpSchema.AssertValidV2("UpdateSessionNotification", root.GetProperty("params").GetRawText());
                    var update = root.GetProperty("params").GetProperty("update");
                    var kind = update.GetProperty("sessionUpdate").GetString()!;
                    if (kind == "state_update")
                    {
                        states.Add(update.GetProperty("state").GetString()!);
                        if (update.TryGetProperty("stopReason", out var sr))
                            idleStopReason = sr.GetString();
                    }
                    else if (kind == "agent_message_chunk")
                    {
                        chunkMessageId = update.GetProperty("messageId").GetString();
                    }
                }
                else if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt64() == 2)
                {
                    promptResult = root.GetProperty("result").Clone();
                }
            }

            // v2 lifecycle: running -> ... -> idle(stopReason); the response is an empty ACK.
            Assert.Equal(new[] { "running", "idle" }, states);
            Assert.Equal("end_turn", idleStopReason);
            Assert.False(string.IsNullOrEmpty(chunkMessageId));
            Assert.False(promptResult.Value.TryGetProperty("stopReason", out _));

            cts.Cancel();
            harness.Complete();
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }

        [Fact]
        public async Task VersionGating_V1MethodOnV2Connection_IsMethodNotFound()
        {
            var harness = new InMemoryDuplex();
            var server = new AcpServer(new V2Agent(), options: new AcpServerOptions { EnableV2Alpha = true });

            using var cts = new CancellationTokenSource();
            var serverTask = server.RunAsync(harness.ServerTransport, cts.Token);

            await harness.SendAsync("""{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":2,"info":{"name":"c","version":"1"}}}""");
            await harness.ReadMessageAsync();

            // session/load and session/set_mode do not exist in v2.
            await harness.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"session/load","params":{"sessionId":"s","cwd":"/tmp","mcpServers":[]}}""");
            var doc = JsonDocument.Parse(await harness.ReadMessageAsync());
            Assert.Equal(JsonRpcErrorCodes.MethodNotFound, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());

            cts.Cancel();
            harness.Complete();
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }

        [Fact]
        public async Task VersionGating_V2MethodOnV1Connection_IsMethodNotFound()
        {
            var harness = new InMemoryDuplex();
            var server = new AcpServer(new V2Agent(), options: new AcpServerOptions { EnableV2Alpha = true });

            using var cts = new CancellationTokenSource();
            var serverTask = server.RunAsync(harness.ServerTransport, cts.Token);

            // Negotiate v1 on the v2-capable server.
            await harness.SendAsync("""{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":1}}""");
            await harness.ReadMessageAsync();

            await harness.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"auth/login","params":{"methodId":"x"}}""");
            var doc = JsonDocument.Parse(await harness.ReadMessageAsync());
            Assert.Equal(JsonRpcErrorCodes.MethodNotFound, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());

            cts.Cancel();
            harness.Complete();
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }

        [Fact]
        public async Task CrossVersion_V1ClientOnV2EnabledServer_GetsV1Behavior()
        {
            var harness = new InMemoryDuplex();
            var server = new AcpServer(new V2Agent(), options: new AcpServerOptions { EnableV2Alpha = true });

            using var cts = new CancellationTokenSource();
            var serverTask = server.RunAsync(harness.ServerTransport, cts.Token);

            await harness.SendAsync("""{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":1}}""");
            await harness.ReadMessageAsync();

            await harness.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"session/new","params":{"cwd":"/tmp","mcpServers":[]}}""");
            var sid = JsonDocument.Parse(await harness.ReadMessageAsync())
                .RootElement.GetProperty("result").GetProperty("sessionId").GetString();

            await harness.SendAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/prompt\",\"params\":{\"sessionId\":\"" +
                sid + "\",\"prompt\":[{\"type\":\"text\",\"text\":\"hi\"}]}}");

            // v1 behavior: the prompt response itself carries stopReason.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                var root = JsonDocument.Parse(await harness.ReadMessageAsync(timeout.Token)).RootElement;
                if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt64() == 2)
                {
                    Assert.Equal("end_turn", root.GetProperty("result").GetProperty("stopReason").GetString());
                    break;
                }
            }

            cts.Cancel();
            harness.Complete();
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }

        // ---- v2 streamer wire shapes (#29) ------------------------------------------

        [Fact]
        public async Task V2Streamer_PlanUpdate_IsSchemaValid()
        {
            var (streamer, transport) = V2Streamer();
            await streamer.SendExecutionPlanAsync(new ExecutionPlan
            {
                Entries = new List<PlanEntry> { new() { Content = "step", Priority = "high", Status = "pending" } }
            }, CancellationToken.None);

            var p = ParamsOf(transport.Messages[0]);
            AcpSchema.AssertValidV2("UpdateSessionNotification", p);
            var plan = JsonDocument.Parse(p).RootElement.GetProperty("update").GetProperty("plan");
            Assert.Equal("items", plan.GetProperty("type").GetString());
            Assert.False(string.IsNullOrEmpty(plan.GetProperty("planId").GetString()));
        }

        [Fact]
        public async Task V2Streamer_ToolCallWithDiff_MapsToV2DiffShape()
        {
            var (streamer, transport) = V2Streamer();
            await streamer.SendToolCallAsync(new ToolCall
            {
                Id = "tc1",
                Name = "edit",
                Kind = "edit",
                ContentItems = new List<ToolCallContent>
                {
                    new() { Type = "diff", Path = "/a.cs", OldText = "old", NewText = "new" }
                }
            }, CancellationToken.None);

            var p = ParamsOf(transport.Messages[0]);
            AcpSchema.AssertValidV2("UpdateSessionNotification", p);
            var update = JsonDocument.Parse(p).RootElement.GetProperty("update");
            // v2 has no tool_call variant: creation is the first tool_call_update.
            Assert.Equal("tool_call_update", update.GetProperty("sessionUpdate").GetString());
            var change = update.GetProperty("content")[0].GetProperty("changes")[0];
            Assert.Equal("modify", change.GetProperty("operation").GetString());
            Assert.Equal("/a.cs", change.GetProperty("path").GetString());
        }

        [Fact]
        public async Task V2Streamer_ConfigOptions_UseConfigIdAndGroupId()
        {
            var (streamer, transport) = V2Streamer();
            await streamer.SendConfigOptionsAsync(new List<SessionConfigOption>
            {
                new()
                {
                    Type = "select",
                    Id = "model",
                    Name = "Model",
                    CurrentValueId = "m1",
                    Groups = new List<SessionConfigSelectGroup>
                    {
                        new()
                        {
                            Group = "g1",
                            Name = "Group",
                            Options = new List<SessionConfigSelectOption> { new() { Value = "m1", Name = "M1" } }
                        }
                    }
                }
            }, CancellationToken.None);

            var p = ParamsOf(transport.Messages[0]);
            AcpSchema.AssertValidV2("UpdateSessionNotification", p);
            var option = JsonDocument.Parse(p).RootElement.GetProperty("update").GetProperty("configOptions")[0];
            Assert.Equal("model", option.GetProperty("configId").GetString());
            Assert.False(option.TryGetProperty("id", out _)); // v1 field name must not leak
            Assert.Equal("g1", option.GetProperty("options")[0].GetProperty("groupId").GetString());
        }

        [Fact]
        public async Task V2Streamer_CurrentMode_IsDroppedWithoutThrowing()
        {
            // v2 has no current_mode_update; the version-neutral streamer API must not
            // crash an in-flight prompt — the update is dropped (with a log warning).
            var (streamer, transport) = V2Streamer();
            await streamer.SendCurrentModeAsync("chat", CancellationToken.None);
            Assert.Empty(transport.Messages);
        }

        private static (SessionUpdateStreamerV2 streamer, CapturingTransport transport) V2Streamer()
        {
            var transport = new CapturingTransport();
            return (new SessionUpdateStreamerV2(transport, "s1"), transport);
        }

        private static string ParamsOf(string notificationJson)
            => JsonDocument.Parse(notificationJson).RootElement.GetProperty("params").GetRawText();

        /// <summary>Streaming agent used across the v2 flow tests.</summary>
        private sealed class V2Agent : IAgentProvider
        {
            public async Task<AgentResponse> ProcessPromptAsync(string sessionId, PromptMessage prompt, IResponseStreamer streamer, CancellationToken ct)
            {
                await streamer.SendMessageChunkAsync("Hello", ct);
                return new AgentResponse { StopReason = StopReason.Completed };
            }

            public Task<SessionMetadata> CreateSessionAsync(NewSessionParams? parameters, CancellationToken ct)
                => Task.FromResult(new SessionMetadata { SessionId = "s1" });
            public Task<SessionMetadata?> LoadSessionAsync(LoadSessionParams parameters, IResponseStreamer streamer, CancellationToken ct)
                => Task.FromResult<SessionMetadata?>(null);
            public Task CancelSessionAsync(string sessionId, CancellationToken ct) => Task.CompletedTask;
            public Task<bool> SetSessionModeAsync(string sessionId, string modeId, CancellationToken ct) => Task.FromResult(true);
            public AgentCapabilities GetCapabilities() => new();
        }

        private sealed class CapturingTransport : Andy.Acp.Core.Transport.ITransport
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
