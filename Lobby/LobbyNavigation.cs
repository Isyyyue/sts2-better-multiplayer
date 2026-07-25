namespace BetterMultiplayer.Lobby;

internal enum LobbyMenuExitReason
{
    UserBack,
    JoinHandoff,
    HostHandoff
}

internal readonly record struct LobbyMenuClosePlan(
    bool CancelPendingRoom,
    bool RestoreParentBackButton)
{
    internal static LobbyMenuClosePlan For(LobbyMenuExitReason reason, bool submenuVisible)
    {
        return new LobbyMenuClosePlan(
            CancelPendingRoom: reason is not LobbyMenuExitReason.HostHandoff,
            RestoreParentBackButton: reason is not LobbyMenuExitReason.JoinHandoff && submenuVisible);
    }
}

internal static class LobbyJoinHandoff
{
    internal static async Task Run<TSubmenu, TJoinScreen>(
        TSubmenu currentSubmenu,
        Func<TSubmenu, TJoinScreen> openJoinOnCurrent,
        Action handOffToOfficialFlow,
        Func<TJoinScreen, Task> joinAsync)
    {
        TJoinScreen joinScreen = openJoinOnCurrent(currentSubmenu);
        handOffToOfficialFlow();
        await joinAsync(joinScreen);
    }
}
