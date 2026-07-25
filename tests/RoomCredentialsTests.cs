using BetterMultiplayer.Lobby;
using BetterMultiplayer.Security;

namespace BetterMultiplayer.Tests;

public sealed class RoomCredentialsTests
{
    [Fact]
    public void RoomNameAndPasswordMustBothMatch()
    {
        byte[] salt = PasswordProof.CreateSalt();
        byte[] key = PasswordProof.DeriveKey("正确密码", salt);
        RoomRecord room = new(
            10,
            "周末爬塔",
            "房主",
            true,
            1,
            4,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(PasswordProof.CreateVerifier(key)));

        Assert.True(RoomCredentials.Matches(room, "周末爬塔", "正确密码"));
        Assert.False(RoomCredentials.Matches(room, "周末爬塔", "错误密码"));
        Assert.False(RoomCredentials.Matches(room, "其他房间", "正确密码"));
    }
}
