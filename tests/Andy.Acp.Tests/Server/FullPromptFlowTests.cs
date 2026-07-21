using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.Server;
using Xunit;

namespace Andy.Acp.Tests.Server
{
    /// <summary>
    /// Full text-prompt flow through the real AcpServer over the in-memory duplex transport
    /// (issue #21): initialize → session/new → session/prompt → streamed updates → completion,
    /// asserting that all session/update chunks arrive before the final prompt response.
    /// </summary>
    public class FullPromptFlowTests
    {
        [Fact]
        public async Task TextPrompt_StreamsChunksThenCompletes_InOrder()
        {
            var harness = new InMemoryDuplex();
            var server = new AcpServer(new StreamingEchoAgent());

            using var serverCts = new CancellationTokenSource();
            var serverTask = server.RunAsync(harness.ServerTransport, serverCts.Token);

            await harness.SendAsync("""{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":1,"clientCapabilities":{}}}""");
            await harness.ReadMessageAsync();

            await harness.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"session/new","params":{"cwd":"/tmp","mcpServers":[]}}""");
            var sid = JsonDocument.Parse(await harness.ReadMessageAsync())
                .RootElement.GetProperty("result").GetProperty("sessionId").GetString();

            await harness.SendAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/prompt\",\"params\":{\"sessionId\":\"" +
                sid + "\",\"prompt\":[{\"type\":\"text\",\"text\":\"hi\"}]}}");

            var chunks = new StringBuilder();
            int chunkCount = 0;
            bool responseSeen = false;
            string? stopReason = null;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!responseSeen)
            {
                var root = JsonDocument.Parse(await harness.ReadMessageAsync(timeout.Token)).RootElement;

                if (root.TryGetProperty("method", out var m) && m.GetString() == "session/update")
                {
                    var update = root.GetProperty("params").GetProperty("update");
                    if (update.GetProperty("sessionUpdate").GetString() == "agent_message_chunk")
                    {
                        chunkCount++;
                        chunks.Append(update.GetProperty("content").GetProperty("text").GetString());
                    }
                }
                else if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt64() == 2)
                {
                    responseSeen = true;
                    stopReason = root.GetProperty("result").GetProperty("stopReason").GetString();
                    // Ordering: all chunks must have been received before this final response.
                    Assert.True(chunkCount >= 2, "expected streamed chunks before the prompt response");
                }
            }

            Assert.Equal("Hello world", chunks.ToString());
            Assert.Equal("end_turn", stopReason);

            serverCts.Cancel();
            harness.Complete();
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }

        private sealed class StreamingEchoAgent : IAgentProvider
        {
            public async Task<AgentResponse> ProcessPromptAsync(string sessionId, PromptMessage prompt, IResponseStreamer streamer, CancellationToken cancellationToken)
            {
                await streamer.SendMessageChunkAsync("Hello ", cancellationToken);
                await streamer.SendMessageChunkAsync("world", cancellationToken);
                return new AgentResponse { StopReason = StopReason.Completed };
            }

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
