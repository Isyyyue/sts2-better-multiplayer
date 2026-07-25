using BetterMultiplayer.Trading;
using MegaCrit.Sts2.Core.Models.Relics;

namespace BetterMultiplayer.Tests;

public sealed class TradeRelicRulesTests
{
    [Fact]
    public void UponPickupRelicsCannotBeTraded()
    {
        Assert.False(TradeValidator.CanTradeRelic(new GoldenPearl()));
        Assert.False(TradeValidator.CanTradeRelic(new OldCoin()));
    }
}
