using System.Reflection;
using BetterMultiplayer.Lobby;
using BetterMultiplayer.UI;
using Godot;

namespace BetterMultiplayer.Tests;

public sealed class FeedbackButtonLayoutTests
{
    [Fact]
    public void FeedbackStoneIsTheClickableButtonInsteadOfAnOuterWrapper()
    {
        MethodInfo? factory = typeof(UiFactory).GetMethod(
            "OfficialPaperButton",
            BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? wrapper = typeof(LobbyMenu).GetMethod(
            "CreateTexturedAction",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(factory);
        Assert.Equal(typeof(Button), factory!.ReturnType);
        Assert.Null(wrapper);
    }
}
