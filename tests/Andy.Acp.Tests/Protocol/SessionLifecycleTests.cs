using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Protocol;
using Andy.Acp.Core.Transport;
using Xunit;

namespace Andy.Acp.Tests.Protocol
{
    /// <summary>
    /// Tests for ACP session lifecycle behavior (issue #12): initialize-before-session
    /// ordering, required cwd/mcpServers, modeId for set_mode, and session/load replay.
    /// </summary>
    public class SessionLifecycleTests
    {
        private static (JsonRpcHandler rpc, LifecycleAgent agent, CapturingTransport transport) Setup(bool initialized)
        {
            var rpc = new JsonRpcHandler();
            var agent = new LifecycleAgent();
            var state = new AcpConnectionState { Initialized = initialized };
            var sessionHandler = new AcpSessionHandler(agent, rpc, state);
            var transport = new CapturingTransport();
            sessionHandler.SetTransport(transport);
            sessionHandler.RegisterMethods();
            return (rpc, agent, transport);
        }

        private static JsonRpcRequest Req(string method, object paramsObj, long id = 1) => new()
        {
            Method = method,
            Id = id,
            Params = JsonDocument.Parse(JsonSerializer.Serialize(paramsObj)).RootElement.Clone()
        };

        [Fact]
        public async Task SessionNew_BeforeInitialize_ThrowsNotInitialized()
        {
            var (rpc, _, _) = Setup(initialized: false);

            var resp = await rpc.HandleRequestAsync(Req("session/new", new { cwd = "/tmp", mcpServers = new object[0] }));

            Assert.True(resp!.IsError);
            Assert.Equal(JsonRpcErrorCodes.SessionNotInitialized, resp.Error!.Code);
        }

        [Fact]
        public async Task SessionNew_MissingCwd_InvalidParams()
        {
            var (rpc, _, _) = Setup(initialized: true);

            var resp = await rpc.HandleRequestAsync(Req("session/new", new { mcpServers = new object[0] }));

            Assert.True(resp!.IsError);
            Assert.Equal(JsonRpcErrorCodes.InvalidParams, resp.Error!.Code);
        }

        [Fact]
        public async Task SessionNew_PassesCwdAndMcpServersToAgent()
        {
            var (rpc, agent, _) = Setup(initialized: true);

            var resp = await rpc.HandleRequestAsync(Req("session/new", new
            {
                cwd = "/work/dir",
                mcpServers = new[] { new { name = "srv", command = "/bin/tool", args = new[] { "--x" }, env = new object[0] } }
            }));

            Assert.True(resp!.IsSuccess);
            Assert.Equal("/work/dir", agent.LastNew!.Cwd);
            Assert.Single(agent.LastNew.McpServers);
            Assert.Equal("srv", agent.LastNew.McpServers[0].Name);
            Assert.Equal("/bin/tool", agent.LastNew.McpServers[0].Command);

            var doc = JsonDocument.Parse(JsonRpcSerializer.Serialize(resp));
            Assert.Equal("sess-1", doc.RootElement.GetProperty("result").GetProperty("sessionId").GetString());
        }

        [Fact]
        public async Task SetMode_UsesModeId_AndReturnsEmptyResult()
        {
            var (rpc, agent, _) = Setup(initialized: true);

            var resp = await rpc.HandleRequestAsync(Req("session/set_mode", new { sessionId = "s", modeId = "architect" }));

            Assert.True(resp!.IsSuccess);
            Assert.Equal("architect", agent.LastModeId);
        }

        [Fact]
        public async Task Load_ReplaysHistoryAndReturnsModes()
        {
            var (rpc, agent, transport) = Setup(initialized: true);

            var resp = await rpc.HandleRequestAsync(Req("session/load", new
            {
                sessionId = "s1",
                cwd = "/work",
                mcpServers = new object[0]
            }));

            Assert.True(resp!.IsSuccess);
            // The agent replayed one message chunk during load.
            Assert.Contains(transport.Messages, m => m.Contains("agent_message_chunk") && m.Contains("replayed"));
            // Response includes modes and omits sessionId.
            var doc = JsonDocument.Parse(JsonRpcSerializer.Serialize(resp));
            var result = doc.RootElement.GetProperty("result");
            Assert.False(result.TryGetProperty("sessionId", out _));
            Assert.Equal("chat", result.GetProperty("modes").GetProperty("currentModeId").GetString());
        }

        private sealed class LifecycleAgent : IAgentProvider
        {
            public NewSessionParams? LastNew;
            public string? LastModeId;

            public Task<SessionMetadata> CreateSessionAsync(NewSessionParams? parameters, CancellationToken cancellationToken)
            {
                LastNew = parameters;
                return Task.FromResult(new SessionMetadata { SessionId = "sess-1" });
            }

            public async Task<SessionMetadata?> LoadSessionAsync(LoadSessionParams parameters, IResponseStreamer streamer, CancellationToken cancellationToken)
            {
                await streamer.SendMessageChunkAsync("replayed", cancellationToken);
                return new SessionMetadata
                {
                    SessionId = parameters.SessionId,
                    Modes = new SessionModeState
                    {
                        CurrentModeId = "chat",
                        AvailableModes = new List<SessionMode> { new() { Id = "chat", Name = "Chat" } }
                    }
                };
            }

            public Task<AgentResponse> ProcessPromptAsync(string sessionId, PromptMessage prompt, IResponseStreamer streamer, CancellationToken cancellationToken)
                => Task.FromResult(new AgentResponse { StopReason = StopReason.Completed });

            public Task CancelSessionAsync(string sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<bool> SetSessionModeAsync(string sessionId, string modeId, CancellationToken cancellationToken)
            {
                LastModeId = modeId;
                return Task.FromResult(true);
            }

            public AgentCapabilities GetCapabilities() => new() { LoadSession = true };
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
