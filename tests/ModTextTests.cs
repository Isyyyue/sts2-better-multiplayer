using BetterMultiplayer.Localization;

namespace BetterMultiplayer.Tests;

public sealed class ModTextTests : IDisposable
{
    public void Dispose() => ModText.SetLanguage("eng");

    [Theory]
    [InlineData("zhs")]
    [InlineData("zh-CN")]
    [InlineData("zh_CN")]
    [InlineData("zh-Hans")]
    public void SimplifiedChineseCodesUseChinese(string language)
    {
        Assert.Equal("房间联机", ModText.ForLanguage(language, TextKey.RoomMultiplayer));
    }

    [Theory]
    [InlineData("eng")]
    [InlineData("zht")]
    [InlineData("jpn")]
    [InlineData("fra")]
    [InlineData("")]
    public void EveryOtherLanguageDefaultsToEnglish(string language)
    {
        Assert.Equal("Private Rooms", ModText.ForLanguage(language, TextKey.RoomMultiplayer));
    }

    [Fact]
    public void EveryTextKeyHasChineseAndEnglishContent()
    {
        foreach (TextKey key in Enum.GetValues<TextKey>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ModText.ForLanguage("zhs", key)));
            Assert.False(string.IsNullOrWhiteSpace(ModText.ForLanguage("eng", key)));
        }
    }

    [Fact]
    public void NetworkErrorTokenIsLocalizedByReceivingPlayer()
    {
        string token = ModText.Token(TextKey.OfferChanged);

        ModText.SetLanguage("zhs");
        Assert.Equal("报价刚刚发生变化，请重新确认。", ModText.Resolve(token));

        ModText.SetLanguage("eng");
        Assert.Equal("The offer changed. Review it and confirm again.", ModText.Resolve(token));
    }

    [Fact]
    public void FormattedTextUsesNaturalLanguageSpecificPunctuation()
    {
        Assert.Equal(
            "选择卡牌（2/3）",
            ModText.ForLanguage("zhs", TextKey.SelectionCount, "选择卡牌", 2, 3));
        Assert.Equal(
            "Choose Card (2/3)",
            ModText.ForLanguage("eng", TextKey.SelectionCount, "Choose Card", 2, 3));
    }
}
