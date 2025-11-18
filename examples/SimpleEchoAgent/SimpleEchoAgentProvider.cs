using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;

namespace SimpleEchoAgent
{
    /// <summary>
    /// Simple echo agent for testing ACP integration.
    /// This echoes back user prompts with basic formatting.
    /// This is a minimal example showing how to implement IAgentProvider.
    /// </summary>
    public class SimpleEchoAgentProvider : IAgentProvider
    {
        private readonly Dictionary<string, SessionData> _sessions = new();
        private int _sessionCounter = 0;

        public Task<SessionMetadata> CreateSessionAsync(NewSessionParams? parameters, CancellationToken cancellationToken)
        {
            var sessionId = parameters?.SessionId ?? $"session-{++_sessionCounter}";
            var sessionData = new SessionData
            {
                SessionId = sessionId,
                CreatedAt = DateTime.UtcNow,
                Mode = parameters?.Mode ?? "chat",
                Model = parameters?.Model ?? "echo-v1"
            };

            _sessions[sessionId] = sessionData;

            return Task.FromResult(new SessionMetadata
            {
                SessionId = sessionId,
                CreatedAt = sessionData.CreatedAt,
                LastAccessedAt = sessionData.CreatedAt,
                Mode = sessionData.Mode,
                Model = sessionData.Model,
                MessageCount = 0
            });
        }

        public Task<SessionMetadata?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            if (_sessions.TryGetValue(sessionId, out var sessionData))
            {
                sessionData.LastAccessedAt = DateTime.UtcNow;
                return Task.FromResult<SessionMetadata?>(new SessionMetadata
                {
                    SessionId = sessionId,
                    CreatedAt = sessionData.CreatedAt,
                    LastAccessedAt = sessionData.LastAccessedAt,
                    Mode = sessionData.Mode,
                    Model = sessionData.Model,
                    MessageCount = sessionData.Messages.Count
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
            if (!_sessions.TryGetValue(sessionId, out var sessionData))
            {
                return new AgentResponse
                {
                    Message = $"Error: Session {sessionId} not found",
                    StopReason = StopReason.Error,
                    Error = "Session not found"
                };
            }

            // Store the user message
            sessionData.Messages.Add(("user", prompt.Text));
            sessionData.LastAccessedAt = DateTime.UtcNow;

            // Simulate streaming response
            var responseText = $"Echo: {prompt.Text}\n\nThis is a test ACP agent. Your message was received and echoed back.";

            // Stream the response in chunks (word by word)
            var words = responseText.Split(' ');
            foreach (var word in words)
            {
                await streamer.SendMessageChunkAsync(word + " ", cancellationToken);
                await Task.Delay(10, cancellationToken); // Simulate typing delay
            }

            sessionData.Messages.Add(("assistant", responseText));

            return new AgentResponse
            {
                Message = responseText,
                StopReason = StopReason.Completed
            };
        }

        public Task CancelSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            // Nothing to cancel in this simple implementation
            return Task.CompletedTask;
        }

        public Task<bool> SetSessionModeAsync(string sessionId, string mode, CancellationToken cancellationToken)
        {
            if (_sessions.TryGetValue(sessionId, out var sessionData))
            {
                sessionData.Mode = mode;
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public Task<bool> SetSessionModelAsync(string sessionId, string model, CancellationToken cancellationToken)
        {
            if (_sessions.TryGetValue(sessionId, out var sessionData))
            {
                sessionData.Model = model;
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public AgentCapabilities GetCapabilities()
        {
            return new AgentCapabilities
            {
                LoadSession = true,
                AudioPrompts = false,
                ImagePrompts = false,
                EmbeddedContext = false
            };
        }

        private class SessionData
        {
            public string SessionId { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime LastAccessedAt { get; set; }
            public string Mode { get; set; } = "chat";
            public string Model { get; set; } = "echo-v1";
            public List<(string role, string content)> Messages { get; set; } = new();
        }
    }
}
