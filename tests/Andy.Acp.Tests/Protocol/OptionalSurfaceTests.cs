using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Tests for the optional ACP v1 surface: authenticate/logout with auth gating,
    /// session/set_config_option, and the session catalog
    /// (session/list, delete, resume, close).
    /// </summary>
    public class OptionalSurfaceTests
    {
        private static JsonElement Params(object o) =>
            JsonDocument.Parse(JsonSerializer.Serialize(o, JsonRpcSerializer.Options)).RootElement.Clone();

        private static JsonRpcRequest Req(string method, object p, long id = 1) =>
            new() { Method = method, Id = id, Params = Params(p) };

        private static (JsonRpcHandler rpc, FullAgent agent, AcpConnectionState state) Setup()
        {
            var rpc = new JsonRpcHandler();
            var agent = new FullAgent();
            var state = new AcpConnectionState();
            var protocolHandler = new AcpProtocolHandler(
                state,
                new ServerInfo { Name = "T", Version = "1.0" },
                new AcpAgentCapabilities(),
                authProvider: agent);
            protocolHandler.RegisterMethods(rpc);
            var sessionHandler = new AcpSessionHandler(agent, rpc, state);
            sessionHandler.RegisterMethods();
            return (rpc, agent, state);
        }

        private static async Task InitializeAsync(JsonRpcHandler rpc)
        {
            var resp = await rpc.HandleRequestAsync(Req("initialize", new { protocolVersion = 1 }, 99));
            Assert.True(resp!.IsSuccess);
        }

        // ---- authenticate / logout -------------------------------------------------

        [Fact]
        public async Task SessionNew_BeforeAuthenticate_ReturnsAuthRequired()
        {
            var (rpc, _, _) = Setup();
            await InitializeAsync(rpc);

            var resp = await rpc.HandleRequestAsync(Req("session/new", new { cwd = "/tmp", mcpServers = Array.Empty<object>() }));

            Assert.True(resp!.IsError);
            Assert.Equal(JsonRpcErrorCodes.AuthRequired, resp.Error!.Code);
        }

        [Fact]
        public async Task Authenticate_UnlocksSessionMethods()
        {
            var (rpc, agent, state) = Setup();
            await InitializeAsync(rpc);

            var auth = await rpc.HandleRequestAsync(Req("authenticate", new { methodId = "api-key" }));
            Assert.True(auth!.IsSuccess);
            Assert.True(state.Authenticated);
            Assert.Equal("api-key", agent.AuthenticatedWith);

            var resp = await rpc.HandleRequestAsync(Req("session/new", new { cwd = "/tmp", mcpServers = Array.Empty<object>() }, 2));
            Assert.True(resp!.IsSuccess);
        }

        [Fact]
        public async Task Authenticate_UnknownMethod_InvalidParams()
        {
            var (rpc, _, _) = Setup();
            await InitializeAsync(rpc);

            var resp = await rpc.HandleRequestAsync(Req("authenticate", new { methodId = "nope" }));

            Assert.True(resp!.IsError);
            Assert.Equal(JsonRpcErrorCodes.InvalidParams, resp.Error!.Code);
        }

        [Fact]
        public async Task Logout_RestoresAuthRequirement()
        {
            var (rpc, _, state) = Setup();
            await InitializeAsync(rpc);
            await rpc.HandleRequestAsync(Req("authenticate", new { methodId = "api-key" }));

            var logout = await rpc.HandleRequestAsync(Req("logout", new { }, 3));
            Assert.True(logout!.IsSuccess);
            Assert.False(state.Authenticated);

            var resp = await rpc.HandleRequestAsync(Req("session/new", new { cwd = "/tmp", mcpServers = Array.Empty<object>() }, 4));
            Assert.Equal(JsonRpcErrorCodes.AuthRequired, resp!.Error!.Code);
        }

        [Fact]
        public async Task Initialize_AdvertisesAuthMethods()
        {
            var (rpc, _, _) = Setup();

            var resp = await rpc.HandleRequestAsync(Req("initialize", new { protocolVersion = 1 }, 99));

            var json = JsonRpcSerializer.Serialize(resp!);
            var result = JsonDocument.Parse(json).RootElement.GetProperty("result");
            var methods = result.GetProperty("authMethods");
            Assert.Equal(1, methods.GetArrayLength());
            Assert.Equal("api-key", methods[0].GetProperty("id").GetString());
        }

        // ---- session/set_config_option ---------------------------------------------

        [Fact]
        public async Task SetConfigOption_ValueId_RoundTrips()
        {
            var (rpc, agent, _) = await AuthedSetup();

            var resp = await rpc.HandleRequestAsync(Req("session/set_config_option",
                new { sessionId = "s1", configId = "model", value = "claude-fable-5" }, 5));

            Assert.True(resp!.IsSuccess);
            Assert.Equal("model", agent.LastConfigId);
            Assert.Equal("claude-fable-5", agent.LastConfigValue!.ValueId);

            var json = JsonRpcSerializer.Serialize(resp);
            var options = JsonDocument.Parse(json).RootElement.GetProperty("result").GetProperty("configOptions");
            Assert.Equal("claude-fable-5", options[0].GetProperty("currentValue").GetString());
        }

        [Fact]
        public async Task SetConfigOption_Boolean_RoundTrips()
        {
            var (rpc, agent, _) = await AuthedSetup();

            var resp = await rpc.HandleRequestAsync(Req("session/set_config_option",
                new { sessionId = "s1", configId = "thinking", type = "boolean", value = true }, 5));

            Assert.True(resp!.IsSuccess);
            Assert.True(agent.LastConfigValue!.BoolValue);
        }

        [Fact]
        public async Task SetConfigOption_StructuredValue_InvalidParams()
        {
            var (rpc, _, _) = await AuthedSetup();

            var resp = await rpc.HandleRequestAsync(Req("session/set_config_option",
                new { sessionId = "s1", configId = "model", value = new { nested = true } }, 5));

            Assert.Equal(JsonRpcErrorCodes.InvalidParams, resp!.Error!.Code);
        }

        // ---- session catalog --------------------------------------------------------

        [Fact]
        public async Task ListSessions_ReturnsEntriesAndCursor()
        {
            var (rpc, _, _) = await AuthedSetup();

            var resp = await rpc.HandleRequestAsync(Req("session/list", new { cwd = "/tmp" }, 6));

            var json = JsonRpcSerializer.Serialize(resp!);
            var result = JsonDocument.Parse(json).RootElement.GetProperty("result");
            var sessions = result.GetProperty("sessions");
            Assert.Equal(1, sessions.GetArrayLength());
            Assert.Equal("stored-1", sessions[0].GetProperty("sessionId").GetString());
            Assert.Equal("/tmp", sessions[0].GetProperty("cwd").GetString());
            Assert.Equal("next", result.GetProperty("nextCursor").GetString());
        }

        [Fact]
        public async Task DeleteSession_Unknown_ReturnsResourceNotFound()
        {
            var (rpc, _, _) = await AuthedSetup();

            var resp = await rpc.HandleRequestAsync(Req("session/delete", new { sessionId = "missing" }, 7));

            Assert.Equal(JsonRpcErrorCodes.ResourceNotFound, resp!.Error!.Code);
        }

        [Fact]
        public async Task DeleteSession_Known_Succeeds()
        {
            var (rpc, agent, _) = await AuthedSetup();

            var resp = await rpc.HandleRequestAsync(Req("session/delete", new { sessionId = "stored-1" }, 7));

            Assert.True(resp!.IsSuccess);
            Assert.Equal("stored-1", agent.Deleted);
        }

        [Fact]
        public async Task ResumeSession_ReturnsModesWithoutReplay()
        {
            var (rpc, agent, _) = await AuthedSetup();

            var resp = await rpc.HandleRequestAsync(Req("session/resume",
                new { sessionId = "stored-1", cwd = "/tmp" }, 8));

            Assert.True(resp!.IsSuccess);
            Assert.Equal("stored-1", agent.Resumed);
            var json = JsonRpcSerializer.Serialize(resp);
            var result = JsonDocument.Parse(json).RootElement.GetProperty("result");
            Assert.Equal("chat", result.GetProperty("modes").GetProperty("currentModeId").GetString());
            Assert.False(result.TryGetProperty("sessionId", out _));
        }

        [Fact]
        public async Task CloseSession_Succeeds()
        {
            var (rpc, agent, _) = await AuthedSetup();

            var resp = await rpc.HandleRequestAsync(Req("session/close", new { sessionId = "stored-1" }, 9));

            Assert.True(resp!.IsSuccess);
            Assert.Equal("stored-1", agent.Closed);
        }

        [Fact]
        public async Task CatalogMethods_NotRegistered_WhenAgentLacksInterface()
        {
            var rpc = new JsonRpcHandler();
            var state = new AcpConnectionState { Initialized = true };
            var plainAgent = new PlainAgent();
            new AcpSessionHandler(plainAgent, rpc, state).RegisterMethods();

            Assert.False(rpc.SupportsMethod("session/list"));
            Assert.False(rpc.SupportsMethod("session/set_config_option"));

            var resp = await rpc.HandleRequestAsync(Req("session/list", new { }, 1));
            Assert.Equal(JsonRpcErrorCodes.MethodNotFound, resp!.Error!.Code);
        }

        private static async Task<(JsonRpcHandler rpc, FullAgent agent, AcpConnectionState state)> AuthedSetup()
        {
            var setup = Setup();
            await InitializeAsync(setup.rpc);
            await setup.rpc.HandleRequestAsync(Req("authenticate", new { methodId = "api-key" }, 98));
            return setup;
        }

        /// <summary>Agent implementing every optional provider interface.</summary>
        private sealed class FullAgent : IAgentProvider, IAuthenticationProvider, ISessionConfigProvider, ISessionCatalogProvider
        {
            public string? AuthenticatedWith;
            public string? LastConfigId;
            public SessionConfigValue? LastConfigValue;
            public string? Deleted;
            public string? Resumed;
            public string? Closed;

            // IAgentProvider
            public Task<AgentResponse> ProcessPromptAsync(string sessionId, PromptMessage prompt, IResponseStreamer streamer, CancellationToken ct)
                => Task.FromResult(new AgentResponse { StopReason = StopReason.Completed });
            public Task<SessionMetadata> CreateSessionAsync(NewSessionParams? parameters, CancellationToken ct)
                => Task.FromResult(new SessionMetadata { SessionId = "s1" });
            public Task<SessionMetadata?> LoadSessionAsync(LoadSessionParams parameters, IResponseStreamer streamer, CancellationToken ct)
                => Task.FromResult<SessionMetadata?>(null);
            public Task CancelSessionAsync(string sessionId, CancellationToken ct) => Task.CompletedTask;
            public Task<bool> SetSessionModeAsync(string sessionId, string modeId, CancellationToken ct) => Task.FromResult(true);
            public AgentCapabilities GetCapabilities() => new() { LoadSession = true };

            // IAuthenticationProvider
            public IReadOnlyList<AuthMethod> GetAuthMethods()
                => new List<AuthMethod> { new() { Id = "api-key", Name = "API key" } };
            public bool RequiresAuthentication => true;
            public bool SupportsLogout => true;
            public Task AuthenticateAsync(string methodId, CancellationToken ct)
            {
                AuthenticatedWith = methodId;
                return Task.CompletedTask;
            }
            public Task LogoutAsync(CancellationToken ct) => Task.CompletedTask;

            // ISessionConfigProvider
            public Task<IReadOnlyList<SessionConfigOption>> SetConfigOptionAsync(string sessionId, string configId, SessionConfigValue value, CancellationToken ct)
            {
                LastConfigId = configId;
                LastConfigValue = value;
                return Task.FromResult<IReadOnlyList<SessionConfigOption>>(new List<SessionConfigOption>
                {
                    new()
                    {
                        Type = "select",
                        Id = "model",
                        Name = "Model",
                        Category = "model",
                        CurrentValueId = value.ValueId ?? "claude-fable-5",
                        Options = new List<SessionConfigSelectOption>
                        {
                            new() { Value = "claude-fable-5", Name = "Fable 5" }
                        }
                    }
                });
            }

            // ISessionCatalogProvider
            public Task<SessionListResult> ListSessionsAsync(string? cwd, string? cursor, CancellationToken ct)
                => Task.FromResult(new SessionListResult
                {
                    Sessions = new List<SessionCatalogEntry>
                    {
                        new()
                        {
                            SessionId = "stored-1",
                            Cwd = cwd ?? "/",
                            Title = "First",
                            UpdatedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
                        }
                    },
                    NextCursor = "next"
                });
            public Task<bool> DeleteSessionAsync(string sessionId, CancellationToken ct)
            {
                if (sessionId != "stored-1")
                    return Task.FromResult(false);
                Deleted = sessionId;
                return Task.FromResult(true);
            }
            public Task<SessionMetadata?> ResumeSessionAsync(LoadSessionParams parameters, CancellationToken ct)
            {
                Resumed = parameters.SessionId;
                return Task.FromResult<SessionMetadata?>(new SessionMetadata
                {
                    SessionId = parameters.SessionId,
                    Modes = new SessionModeState
                    {
                        CurrentModeId = "chat",
                        AvailableModes = new List<SessionMode> { new() { Id = "chat", Name = "Chat" } }
                    }
                });
            }
            public Task CloseSessionAsync(string sessionId, CancellationToken ct)
            {
                Closed = sessionId;
                return Task.CompletedTask;
            }
        }

        /// <summary>Baseline agent with no optional interfaces.</summary>
        private sealed class PlainAgent : IAgentProvider
        {
            public Task<AgentResponse> ProcessPromptAsync(string sessionId, PromptMessage prompt, IResponseStreamer streamer, CancellationToken ct)
                => Task.FromResult(new AgentResponse { StopReason = StopReason.Completed });
            public Task<SessionMetadata> CreateSessionAsync(NewSessionParams? parameters, CancellationToken ct)
                => Task.FromResult(new SessionMetadata { SessionId = "s1" });
            public Task<SessionMetadata?> LoadSessionAsync(LoadSessionParams parameters, IResponseStreamer streamer, CancellationToken ct)
                => Task.FromResult<SessionMetadata?>(null);
            public Task CancelSessionAsync(string sessionId, CancellationToken ct) => Task.CompletedTask;
            public Task<bool> SetSessionModeAsync(string sessionId, string modeId, CancellationToken ct) => Task.FromResult(true);
            public AgentCapabilities GetCapabilities() => new();
        }
    }
}
