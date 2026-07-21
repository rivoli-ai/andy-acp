using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Protocol;
using Andy.Acp.Tests.Schema;
using Xunit;

namespace Andy.Acp.Tests.Protocol
{
    /// <summary>
    /// Tests for the v1 gap-closure epic (#26): grouped select config options,
    /// additionalDirectories capability marker, and agent-declared MCP transport
    /// capabilities with mcpServers validation.
    /// </summary>
    public class GapClosureTests
    {
        // ---- grouped select options -------------------------------------------------

        [Fact]
        public void GroupedSelectOptions_SerializeSchemaValid()
        {
            var option = new SessionConfigOption
            {
                Type = "select",
                Id = "model",
                Name = "Model",
                Category = "model",
                CurrentValueId = "claude-fable-5",
                Groups = new List<SessionConfigSelectGroup>
                {
                    new()
                    {
                        Group = "anthropic",
                        Name = "Anthropic",
                        Options = new List<SessionConfigSelectOption>
                        {
                            new() { Value = "claude-fable-5", Name = "Fable 5" },
                            new() { Value = "claude-opus-4-8", Name = "Opus 4.8" }
                        }
                    },
                    new()
                    {
                        Group = "local",
                        Name = "Local",
                        Options = new List<SessionConfigSelectOption>
                        {
                            new() { Value = "llama", Name = "Llama" }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(new { configOptions = new[] { option } }, JsonRpcSerializer.Options);
            AcpSchema.AssertValid("SetSessionConfigOptionResponse", json);

            // The wire options member must be the group array.
            var options = JsonDocument.Parse(json).RootElement
                .GetProperty("configOptions")[0].GetProperty("options");
            Assert.Equal(2, options.GetArrayLength());
            Assert.Equal("anthropic", options[0].GetProperty("group").GetString());
            Assert.Equal(2, options[0].GetProperty("options").GetArrayLength());
        }

        [Fact]
        public void FlatSelectOptions_StillSerializeFlat()
        {
            var option = new SessionConfigOption
            {
                Type = "select",
                Id = "model",
                Name = "Model",
                CurrentValueId = "m1",
                Options = new List<SessionConfigSelectOption> { new() { Value = "m1", Name = "M1" } }
            };

            var json = JsonSerializer.Serialize(new { configOptions = new[] { option } }, JsonRpcSerializer.Options);
            AcpSchema.AssertValid("SetSessionConfigOptionResponse", json);

            var options = JsonDocument.Parse(json).RootElement
                .GetProperty("configOptions")[0].GetProperty("options");
            Assert.Equal("m1", options[0].GetProperty("value").GetString());
        }

        // ---- capability advertisement ----------------------------------------------

        [Fact]
        public void InitializeResponse_WithAdditionalDirectoriesAndMcpCaps_IsSchemaValid()
        {
            var result = new AcpInitializeResult
            {
                ProtocolVersion = AcpVersions.V1,
                AgentInfo = new Implementation { Name = "T", Version = "1.0" },
                AgentCapabilities = new AcpAgentCapabilities
                {
                    McpCapabilities = new AcpMcpCapabilities { Http = true, Sse = true },
                    SessionCapabilities = new AcpSessionCapabilities
                    {
                        AdditionalDirectories = new CapabilityMarker()
                    }
                }
            };

            var json = JsonSerializer.Serialize(result, JsonRpcSerializer.Options);
            AcpSchema.AssertValid("InitializeResponse", json);

            var caps = JsonDocument.Parse(json).RootElement.GetProperty("agentCapabilities");
            Assert.True(caps.GetProperty("mcpCapabilities").GetProperty("http").GetBoolean());
            Assert.True(caps.GetProperty("sessionCapabilities").TryGetProperty("additionalDirectories", out _));
            // Markers not set must be absent, not null.
            Assert.False(caps.GetProperty("sessionCapabilities").TryGetProperty("list", out _));
        }

        // ---- MCP server validation --------------------------------------------------

        private static (JsonRpcHandler rpc, McpAgent agent) Setup(bool http, bool sse)
        {
            var rpc = new JsonRpcHandler();
            var agent = new McpAgent { Http = http, Sse = sse };
            var state = new AcpConnectionState { Initialized = true };
            new AcpSessionHandler(agent, rpc, state).RegisterMethods();
            return (rpc, agent);
        }

        private static JsonRpcRequest NewSession(string mcpServersJson) => new()
        {
            Method = "session/new",
            Id = 1,
            Params = JsonDocument.Parse(
                "{\"cwd\":\"/tmp\",\"mcpServers\":" + mcpServersJson + "}").RootElement.Clone()
        };

        [Fact]
        public async Task HttpMcpServer_WithoutCapability_IsRejected()
        {
            var (rpc, agent) = Setup(http: false, sse: false);

            var resp = await rpc.HandleRequestAsync(NewSession(
                """[{"type":"http","name":"srv","url":"https://x","headers":[]}]"""));

            Assert.True(resp!.IsError);
            Assert.Equal(JsonRpcErrorCodes.InvalidParams, resp.Error!.Code);
            Assert.Null(agent.LastNew);
        }

        [Fact]
        public async Task HttpMcpServer_WithCapability_IsAccepted()
        {
            var (rpc, agent) = Setup(http: true, sse: false);

            var resp = await rpc.HandleRequestAsync(NewSession(
                """[{"type":"http","name":"srv","url":"https://x","headers":[]}]"""));

            Assert.True(resp!.IsSuccess);
            Assert.Equal("http", agent.LastNew!.McpServers[0].Type);
            Assert.Equal("https://x", agent.LastNew.McpServers[0].Url);
        }

        [Fact]
        public async Task SseMcpServer_WithoutCapability_IsRejected()
        {
            var (rpc, _) = Setup(http: true, sse: false);

            var resp = await rpc.HandleRequestAsync(NewSession(
                """[{"type":"sse","name":"srv","url":"https://x","headers":[]}]"""));

            Assert.Equal(JsonRpcErrorCodes.InvalidParams, resp!.Error!.Code);
        }

        [Fact]
        public async Task StdioMcpServer_AlwaysAccepted()
        {
            var (rpc, agent) = Setup(http: false, sse: false);

            var resp = await rpc.HandleRequestAsync(NewSession(
                """[{"name":"srv","command":"/bin/tool","args":[],"env":[]}]"""));

            Assert.True(resp!.IsSuccess);
            Assert.Equal("/bin/tool", agent.LastNew!.McpServers[0].Command);
        }

        private sealed class McpAgent : IAgentProvider
        {
            public bool Http;
            public bool Sse;
            public NewSessionParams? LastNew;

            public Task<SessionMetadata> CreateSessionAsync(NewSessionParams? parameters, CancellationToken ct)
            {
                LastNew = parameters;
                return Task.FromResult(new SessionMetadata { SessionId = "s1" });
            }

            public Task<AgentResponse> ProcessPromptAsync(string sessionId, PromptMessage prompt, IResponseStreamer streamer, CancellationToken ct)
                => Task.FromResult(new AgentResponse { StopReason = StopReason.Completed });
            public Task<SessionMetadata?> LoadSessionAsync(LoadSessionParams parameters, IResponseStreamer streamer, CancellationToken ct)
                => Task.FromResult<SessionMetadata?>(null);
            public Task CancelSessionAsync(string sessionId, CancellationToken ct) => Task.CompletedTask;
            public Task<bool> SetSessionModeAsync(string sessionId, string modeId, CancellationToken ct) => Task.FromResult(true);
            public AgentCapabilities GetCapabilities() => new() { McpHttp = Http, McpSse = Sse };
        }
    }
}
