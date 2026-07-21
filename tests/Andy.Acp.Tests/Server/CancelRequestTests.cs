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
    /// End-to-end tests for the ACP <c>$/cancel_request</c> protocol notification:
    /// cancelling an in-flight request produces a -32800 (request cancelled) response.
    /// </summary>
    public class CancelRequestTests
    {
        [Fact]
        public async Task CancelRequest_CancelsInFlightLoad_WithRequestCancelledError()
        {
            var harness = new InMemoryDuplex();
            var agent = new BlockingLoadAgent();
            var server = new AcpServer(agent);

            using var serverCts = new CancellationTokenSource();
            var serverTask = server.RunAsync(harness.ServerTransport, serverCts.Token);

            await harness.SendAsync("""{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":1}}""");
            await harness.ReadMessageAsync();

            // Start a session/load that blocks until cancelled.
            await harness.SendAsync("""{"jsonrpc":"2.0","id":7,"method":"session/load","params":{"sessionId":"s1","cwd":"/tmp","mcpServers":[]}}""");
            Assert.True(await agent.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)), "load did not start");

            // Cancel it at the protocol level.
            await harness.SendAsync("""{"jsonrpc":"2.0","method":"$/cancel_request","params":{"requestId":7}}""");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var doc = JsonDocument.Parse(await harness.ReadMessageAsync(timeout.Token));
            Assert.Equal(7, doc.RootElement.GetProperty("id").GetInt64());
            Assert.Equal(JsonRpcErrorCodes.RequestCancelled,
                doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());

            serverCts.Cancel();
            harness.Complete();
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }

        [Fact]
        public async Task CancelRequest_UnknownId_IsHarmless()
        {
            var harness = new InMemoryDuplex();
            var server = new AcpServer(new BlockingLoadAgent());

            using var serverCts = new CancellationTokenSource();
            var serverTask = server.RunAsync(harness.ServerTransport, serverCts.Token);

            await harness.SendAsync("""{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":1}}""");
            await harness.ReadMessageAsync();

            // Cancel a request that does not exist; no response, and the connection stays usable.
            await harness.SendAsync("""{"jsonrpc":"2.0","method":"$/cancel_request","params":{"requestId":999}}""");

            await harness.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"session/new","params":{"cwd":"/tmp","mcpServers":[]}}""");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var doc = JsonDocument.Parse(await harness.ReadMessageAsync(timeout.Token));
            Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt64());
            Assert.True(doc.RootElement.TryGetProperty("result", out _));

            serverCts.Cancel();
            harness.Complete();
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }

        /// <summary>Agent whose session/load blocks until its token is cancelled.</summary>
        private sealed class BlockingLoadAgent : IAgentProvider
        {
            public readonly TaskCompletionSource<bool> Started =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task<SessionMetadata?> LoadSessionAsync(LoadSessionParams parameters, IResponseStreamer streamer, CancellationToken cancellationToken)
            {
                Started.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return null;
            }

            public Task<AgentResponse> ProcessPromptAsync(string sessionId, PromptMessage prompt, IResponseStreamer streamer, CancellationToken ct)
                => Task.FromResult(new AgentResponse { StopReason = StopReason.Completed });
            public Task<SessionMetadata> CreateSessionAsync(NewSessionParams? parameters, CancellationToken ct)
                => Task.FromResult(new SessionMetadata { SessionId = "s1" });
            public Task CancelSessionAsync(string sessionId, CancellationToken ct) => Task.CompletedTask;
            public Task<bool> SetSessionModeAsync(string sessionId, string modeId, CancellationToken ct) => Task.FromResult(true);
            public AgentCapabilities GetCapabilities() => new() { LoadSession = true };
        }
    }
}
