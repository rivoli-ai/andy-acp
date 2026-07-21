using Andy.Acp.Core.Agent;

namespace Andy.Acp.Core.Protocol
{
    /// <summary>
    /// The single mapping from the provider-level <see cref="StopReason"/> to the ACP
    /// wire value, shared by the v1 prompt response and the v2 <c>state_update</c>.
    /// ACP defines only <c>end_turn</c>, <c>max_tokens</c>, <c>max_turn_requests</c>,
    /// <c>refusal</c>, and <c>cancelled</c>, so two provider reasons have no exact match:
    /// <list type="bullet">
    /// <item><see cref="StopReason.TimeLimit"/> maps to <c>max_turn_requests</c>
    /// (budget/limit exhaustion, not token-specific).</item>
    /// <item><see cref="StopReason.Error"/> maps to <c>refusal</c> — the closest
    /// "turn did not complete normally" value; <c>end_turn</c> would falsely claim
    /// success. Agents should report the actual error via message content.</item>
    /// </list>
    /// </summary>
    public static class AcpStopReason
    {
        /// <summary>Maps a provider stop reason to its ACP wire string.</summary>
        public static string ToWire(StopReason stopReason) => stopReason switch
        {
            StopReason.Completed => "end_turn",
            StopReason.Cancelled => "cancelled",
            StopReason.TokenLimit => "max_tokens",
            StopReason.TimeLimit => "max_turn_requests",
            StopReason.Error => "refusal",
            _ => "end_turn"
        };
    }
}
