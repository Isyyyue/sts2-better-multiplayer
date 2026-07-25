using BetterMultiplayer.Lobby;
using BetterMultiplayer.Localization;

namespace BetterMultiplayer.Tests;

public sealed class RoomSessionTests
{
    [Fact]
    public void CancelPendingClearsUncreatedRoomConfiguration()
    {
        Assert.True(RoomSession.BeginHosting("测试房间", "test-password", out string error));
        Assert.Equal(string.Empty, error);
        Assert.True(RoomSession.HasPending);

        RoomSession.CancelPending();

        Assert.False(RoomSession.HasPending);
    }

    [Fact]
    public void PasswordIsRequiredWhenCreatingARoom()
    {
        RoomSession.Clear();

        Assert.False(RoomSession.BeginHosting("测试房间", string.Empty, out string error));
        Assert.Equal(ModText.Token(TextKey.EnterRoomPassword), error);
        Assert.False(RoomSession.HasPending);
    }
}
