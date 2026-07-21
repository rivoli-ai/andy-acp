using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Andy.Acp.Core.Transport;

namespace Andy.Acp.Tests.Server
{
    /// <summary>
    /// An in-memory, bidirectional stdio harness. The server side is exposed as an
    /// <see cref="ITransport"/>; the client side sends line-delimited JSON messages and
    /// reads framed responses. Used to drive the full AcpServer stack in tests.
    /// </summary>
    internal sealed class InMemoryDuplex
    {
        private readonly InMemoryPipeStream _clientToServer = new();
        private readonly InMemoryPipeStream _serverToClient = new();

        public ITransport ServerTransport { get; }

        public InMemoryDuplex()
        {
            // Server reads client->server and writes server->client.
            ServerTransport = new StdioTransport(_clientToServer, _serverToClient);
        }

        /// <summary>Sends a raw JSON message from the client (newline-delimited framing).</summary>
        public async Task SendAsync(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            await _clientToServer.WriteAsync(bytes, 0, bytes.Length);
        }

        /// <summary>Reads the next newline-delimited message written by the server.</summary>
        public async Task<string> ReadMessageAsync(CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream();
            var one = new byte[1];
            while (true)
            {
                int n = await _serverToClient.ReadAsync(one.AsMemory(0, 1), cancellationToken);
                if (n == 0)
                    break;
                if (one[0] == (byte)'\n')
                    break;
                ms.WriteByte(one[0]);
            }

            var text = Encoding.UTF8.GetString(ms.ToArray());
            return text.TrimEnd('\r');
        }

        public void Complete()
        {
            _clientToServer.CompleteWriting();
            _serverToClient.CompleteWriting();
        }
    }

    /// <summary>
    /// A one-directional in-memory stream: one side writes, the other reads. Reads block
    /// (asynchronously) until data is available, EOF (writer completed), or cancellation.
    /// </summary>
    internal sealed class InMemoryPipeStream : Stream
    {
        private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();
        private byte[]? _current;
        private int _pos;

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set { } }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_current == null || _pos >= _current.Length)
            {
                if (!await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    return 0; // writer completed and drained
                _channel.Reader.TryRead(out _current);
                _pos = 0;
            }

            if (_current == null)
                return 0;

            int count = Math.Min(buffer.Length, _current.Length - _pos);
            _current.AsSpan(_pos, count).CopyTo(buffer.Span);
            _pos += count;
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer, offset, count, default).GetAwaiter().GetResult();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _channel.Writer.TryWrite(buffer.ToArray());
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Write(byte[] buffer, int offset, int count)
        {
            var copy = new byte[count];
            Array.Copy(buffer, offset, copy, 0, count);
            _channel.Writer.TryWrite(copy);
        }

        public void CompleteWriting() => _channel.Writer.TryComplete();

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
