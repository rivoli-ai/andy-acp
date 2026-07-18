using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.Server;
using Xunit;

namespace Andy.Acp.Tests.Server
{
    /// <summary>
    /// End-to-end test for issue #13: an agent reads a file through the client over the
    /// same stdio connection during a prompt, and the client's response is correlated back.
    /// </summary>
    public class BidirectionalE2ETests
    {
        [Fact]
        public async Task Agent_ReadsFileThroughClient_DuringPrompt()
        {
            var harness = new InMemoryDuplex();
            var server = new AcpServer(new FileReadingAgent());

            using var serverCts = new CancellationTokenSource();
            var serverTask = server.RunAsync(harness.ServerTransport, serverCts.Token);

            // Initialize advertising client filesystem read capability.
            await harness.SendAsync("""{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":1,"clientCapabilities":{"fs":{"readTextFile":true}}}}""");
            await harness.ReadMessageAsync();

            await harness.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"session/new","params":{"cwd":"/tmp","mcpServers":[]}}""");
            var sid = JsonDocument.Parse(await harness.ReadMessageAsync())
                .RootElement.GetProperty("result").GetProperty("sessionId").GetString();

            await harness.SendAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/prompt\",\"params\":{\"sessionId\":\"" +
                sid + "\",\"prompt\":[{\"type\":\"text\",\"text\":\"read it\"}]}}");

            bool sawFsRequest = false;
            string? chunkText = null;
            JsonDocument? promptResponse = null;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (promptResponse == null)
            {
                var msg = await harness.ReadMessageAsync(timeout.Token);
                var root = JsonDocument.Parse(msg).RootElement;

                if (root.TryGetProperty("method", out var method) && method.GetString() == "fs/read_text_file")
                {
                    // This is the agent-to-client request. Verify params and respond.
                    sawFsRequest = true;
                    var reqId = root.GetProperty("id").GetInt64();
                    Assert.Equal("/f.txt", root.GetProperty("params").GetProperty("path").GetString());
                    await harness.SendAsync(
                        "{\"jsonrpc\":\"2.0\",\"id\":" + reqId + ",\"result\":{\"content\":\"FILE-CONTENTS\"}}");
                }
                else if (root.TryGetProperty("method", out var m2) && m2.GetString() == "session/update")
                {
                    var update = root.GetProperty("params").GetProperty("update");
                    if (update.GetProperty("sessionUpdate").GetString() == "agent_message_chunk")
                        chunkText = update.GetProperty("content").GetProperty("text").GetString();
                }
                else if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number && idEl.GetInt64() == 2)
                {
                    promptResponse = JsonDocument.Parse(msg);
                }
            }

            Assert.True(sawFsRequest, "agent did not issue an fs/read_text_file request");
            Assert.Equal("FILE-CONTENTS", chunkText);
            Assert.Equal("end_turn", promptResponse!.RootElement.GetProperty("result").GetProperty("stopReason").GetString());

            serverCts.Cancel();
            harness.Complete();
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }

        private sealed class FileReadingAgent : IAgentProvider
        {
            public async Task<AgentResponse> ProcessPromptAsync(string sessionId, PromptMessage prompt, IResponseStreamer streamer, CancellationToken cancellationToken)
            {
                var content = await streamer.Client.FileSystem.ReadTextFileAsync(sessionId, "/f.txt", cancellationToken: cancellationToken);
                await streamer.SendMessageChunkAsync(content, cancellationToken);
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
