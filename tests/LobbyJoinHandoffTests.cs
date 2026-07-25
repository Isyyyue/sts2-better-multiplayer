using BetterMultiplayer.Lobby;

namespace BetterMultiplayer.Tests;

public sealed class LobbyJoinHandoffTests
{
    [Fact]
    public async Task JoinHandoffUsesExistingMultiplayerSubmenuWithoutPushingItAgain()
    {
        object multiplayerSubmenu = new();
        object joinScreen = new();
        List<object> stack = [multiplayerSubmenu];

        await LobbyJoinHandoff.Run(
            multiplayerSubmenu,
            current =>
            {
                Assert.Same(multiplayerSubmenu, current);
                stack.Add(joinScreen);
                return joinScreen;
            },
            () => Assert.Equal([multiplayerSubmenu, joinScreen], stack),
            screen =>
            {
                Assert.Same(joinScreen, screen);
                return Task.CompletedTask;
            });

        Assert.Equal([multiplayerSubmenu, joinScreen], stack);
        Assert.Single(stack, item => ReferenceEquals(item, multiplayerSubmenu));
    }

    [Theory]
    [InlineData((int)LobbyMenuExitReason.UserBack, true, true, true)]
    [InlineData((int)LobbyMenuExitReason.UserBack, false, true, false)]
    [InlineData((int)LobbyMenuExitReason.JoinHandoff, true, true, false)]
    [InlineData((int)LobbyMenuExitReason.HostHandoff, true, false, true)]
    [InlineData((int)LobbyMenuExitReason.HostHandoff, false, false, false)]
    public void ClosePlanPreservesOfficialNavigationOwnership(
        int reason,
        bool submenuVisible,
        bool expectedCancelPendingRoom,
        bool expectedRestoreParentBackButton)
    {
        LobbyMenuClosePlan plan = LobbyMenuClosePlan.For((LobbyMenuExitReason)reason, submenuVisible);

        Assert.Equal(expectedCancelPendingRoom, plan.CancelPendingRoom);
        Assert.Equal(expectedRestoreParentBackButton, plan.RestoreParentBackButton);
    }
}
