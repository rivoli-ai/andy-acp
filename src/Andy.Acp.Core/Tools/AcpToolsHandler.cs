using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Protocol;
using Andy.Acp.Core.Session;

namespace Andy.Acp.Core.Tools
{
    /// <summary>
    /// Handles ACP tool-related protocol methods (tools/list, tools/call).
    /// </summary>
    public class AcpToolsHandler
    {
        private readonly IAcpToolProvider _toolProvider;
        private readonly AcpProtocolHandler? _protocolHandler;
        private readonly ILogger<AcpToolsHandler>? _logger;

        /// <summary>
        /// Initializes a new instance of the AcpToolsHandler class.
        /// </summary>
        /// <param name="toolProvider">The tool provider.</param>
        /// <param name="logger">Optional logger.</param>
        public AcpToolsHandler(
            IAcpToolProvider toolProvider,
            ILogger<AcpToolsHandler>? logger = null)
            : this(toolProvider, null, logger)
        {
        }

        /// <summary>
        /// Initializes a new instance of the AcpToolsHandler class with protocol handler.
        /// </summary>
        /// <param name="toolProvider">The tool provider.</param>
        /// <param name="protocolHandler">Optional protocol handler for session validation.</param>
        /// <param name="logger">Optional logger.</param>
        public AcpToolsHandler(
            IAcpToolProvider toolProvider,
            AcpProtocolHandler? protocolHandler,
            ILogger<AcpToolsHandler>? logger = null)
        {
            _toolProvider = toolProvider ?? throw new ArgumentNullException(nameof(toolProvider));
            _protocolHandler = protocolHandler;
            _logger = logger;
        }

        /// <summary>
        /// Validates that the session is in a valid state for tool operations.
        /// </summary>
        private void ValidateSessionState()
        {
            if (_protocolHandler == null)
            {
                // No protocol handler means no session validation
                return;
            }

            var session = _protocolHandler.CurrentSession;
            if (session == null)
            {
                throw new JsonRpcProtocolException(
                    JsonRpcErrorCodes.SessionNotInitialized,
                    "No active session. Call initialize first."
                );
            }

            if (session.State == SessionState.ShuttingDown ||
                session.State == SessionState.Terminated)
            {
                throw new JsonRpcProtocolException(
                    JsonRpcErrorCodes.InvalidRequest,
                    "Session is shutting down or terminated. Tool operations are not allowed."
                );
            }
        }

        /// <summary>
        /// Handles the tools/list request.
        /// Returns the list of available tools with their definitions.
        /// </summary>
        /// <param name="parameters">Request parameters (typically none for tools/list).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that returns the list of tool definitions.</returns>
        public async Task<object?> HandleToolsListAsync(object? parameters, CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("Processing tools/list request");

            // Validate session state if protocol handler is available
            ValidateSessionState();

            try
            {
                var tools = await _toolProvider.ListToolsAsync(cancellationToken);

                _logger?.LogInformation("Retrieved {Count} tools", tools is ICollection<AcpToolDefinition> collection ? collection.Count : 0);

                return new ToolsListResult
                {
                    Tools = tools
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error listing tools");
                throw new JsonRpcProtocolException(
                    JsonRpcErrorCodes.InternalError,
                    $"Failed to list tools: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// Handles the tools/call request.
        /// Executes a tool with the given parameters.
        /// </summary>
        /// <param name="parameters">Request parameters containing tool name and arguments.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that returns the tool execution result.</returns>
        public async Task<object?> HandleToolsCallAsync(object? parameters, CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("Processing tools/call request");

            // Validate session state if protocol handler is available
            ValidateSessionState();

            // Parse parameters
            ToolsCallParams? callParams = null;
            try
            {
                if (parameters is JsonElement jsonElement)
                {
                    callParams = JsonSerializer.Deserialize<ToolsCallParams>(jsonElement.GetRawText());
                }
                else if (parameters != null)
                {
                    var json = JsonSerializer.Serialize(parameters);
                    callParams = JsonSerializer.Deserialize<ToolsCallParams>(json);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to parse tools/call parameters");
                throw new JsonRpcProtocolException(
                    JsonRpcErrorCodes.InvalidParams,
                    "Invalid tools/call parameters"
                );
            }

            if (callParams == null || string.IsNullOrEmpty(callParams.Name))
            {
                throw new JsonRpcProtocolException(
                    JsonRpcErrorCodes.InvalidParams,
                    "Tool name is required"
                );
            }

            _logger?.LogInformation("Executing tool: {ToolName}", callParams.Name);

            try
            {
                var result = await _toolProvider.ExecuteToolAsync(
                    callParams.Name,
                    callParams.Parameters ?? new Dictionary<string, object?>(),
                    cancellationToken
                );

                if (result.IsError)
                {
                    _logger?.LogWarning("Tool {ToolName} execution failed: {Error}", callParams.Name, result.Error);
                }
                else
                {
                    _logger?.LogInformation("Tool {ToolName} executed successfully", callParams.Name);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error executing tool: {ToolName}", callParams.Name);

                // Return error result instead of throwing exception
                return AcpToolResult.Failure($"Tool execution failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Registers the tool methods with a JSON-RPC handler.
        /// </summary>
        /// <param name="jsonRpcHandler">The JSON-RPC handler.</param>
        public void RegisterMethods(JsonRpcHandler jsonRpcHandler)
        {
            if (jsonRpcHandler == null)
                throw new ArgumentNullException(nameof(jsonRpcHandler));

            jsonRpcHandler.RegisterMethod("tools/list", HandleToolsListAsync);
            jsonRpcHandler.RegisterMethod("tools/call", HandleToolsCallAsync);

            _logger?.LogInformation("Registered ACP tool methods: tools/list, tools/call");
        }
    }

    /// <summary>
    /// Parameters for tools/call request.
    /// </summary>
    public class ToolsCallParams
    {
        /// <summary>
        /// Gets or sets the name of the tool to call.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the parameters to pass to the tool.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("parameters")]
        public Dictionary<string, object?>? Parameters { get; set; }
    }

    /// <summary>
    /// Result for tools/list request.
    /// </summary>
    public class ToolsListResult
    {
        /// <summary>
        /// Gets or sets the list of available tools.
        /// </summary>
        public IEnumerable<AcpToolDefinition> Tools { get; set; } = Array.Empty<AcpToolDefinition>();
    }
}
