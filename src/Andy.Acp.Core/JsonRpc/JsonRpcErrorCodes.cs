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

        // ACP-defined error codes (reserved meanings in the Agent Client Protocol)

        /// <summary>
        /// ACP: authentication is required before this method can be called.
        /// </summary>
        public const int AuthRequired = -32000;

        /// <summary>
        /// ACP: the referenced resource was not found.
        /// </summary>
        public const int ResourceNotFound = -32002;

        /// <summary>
        /// ACP: the request was cancelled (e.g. via $/cancel_request).
        /// </summary>
        public const int RequestCancelled = -32800;

        /// <summary>
        /// Alias for <see cref="RequestCancelled"/> kept for source compatibility.
        /// </summary>
        public const int Cancelled = RequestCancelled;

        // Implementation-defined server error codes. These deliberately avoid the
        // ACP-reserved values above (-32000, -32002, -32800).

        /// <summary>
        /// Connection not initialized (initialize has not completed)
        /// </summary>
        public const int SessionNotInitialized = -32010;

        /// <summary>
        /// Connection already initialized
        /// </summary>
        public const int SessionAlreadyInitialized = -32011;

        /// <summary>
        /// Invalid ACP protocol version
        /// </summary>
        public const int InvalidProtocolVersion = -32012;

        /// <summary>
        /// Tool not found or not available
        /// </summary>
        public const int ToolNotFound = -32013;

        /// <summary>
        /// Tool execution failed
        /// </summary>
        public const int ToolExecutionFailed = -32014;

        /// <summary>
        /// Resource access denied
        /// </summary>
        public const int ResourceAccessDenied = -32016;

        /// <summary>
        /// Operation timeout
        /// </summary>
        public const int Timeout = -32017;

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
                AuthRequired => "Authentication required",
                ResourceNotFound => "Resource not found",
                RequestCancelled => "Request cancelled",
                SessionNotInitialized => "Session not initialized",
                SessionAlreadyInitialized => "Session already initialized",
                InvalidProtocolVersion => "Invalid protocol version",
                ToolNotFound => "Tool not found",
                ToolExecutionFailed => "Tool execution failed",
                ResourceAccessDenied => "Resource access denied",
                Timeout => "Operation timeout",
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