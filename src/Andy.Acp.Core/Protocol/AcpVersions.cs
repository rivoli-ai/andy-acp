using System.Collections.Generic;

namespace Andy.Acp.Core.Protocol
{
    /// <summary>
    /// The single source of truth for the ACP protocol versions this library knows about.
    /// All version constants, negotiation logic, method gating, and documentation must
    /// reference these values — no other code may hardcode a protocol version number.
    /// </summary>
    public static class AcpVersions
    {
        /// <summary>
        /// Stable ACP v1 (pinned to schema-v1.20.0). This is the default version, and the
        /// only one served unless v2 alpha is explicitly enabled.
        /// </summary>
        public const int V1 = 1;

        /// <summary>
        /// ACP v2 — **alpha** (pinned to schema-v2.0.0-alpha.x). Upstream may still make
        /// breaking changes; serving it requires an explicit opt-in via
        /// <c>AcpServerOptions.EnableV2Alpha</c>.
        /// </summary>
        public const int V2Alpha = 2;

        /// <summary>The versions served by default: stable v1 only.</summary>
        public static readonly IReadOnlySet<int> Default = new HashSet<int> { V1 };

        /// <summary>All versions this library is capable of serving.</summary>
        public static readonly IReadOnlySet<int> All = new HashSet<int> { V1, V2Alpha };
    }
}
