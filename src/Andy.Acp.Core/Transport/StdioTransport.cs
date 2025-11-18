using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Core.Transport
{
    /// <summary>
    /// Transport implementation using standard input/output streams
    /// </summary>
    public class StdioTransport : ITransport
    {
        private readonly ILogger<StdioTransport>? _logger;
        private readonly Stream _inputStream;
        private readonly Stream _outputStream;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the StdioTransport class with optional logger
        /// </summary>
        /// <param name="logger">Optional logger instance</param>
        public StdioTransport(ILogger<StdioTransport>? logger = null)
        {
            _logger = logger;

            // Use the raw stdin/stdout streams
            _inputStream = Console.OpenStandardInput();
            _outputStream = Console.OpenStandardOutput();

            // Create our own readers/writers with explicit settings
            _reader = new StreamReader(_inputStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            _writer = new StreamWriter(_outputStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n" // Use LF only, not CRLF
            };

            _logger?.LogDebug("StdioTransport initialized using stdin/stdout with custom readers");
        }

        /// <summary>
        /// Initializes a new instance of the StdioTransport class with custom streams
        /// </summary>
        /// <param name="inputStream">Input stream</param>
        /// <param name="outputStream">Output stream</param>
        /// <param name="logger">Optional logger instance</param>
        public StdioTransport(Stream inputStream, Stream outputStream, ILogger<StdioTransport>? logger = null)
        {
            _logger = logger;
            _inputStream = inputStream ?? throw new ArgumentNullException(nameof(inputStream));
            _outputStream = outputStream ?? throw new ArgumentNullException(nameof(outputStream));

            _reader = new StreamReader(_inputStream, Encoding.UTF8);
            _writer = new StreamWriter(_outputStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
                NewLine = "\r\n" // Use CRLF for protocol compatibility
            };

            _logger?.LogDebug("StdioTransport initialized");
        }

        /// <inheritdoc />
        public bool IsConnected => !_disposed && _inputStream.CanRead && _outputStream.CanWrite;

        /// <inheritdoc />
        public async Task<string> ReadMessageAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            try
            {
                _logger?.LogTrace("Reading message from stdin");

                // Read headers until we find Content-Length
                int contentLength = -1;
                string headerLine;

                while (true)
                {
                    headerLine = await ReadLineAsync(cancellationToken);

                    if (string.IsNullOrEmpty(headerLine))
                    {
                        // Empty line indicates end of headers
                        if (contentLength == -1)
                        {
                            throw new InvalidOperationException("Missing Content-Length header");
                        }
                        break;
                    }

                    if (headerLine.StartsWith("Content-Length: ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!int.TryParse(headerLine.Substring(16), out contentLength) || contentLength < 0)
                        {
                            throw new InvalidOperationException($"Invalid Content-Length value: {headerLine}");
                        }
                    }
                    // Ignore other headers
                }

                // Read message content
                char[] buffer = new char[contentLength];
                int totalRead = 0;

                while (totalRead < contentLength)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int bytesRead = await _reader.ReadAsync(buffer, totalRead, contentLength - totalRead);
                    if (bytesRead == 0)
                    {
                        throw new InvalidOperationException("Unexpected end of stream while reading message content");
                    }
                    totalRead += bytesRead;
                }

                string message = new string(buffer);
                _logger?.LogTrace("Successfully read message of {Length} characters", contentLength);

                return message;
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
                _logger?.LogError(ex, "Error reading message from stdin");
                throw;
            }
        }

        /// <inheritdoc />
        public async Task WriteMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (message == null)
                throw new ArgumentNullException(nameof(message));

            cancellationToken.ThrowIfCancellationRequested();

            await _writeSemaphore.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger?.LogTrace("Writing message to stdout with {Length} characters", message.Length);

                var messageBytes = Encoding.UTF8.GetBytes(message);
                var contentLength = messageBytes.Length;

                // Write Content-Length header
                await _writer.WriteLineAsync($"Content-Length: {contentLength}");

                // Write empty line separator
                await _writer.WriteLineAsync();

                // Write message content
                await _writer.WriteAsync(message);
                await _writer.FlushAsync();

                _logger?.LogTrace("Successfully wrote message");
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger?.LogError(ex, "Error writing message to stdout");
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
            if (_disposed)
                return;

            try
            {
                _logger?.LogDebug("Closing StdioTransport");

                await _writer.FlushAsync();
                _writer.Close();
                _reader.Close();

                _logger?.LogDebug("StdioTransport closed successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error occurred while closing StdioTransport");
            }
        }

        /// <summary>
        /// Reads a line from the input stream asynchronously
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The line read from the stream</returns>
        private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Use standard ReadLineAsync for better compatibility with piped stdin
            var line = await _reader.ReadLineAsync();

            if (line == null)
            {
                throw new EndOfStreamException("Unexpected end of stream");
            }

            return line;
        }

        /// <summary>
        /// Reads a single character from the input stream asynchronously
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The character read, or -1 if end of stream</returns>
        private async Task<int> ReadCharAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // For console stdin, we need to use a different approach since ReadAsync doesn't respect cancellation
            var buffer = new char[1];
            var readTask = _reader.ReadAsync(buffer, 0, 1);

            // Create a delay task that completes when cancelled
            var delayTask = Task.Delay(100, cancellationToken);

            while (!readTask.IsCompleted)
            {
                var completedTask = await Task.WhenAny(readTask, delayTask);

                if (completedTask == delayTask)
                {
                    // Cancellation was requested
                    cancellationToken.ThrowIfCancellationRequested();
                    // Create a new delay task for the next iteration
                    delayTask = Task.Delay(100, cancellationToken);
                }
                else
                {
                    // Read task completed
                    break;
                }
            }

            int result = await readTask;
            return result == 0 ? -1 : buffer[0];
        }

        /// <summary>
        /// Throws ObjectDisposedException if the transport has been disposed
        /// </summary>
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

        /// <summary>
        /// Disposes the transport resources
        /// </summary>
        /// <param name="disposing">True if disposing managed resources</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _logger?.LogDebug("Disposing StdioTransport");

                try
                {
                    _writeSemaphore?.Dispose();
                    _writer?.Dispose();
                    _reader?.Dispose();

                    // Only dispose streams if we don't own the standard streams
                    if (_inputStream != Console.OpenStandardInput())
                        _inputStream?.Dispose();
                    if (_outputStream != Console.OpenStandardOutput())
                        _outputStream?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error occurred while disposing StdioTransport");
                }

                _disposed = true;
            }
        }
    }
}