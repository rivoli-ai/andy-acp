using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Transport;
using Xunit;

namespace Andy.Acp.Tests.Transport
{
    /// <summary>
    /// Byte-framing, framing-mode, cancellation, and size-limit tests for issue #18.
    /// </summary>
    public class StdioFramingTests
    {
        [Fact]
        public async Task RawUtf8ContentLength_RoundTripsExactly()
        {
            // Body contains unescaped multi-byte UTF-8 characters. Byte-accurate framing
            // is required; a char-count read would truncate the trailing bytes.
            var body = "{\"m\":\"世界 🌍 café\"}";
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var frame = Encoding.ASCII.GetBytes($"Content-Length: {bodyBytes.Length}\r\n\r\n");

            var input = new MemoryStream();
            input.Write(frame, 0, frame.Length);
            input.Write(bodyBytes, 0, bodyBytes.Length);
            input.Position = 0;

            var transport = new StdioTransport(input, new MemoryStream());
            var result = await transport.ReadMessageAsync(CancellationToken.None);

            Assert.Equal(body, result);
        }

        [Fact]
        public async Task MultipleAdjacentFramedMessages_ReadWithoutCorruption()
        {
            var m1 = "{\"a\":\"föö\"}";
            var m2 = "{\"b\":\"世界\"}";
            var b1 = Encoding.UTF8.GetBytes(m1);
            var b2 = Encoding.UTF8.GetBytes(m2);

            using var input = new MemoryStream();
            void WriteFrame(byte[] body)
            {
                var h = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
                input.Write(h, 0, h.Length);
                input.Write(body, 0, body.Length);
            }
            WriteFrame(b1);
            WriteFrame(b2);
            input.Position = 0;

            var transport = new StdioTransport(input, new MemoryStream());
            Assert.Equal(m1, await transport.ReadMessageAsync(CancellationToken.None));
            Assert.Equal(m2, await transport.ReadMessageAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ResponseFramingMatchesDetectedContentLengthMode()
        {
            var body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"x\"}";
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var frame = Encoding.ASCII.GetBytes($"Content-Length: {bodyBytes.Length}\r\n\r\n");

            var input = new MemoryStream();
            input.Write(frame, 0, frame.Length);
            input.Write(bodyBytes, 0, bodyBytes.Length);
            input.Position = 0;
            var output = new MemoryStream();

            var transport = new StdioTransport(input, output);
            await transport.ReadMessageAsync(CancellationToken.None);
            Assert.Equal(StdioFramingMode.ContentLength, transport.FramingMode);

            await transport.WriteMessageAsync("{\"ok\":true}", CancellationToken.None);

            var written = Encoding.UTF8.GetString(output.ToArray());
            Assert.StartsWith("Content-Length: 11\r\n\r\n", written);
            Assert.EndsWith("{\"ok\":true}", written);
        }

        [Fact]
        public async Task IdleCancellation_StopsWithinBoundedTime()
        {
            var transport = new StdioTransport(new BlockingStream(), new MemoryStream());
            using var cts = new CancellationTokenSource();

            var readTask = transport.ReadMessageAsync(cts.Token);
            cts.CancelAfter(200);

            var sw = Stopwatch.StartNew();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 5000, $"Cancellation took too long: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task OversizedContentLength_IsRejected()
        {
            var frame = Encoding.ASCII.GetBytes("Content-Length: 1000000\r\n\r\n");
            var input = new MemoryStream(frame);

            // maxMessageSize below the advertised Content-Length -> reject before allocating.
            var transport = new StdioTransport(input, new MemoryStream(), maxMessageSize: 1024);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                transport.ReadMessageAsync(CancellationToken.None));
        }

        [Fact]
        public async Task OversizedLineDelimitedMessage_IsRejected()
        {
            var big = "{\"data\":\"" + new string('x', 5000) + "\"}\n";
            var input = new MemoryStream(Encoding.UTF8.GetBytes(big));

            var transport = new StdioTransport(input, new MemoryStream(), maxMessageSize: 1024);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                transport.ReadMessageAsync(CancellationToken.None));
        }

        /// <summary>
        /// A stream whose reads block until the cancellation token fires, used to
        /// simulate an idle stdin.
        /// </summary>
        private sealed class BlockingStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set { } }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
