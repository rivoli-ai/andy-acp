using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Protocol;
using Andy.Acp.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Core.Client
{
    /// <summary>
    /// Implements <see cref="IAcpClient"/> by issuing JSON-RPC requests to the client over
    /// the shared transport and correlating the client's responses back to the awaiting
    /// agent operations by request id.
    /// <para>
    /// Behavior of pending outbound requests: a caller's cancellation token cancels the wait;
    /// a JSON-RPC error response surfaces as <see cref="AcpRequestException"/>; connection
    /// close fails all pending requests with <see cref="AcpClientDisconnectedException"/> via
    /// <see cref="FailAllPending"/>.
    /// </para>
    /// </summary>
    public sealed class AcpClient : IAcpClient
    {
        private readonly ITransport _transport;
        private readonly AcpConnectionState _state;
        private readonly ILogger? _logger;
        private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement?>> _pending = new();
        private long _nextId;

        public AcpClient(ITransport transport, AcpConnectionState state, ILogger? logger = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _logger = logger;
            FileSystem = new ClientFileSystem(this);
            Terminal = new ClientTerminal(this);
        }

        public IClientFileSystem FileSystem { get; }
        public IClientTerminal Terminal { get; }

        public bool CanReadFiles => _state.ClientCapabilities.Fs?.ReadTextFile == true;
        public bool CanWriteFiles => _state.ClientCapabilities.Fs?.WriteTextFile == true;
        public bool CanUseTerminal => _state.ClientCapabilities.Terminal;

        public async Task<PermissionOutcome> RequestPermissionAsync(
            string sessionId,
            PermissionToolCall toolCall,
            IReadOnlyList<PermissionOption> options,
            CancellationToken cancellationToken = default)
        {
            // The wire shape differs by negotiated protocol version: v1 uses a flattened
            // required toolCall; v2 requires a title and nests the tool call in a subject.
            object requestParams = _state.ProtocolVersion == Protocol.AcpVersions.V2Alpha
                ? new
                {
                    sessionId,
                    title = string.IsNullOrEmpty(toolCall.Title) ? "Permission required" : toolCall.Title!,
                    subject = new { type = "tool_call", toolCall },
                    options
                }
                : new
                {
                    sessionId,
                    toolCall,
                    options
                };

            var result = await SendRequestAsync<PermissionResponseDto>(
                "session/request_permission", requestParams, cancellationToken).ConfigureAwait(false);

            var outcome = result?.Outcome;
            if (outcome == null || !string.Equals(outcome.Outcome, "selected", StringComparison.Ordinal))
                return PermissionOutcome.CancelledOutcome();

            return PermissionOutcome.Selected(outcome.OptionId ?? string.Empty);
        }

        /// <summary>
        /// Routes a client response to the awaiting outbound request. Called by the server
        /// read loop for every inbound <see cref="JsonRpcResponse"/>.
        /// </summary>
        public void HandleResponse(JsonRpcResponse response)
        {
            var id = ToLong(response.Id);
            if (id == null)
            {
                _logger?.LogWarning("Received response with non-correlatable id");
                return;
            }

            if (!_pending.TryRemove(id.Value, out var tcs))
            {
                _logger?.LogDebug("No pending outbound request for response id {Id}", id.Value);
                return;
            }

            if (response.Error != null)
                tcs.TrySetException(new AcpRequestException(response.Error));
            else
                tcs.TrySetResult(response.Result is JsonElement je ? je : (JsonElement?)null);
        }

        /// <summary>Fails every pending outbound request, e.g. when the connection closes.</summary>
        public void FailAllPending(Exception exception)
        {
            foreach (var key in _pending.Keys)
            {
                if (_pending.TryRemove(key, out var tcs))
                    tcs.TrySetException(exception);
            }
        }

        private async Task<TResult?> SendRequestAsync<TResult>(string method, object? paramsObj, CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _nextId);
            var tcs = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            using var reg = cancellationToken.Register(() =>
            {
                if (_pending.TryRemove(id, out var t))
                    t.TrySetCanceled(cancellationToken);
            });

            try
            {
                var request = new { jsonrpc = "2.0", id, method, @params = paramsObj };
                var json = JsonSerializer.Serialize(request, JsonRpcSerializer.Options);
                _logger?.LogTrace("Sending agent->client request {Method} (id {Id})", method, id);
                await _transport.WriteMessageAsync(json, cancellationToken).ConfigureAwait(false);

                var element = await tcs.Task.ConfigureAwait(false);
                if (element == null || element.Value.ValueKind == JsonValueKind.Null)
                    return default;
                return element.Value.Deserialize<TResult>(JsonRpcSerializer.Options);
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }

        private static long? ToLong(object? id) => id switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            _ => null
        };

        private sealed class ClientFileSystem : IClientFileSystem
        {
            private readonly AcpClient _c;
            public ClientFileSystem(AcpClient c) => _c = c;

            public async Task<string> ReadTextFileAsync(string sessionId, string path, int? line = null, int? limit = null, CancellationToken cancellationToken = default)
            {
                if (!_c.CanReadFiles)
                    throw new AcpCapabilityNotSupportedException("Client does not support fs/read_text_file");

                var result = await _c.SendRequestAsync<ReadTextFileDto>("fs/read_text_file", new
                {
                    sessionId,
                    path,
                    line,
                    limit
                }, cancellationToken).ConfigureAwait(false);
                return result?.Content ?? string.Empty;
            }

            public async Task WriteTextFileAsync(string sessionId, string path, string content, CancellationToken cancellationToken = default)
            {
                if (!_c.CanWriteFiles)
                    throw new AcpCapabilityNotSupportedException("Client does not support fs/write_text_file");

                await _c.SendRequestAsync<object>("fs/write_text_file", new { sessionId, path, content }, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private sealed class ClientTerminal : IClientTerminal
        {
            private readonly AcpClient _c;
            public ClientTerminal(AcpClient c) => _c = c;

            private void EnsureSupported()
            {
                if (!_c.CanUseTerminal)
                    throw new AcpCapabilityNotSupportedException("Client does not support terminal operations");
            }

            public async Task<string> CreateAsync(string sessionId, string command, IReadOnlyList<string>? args = null,
                IReadOnlyList<EnvVariable>? env = null, string? cwd = null, long? outputByteLimit = null, CancellationToken cancellationToken = default)
            {
                EnsureSupported();
                var result = await _c.SendRequestAsync<CreateTerminalDto>("terminal/create", new
                {
                    sessionId,
                    command,
                    args,
                    env,
                    cwd,
                    outputByteLimit
                }, cancellationToken).ConfigureAwait(false);
                return result?.TerminalId ?? string.Empty;
            }

            public Task<TerminalOutput> GetOutputAsync(string sessionId, string terminalId, CancellationToken cancellationToken = default)
            {
                EnsureSupported();
                return NonNull(_c.SendRequestAsync<TerminalOutput>("terminal/output", new { sessionId, terminalId }, cancellationToken));
            }

            public Task<TerminalExit> WaitForExitAsync(string sessionId, string terminalId, CancellationToken cancellationToken = default)
            {
                EnsureSupported();
                return NonNull(_c.SendRequestAsync<TerminalExit>("terminal/wait_for_exit", new { sessionId, terminalId }, cancellationToken));
            }

            public async Task KillAsync(string sessionId, string terminalId, CancellationToken cancellationToken = default)
            {
                EnsureSupported();
                await _c.SendRequestAsync<object>("terminal/kill", new { sessionId, terminalId }, cancellationToken).ConfigureAwait(false);
            }

            public async Task ReleaseAsync(string sessionId, string terminalId, CancellationToken cancellationToken = default)
            {
                EnsureSupported();
                await _c.SendRequestAsync<object>("terminal/release", new { sessionId, terminalId }, cancellationToken).ConfigureAwait(false);
            }

            private static async Task<T> NonNull<T>(Task<T?> task) where T : class, new()
                => await task.ConfigureAwait(false) ?? new T();
        }

        private sealed class ReadTextFileDto
        {
            [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
        }

        private sealed class CreateTerminalDto
        {
            [JsonPropertyName("terminalId")] public string TerminalId { get; set; } = string.Empty;
        }

        private sealed class PermissionResponseDto
        {
            [JsonPropertyName("outcome")] public PermissionOutcomeDto? Outcome { get; set; }
        }

        private sealed class PermissionOutcomeDto
        {
            [JsonPropertyName("outcome")] public string Outcome { get; set; } = "cancelled";
            [JsonPropertyName("optionId")] public string? OptionId { get; set; }
        }
    }
}
