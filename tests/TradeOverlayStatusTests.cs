using BetterMultiplayer.Localization;

namespace BetterMultiplayer.Tests;

public sealed class TradeOverlayStatusTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void PlayerListStatusReflectsWhetherAnyPeerCanTrade(
        int availableCount,
        bool expectedReady)
    {
        TextKey actual = TradeOverlay.PlayerListStatusKey(availableCount);
        TextKey expected = expectedReady ? TextKey.ReadyToTrade : TextKey.WaitingForPlayers;
        Assert.Equal(expected, actual);
    }
}
