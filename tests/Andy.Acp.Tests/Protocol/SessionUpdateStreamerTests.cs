using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.Protocol;
using Andy.Acp.Core.Transport;
using Xunit;

namespace Andy.Acp.Tests.Protocol
{
    /// <summary>
    /// Verifies that every IResponseStreamer operation emits a schema-valid ACP v1
    /// session/update notification (issue #14). Tests assert the full serialized JSON.
    /// </summary>
    public class SessionUpdateStreamerTests
    {
        private static (SessionUpdateStreamer streamer, CapturingTransport transport) Create()
        {
            var transport = new CapturingTransport();
            return (new SessionUpdateStreamer(transport, "sess-1"), transport);
        }

        private static JsonElement Update(CapturingTransport t)
        {
            var doc = JsonDocument.Parse(t.Messages[^1]);
            var root = doc.RootElement;
            Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
            Assert.Equal("session/update", root.GetProperty("method").GetString());
            var p = root.GetProperty("params");
            Assert.Equal("sess-1", p.GetProperty("sessionId").GetString());
            return p.GetProperty("update").Clone();
        }

        [Fact]
        public async Task MessageChunk_EmitsAgentMessageChunk()
        {
            var (s, t) = Create();
            await s.SendMessageChunkAsync("hello", CancellationToken.None);

            var u = Update(t);
            Assert.Equal("agent_message_chunk", u.GetProperty("sessionUpdate").GetString());
            Assert.Equal("text", u.GetProperty("content").GetProperty("type").GetString());
            Assert.Equal("hello", u.GetProperty("content").GetProperty("text").GetString());
            // No nonstandard top-level type/data members.
            Assert.False(u.TryGetProperty("type", out _));
            Assert.False(u.TryGetProperty("data", out _));
        }

        [Fact]
        public async Task Thinking_EmitsAgentThoughtChunk()
        {
            var (s, t) = Create();
            await s.SendThinkingAsync("pondering", CancellationToken.None);

            var u = Update(t);
            Assert.Equal("agent_thought_chunk", u.GetProperty("sessionUpdate").GetString());
            Assert.Equal("pondering", u.GetProperty("content").GetProperty("text").GetString());
        }

        [Fact]
        public async Task ToolCall_EmitsToolCallWithDefaults()
        {
            var (s, t) = Create();
            await s.SendToolCallAsync(new ToolCall { Id = "t1", Name = "read_file", Input = new { path = "/x" } }, CancellationToken.None);

            var u = Update(t);
            Assert.Equal("tool_call", u.GetProperty("sessionUpdate").GetString());
            Assert.Equal("t1", u.GetProperty("toolCallId").GetString());
            Assert.Equal("read_file", u.GetProperty("title").GetString());
            Assert.Equal("other", u.GetProperty("kind").GetString());
            Assert.Equal("pending", u.GetProperty("status").GetString());
            Assert.Equal("/x", u.GetProperty("rawInput").GetProperty("path").GetString());
        }

        [Fact]
        public async Task ToolResult_Success_EmitsCompletedToolCallUpdate()
        {
            var (s, t) = Create();
            await s.SendToolResultAsync(new ToolResult { CallId = "t1", Content = "done" }, CancellationToken.None);

            var u = Update(t);
            Assert.Equal("tool_call_update", u.GetProperty("sessionUpdate").GetString());
            Assert.Equal("t1", u.GetProperty("toolCallId").GetString());
            Assert.Equal("completed", u.GetProperty("status").GetString());
            var content = u.GetProperty("content");
            Assert.Equal("content", content[0].GetProperty("type").GetString());
            Assert.Equal("done", content[0].GetProperty("content").GetProperty("text").GetString());
        }

        [Fact]
        public async Task ToolResult_Error_EmitsFailedStatus()
        {
            var (s, t) = Create();
            await s.SendToolResultAsync(new ToolResult { CallId = "t1", IsError = true, Content = "boom" }, CancellationToken.None);

            var u = Update(t);
            Assert.Equal("failed", u.GetProperty("status").GetString());
        }

        [Fact]
        public async Task Plan_EmitsEntriesWithPriorityAndStatus()
        {
            var (s, t) = Create();
            await s.SendExecutionPlanAsync(new ExecutionPlan
            {
                Entries = new List<PlanEntry>
                {
                    new() { Content = "step a", Priority = "high", Status = "in_progress" }
                }
            }, CancellationToken.None);

            var u = Update(t);
            Assert.Equal("plan", u.GetProperty("sessionUpdate").GetString());
            var e = u.GetProperty("entries")[0];
            Assert.Equal("step a", e.GetProperty("content").GetString());
            Assert.Equal("high", e.GetProperty("priority").GetString());
            Assert.Equal("in_progress", e.GetProperty("status").GetString());
        }

        [Fact]
        public async Task Plan_FromSteps_DerivesValidEntries()
        {
            var (s, t) = Create();
            await s.SendExecutionPlanAsync(new ExecutionPlan { Steps = new[] { "first", "second" } }, CancellationToken.None);

            var u = Update(t);
            var entries = u.GetProperty("entries");
            Assert.Equal(2, entries.GetArrayLength());
            Assert.Equal("first", entries[0].GetProperty("content").GetString());
            Assert.Equal("medium", entries[0].GetProperty("priority").GetString());
            Assert.Equal("pending", entries[0].GetProperty("status").GetString());
        }

        private sealed class CapturingTransport : ITransport
        {
            public List<string> Messages { get; } = new();
            public bool IsConnected => true;
            public Task<string> ReadMessageAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(string.Empty);
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
