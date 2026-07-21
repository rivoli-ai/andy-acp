namespace Andy.Acp.Core.Protocol
{
    /// <summary>
    /// The single source of truth for the ACP protocol versions this library knows about.
    /// All version constants, negotiation logic, and documentation must reference these
    /// values — no other code may hardcode a protocol version number.
    /// </summary>
    public static class AcpVersions
    {
        /// <summary>
        /// Stable ACP v1 (pinned to schema-v1.20.0). This is the default and currently
        /// the only version served.
        /// </summary>
        public const int V1 = 1;

        /// <summary>
        /// The highest protocol version this library currently serves.
        /// </summary>
        public const int Current = V1;

        /// <summary>
        /// The lowest protocol version this library currently serves.
        /// </summary>
        public const int Minimum = V1;
    }
}
