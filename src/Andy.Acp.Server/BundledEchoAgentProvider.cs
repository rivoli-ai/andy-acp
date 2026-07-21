using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;

namespace Andy.Acp.Server
{
    /// <summary>
    /// A minimal, self-contained <see cref="IAgentProvider"/> that echoes prompts back
    /// as streamed agent message chunks. It makes the Andy.Acp.Server executable a
    /// working ACP agent without any external model dependency.
    /// </summary>
    internal sealed class BundledEchoAgentProvider : IAgentProvider
    {
        private readonly ConcurrentDictionary<string, DateTime> _sessions = new();
        private int _counter;

        public Task<SessionMetadata> CreateSessionAsync(NewSessionParams? parameters, CancellationToken cancellationToken)
        {
            var sessionId = parameters?.SessionId ?? $"session-{Interlocked.Increment(ref _counter)}";
            var now = DateTime.UtcNow;
            _sessions[sessionId] = now;

            return Task.FromResult(new SessionMetadata
            {
                SessionId = sessionId,
                CreatedAt = now,
                LastAccessedAt = now,
                Mode = parameters?.Mode ?? "chat",
                Model = parameters?.Model ?? "echo-v1",
                MessageCount = 0
            });
        }

        public Task<SessionMetadata?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            if (_sessions.TryGetValue(sessionId, out var createdAt))
            {
                return Task.FromResult<SessionMetadata?>(new SessionMetadata
                {
                    SessionId = sessionId,
                    CreatedAt = createdAt,
                    LastAccessedAt = DateTime.UtcNow,
                    Mode = "chat",
                    Model = "echo-v1"
                });
            }

            return Task.FromResult<SessionMetadata?>(null);
        }

        public async Task<AgentResponse> ProcessPromptAsync(
            string sessionId,
            PromptMessage prompt,
            IResponseStreamer streamer,
            CancellationToken cancellationToken)
        {
            var responseText = $"Echo: {prompt.Text}";

            foreach (var word in responseText.Split(' '))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await streamer.SendMessageChunkAsync(word + " ", cancellationToken);
            }

            return new AgentResponse
            {
                Message = responseText,
                StopReason = StopReason.Completed
            };
        }

        public Task CancelSessionAsync(string sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> SetSessionModeAsync(string sessionId, string mode, CancellationToken cancellationToken)
            => Task.FromResult(_sessions.ContainsKey(sessionId));

        public Task<bool> SetSessionModelAsync(string sessionId, string model, CancellationToken cancellationToken)
            => Task.FromResult(_sessions.ContainsKey(sessionId));

        public AgentCapabilities GetCapabilities() => new()
        {
            LoadSession = true
        };
    }
}
