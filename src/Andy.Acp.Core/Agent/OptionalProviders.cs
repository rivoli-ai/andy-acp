using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Andy.Acp.Core.Agent
{
    /// <summary>
    /// Optional interface an <see cref="IAgentProvider"/> implementation can additionally
    /// implement to support ACP authentication (<c>authenticate</c> / <c>logout</c>).
    /// When implemented, the advertised auth methods are returned from <c>initialize</c>,
    /// and session methods are gated behind authentication when
    /// <see cref="RequiresAuthentication"/> is true.
    /// </summary>
    public interface IAuthenticationProvider
    {
        /// <summary>The auth methods advertised in the initialize response.</summary>
        IReadOnlyList<AuthMethod> GetAuthMethods();

        /// <summary>
        /// Whether session methods require a successful <c>authenticate</c> call first.
        /// When false, the agent merely offers optional authentication.
        /// </summary>
        bool RequiresAuthentication { get; }

        /// <summary>Whether the agent supports the <c>logout</c> method.</summary>
        bool SupportsLogout { get; }

        /// <summary>
        /// Performs authentication with the selected method. Throw to reject
        /// (surfaces as a JSON-RPC error).
        /// </summary>
        Task AuthenticateAsync(string methodId, CancellationToken cancellationToken);

        /// <summary>Ends the authenticated state. Only called when <see cref="SupportsLogout"/>.</summary>
        Task LogoutAsync(CancellationToken cancellationToken);
    }

    /// <summary>An auth method offered by the agent.</summary>
    public class AuthMethod
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    /// <summary>
    /// Optional interface for ACP session config options
    /// (<c>session/set_config_option</c>). Config options are the ACP mechanism for
    /// model selection, reasoning levels, and other session settings; the current
    /// option set is returned via <see cref="SessionMetadata.ConfigOptions"/> on
    /// session/new, load, and resume.
    /// </summary>
    public interface ISessionConfigProvider
    {
        /// <summary>
        /// Applies a config option change and returns the full updated option list
        /// (required by the ACP response shape). Throw for unknown configId/value
        /// (surfaces as invalid-params).
        /// </summary>
        Task<IReadOnlyList<SessionConfigOption>> SetConfigOptionAsync(
            string sessionId,
            string configId,
            SessionConfigValue value,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// The requested new value for a config option: either a select value id or a boolean.
    /// </summary>
    public class SessionConfigValue
    {
        /// <summary>The selected value id (select options).</summary>
        public string? ValueId { get; set; }

        /// <summary>The boolean value (boolean options).</summary>
        public bool? BoolValue { get; set; }
    }

    /// <summary>
    /// An ACP <c>SessionConfigOption</c>. <see cref="Type"/> selects the variant:
    /// <c>select</c> uses <see cref="CurrentValueId"/> + <see cref="Options"/>;
    /// <c>boolean</c> uses <see cref="CurrentBoolValue"/>.
    /// </summary>
    public class SessionConfigOption
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "select";

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>Optional ACP category: mode, model, model_config, or thought_level.</summary>
        [JsonPropertyName("category")]
        public string? Category { get; set; }

        /// <summary>Current value id (select variant).</summary>
        [JsonIgnore]
        public string? CurrentValueId { get; set; }

        /// <summary>Current value (boolean variant).</summary>
        [JsonIgnore]
        public bool? CurrentBoolValue { get; set; }

        /// <summary>
        /// The wire <c>currentValue</c>: a string for select options, a boolean for
        /// boolean options.
        /// </summary>
        [JsonPropertyName("currentValue")]
        public object? CurrentValue
        {
            get => Type == "boolean" ? CurrentBoolValue ?? false : CurrentValueId;
            set
            {
                switch (value)
                {
                    case bool b: CurrentBoolValue = b; break;
                    case string s: CurrentValueId = s; break;
                    case System.Text.Json.JsonElement el when el.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False:
                        CurrentBoolValue = el.GetBoolean(); break;
                    case System.Text.Json.JsonElement el when el.ValueKind is System.Text.Json.JsonValueKind.String:
                        CurrentValueId = el.GetString(); break;
                }
            }
        }

        /// <summary>Available options as a flat list (select variant).</summary>
        [JsonIgnore]
        public List<SessionConfigSelectOption>? Options { get; set; }

        /// <summary>
        /// Available options organized in groups (select variant). When set, takes
        /// precedence over <see cref="Options"/> on the wire.
        /// </summary>
        [JsonIgnore]
        public List<SessionConfigSelectGroup>? Groups { get; set; }

        /// <summary>
        /// The wire <c>options</c> member: ACP allows either a flat option array or a
        /// group array.
        /// </summary>
        [JsonPropertyName("options")]
        public object? OptionsWire => Groups is { Count: > 0 } ? Groups : Options;
    }

    /// <summary>A selectable value of a select config option.</summary>
    public class SessionConfigSelectOption
    {
        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    /// <summary>A named group of selectable values (ACP <c>SessionConfigSelectGroup</c>).</summary>
    public class SessionConfigSelectGroup
    {
        /// <summary>Group id (ACP <c>group</c>).</summary>
        [JsonPropertyName("group")]
        public string Group { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("options")]
        public List<SessionConfigSelectOption> Options { get; set; } = new();
    }

    /// <summary>
    /// Optional interface for the ACP session catalog methods
    /// (<c>session/list</c>, <c>session/delete</c>, <c>session/resume</c>,
    /// <c>session/close</c>). Implementing it advertises all four capabilities.
    /// </summary>
    public interface ISessionCatalogProvider
    {
        /// <summary>Lists stored sessions, optionally filtered by cwd, with cursor paging.</summary>
        Task<SessionListResult> ListSessionsAsync(string? cwd, string? cursor, CancellationToken cancellationToken);

        /// <summary>Deletes a stored session. Return false when the session does not exist.</summary>
        Task<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken);

        /// <summary>
        /// Resumes a session without history replay (unlike session/load).
        /// Return null when the session does not exist.
        /// </summary>
        Task<SessionMetadata?> ResumeSessionAsync(LoadSessionParams parameters, CancellationToken cancellationToken);

        /// <summary>Closes an active session, releasing per-session resources.</summary>
        Task CloseSessionAsync(string sessionId, CancellationToken cancellationToken);
    }

    /// <summary>Result of listing sessions.</summary>
    public class SessionListResult
    {
        public List<SessionCatalogEntry> Sessions { get; set; } = new();

        /// <summary>Opaque cursor for the next page, or null when exhausted.</summary>
        public string? NextCursor { get; set; }
    }

    /// <summary>A stored session, as listed by <c>session/list</c> (ACP <c>SessionInfo</c>).</summary>
    public class SessionCatalogEntry
    {
        public string SessionId { get; set; } = string.Empty;

        public string Cwd { get; set; } = string.Empty;

        public List<string>? AdditionalDirectories { get; set; }

        public string? Title { get; set; }

        public DateTimeOffset? UpdatedAt { get; set; }
    }
}
