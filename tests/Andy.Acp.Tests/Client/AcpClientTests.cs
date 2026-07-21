using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Client;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Protocol;
using Andy.Acp.Core.Transport;
using Xunit;

namespace Andy.Acp.Tests.Client
{
    /// <summary>
    /// Unit tests for the agent-to-client request layer (issue #13): capability gating,
    /// response correlation, error/cancel/disconnect behavior, and permission round-trips.
    /// </summary>
    public class AcpClientTests
    {
        private static AcpConnectionState Caps(bool read = false, bool write = false, bool terminal = false) => new()
        {
            Initialized = true,
            ClientCapabilities = new AcpClientCapabilities
            {
                Fs = new AcpFileSystemCapabilities { ReadTextFile = read, WriteTextFile = write },
                Terminal = terminal
            }
        };

        private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

        [Fact]
        public async Task ReadTextFile_WithoutCapability_FailsLocallyWithoutRequest()
        {
            var transport = new CapturingTransport();
            var client = new AcpClient(transport, Caps(read: false));

            await Assert.ThrowsAsync<AcpCapabilityNotSupportedException>(
                () => client.FileSystem.ReadTextFileAsync("s", "/x"));
            Assert.Empty(transport.Sent);
        }

        [Fact]
        public async Task ReadTextFile_RoundTrips()
        {
            var transport = new CapturingTransport();
            var client = new AcpClient(transport, Caps(read: true));

            var task = client.FileSystem.ReadTextFileAsync("s", "/x");
            await transport.WaitForSendAsync();

            Assert.Contains("fs/read_text_file", transport.Sent[0]);
            client.HandleResponse(new JsonRpcResponse { Id = 1L, Result = Json("{\"content\":\"hello\"}") });

            Assert.Equal("hello", await task);
        }

        [Fact]
        public async Task Request_ErrorResponse_ThrowsAcpRequestException()
        {
            var transport = new CapturingTransport();
            var client = new AcpClient(transport, Caps(read: true));

            var task = client.FileSystem.ReadTextFileAsync("s", "/x");
            await transport.WaitForSendAsync();

            client.HandleResponse(new JsonRpcResponse
            {
                Id = 1L,
                Error = new JsonRpcError { Code = JsonRpcErrorCodes.InvalidParams, Message = "bad path" }
            });

            var ex = await Assert.ThrowsAsync<AcpRequestException>(() => task);
            Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.Code);
        }

        [Fact]
        public async Task Request_Cancellation_CancelsPending()
        {
            var transport = new CapturingTransport();
            var client = new AcpClient(transport, Caps(read: true));
            using var cts = new CancellationTokenSource();

            var task = client.FileSystem.ReadTextFileAsync("s", "/x", cancellationToken: cts.Token);
            await transport.WaitForSendAsync();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        }

        [Fact]
        public async Task Disconnect_FailsPendingRequests()
        {
            var transport = new CapturingTransport();
            var client = new AcpClient(transport, Caps(read: true));

            var task = client.FileSystem.ReadTextFileAsync("s", "/x");
            await transport.WaitForSendAsync();

            client.FailAllPending(new AcpClientDisconnectedException("closed"));

            await Assert.ThrowsAsync<AcpClientDisconnectedException>(() => task);
        }

        [Fact]
        public async Task Permission_Selected_RoundTrips()
        {
            var transport = new CapturingTransport();
            var client = new AcpClient(transport, Caps());

            var task = client.RequestPermissionAsync("s",
                new PermissionToolCall { ToolCallId = "t" },
                new List<PermissionOption> { new() { OptionId = "allow", Name = "Allow", Kind = "allow_once" } });
            await transport.WaitForSendAsync();

            Assert.Contains("session/request_permission", transport.Sent[0]);
            client.HandleResponse(new JsonRpcResponse
            {
                Id = 1L,
                Result = Json("{\"outcome\":{\"outcome\":\"selected\",\"optionId\":\"allow\"}}")
            });

            var outcome = await task;
            Assert.False(outcome.Cancelled);
            Assert.Equal("allow", outcome.OptionId);
        }

        [Fact]
        public async Task Permission_Cancelled_RoundTrips()
        {
            var transport = new CapturingTransport();
            var client = new AcpClient(transport, Caps());

            var task = client.RequestPermissionAsync("s",
                new PermissionToolCall { ToolCallId = "t" },
                new List<PermissionOption>());
            await transport.WaitForSendAsync();

            client.HandleResponse(new JsonRpcResponse { Id = 1L, Result = Json("{\"outcome\":{\"outcome\":\"cancelled\"}}") });

            var outcome = await task;
            Assert.True(outcome.Cancelled);
        }

        [Fact]
        public async Task Terminal_WithoutCapability_FailsLocally()
        {
            var transport = new CapturingTransport();
            var client = new AcpClient(transport, Caps(terminal: false));

            await Assert.ThrowsAsync<AcpCapabilityNotSupportedException>(
                () => client.Terminal.CreateAsync("s", "ls"));
            Assert.Empty(transport.Sent);
        }

        private sealed class CapturingTransport : ITransport
        {
            public List<string> Sent { get; } = new();
            private readonly SemaphoreSlim _sent = new(0);

            public bool IsConnected => true;
            public Task<string> ReadMessageAsync(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);

            public Task WriteMessageAsync(string message, CancellationToken cancellationToken = default)
            {
                Sent.Add(message);
                _sent.Release();
                return Task.CompletedTask;
            }

            public Task WaitForSendAsync() => _sent.WaitAsync(TimeSpan.FromSeconds(5));

            public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void Dispose() { }
        }
    }
}
