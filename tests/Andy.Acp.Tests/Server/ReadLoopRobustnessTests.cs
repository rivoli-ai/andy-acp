using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Server;
using Xunit;

namespace Andy.Acp.Tests.Server
{
    /// <summary>
    /// Read-loop robustness (epic #33): a malformed frame must not kill the server,
    /// and $/cancel_request sent with an id must still get a response.
    /// </summary>
    public class ReadLoopRobustnessTests
    {
        [Fact]
        public async Task MalformedFrame_IsAnsweredWithParseError_AndServerKeepsServing()
        {
            var harness = new InMemoryDuplex();
            var server = new AcpServer(new StubAgent());

            using var serverCts = new CancellationTokenSource();
            var serverTask = server.RunAsync(harness.ServerTransport, serverCts.Token);

            // A garbage line: not JSON, not Content-Length framing.
            await harness.SendAsync("this is not a json-rpc message");

            var errorJson = await harness.ReadMessageAsync();
            var errorRoot = JsonDocument.Parse(errorJson).RootElement;
            Assert.Equal(JsonRpcErrorCodes.ParseError, errorRoot.GetProperty("error").GetProperty("code").GetInt32());

            // The connection survived: a well-formed request right after is handled.
            await harness.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1,"clientCapabilities":{}}}""");
            var initJson = await harness.ReadMessageAsync();
            var initRoot = JsonDocument.Parse(initJson).RootElement;
            Assert.Equal(1, initRoot.GetProperty("id").GetInt32());
            Assert.Equal(1, initRoot.GetProperty("result").GetProperty("protocolVersion").GetInt32());

            serverCts.Cancel();
            harness.Complete();
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }

        [Fact]
        public async Task CancelRequest_SentWithId_StillGetsAResponse()
        {
            var harness = new InMemoryDuplex();
            var server = new AcpServer(new StubAgent());

            using var serverCts = new CancellationTokenSource();
            var serverTask = server.RunAsync(harness.ServerTransport, serverCts.Token);

            // $/cancel_request is defined as a notification, but a client sending it
            // with an id must not be left hanging.
            await harness.SendAsync("""{"jsonrpc":"2.0","id":5,"method":"$/cancel_request","params":{"requestId":999}}""");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var responseJson = await harness.ReadMessageAsync(timeout.Token);
            var root = JsonDocument.Parse(responseJson).RootElement;
            Assert.Equal(5, root.GetProperty("id").GetInt32());
            Assert.True(root.TryGetProperty("result", out _));
            Assert.False(root.TryGetProperty("error", out _));

            serverCts.Cancel();
            harness.Complete();
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }

        private sealed class StubAgent : IAgentProvider
        {
            public Task<AgentResponse> ProcessPromptAsync(string sessionId, PromptMessage prompt, IResponseStreamer streamer, CancellationToken cancellationToken)
                => Task.FromResult(new AgentResponse { StopReason = StopReason.Completed });
            public Task<SessionMetadata> CreateSessionAsync(NewSessionParams? parameters, CancellationToken cancellationToken)
                => Task.FromResult(new SessionMetadata { SessionId = "s1" });
            public Task<SessionMetadata?> LoadSessionAsync(LoadSessionParams parameters, IResponseStreamer streamer, CancellationToken cancellationToken)
                => Task.FromResult<SessionMetadata?>(null);
            public Task CancelSessionAsync(string sessionId, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<bool> SetSessionModeAsync(string sessionId, string modeId, CancellationToken cancellationToken) => Task.FromResult(true);
            public AgentCapabilities GetCapabilities() => new();
        }
    }
}
