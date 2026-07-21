using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Core.Transport
{
    /// <summary>
    /// The line/message framing used on a stdio connection.
    /// </summary>
    public enum StdioFramingMode
    {
        /// <summary>Newline-delimited JSON (Zed/Gemini style).</summary>
        LineDelimited,

        /// <summary>Content-Length header framing (LSP/MCP style).</summary>
        ContentLength
    }

    /// <summary>
    /// Transport implementation using standard input/output streams.
    /// Framing is performed at the byte-stream layer so that raw UTF-8 payloads
    /// round-trip exactly under Content-Length framing, and responses are written
    /// using the same framing mode that was detected on input.
    /// </summary>
    public class StdioTransport : ITransport
    {
        private const int DefaultMaxMessageSize = 64 * 1024 * 1024; // 64 MiB
        private const int MaxHeaderLineLength = 8 * 1024;           // 8 KiB per header line
        private const int MaxHeaderCount = 100;

        private readonly ILogger<StdioTransport>? _logger;
        private readonly Stream _inputStream;
        private readonly Stream _outputStream;
        private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
        private readonly bool _ownsStreams;
        private readonly string _lineTerminator;
        private readonly long _maxMessageSize;

        private readonly byte[] _buffer = new byte[8192];
        private int _bufferPos;
        private int _bufferLen;

        // Framing mode used for outbound writes. Starts as line-delimited and is
        // updated to match whatever framing is detected on the first inbound message.
        private StdioFramingMode _framingMode = StdioFramingMode.LineDelimited;

        private volatile bool _closed;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance using the process standard input/output streams.
        /// </summary>
        public StdioTransport(ILogger<StdioTransport>? logger = null, long maxMessageSize = DefaultMaxMessageSize)
        {
            _logger = logger;
            _inputStream = Console.OpenStandardInput();
            _outputStream = Console.OpenStandardOutput();
            _ownsStreams = true;
            _lineTerminator = "\n";
            _maxMessageSize = maxMessageSize;

            _logger?.LogDebug("StdioTransport initialized using stdin/stdout (byte framing)");
        }

        /// <summary>
        /// Initializes a new instance using caller-provided streams. The caller retains
        /// ownership of the streams and is responsible for disposing them.
        /// </summary>
        public StdioTransport(Stream inputStream, Stream outputStream, ILogger<StdioTransport>? logger = null, long maxMessageSize = DefaultMaxMessageSize)
        {
            _logger = logger;
            _inputStream = inputStream ?? throw new ArgumentNullException(nameof(inputStream));
            _outputStream = outputStream ?? throw new ArgumentNullException(nameof(outputStream));
            _ownsStreams = false;
            _lineTerminator = "\r\n";
            _maxMessageSize = maxMessageSize;

            _logger?.LogDebug("StdioTransport initialized (byte framing)");
        }

        /// <summary>
        /// The framing mode currently used for outbound writes.
        /// </summary>
        public StdioFramingMode FramingMode => _framingMode;

        /// <inheritdoc />
        public bool IsConnected => !_disposed && !_closed && _inputStream.CanRead && _outputStream.CanWrite;

        /// <inheritdoc />
        public async Task<string> ReadMessageAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Read the first line. In line-delimited mode this is the whole message,
                // so allow it to grow up to the maximum message size.
                var firstLineBytes = await ReadLineBytesAsync(_maxMessageSize, cancellationToken).ConfigureAwait(false);
                if (firstLineBytes == null)
                    throw new EndOfStreamException("End of input stream");

                var firstLine = Encoding.UTF8.GetString(firstLineBytes);

                if (string.IsNullOrEmpty(firstLine))
                    throw new InvalidOperationException("Unexpected empty first line");

                if (firstLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    _framingMode = StdioFramingMode.ContentLength;
                    return await ReadContentLengthMessageAsync(firstLine, cancellationToken).ConfigureAwait(false);
                }

                if (firstLine.TrimStart().StartsWith("{") || firstLine.TrimStart().StartsWith("["))
                {
                    _framingMode = StdioFramingMode.LineDelimited;
                    _logger?.LogTrace("Detected line-delimited JSON message");
                    return firstLine.Trim();
                }

                throw new InvalidOperationException($"Unexpected message framing. Line: {firstLine}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (EndOfStreamException)
            {
                _logger?.LogDebug("End of stream reached");
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error reading message from input");
                throw;
            }
        }

        private async Task<string> ReadContentLengthMessageAsync(string firstLine, CancellationToken cancellationToken)
        {
            int contentLength = ParseContentLength(firstLine);

            // Consume remaining headers until the blank line that separates headers from body.
            int headerCount = 0;
            while (true)
            {
                var headerBytes = await ReadLineBytesAsync(MaxHeaderLineLength, cancellationToken).ConfigureAwait(false);
                if (headerBytes == null)
                    throw new EndOfStreamException("Unexpected end of stream while reading headers");

                if (headerBytes.Length == 0)
                    break; // blank line -> end of headers

                if (++headerCount > MaxHeaderCount)
                    throw new InvalidOperationException("Too many headers");
            }

            var body = await ReadExactBytesAsync(contentLength, cancellationToken).ConfigureAwait(false);
            _logger?.LogTrace("Read Content-Length message of {Length} bytes", contentLength);
            return Encoding.UTF8.GetString(body);
        }

        private int ParseContentLength(string headerLine)
        {
            int colon = headerLine.IndexOf(':');
            var value = colon >= 0 ? headerLine.Substring(colon + 1).Trim() : string.Empty;

            if (!int.TryParse(value, out int contentLength) || contentLength < 0)
                throw new InvalidOperationException($"Invalid Content-Length value: {headerLine}");

            if (contentLength > _maxMessageSize)
                throw new InvalidOperationException(
                    $"Content-Length {contentLength} exceeds maximum message size {_maxMessageSize}");

            return contentLength;
        }

        /// <inheritdoc />
        public async Task WriteMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (message == null)
                throw new ArgumentNullException(nameof(message));

            cancellationToken.ThrowIfCancellationRequested();

            await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var payload = Encoding.UTF8.GetBytes(message);
                byte[] frame;

                if (_framingMode == StdioFramingMode.ContentLength)
                {
                    var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
                    frame = new byte[header.Length + payload.Length];
                    Buffer.BlockCopy(header, 0, frame, 0, header.Length);
                    Buffer.BlockCopy(payload, 0, frame, header.Length, payload.Length);
                }
                else
                {
                    var terminator = Encoding.ASCII.GetBytes(_lineTerminator);
                    frame = new byte[payload.Length + terminator.Length];
                    Buffer.BlockCopy(payload, 0, frame, 0, payload.Length);
                    Buffer.BlockCopy(terminator, 0, frame, payload.Length, terminator.Length);
                }

                await _outputStream.WriteAsync(frame.AsMemory(0, frame.Length), cancellationToken).ConfigureAwait(false);
                await _outputStream.FlushAsync(cancellationToken).ConfigureAwait(false);

                _logger?.LogTrace("Wrote message ({Length} bytes, {Mode})", payload.Length, _framingMode);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger?.LogError(ex, "Error writing message to output");
                throw;
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        /// <inheritdoc />
        public async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            if (_closed || _disposed)
                return;

            _closed = true;
            try
            {
                _logger?.LogDebug("Closing StdioTransport");
                // Flush any buffered output. Cleanup must not itself be cancelled.
                await _outputStream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error occurred while closing StdioTransport");
            }
        }

        /// <summary>
        /// Reads one line of raw bytes terminated by <c>\n</c>. A single preceding
        /// <c>\r</c> is stripped. Returns an empty array for a blank line and
        /// <c>null</c> at end of stream (with no partial data buffered).
        /// </summary>
        private async Task<byte[]?> ReadLineBytesAsync(long maxLength, CancellationToken cancellationToken)
        {
            using var ms = new MemoryStream();
            while (true)
            {
                int b = await ReadRawByteAsync(cancellationToken).ConfigureAwait(false);
                if (b < 0)
                {
                    if (ms.Length == 0)
                        return null; // clean EOF at a message boundary
                    break;           // trailing line without newline
                }

                if (b == '\n')
                    break;

                ms.WriteByte((byte)b);
                if (ms.Length > maxLength)
                    throw new InvalidOperationException($"Line exceeds maximum length of {maxLength} bytes");
            }

            var arr = ms.ToArray();
            int len = arr.Length;
            if (len > 0 && arr[len - 1] == (byte)'\r')
            {
                Array.Resize(ref arr, len - 1);
            }
            return arr;
        }

        /// <summary>
        /// Reads exactly <paramref name="count"/> bytes, draining the internal buffer
        /// first and then reading from the underlying stream.
        /// </summary>
        private async Task<byte[]> ReadExactBytesAsync(int count, CancellationToken cancellationToken)
        {
            var result = new byte[count];
            int filled = 0;

            while (filled < count && _bufferPos < _bufferLen)
                result[filled++] = _buffer[_bufferPos++];

            while (filled < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int n = await _inputStream.ReadAsync(result.AsMemory(filled, count - filled), cancellationToken).ConfigureAwait(false);
                if (n == 0)
                    throw new EndOfStreamException("Unexpected end of stream while reading message content");
                filled += n;
            }

            return result;
        }

        /// <summary>
        /// Reads a single byte from the buffered input, refilling from the underlying
        /// stream as needed. The read observes the cancellation token so an idle read
        /// can be aborted within a bounded time.
        /// </summary>
        private async ValueTask<int> ReadRawByteAsync(CancellationToken cancellationToken)
        {
            if (_bufferPos >= _bufferLen)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _bufferLen = await _inputStream.ReadAsync(_buffer.AsMemory(0, _buffer.Length), cancellationToken).ConfigureAwait(false);
                _bufferPos = 0;
                if (_bufferLen == 0)
                    return -1;
            }
            return _buffer[_bufferPos++];
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StdioTransport));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed || !disposing)
                return;

            _logger?.LogDebug("Disposing StdioTransport");
            _closed = true;

            try
            {
                _writeSemaphore.Dispose();

                // Only dispose streams we opened ourselves; caller-provided streams
                // remain the caller's responsibility.
                if (_ownsStreams)
                {
                    _inputStream.Dispose();
                    _outputStream.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error occurred while disposing StdioTransport");
            }

            _disposed = true;
        }
    }
}
