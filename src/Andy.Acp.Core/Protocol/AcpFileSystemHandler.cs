using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.FileSystem;
using Andy.Acp.Core.JsonRpc;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Core.Protocol
{
    /// <summary>
    /// Handles ACP file system protocol methods (fs/read_text_file, fs/write_text_file)
    /// </summary>
    public class AcpFileSystemHandler
    {
        private readonly IFileSystemProvider _fileSystemProvider;
        private readonly ILogger<AcpFileSystemHandler>? _logger;
        private readonly JsonRpcHandler _jsonRpcHandler;

        public AcpFileSystemHandler(
            IFileSystemProvider fileSystemProvider,
            JsonRpcHandler jsonRpcHandler,
            ILogger<AcpFileSystemHandler>? logger = null)
        {
            _fileSystemProvider = fileSystemProvider ?? throw new ArgumentNullException(nameof(fileSystemProvider));
            _jsonRpcHandler = jsonRpcHandler ?? throw new ArgumentNullException(nameof(jsonRpcHandler));
            _logger = logger;
        }

        /// <summary>
        /// Register all fs/* methods with the JSON-RPC handler
        /// </summary>
        public void RegisterMethods()
        {
            _jsonRpcHandler.RegisterMethod("fs/read_text_file", HandleReadTextFileAsync);
            _jsonRpcHandler.RegisterMethod("fs/write_text_file", HandleWriteTextFileAsync);

            _logger?.LogInformation("Registered ACP file system methods: fs/read_text_file, fs/write_text_file");
        }

        private async Task<object?> HandleReadTextFileAsync(object? parameters, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Handling fs/read_text_file request");

            try
            {
                var readParams = DeserializeParams<ReadTextFileRequest>(parameters);

                if (string.IsNullOrEmpty(readParams?.Path))
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "File path is required");
                }

                _logger?.LogInformation("Reading file: {Path}", readParams.Path);

                var content = await _fileSystemProvider.ReadTextFileAsync(readParams.Path, cancellationToken);

                return new
                {
                    path = readParams.Path,
                    content
                };
            }
            catch (System.IO.FileNotFoundException ex)
            {
                _logger?.LogWarning(ex, "File not found");
                throw new JsonRpcProtocolException(
                    JsonRpcErrorCodes.InvalidParams,
                    $"File not found: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger?.LogWarning(ex, "Access denied");
                throw new JsonRpcProtocolException(
                    JsonRpcErrorCodes.InvalidParams,
                    $"Access denied: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error reading file");
                throw;
            }
        }

        private async Task<object?> HandleWriteTextFileAsync(object? parameters, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Handling fs/write_text_file request");

            try
            {
                var writeParams = DeserializeParams<WriteTextFileRequest>(parameters);

                if (string.IsNullOrEmpty(writeParams?.Path))
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "File path is required");
                }

                if (writeParams.Content == null)
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "File content is required");
                }

                _logger?.LogInformation("Writing file: {Path} ({Length} bytes)",
                    writeParams.Path, writeParams.Content.Length);

                await _fileSystemProvider.WriteTextFileAsync(
                    writeParams.Path,
                    writeParams.Content,
                    cancellationToken);

                return new
                {
                    path = writeParams.Path,
                    success = true
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger?.LogWarning(ex, "Access denied");
                throw new JsonRpcProtocolException(
                    JsonRpcErrorCodes.InvalidParams,
                    $"Access denied: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error writing file");
                throw;
            }
        }

        private T? DeserializeParams<T>(object? parameters) where T : class
        {
            if (parameters == null)
                return null;

            if (parameters is JsonElement jsonElement)
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
            }

            var json = JsonSerializer.Serialize(parameters);
            return JsonSerializer.Deserialize<T>(json);
        }

        private class ReadTextFileRequest
        {
            public string Path { get; set; } = string.Empty;
        }

        private class WriteTextFileRequest
        {
            public string Path { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
        }
    }
}
