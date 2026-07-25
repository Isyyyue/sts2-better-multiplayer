using BetterMultiplayer.Trading;

namespace BetterMultiplayer.Tests;

public sealed class TradeGoldBalanceTests
{
    [Fact]
    public void OwnerReportedBalanceOverridesStaleRemoteMirror()
    {
        Assert.True(TradeGoldBalance.TryValidateOffer(568, 150, out _));
        Assert.False(TradeGoldBalance.TryValidateOffer(0, 150, out _));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(10, -1)]
    [InlineData(10, 11)]
    public void InvalidBalancesAndOffersAreRejected(int balance, int offer)
    {
        Assert.False(TradeGoldBalance.TryValidateOffer(balance, offer, out _));
    }

    [Fact]
    public void FinalBalancesUseBothOwnerReportedStartingBalances()
    {
        Assert.True(TradeGoldBalance.TryCalculateFinal(568, 150, 20, out int first));
        Assert.True(TradeGoldBalance.TryCalculateFinal(53, 20, 150, out int second));
        Assert.Equal(438, first);
        Assert.Equal(183, second);
    }
}
