using System.Collections.Generic;
using Andy.Acp.Core.Protocol;

namespace Andy.Acp.Core.Server
{
    /// <summary>
    /// Configuration for <see cref="AcpServer"/>. The protocol-version choice is made
    /// here and nowhere else.
    /// </summary>
    public class AcpServerOptions
    {
        /// <summary>
        /// Opt-in for ACP v2 (**alpha**). Off by default: the server then speaks stable v1
        /// only, and a client requesting v2 is negotiated down to v1. When enabled, the
        /// server serves both v1 and v2, and a connection's version is fixed by the
        /// initialize negotiation. v2 is alpha upstream and may change incompatibly.
        /// </summary>
        public bool EnableV2Alpha { get; set; }

        /// <summary>The resulting set of protocol versions this server will serve.</summary>
        public IReadOnlySet<int> SupportedVersions =>
            EnableV2Alpha ? AcpVersions.All : AcpVersions.Default;
    }
}
