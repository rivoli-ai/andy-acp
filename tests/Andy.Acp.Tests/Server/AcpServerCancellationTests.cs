using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.Server;
using Xunit;

namespace Andy.Acp.Tests.Server
{
    /// <summary>
    /// End-to-end cancellation tests for issue #19: session/cancel must interrupt an
    /// in-flight prompt promptly and produce a cancelled prompt result.
    /// </summary>
    public class AcpServerCancellationTests
    {
        [Fact]
        public async Task SessionCancel_InterruptsInFlightPrompt_Promptly()
        {
            var harness = new InMemoryDuplex();
            var agent = new BlockingAgent();
            var server = new AcpServer(agent);

            using var serverCts = new CancellationTokenSource();
            var serverTask = server.RunAsync(harness.ServerTransport, serverCts.Token);

            await InitializeAsync(harness);

            // Create a session.
            await harness.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"session/new","params":{"cwd":"/tmp","mcpServers":[]}}""");
            var newDoc = JsonDocument.Parse(await harness.ReadMessageAsync());
            var sessionId = newDoc.RootElement.GetProperty("result").GetProperty("sessionId").GetString();
            Assert.False(string.IsNullOrEmpty(sessionId));

            // Start a prompt that blocks until cancelled.
            await harness.SendAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/prompt\",\"params\":{\"sessionId\":\"" +
                sessionId + "\",\"prompt\":[{\"type\":\"text\",\"text\":\"hello\"}]}}");

            Assert.True(await agent.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)), "prompt did not start");

            // Cancel it via a notification (no id).
            var sw = Stopwatch.StartNew();
            await harness.SendAsync(
                "{\"jsonrpc\":\"2.0\",\"method\":\"session/cancel\",\"params\":{\"sessionId\":\"" +
                sessionId + "\"}}");

            // The prompt (id=2) should complete promptly with a cancelled stop reason.
            var promptResponse = await ReadUntilIdAsync(harness, 2, TimeSpan.FromSeconds(5));
            sw.Stop();

            Assert.Equal("cancelled", promptResponse.RootElement.GetProperty("result").GetProperty("stopReason").GetString());
            Assert.True(sw.ElapsedMilliseconds < 5000, $"cancellation took too long: {sw.ElapsedMilliseconds}ms");

            serverCts.Cancel();
            harness.Complete();
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* shutdown races are fine */ }
        }

        [Fact]
        public async Task SessionCancel_ForUnknownSession_IsHarmless()
        {
            var harness = new InMemoryDuplex();
            var agent = new BlockingAgent();
            var server = new AcpServer(agent);

            using var serverCts = new CancellationTokenSource();
            var serverTask = server.RunAsync(harness.ServerTransport, serverCts.Token);

            await InitializeAsync(harness);

            // Cancel with no active prompt: must not throw or produce a response.
            await harness.SendAsync("""{"jsonrpc":"2.0","method":"session/cancel","params":{"sessionId":"nope"}}""");

            // A subsequent normal request still works.
            await harness.SendAsync("""{"jsonrpc":"2.0","id":9,"method":"session/new","params":{"cwd":"/tmp","mcpServers":[]}}""");
            var doc = await ReadUntilIdAsync(harness, 9, TimeSpan.FromSeconds(5));
            Assert.True(doc.RootElement.GetProperty("result").TryGetProperty("sessionId", out _));

            serverCts.Cancel();
            harness.Complete();
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }

        private static async Task InitializeAsync(InMemoryDuplex harness)
        {
            // Complete the handshake and consume the response so that the connection is
            // initialized before any session method is sent.
            await harness.SendAsync("""{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":1,"clientCapabilities":{}}}""");
            await harness.ReadMessageAsync();
        }

        private static async Task<JsonDocument> ReadUntilIdAsync(InMemoryDuplex harness, long id, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            while (true)
            {
                var message = await harness.ReadMessageAsync(cts.Token);
                if (string.IsNullOrEmpty(message))
                    throw new InvalidOperationException("stream closed before expected response");

                var doc = JsonDocument.Parse(message);
                if (doc.RootElement.TryGetProperty("id", out var idEl) &&
                    idEl.ValueKind == JsonValueKind.Number && idEl.GetInt64() == id)
                {
                    return doc;
                }
            }
        }

        /// <summary>An agent whose prompt blocks until the cancellation token fires.</summary>
        private sealed class BlockingAgent : IAgentProvider
        {
            public readonly TaskCompletionSource<bool> Started =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<SessionMetadata> CreateSessionAsync(NewSessionParams? parameters, CancellationToken cancellationToken)
                => Task.FromResult(new SessionMetadata
                {
                    SessionId = "s1",
                    CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Mode = "chat",
                    Model = "test"
                });

            public Task<SessionMetadata?> LoadSessionAsync(LoadSessionParams parameters, IResponseStreamer streamer, CancellationToken cancellationToken)
                => Task.FromResult<SessionMetadata?>(null);

            public async Task<AgentResponse> ProcessPromptAsync(
                string sessionId, PromptMessage prompt, IResponseStreamer streamer, CancellationToken cancellationToken)
            {
                Started.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return new AgentResponse { StopReason = StopReason.Completed };
            }

            public Task CancelSessionAsync(string sessionId, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<bool> SetSessionModeAsync(string sessionId, string modeId, CancellationToken cancellationToken) => Task.FromResult(true);
            public AgentCapabilities GetCapabilities() => new();
        }
    }
}
