using BetterMultiplayer.Lobby;

namespace BetterMultiplayer.Tests;

public sealed class RoomTextTests
{
    [Fact]
    public void RoomNameIsTrimmedNormalizedAndControlCharactersAreRemoved()
    {
        string result = RoomText.NormalizeRoomName("  周末\u0000爬塔Ａ  ");

        Assert.Equal("周末爬塔A", result);
    }

    [Fact]
    public void RoomNameIsLimitedToProtocolLength()
    {
        string result = RoomText.NormalizeRoomName(new string('房', 100));

        Assert.Equal(RoomText.MaxRoomNameLength, result.Length);
    }

    [Fact]
    public void SearchIsCaseInsensitiveAfterNormalization()
    {
        Assert.Equal(RoomText.NormalizeSearch("abc"), RoomText.NormalizeSearch("ＡＢＣ"));
    }

    [Fact]
    public void PasswordRejectsControlCharactersAndOverlongInput()
    {
        Assert.True(RoomText.IsValidPassword(new string('密', RoomText.MaxPasswordLength)));
        Assert.False(RoomText.IsValidPassword(new string('密', RoomText.MaxPasswordLength + 1)));
        Assert.False(RoomText.IsValidPassword("abc\n123"));
    }
}
