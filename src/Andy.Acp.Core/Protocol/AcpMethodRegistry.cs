using System;
using System.Collections.Generic;

namespace Andy.Acp.Core.Protocol
{
    /// <summary>
    /// The per-method protocol-version map: which agent-side methods exist in which ACP
    /// version. Derived from the vendored upstream method metadata
    /// (<c>schema/v1/meta.json</c> and <c>schema/v2/meta.json</c>); this table is the only
    /// place that knowledge lives. Dispatch consults it so a method that does not exist in
    /// the connection's negotiated version returns method-not-found even when a handler is
    /// registered.
    /// </summary>
    public static class AcpMethodRegistry
    {
        private static readonly Dictionary<string, int[]> Methods = new(StringComparer.Ordinal)
        {
            // Lifecycle
            ["initialize"] = new[] { AcpVersions.V1, AcpVersions.V2Alpha },
            ["authenticate"] = new[] { AcpVersions.V1 },          // v2: auth/login
            ["logout"] = new[] { AcpVersions.V1 },                // v2: auth/logout
            ["auth/login"] = new[] { AcpVersions.V2Alpha },
            ["auth/logout"] = new[] { AcpVersions.V2Alpha },

            // Sessions
            ["session/new"] = new[] { AcpVersions.V1, AcpVersions.V2Alpha },
            ["session/load"] = new[] { AcpVersions.V1 },          // v2: session/resume + replayFrom
            ["session/set_mode"] = new[] { AcpVersions.V1 },      // v2: config option category "mode"
            ["session/set_config_option"] = new[] { AcpVersions.V1, AcpVersions.V2Alpha },
            ["session/prompt"] = new[] { AcpVersions.V1, AcpVersions.V2Alpha },
            ["session/cancel"] = new[] { AcpVersions.V1, AcpVersions.V2Alpha },
            ["session/list"] = new[] { AcpVersions.V1, AcpVersions.V2Alpha },
            ["session/delete"] = new[] { AcpVersions.V1, AcpVersions.V2Alpha },
            ["session/resume"] = new[] { AcpVersions.V1, AcpVersions.V2Alpha },
            ["session/close"] = new[] { AcpVersions.V1, AcpVersions.V2Alpha },

            // Protocol-level
            ["$/cancel_request"] = new[] { AcpVersions.V1, AcpVersions.V2Alpha }
        };

        /// <summary>
        /// Whether <paramref name="method"/> exists in protocol <paramref name="version"/>.
        /// Methods not in the registry (e.g. the nonstandard MCP-compat tools/*) are not
        /// version-gated and return true.
        /// </summary>
        public static bool IsAvailable(string method, int version)
        {
            return !Methods.TryGetValue(method, out var versions) || Array.IndexOf(versions, version) >= 0;
        }

        /// <summary>Whether the registry knows <paramref name="method"/> at all.</summary>
        public static bool IsKnown(string method) => Methods.ContainsKey(method);
    }
}
