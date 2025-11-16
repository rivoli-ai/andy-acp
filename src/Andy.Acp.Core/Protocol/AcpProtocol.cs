using System.Collections.Generic;
using Andy.Acp.Core.Session;

namespace Andy.Acp.Core.Protocol
{
    /// <summary>
    /// Represents the parameters for the initialize request
    /// </summary>
    public class InitializeParams
    {
        /// <summary>
        /// The protocol version supported by the client
        /// </summary>
        public string? ProtocolVersion { get; set; }

        /// <summary>
        /// Capabilities provided by the client
        /// </summary>
        public ClientCapabilities? Capabilities { get; set; }

        /// <summary>
        /// Information about the client
        /// </summary>
        public ClientInfo? ClientInfo { get; set; }
    }

    /// <summary>
    /// Represents the result of the initialize request
    /// </summary>
    public class InitializeResult
    {
        /// <summary>
        /// The protocol version supported by the server
        /// </summary>
        public required string ProtocolVersion { get; set; }

        /// <summary>
        /// Information about the server
        /// </summary>
        public required ServerInfo ServerInfo { get; set; }

        /// <summary>
        /// Capabilities provided by the server
        /// </summary>
        public required ServerCapabilities Capabilities { get; set; }

        /// <summary>
        /// Optional session information
        /// </summary>
        public SessionInfo? SessionInfo { get; set; }
    }

    /// <summary>
    /// Information about the ACP server
    /// </summary>
    public class ServerInfo
    {
        /// <summary>
        /// Name of the server
        /// </summary>
        public required string Name { get; set; }

        /// <summary>
        /// Version of the server
        /// </summary>
        public required string Version { get; set; }

        /// <summary>
        /// Optional description
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Optional vendor information
        /// </summary>
        public string? Vendor { get; set; }
    }

    /// <summary>
    /// Capabilities provided by the server
    /// </summary>
    public class ServerCapabilities
    {
        /// <summary>
        /// Tools capability
        /// </summary>
        public ToolsCapability? Tools { get; set; }

        /// <summary>
        /// Prompts capability
        /// </summary>
        public PromptsCapability? Prompts { get; set; }

        /// <summary>
        /// Resources capability
        /// </summary>
        public ResourcesCapability? Resources { get; set; }

        /// <summary>
        /// Logging capability
        /// </summary>
        public LoggingCapability? Logging { get; set; }

        /// <summary>
        /// Additional custom capabilities
        /// </summary>
        public Dictionary<string, object>? Extensions { get; set; }
    }

    /// <summary>
    /// Tools capability configuration
    /// </summary>
    public class ToolsCapability
    {
        /// <summary>
        /// Whether tools are supported
        /// </summary>
        public bool Supported { get; set; }

        /// <summary>
        /// List of available tool names
        /// </summary>
        public string[]? Available { get; set; }

        /// <summary>
        /// Whether tool listing is supported
        /// </summary>
        public bool? ListSupported { get; set; }

        /// <summary>
        /// Whether tool execution is supported
        /// </summary>
        public bool? ExecutionSupported { get; set; }
    }

    /// <summary>
    /// Prompts capability configuration
    /// </summary>
    public class PromptsCapability
    {
        /// <summary>
        /// Whether prompts are supported
        /// </summary>
        public bool Supported { get; set; }

        /// <summary>
        /// List of available prompt names
        /// </summary>
        public string[]? Available { get; set; }

        /// <summary>
        /// Whether prompt listing is supported
        /// </summary>
        public bool? ListSupported { get; set; }
    }

    /// <summary>
    /// Resources capability configuration
    /// </summary>
    public class ResourcesCapability
    {
        /// <summary>
        /// Whether resources are supported
        /// </summary>
        public bool Supported { get; set; }

        /// <summary>
        /// Supported URI schemes (e.g., "file://", "http://")
        /// </summary>
        public string[]? SupportedSchemes { get; set; }

        /// <summary>
        /// Whether resource listing is supported
        /// </summary>
        public bool? ListSupported { get; set; }

        /// <summary>
        /// Whether resource subscription is supported
        /// </summary>
        public bool? SubscriptionSupported { get; set; }
    }

    /// <summary>
    /// Logging capability configuration
    /// </summary>
    public class LoggingCapability
    {
        /// <summary>
        /// Whether logging is supported
        /// </summary>
        public bool Supported { get; set; }

        /// <summary>
        /// Supported log levels
        /// </summary>
        public string[]? SupportedLevels { get; set; }
    }

    /// <summary>
    /// Session information returned with initialize result
    /// </summary>
    public class SessionInfo
    {
        /// <summary>
        /// Unique session identifier
        /// </summary>
        public required string SessionId { get; set; }

        /// <summary>
        /// Session timeout in milliseconds
        /// </summary>
        public int? TimeoutMs { get; set; }

        /// <summary>
        /// Additional session metadata
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }
    }

    /// <summary>
    /// Parameters for the shutdown request
    /// </summary>
    public class ShutdownParams
    {
        /// <summary>
        /// Optional reason for shutdown
        /// </summary>
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Result of the shutdown request
    /// </summary>
    public class ShutdownResult
    {
        /// <summary>
        /// Whether the shutdown was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Optional message
        /// </summary>
        public string? Message { get; set; }
    }
}
