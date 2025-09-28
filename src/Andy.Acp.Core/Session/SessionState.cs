namespace Andy.Acp.Core.Session
{
    /// <summary>
    /// Represents the state of an ACP session
    /// </summary>
    public enum SessionState
    {
        /// <summary>
        /// Session has been created but not yet initialized
        /// </summary>
        Created,

        /// <summary>
        /// Session is in the process of initialization
        /// </summary>
        Initializing,

        /// <summary>
        /// Session has been initialized and is ready to handle requests
        /// </summary>
        Initialized,

        /// <summary>
        /// Session is actively processing requests
        /// </summary>
        Active,

        /// <summary>
        /// Session is shutting down gracefully
        /// </summary>
        ShuttingDown,

        /// <summary>
        /// Session has been terminated
        /// </summary>
        Terminated,

        /// <summary>
        /// Session encountered an error and is in a faulted state
        /// </summary>
        Faulted
    }

    /// <summary>
    /// Represents client capabilities sent during initialization
    /// </summary>
    public class ClientCapabilities
    {
        /// <summary>
        /// Information about the client
        /// </summary>
        public ClientInfo? ClientInfo { get; set; }

        /// <summary>
        /// Supported tools by the client
        /// </summary>
        public string[]? SupportedTools { get; set; }

        /// <summary>
        /// Supported resource schemes (e.g., "file://", "http://")
        /// </summary>
        public string[]? SupportedResources { get; set; }

        /// <summary>
        /// Maximum number of concurrent tool invocations
        /// </summary>
        public int? MaxConcurrentTools { get; set; }

        /// <summary>
        /// Timeout for tool execution in milliseconds
        /// </summary>
        public int? ToolTimeoutMs { get; set; }

        /// <summary>
        /// Additional client-specific capabilities
        /// </summary>
        public Dictionary<string, object>? Extensions { get; set; }
    }

    /// <summary>
    /// Information about the ACP client
    /// </summary>
    public class ClientInfo
    {
        /// <summary>
        /// Name of the client application
        /// </summary>
        public required string Name { get; set; }

        /// <summary>
        /// Version of the client application
        /// </summary>
        public required string Version { get; set; }

        /// <summary>
        /// Optional description of the client
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Optional homepage or documentation URL
        /// </summary>
        public string? Homepage { get; set; }

        /// <summary>
        /// Optional contact information
        /// </summary>
        public string? Contact { get; set; }
    }

    /// <summary>
    /// Represents a pending JSON-RPC request
    /// </summary>
    public class PendingRequest
    {
        /// <summary>
        /// Initializes a new pending request
        /// </summary>
        /// <param name="id">The request ID</param>
        /// <param name="method">The method name</param>
        /// <param name="timestamp">When the request was sent</param>
        public PendingRequest(object id, string method, DateTime timestamp)
        {
            Id = id;
            Method = method;
            Timestamp = timestamp;
            CancellationSource = new CancellationTokenSource();
        }

        /// <summary>
        /// The request ID
        /// </summary>
        public object Id { get; }

        /// <summary>
        /// The method name being called
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// When the request was initiated
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// Cancellation token source for this request
        /// </summary>
        public CancellationTokenSource CancellationSource { get; }

        /// <summary>
        /// Whether the request has timed out
        /// </summary>
        public bool IsTimedOut(TimeSpan timeout) => DateTime.UtcNow - Timestamp > timeout;

        /// <summary>
        /// Disposes the pending request and its resources
        /// </summary>
        public void Dispose()
        {
            CancellationSource?.Dispose();
        }
    }
}