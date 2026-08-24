using BetterMultiplayer.Trading;

namespace BetterMultiplayer.Tests;

[Collection("TradeState")]
public sealed class TradeLifecycleRegressionTests
{
    [Fact]
    public void ReenteringTheSameMerchantDoesNotClearAvailability()
    {
        const ulong playerId = 987654321;

        TradeCoordinator.BeginLocation(TradeLocation.Merchant);
        TradeStateStore.SetAvailability(playerId, available: true, TradeLocation.Merchant);

        try
        {
            TradeCoordinator.BeginLocation(TradeLocation.Merchant);

            Assert.True(TradeStateStore.IsAvailable(playerId, TradeLocation.Merchant));
        }
        finally
        {
            TradeStateStore.SetAvailability(playerId, available: false, TradeLocation.Merchant);
        }
    }
}
