using BetterMultiplayer.Trading;
using BetterMultiplayer.Localization;

namespace BetterMultiplayer.Tests;

public sealed class TradeShapeRulesTests
{
    [Fact]
    public void RestSiteAllowsItemsButRejectsGold()
    {
        Assert.True(TradeShapeRules.TryValidate(TradeLocation.RestSite, 3, 1, 1, 0, out _));
        Assert.False(TradeShapeRules.TryValidate(TradeLocation.RestSite, 0, 0, 0, 1, out string error));
        Assert.Equal(ModText.Token(TextKey.GoldOnlyAtMerchant), error);
    }

    [Fact]
    public void MerchantAllowsGoldButRejectsItems()
    {
        Assert.True(TradeShapeRules.TryValidate(TradeLocation.Merchant, 0, 0, 0, 999, out _));
        Assert.False(TradeShapeRules.TryValidate(TradeLocation.Merchant, 1, 0, 0, 0, out string error));
        Assert.Equal(ModText.Token(TextKey.ItemsNotAllowedAtMerchant), error);
    }

    [Fact]
    public void CountsAndGoldMustStayWithinProtocolRules()
    {
        Assert.False(TradeShapeRules.TryValidate(TradeLocation.RestSite, 4, 0, 0, 0, out _));
        Assert.False(TradeShapeRules.TryValidate(TradeLocation.RestSite, 0, 2, 0, 0, out _));
        Assert.False(TradeShapeRules.TryValidate(TradeLocation.RestSite, 0, 0, 2, 0, out _));
        Assert.False(TradeShapeRules.TryValidate(TradeLocation.Merchant, 0, 0, 0, -1, out _));
    }

    [Fact]
    public async Task RestSiteWaiterCompletesOnlyWithReportedResult()
    {
        TradeRestSiteFlow.BeginRestSite();
        Task<bool> success = TradeRestSiteFlow.WaitForResult(10);
        Assert.False(success.IsCompleted);
        TradeRestSiteFlow.Complete(10, success: true);
        Assert.True(await success);

        Task<bool> canceled = TradeRestSiteFlow.WaitForResult(11);
        TradeRestSiteFlow.BeginRestSite();
        Assert.False(await canceled);
    }
}
