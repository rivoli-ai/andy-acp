using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Andy.Acp.Core.Agent;

namespace Andy.Acp.Core.Protocol.V2
{
    /// <summary>
    /// ACP v2 (alpha) wire models. These deliberately live in their own namespace and are
    /// never mixed with v1 types where the schemas differ. Version-neutral primitives
    /// (<see cref="Implementation"/>, <see cref="CapabilityMarker"/>) are shared from the
    /// v1 namespace because their shapes are identical in both versions.
    /// </summary>
    public class V2InitializeResult
    {
        [JsonPropertyName("protocolVersion")]
        public int ProtocolVersion { get; set; } = AcpVersions.V2Alpha;

        /// <summary>Agent implementation info — required in v2.</summary>
        [JsonPropertyName("info")]
        public Implementation Info { get; set; } = new();

        [JsonPropertyName("capabilities")]
        public V2AgentCapabilities? Capabilities { get; set; }

        [JsonPropertyName("authMethods")]
        public List<V2AuthMethod>? AuthMethods { get; set; }
    }

    /// <summary>v2 <c>InitializeRequest</c> parameters.</summary>
    public class V2InitializeParams
    {
        [JsonPropertyName("protocolVersion")]
        public int? ProtocolVersion { get; set; }

        [JsonPropertyName("info")]
        public Implementation? Info { get; set; }
    }

    /// <summary>v2 <c>AgentCapabilities</c>: marker-object style.</summary>
    public class V2AgentCapabilities
    {
        [JsonPropertyName("session")]
        public V2SessionCapabilities? Session { get; set; }
    }

    /// <summary>
    /// v2 <c>SessionCapabilities</c>. Presence of this object advertises the baseline
    /// session methods (new/list/resume/close/prompt/cancel); <c>delete</c> is gated by
    /// its own marker.
    /// </summary>
    public class V2SessionCapabilities
    {
        [JsonPropertyName("prompt")]
        public V2PromptCapabilities? Prompt { get; set; }

        [JsonPropertyName("mcp")]
        public V2McpCapabilities? Mcp { get; set; }

        [JsonPropertyName("delete")]
        public CapabilityMarker? Delete { get; set; }

        [JsonPropertyName("additionalDirectories")]
        public CapabilityMarker? AdditionalDirectories { get; set; }
    }

    /// <summary>v2 <c>PromptCapabilities</c>: booleans became markers.</summary>
    public class V2PromptCapabilities
    {
        [JsonPropertyName("image")]
        public CapabilityMarker? Image { get; set; }

        [JsonPropertyName("audio")]
        public CapabilityMarker? Audio { get; set; }

        [JsonPropertyName("embeddedContext")]
        public CapabilityMarker? EmbeddedContext { get; set; }
    }

    /// <summary>v2 <c>McpCapabilities</c>: explicit stdio + http markers (sse removed).</summary>
    public class V2McpCapabilities
    {
        [JsonPropertyName("stdio")]
        public CapabilityMarker? Stdio { get; set; }

        [JsonPropertyName("http")]
        public CapabilityMarker? Http { get; set; }
    }

    /// <summary>v2 <c>AuthMethod</c>: required <c>type</c> discriminator, <c>methodId</c> (was <c>id</c>).</summary>
    public class V2AuthMethod
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "agent";

        [JsonPropertyName("methodId")]
        public string MethodId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    /// <summary>
    /// Maps version-neutral provider models to v2 wire shapes where field names differ
    /// from v1 (config options: <c>configId</c>, <c>groupId</c>).
    /// </summary>
    public static class V2Wire
    {
        /// <summary>Maps a provider config option to the v2 wire shape.</summary>
        public static object ConfigOption(SessionConfigOption o) => new
        {
            type = o.Type,
            configId = o.Id,
            name = o.Name,
            description = o.Description,
            category = o.Category,
            currentValue = o.CurrentValue,
            options = o.Type != "select"
                ? null
                : o.Groups is { Count: > 0 }
                    ? o.Groups.Select(g => (object)new
                    {
                        groupId = g.Group,
                        name = g.Name,
                        options = g.Options.Select(SelectOption).ToArray()
                    }).ToArray()
                    : o.Options?.Select(SelectOption).ToArray()
        };

        /// <summary>Maps a provider config option list to the v2 wire shape (null-safe).</summary>
        public static object[]? ConfigOptions(IEnumerable<SessionConfigOption>? options)
            => options?.Select(ConfigOption).ToArray();

        private static object SelectOption(SessionConfigSelectOption s) => new
        {
            value = s.Value,
            name = s.Name,
            description = s.Description
        };
    }
}
