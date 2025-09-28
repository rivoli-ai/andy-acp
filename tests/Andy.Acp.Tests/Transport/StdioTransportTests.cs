using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Andy.Acp.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Tests.Transport
{
    public class StdioTransportTests
    {
        private readonly TestStreamProvider _streamProvider;

        public StdioTransportTests()
        {
            _streamProvider = new TestStreamProvider();
        }

        [Fact]
        public async Task ReadMessageAsync_ValidMessage_ReturnsCorrectMessage()
        {
            // Arrange
            var testMessage = new { id = 1, method = "test", @params = new { value = "hello" } };
            var json = JsonSerializer.Serialize(testMessage);
            var messageWithHeader = $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}";

            _streamProvider.SetInputData(messageWithHeader);
            var transport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Act
            var result = await transport.ReadMessageAsync(CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(json, result);
        }

        [Fact]
        public async Task ReadMessageAsync_MessageWithExtraHeaders_ReturnsCorrectMessage()
        {
            // Arrange
            var testMessage = new { id = 1, method = "test" };
            var json = JsonSerializer.Serialize(testMessage);
            var messageWithHeaders = $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\nContent-Type: application/json\r\nX-Custom-Header: value\r\n\r\n{json}";

            _streamProvider.SetInputData(messageWithHeaders);
            var transport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Act
            var result = await transport.ReadMessageAsync(CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(json, result);
        }

        [Fact]
        public async Task ReadMessageAsync_InvalidContentLength_ThrowsException()
        {
            // Arrange
            var messageWithInvalidHeader = "Content-Length: invalid\r\n\r\n{}";
            _streamProvider.SetInputData(messageWithInvalidHeader);
            var transport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                transport.ReadMessageAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ReadMessageAsync_MissingContentLength_ThrowsException()
        {
            // Arrange
            var messageWithoutHeader = "\r\n{}";
            _streamProvider.SetInputData(messageWithoutHeader);
            var transport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                transport.ReadMessageAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ReadMessageAsync_InvalidJson_ReturnsInvalidJsonString()
        {
            // Arrange
            var invalidJson = "{ invalid json }";
            var messageWithHeader = $"Content-Length: {Encoding.UTF8.GetByteCount(invalidJson)}\r\n\r\n{invalidJson}";
            _streamProvider.SetInputData(messageWithHeader);
            var transport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Act
            var result = await transport.ReadMessageAsync(CancellationToken.None);

            // Assert - Transport returns raw string, JSON validation happens at higher layer
            Assert.Equal(invalidJson, result);
        }

        [Fact]
        public async Task WriteMessageAsync_ValidMessage_WritesCorrectFormat()
        {
            // Arrange
            var testMessage = new { id = 1, method = "test", @params = new { value = "hello" } };
            var transport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Act
            await transport.WriteMessageAsync(JsonSerializer.Serialize(testMessage), CancellationToken.None);

            // Assert
            var output = _streamProvider.GetOutputData();
            var json = JsonSerializer.Serialize(testMessage);
            var expectedOutput = $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}";
            Assert.Equal(expectedOutput, output);
        }

        [Fact]
        public async Task WriteMessageAsync_NullMessage_ThrowsException()
        {
            // Arrange
            var transport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                transport.WriteMessageAsync(null!, CancellationToken.None));
        }

        [Fact]
        public async Task RoundTrip_MessageWrittenAndRead_PreservesData()
        {
            // Arrange
            var originalMessage = new
            {
                id = 42,
                method = "test_method",
                @params = new
                {
                    stringValue = "hello world",
                    numberValue = 123.45,
                    boolValue = true,
                    arrayValue = new[] { 1, 2, 3 },
                    objectValue = new { nested = "value" }
                }
            };

            var writeTransport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Act - Write
            await writeTransport.WriteMessageAsync(JsonSerializer.Serialize(originalMessage), CancellationToken.None);

            // Setup for read
            var outputData = _streamProvider.GetOutputData();
            _streamProvider.SetInputData(outputData);
            var readTransport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Act - Read
            var readMessage = await readTransport.ReadMessageAsync(CancellationToken.None);

            // Assert
            var originalJson = JsonSerializer.Serialize(originalMessage);
            Assert.Equal(originalJson, readMessage);
        }

        [Fact]
        public async Task ReadWriteMultipleMessages_SequentialProcessing_AllMessagesProcessed()
        {
            // Arrange
            var messages = new[]
            {
                new { id = 1, method = "first" },
                new { id = 2, method = "second" },
                new { id = 3, method = "third" }
            };

            var writeTransport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Act - Write all messages
            foreach (var message in messages)
            {
                await writeTransport.WriteMessageAsync(JsonSerializer.Serialize(message), CancellationToken.None);
            }

            // Setup for reading
            var outputData = _streamProvider.GetOutputData();
            _streamProvider.SetInputData(outputData);
            var readTransport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Act & Assert - Read all messages
            for (int i = 0; i < messages.Length; i++)
            {
                var readMessage = await readTransport.ReadMessageAsync(CancellationToken.None);
                var expectedJson = JsonSerializer.Serialize(messages[i]);
                Assert.Equal(expectedJson, readMessage);
            }
        }

        [Fact]
        public async Task ReadWriteLargeMessage_MessageExceedsBufferSize_HandledCorrectly()
        {
            // Arrange
            var largeString = new string('x', 100000); // 100KB string
            var largeMessage = new { id = 1, data = largeString };

            var writeTransport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Act - Write
            await writeTransport.WriteMessageAsync(JsonSerializer.Serialize(largeMessage), CancellationToken.None);

            // Setup for read
            var outputData = _streamProvider.GetOutputData();
            _streamProvider.SetInputData(outputData);
            var readTransport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Act - Read
            var readMessage = await readTransport.ReadMessageAsync(CancellationToken.None);

            // Assert
            var originalJson = JsonSerializer.Serialize(largeMessage);
            Assert.Equal(originalJson, readMessage);
        }

        [Fact]
        public async Task ReadMessageAsync_PreCancelledToken_ThrowsOperationCancelledException()
        {
            // Arrange
            var messageWithHeader = "Content-Length: 10\r\n\r\n{\"test\":1}";
            _streamProvider.SetInputData(messageWithHeader);
            var transport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Create a pre-cancelled token
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                transport.ReadMessageAsync(cts.Token));
        }

        [Fact]
        public async Task WriteMessageAsync_PreCancelledToken_ThrowsOperationCancelledException()
        {
            // Arrange
            var transport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);
            var message = new { id = 1, method = "test" };

            // Create a pre-cancelled token
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                transport.WriteMessageAsync(JsonSerializer.Serialize(message), cts.Token));
        }

        [Fact]
        public async Task ConcurrentWrites_MultipleThreads_AllMessagesWritten()
        {
            // Arrange
            var transport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);
            var tasks = new Task[10];

            // Act
            for (int i = 0; i < tasks.Length; i++)
            {
                int messageId = i;
                tasks[i] = Task.Run(async () =>
                {
                    var message = new { id = messageId, method = $"concurrent_{messageId}" };
                    await transport.WriteMessageAsync(JsonSerializer.Serialize(message), CancellationToken.None);
                });
            }

            await Task.WhenAll(tasks);

            // Assert
            var output = _streamProvider.GetOutputData();

            // Parse messages by looking for complete message patterns
            var messages = new List<string>();
            var currentPos = 0;

            while (currentPos < output.Length)
            {
                var headerStart = output.IndexOf("Content-Length:", currentPos);
                if (headerStart == -1) break;

                var headerEnd = output.IndexOf("\r\n\r\n", headerStart);
                if (headerEnd == -1)
                {
                    headerEnd = output.IndexOf("\n\n", headerStart);
                    if (headerEnd == -1) break;
                }

                messages.Add($"Message found at position {headerStart}");
                currentPos = headerEnd + 4;
            }

            Assert.Equal(10, messages.Count);
        }

        [Fact]
        public async Task ReadMessageAsync_StreamClosed_ThrowsException()
        {
            // Arrange
            var messageWithHeader = "Content-Length: 2\r\n\r\n{}";
            _streamProvider.SetInputData(messageWithHeader);
            var transport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Read one message successfully
            await transport.ReadMessageAsync(CancellationToken.None);

            // Now stream is at EOF

            // Act & Assert
            await Assert.ThrowsAsync<EndOfStreamException>(() =>
                transport.ReadMessageAsync(CancellationToken.None));
        }

        [Fact]
        public async Task WriteMessageAsync_StreamClosed_ThrowsException()
        {
            // Arrange
            var transport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);
            var message = new { id = 1, method = "test" };

            // Close the output stream after transport is created
            _streamProvider.CloseOutputStream();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                transport.WriteMessageAsync(JsonSerializer.Serialize(message), CancellationToken.None));
        }

        [Fact]
        public async Task ReadMessageAsync_EmptyMessage_HandledCorrectly()
        {
            // Arrange
            var emptyMessage = "{}";
            var messageWithHeader = $"Content-Length: {Encoding.UTF8.GetByteCount(emptyMessage)}\r\n\r\n{emptyMessage}";

            _streamProvider.SetInputData(messageWithHeader);
            var transport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Act
            var result = await transport.ReadMessageAsync(CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("{}", result);
        }

        [Fact]
        public async Task ReadMessageAsync_UnicodeContent_HandledCorrectly()
        {
            // Arrange
            var unicodeMessage = new { message = "Hello 世界 🌍" };
            var json = JsonSerializer.Serialize(unicodeMessage);
            var messageWithHeader = $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}";

            _streamProvider.SetInputData(messageWithHeader);
            var transport = new StdioTransport(_streamProvider.InputStream, _streamProvider.OutputStream);

            // Act
            var result = await transport.ReadMessageAsync(CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(json, result);
        }
    }

    /// <summary>
    /// Test helper class that provides controllable input and output streams for testing
    /// </summary>
    public class TestStreamProvider
    {
        private MemoryStream _inputStream;
        private MemoryStream _outputStream;
        private bool _inputClosed;
        private bool _outputClosed;

        public TestStreamProvider()
        {
            _inputStream = new MemoryStream();
            _outputStream = new MemoryStream();
        }

        public Stream InputStream => new TestInputStream(_inputStream, () => _inputClosed);
        public Stream OutputStream => new TestOutputStream(_outputStream, () => _outputClosed);

        public void SetInputData(string data)
        {
            _inputStream = new MemoryStream(Encoding.UTF8.GetBytes(data));
            _inputClosed = false;
        }

        public string GetOutputData()
        {
            return Encoding.UTF8.GetString(_outputStream.ToArray());
        }

        public void CloseInputStream()
        {
            _inputClosed = true;
        }

        public void CloseOutputStream()
        {
            _outputClosed = true;
        }
    }

    /// <summary>
    /// Test wrapper for input stream that can simulate closed state
    /// </summary>
    public class TestInputStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly Func<bool> _isClosedFunc;

        public TestInputStream(Stream innerStream, Func<bool> isClosedFunc)
        {
            _innerStream = innerStream;
            _isClosedFunc = isClosedFunc;
        }

        public override bool CanRead => !_isClosedFunc() && _innerStream.CanRead;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _innerStream.Length;
        public override long Position
        {
            get => _innerStream.Position;
            set => _innerStream.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_isClosedFunc())
                throw new EndOfStreamException();

            return _innerStream.Read(buffer, offset, count);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_isClosedFunc())
                throw new EndOfStreamException();

            cancellationToken.ThrowIfCancellationRequested();
            return await _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override void Flush() => _innerStream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _innerStream?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Test wrapper for output stream that can simulate closed state
    /// </summary>
    public class TestOutputStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly Func<bool> _isClosedFunc;

        public TestOutputStream(Stream innerStream, Func<bool> isClosedFunc)
        {
            _innerStream = innerStream;
            _isClosedFunc = isClosedFunc;
        }

        public override bool CanRead => false;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => !_isClosedFunc() && _innerStream.CanWrite;
        public override long Length => _innerStream.Length;
        public override long Position
        {
            get => _innerStream.Position;
            set => _innerStream.Position = value;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_isClosedFunc())
                throw new ObjectDisposedException(nameof(TestOutputStream));

            _innerStream.Write(buffer, offset, count);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_isClosedFunc())
                throw new ObjectDisposedException(nameof(TestOutputStream));

            cancellationToken.ThrowIfCancellationRequested();
            await _innerStream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => _innerStream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _innerStream?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}