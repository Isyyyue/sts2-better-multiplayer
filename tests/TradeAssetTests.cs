namespace BetterMultiplayer.Tests;

public sealed class TradeAssetTests
{
    [Fact]
    public void OriginalTradeIconsAreEmbeddedInTheModAssembly()
    {
        string[] resources = typeof(BetterMultiplayerMod).Assembly.GetManifestResourceNames();

        Assert.Contains(resources, name => name.EndsWith("rest-trade.png", StringComparison.Ordinal));
        Assert.Contains(resources, name => name.EndsWith("gold-trade.png", StringComparison.Ordinal));
        Assert.Contains(resources, name => name.EndsWith("assist-smith.png", StringComparison.Ordinal));
        Assert.Contains(resources, name => name.EndsWith("room-lobby.png", StringComparison.Ordinal));
        Assert.Contains(resources, name => name.EndsWith("join-room.png", StringComparison.Ordinal));
        Assert.Contains(resources, name => name.EndsWith("create-room.png", StringComparison.Ordinal));
    }
}
