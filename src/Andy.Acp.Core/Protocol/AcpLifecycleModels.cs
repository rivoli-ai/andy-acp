using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Andy.Acp.Core.Protocol
{
    /// <summary>
    /// ACP v1 <c>initialize</c> request parameters (client → agent).
    /// </summary>
    public class AcpInitializeParams
    {
        /// <summary>Protocol version the client speaks (integer).</summary>
        [JsonPropertyName("protocolVersion")]
        public int? ProtocolVersion { get; set; }

        [JsonPropertyName("clientCapabilities")]
        public AcpClientCapabilities? ClientCapabilities { get; set; }

        [JsonPropertyName("clientInfo")]
        public Implementation? ClientInfo { get; set; }
    }

    /// <summary>
    /// ACP v1 <c>initialize</c> response.
    /// </summary>
    public class AcpInitializeResult
    {
        [JsonPropertyName("protocolVersion")]
        public int ProtocolVersion { get; set; }

        [JsonPropertyName("agentCapabilities")]
        public AcpAgentCapabilities? AgentCapabilities { get; set; }

        [JsonPropertyName("authMethods")]
        public List<AuthMethodDescription> AuthMethods { get; set; } = new();

        [JsonPropertyName("agentInfo")]
        public Implementation? AgentInfo { get; set; }
    }

    /// <summary>ACP <c>Implementation</c> (name/version/title).</summary>
    public class Implementation
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    /// <summary>ACP <c>ClientCapabilities</c> — what the client (editor) can do for the agent.</summary>
    public class AcpClientCapabilities
    {
        [JsonPropertyName("fs")]
        public AcpFileSystemCapabilities? Fs { get; set; }

        [JsonPropertyName("terminal")]
        public bool Terminal { get; set; }
    }

    /// <summary>ACP <c>FileSystemCapabilities</c>.</summary>
    public class AcpFileSystemCapabilities
    {
        [JsonPropertyName("readTextFile")]
        public bool ReadTextFile { get; set; }

        [JsonPropertyName("writeTextFile")]
        public bool WriteTextFile { get; set; }
    }

    /// <summary>ACP <c>AgentCapabilities</c> advertised in the initialize response.</summary>
    public class AcpAgentCapabilities
    {
        [JsonPropertyName("loadSession")]
        public bool LoadSession { get; set; }

        [JsonPropertyName("promptCapabilities")]
        public AcpPromptCapabilities? PromptCapabilities { get; set; }

        [JsonPropertyName("mcpCapabilities")]
        public AcpMcpCapabilities? McpCapabilities { get; set; }

        [JsonPropertyName("sessionCapabilities")]
        public AcpSessionCapabilities? SessionCapabilities { get; set; }

        [JsonPropertyName("auth")]
        public AcpAgentAuthCapabilities? Auth { get; set; }
    }

    /// <summary>
    /// An empty-object capability marker: in ACP several capability fields signal support
    /// by their presence as <c>{}</c> rather than by a boolean value.
    /// </summary>
    public sealed class CapabilityMarker
    {
    }

    /// <summary>
    /// ACP <c>SessionCapabilities</c>: presence of a marker means the corresponding
    /// session catalog method is supported.
    /// </summary>
    public class AcpSessionCapabilities
    {
        [JsonPropertyName("list")]
        public CapabilityMarker? List { get; set; }

        [JsonPropertyName("delete")]
        public CapabilityMarker? Delete { get; set; }

        [JsonPropertyName("resume")]
        public CapabilityMarker? Resume { get; set; }

        [JsonPropertyName("close")]
        public CapabilityMarker? Close { get; set; }

        [JsonPropertyName("additionalDirectories")]
        public CapabilityMarker? AdditionalDirectories { get; set; }
    }

    /// <summary>ACP <c>AgentAuthCapabilities</c> (logout marker).</summary>
    public class AcpAgentAuthCapabilities
    {
        [JsonPropertyName("logout")]
        public CapabilityMarker? Logout { get; set; }
    }

    /// <summary>An advertised ACP auth method (variant <c>agent</c>).</summary>
    public class AuthMethodDescription
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    /// <summary>ACP <c>PromptCapabilities</c>. Text and resource_link are baseline (not gated).</summary>
    public class AcpPromptCapabilities
    {
        [JsonPropertyName("image")]
        public bool Image { get; set; }

        [JsonPropertyName("audio")]
        public bool Audio { get; set; }

        [JsonPropertyName("embeddedContext")]
        public bool EmbeddedContext { get; set; }
    }

    /// <summary>ACP <c>McpCapabilities</c>.</summary>
    public class AcpMcpCapabilities
    {
        [JsonPropertyName("http")]
        public bool Http { get; set; }

        [JsonPropertyName("sse")]
        public bool Sse { get; set; }
    }

    /// <summary>
    /// Per-connection ACP state shared between the protocol handler (which completes the
    /// initialize handshake) and the session handler (which enforces that session methods
    /// are only invoked after initialization). Also carries the negotiated client
    /// capabilities so the agent-to-client request layer knows what the client supports.
    /// </summary>
    public class AcpConnectionState
    {
        /// <summary>True once a successful <c>initialize</c> exchange has completed.</summary>
        public bool Initialized { get; set; }

        /// <summary>
        /// True once <c>authenticate</c> has succeeded. Defaults to true; the initialize
        /// handler sets it to false when the agent's authentication provider requires
        /// authentication, and the authenticate handler sets it back on success.
        /// </summary>
        public bool Authenticated { get; set; } = true;

        /// <summary>The negotiated protocol version.</summary>
        public int ProtocolVersion { get; set; }

        /// <summary>Capabilities the client advertised during initialize.</summary>
        public AcpClientCapabilities ClientCapabilities { get; set; } = new();
    }
}
