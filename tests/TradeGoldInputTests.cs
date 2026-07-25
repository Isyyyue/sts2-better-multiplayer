using BetterMultiplayer.Trading;

namespace BetterMultiplayer.Tests;

public sealed class TradeGoldInputTests
{
    [Theory]
    [InlineData("0", 500, 0)]
    [InlineData("111", 500, 111)]
    [InlineData(" 250 ", 500, 250)]
    public void CompleteGoldAmountIsParsedAtConfirmation(string text, int available, int expected)
    {
        Assert.True(TradeGoldInput.TryParse(text, available, out int amount, out string error));
        Assert.Equal(expected, amount);
        Assert.Equal(string.Empty, error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("1a")]
    [InlineData("501")]
    public void InvalidGoldAmountIsRejected(string text)
    {
        Assert.False(TradeGoldInput.TryParse(text, 500, out _, out string error));
        Assert.NotEmpty(error);
    }
}
