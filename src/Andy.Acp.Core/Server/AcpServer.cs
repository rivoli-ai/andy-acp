using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.FileSystem;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Protocol;
using Andy.Acp.Core.Session;
using Andy.Acp.Core.Terminal;
using Andy.Acp.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Core.Server
{
    /// <summary>
    /// Unified ACP server that handles the complete Agent Client Protocol.
    /// This is the main entry point for implementing an ACP-compatible agent.
    /// </summary>
    public class AcpServer
    {
        private readonly IAgentProvider _agentProvider;
        private readonly IFileSystemProvider? _fileSystemProvider;
        private readonly ITerminalProvider? _terminalProvider;
        private readonly ServerInfo _serverInfo;
        private readonly ILoggerFactory? _loggerFactory;
        private readonly ILogger<AcpServer>? _logger;

        /// <summary>
        /// Create a new ACP server with the specified providers.
        /// </summary>
        /// <param name="agentProvider">Required: The agent that handles prompts and reasoning</param>
        /// <param name="fileSystemProvider">Optional: Provider for file operations (fs/*)</param>
        /// <param name="terminalProvider">Optional: Provider for terminal operations (terminal/*)</param>
        /// <param name="serverInfo">Optional: Server identification information</param>
        /// <param name="loggerFactory">Optional: Logger factory for diagnostics</param>
        public AcpServer(
            IAgentProvider agentProvider,
            IFileSystemProvider? fileSystemProvider = null,
            ITerminalProvider? terminalProvider = null,
            ServerInfo? serverInfo = null,
            ILoggerFactory? loggerFactory = null)
        {
            _agentProvider = agentProvider ?? throw new ArgumentNullException(nameof(agentProvider));
            _fileSystemProvider = fileSystemProvider;
            _terminalProvider = terminalProvider;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory?.CreateLogger<AcpServer>();

            _serverInfo = serverInfo ?? new ServerInfo
            {
                Name = "ACP Server",
                Version = "1.0.0",
                Description = "Agent Client Protocol Server"
            };
        }

        /// <summary>
        /// Run the ACP server in stdio mode.
        /// This method blocks until the server is stopped (via shutdown or cancellation).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to stop the server</param>
        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("Starting ACP server: {Name} v{Version}", _serverInfo.Name, _serverInfo.Version);

            try
            {
                // Create transport layer (stdio)
                var transport = new StdioTransport(_loggerFactory?.CreateLogger<StdioTransport>());

                // Create JSON-RPC handler
                var jsonRpcHandler = new JsonRpcHandler(_loggerFactory?.CreateLogger<JsonRpcHandler>());

                // Create session manager
                var sessionManager = new SessionManager(_loggerFactory?.CreateLogger<SessionManager>());

                // Get agent capabilities
                var agentCapabilities = _agentProvider.GetCapabilities();

                // Build server capabilities based on available providers
                var serverCapabilities = new ServerCapabilities
                {
                    // Agent capabilities
                    LoadSession = agentCapabilities.LoadSession,
                    AudioPrompts = agentCapabilities.AudioPrompts,
                    ImagePrompts = agentCapabilities.ImagePrompts,
                    EmbeddedContext = agentCapabilities.EmbeddedContext,

                    // File system capabilities
                    FileSystemSupported = _fileSystemProvider != null,

                    // Terminal capabilities
                    TerminalSupported = _terminalProvider != null,

                    // Tools (MCP compatibility)
                    Tools = new ToolsCapability
                    {
                        Supported = false, // We're using session/prompt, not tools/call
                        Available = Array.Empty<string>(),
                        ListSupported = false,
                        ExecutionSupported = false
                    },

                    // Resources
                    Resources = new ResourcesCapability
                    {
                        Supported = _fileSystemProvider != null,
                        SupportedSchemes = _fileSystemProvider != null ? new[] { "file://" } : Array.Empty<string>()
                    },

                    // Prompts
                    Prompts = new PromptsCapability
                    {
                        Supported = true // We support session/prompt
                    },

                    // Logging
                    Logging = new LoggingCapability
                    {
                        Supported = true,
                        SupportedLevels = new[] { "debug", "info", "warning", "error" }
                    }
                };

                // Register protocol handlers
                var protocolHandler = new AcpProtocolHandler(
                    sessionManager,
                    _serverInfo,
                    serverCapabilities,
                    _loggerFactory?.CreateLogger<AcpProtocolHandler>());
                protocolHandler.RegisterMethods(jsonRpcHandler);

                // Register session handler (the most important one)
                var sessionHandler = new AcpSessionHandler(
                    _agentProvider,
                    jsonRpcHandler,
                    _loggerFactory?.CreateLogger<AcpSessionHandler>());
                sessionHandler.SetTransport(transport);  // Enable sending session/update notifications
                sessionHandler.RegisterMethods();

                // Register file system handler if provider is available
                if (_fileSystemProvider != null)
                {
                    var fileSystemHandler = new AcpFileSystemHandler(
                        _fileSystemProvider,
                        jsonRpcHandler,
                        _loggerFactory?.CreateLogger<AcpFileSystemHandler>());
                    fileSystemHandler.RegisterMethods();
                }

                // Register terminal handler if provider is available
                if (_terminalProvider != null)
                {
                    var terminalHandler = new AcpTerminalHandler(
                        _terminalProvider,
                        jsonRpcHandler,
                        _loggerFactory?.CreateLogger<AcpTerminalHandler>());
                    terminalHandler.RegisterMethods();
                }

                // Start session manager
                await sessionManager.StartAsync();

                var supportedMethods = jsonRpcHandler.GetSupportedMethods();
                _logger?.LogInformation("ACP server initialized with {MethodCount} methods: {Methods}",
                    supportedMethods.Count(),
                    string.Join(", ", supportedMethods));

                // Main server loop - read and process messages
                while (!cancellationToken.IsCancellationRequested && transport.IsConnected)
                {
                    try
                    {
                        // Read message from transport
                        var messageJson = await transport.ReadMessageAsync(cancellationToken);

                        if (string.IsNullOrEmpty(messageJson))
                        {
                            _logger?.LogDebug("Received empty message, connection may be closed");
                            break;
                        }

                        _logger?.LogTrace("Received message: {MessageLength} bytes", messageJson.Length);

                        // Deserialize and handle JSON-RPC message
                        JsonRpcResponse? response = null;

                        try
                        {
                            var jsonRpcMessage = JsonRpcSerializer.Deserialize(messageJson);

                            if (jsonRpcMessage is JsonRpcRequest request)
                            {
                                _logger?.LogDebug("Processing request: {Method} (ID: {Id})", request.Method, request.Id);

                                // Handle the request - session management is done by protocol handler
                                response = await jsonRpcHandler.HandleRequestAsync(request, cancellationToken);
                            }
                            else if (jsonRpcMessage is JsonRpcResponse responseMessage)
                            {
                                _logger?.LogDebug("Received response (ID: {Id}), ignoring in server mode", responseMessage.Id);
                            }
                        }
                        catch (JsonRpcException ex)
                        {
                            _logger?.LogWarning(ex, "JSON-RPC error: {Message}", ex.Message);

                            // Create error response
                            var errorCode = ex is JsonRpcParseException
                                ? JsonRpcErrorCodes.ParseError
                                : JsonRpcErrorCodes.InvalidRequest;

                            response = JsonRpcSerializer.CreateErrorResponse(
                                requestId: null,
                                JsonRpcErrorCodes.CreateError(errorCode, ex.Message));
                        }

                        // Send response if we have one
                        if (response != null)
                        {
                            var responseJson = JsonRpcSerializer.Serialize(response);
                            await transport.WriteMessageAsync(responseJson, cancellationToken);
                            _logger?.LogTrace("Sent response: {ResponseLength} bytes", responseJson.Length);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger?.LogInformation("Server cancelled");
                        break;
                    }
                    catch (EndOfStreamException)
                    {
                        _logger?.LogInformation("Input stream closed");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error processing message");

                        // If inner exception is EndOfStreamException, break
                        if (ex.InnerException is EndOfStreamException)
                        {
                            _logger?.LogInformation("Input stream closed");
                            break;
                        }
                    }
                }

                // Cleanup
                await sessionManager.StopAsync(cancellationToken);
                await transport.CloseAsync(cancellationToken);

                _logger?.LogInformation("ACP server stopped");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fatal error in ACP server");
                throw;
            }
        }
    }
}
