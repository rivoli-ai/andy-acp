namespace Andy.Acp.Core.JsonRpc
{
    /// <summary>
    /// Standard JSON-RPC 2.0 error codes and ACP-specific error codes
    /// </summary>
    public static class JsonRpcErrorCodes
    {
        // Standard JSON-RPC 2.0 error codes

        /// <summary>
        /// Invalid JSON was received by the server. An error occurred on the server while parsing the JSON text.
        /// </summary>
        public const int ParseError = -32700;

        /// <summary>
        /// The JSON sent is not a valid Request object.
        /// </summary>
        public const int InvalidRequest = -32600;

        /// <summary>
        /// The method does not exist / is not available.
        /// </summary>
        public const int MethodNotFound = -32601;

        /// <summary>
        /// Invalid method parameter(s).
        /// </summary>
        public const int InvalidParams = -32602;

        /// <summary>
        /// Internal JSON-RPC error.
        /// </summary>
        public const int InternalError = -32603;

        // Server error codes (-32000 to -32099 are reserved for implementation-defined server-errors)

        /// <summary>
        /// ACP session not initialized
        /// </summary>
        public const int SessionNotInitialized = -32000;

        /// <summary>
        /// ACP session already initialized
        /// </summary>
        public const int SessionAlreadyInitialized = -32001;

        /// <summary>
        /// Invalid ACP protocol version
        /// </summary>
        public const int InvalidProtocolVersion = -32002;

        /// <summary>
        /// Tool not found or not available
        /// </summary>
        public const int ToolNotFound = -32003;

        /// <summary>
        /// Tool execution failed
        /// </summary>
        public const int ToolExecutionFailed = -32004;

        /// <summary>
        /// Resource not found or not accessible
        /// </summary>
        public const int ResourceNotFound = -32005;

        /// <summary>
        /// Resource access denied
        /// </summary>
        public const int ResourceAccessDenied = -32006;

        /// <summary>
        /// Operation timeout
        /// </summary>
        public const int Timeout = -32007;

        /// <summary>
        /// Operation cancelled
        /// </summary>
        public const int Cancelled = -32008;

        /// <summary>
        /// Gets a human-readable message for the given error code
        /// </summary>
        /// <param name="code">The error code</param>
        /// <returns>A descriptive error message</returns>
        public static string GetMessage(int code)
        {
            return code switch
            {
                ParseError => "Parse error",
                InvalidRequest => "Invalid Request",
                MethodNotFound => "Method not found",
                InvalidParams => "Invalid params",
                InternalError => "Internal error",
                SessionNotInitialized => "Session not initialized",
                SessionAlreadyInitialized => "Session already initialized",
                InvalidProtocolVersion => "Invalid protocol version",
                ToolNotFound => "Tool not found",
                ToolExecutionFailed => "Tool execution failed",
                ResourceNotFound => "Resource not found",
                ResourceAccessDenied => "Resource access denied",
                Timeout => "Operation timeout",
                Cancelled => "Operation cancelled",
                _ => "Unknown error"
            };
        }

        /// <summary>
        /// Creates a JsonRpcError with the specified code and optional data
        /// </summary>
        /// <param name="code">The error code</param>
        /// <param name="data">Additional error data</param>
        /// <returns>A JsonRpcError instance</returns>
        public static JsonRpcError CreateError(int code, object? data = null)
        {
            return new JsonRpcError
            {
                Code = code,
                Message = GetMessage(code),
                Data = data
            };
        }

        /// <summary>
        /// Creates a JsonRpcError with the specified code, custom message, and optional data
        /// </summary>
        /// <param name="code">The error code</param>
        /// <param name="message">Custom error message</param>
        /// <param name="data">Additional error data</param>
        /// <returns>A JsonRpcError instance</returns>
        public static JsonRpcError CreateError(int code, string message, object? data = null)
        {
            return new JsonRpcError
            {
                Code = code,
                Message = message,
                Data = data
            };
        }
    }
}