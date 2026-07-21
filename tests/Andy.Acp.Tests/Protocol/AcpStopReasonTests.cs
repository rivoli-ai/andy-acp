using Andy.Acp.Core.Agent;
using Andy.Acp.Core.Protocol;
using Xunit;

namespace Andy.Acp.Tests.Protocol
{
    /// <summary>
    /// Pins the shared provider-to-wire stop-reason mapping (epic #33).
    /// </summary>
    public class AcpStopReasonTests
    {
        [Theory]
        [InlineData(StopReason.Completed, "end_turn")]
        [InlineData(StopReason.Cancelled, "cancelled")]
        [InlineData(StopReason.TokenLimit, "max_tokens")]
        [InlineData(StopReason.TimeLimit, "max_turn_requests")]
        [InlineData(StopReason.Error, "refusal")]
        public void ToWire_MapsAllProviderReasons(StopReason reason, string expected)
        {
            Assert.Equal(expected, AcpStopReason.ToWire(reason));
        }

        [Fact]
        public void ToWire_UnknownReason_FallsBackToEndTurn()
        {
            Assert.Equal("end_turn", AcpStopReason.ToWire((StopReason)999));
        }
    }
}
