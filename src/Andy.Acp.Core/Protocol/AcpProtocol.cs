using System.Collections.Generic;

namespace Andy.Acp.Core.Protocol
{
    /// <summary>
    /// Information about the ACP server/agent. Used to populate the ACP
    /// <see cref="Implementation"/> block in the initialize response.
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
    /// Legacy aggregate capability object retained for the full example server. New code
    /// should advertise capabilities via <see cref="AcpAgentCapabilities"/>.
    /// </summary>
    public class ServerCapabilities
    {
        public ToolsCapability? Tools { get; set; }
        public PromptsCapability? Prompts { get; set; }
        public ResourcesCapability? Resources { get; set; }
        public LoggingCapability? Logging { get; set; }
        public bool LoadSession { get; set; }
        public bool AudioPrompts { get; set; }
        public bool ImagePrompts { get; set; }
        public bool EmbeddedContext { get; set; }
        public bool FileSystemSupported { get; set; }
        public bool TerminalSupported { get; set; }
        public Dictionary<string, object>? Extensions { get; set; }
    }

    /// <summary>Tools capability configuration (legacy/example).</summary>
    public class ToolsCapability
    {
        public bool Supported { get; set; }
        public string[]? Available { get; set; }
        public bool? ListSupported { get; set; }
        public bool? ExecutionSupported { get; set; }
    }

    /// <summary>Prompts capability configuration (legacy/example).</summary>
    public class PromptsCapability
    {
        public bool Supported { get; set; }
        public string[]? Available { get; set; }
        public bool? ListSupported { get; set; }
    }

    /// <summary>Resources capability configuration (legacy/example).</summary>
    public class ResourcesCapability
    {
        public bool Supported { get; set; }
        public string[]? SupportedSchemes { get; set; }
        public bool? ListSupported { get; set; }
        public bool? SubscriptionSupported { get; set; }
    }

    /// <summary>Logging capability configuration (legacy/example).</summary>
    public class LoggingCapability
    {
        public bool Supported { get; set; }
        public string[]? SupportedLevels { get; set; }
    }
}
