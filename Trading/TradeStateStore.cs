using MegaCrit.Sts2.Core.Helpers;
using BetterMultiplayer.Localization;

namespace BetterMultiplayer.Trading;

internal static class TradeStateStore
{
    private static readonly Dictionary<ulong, TradeLocation> AvailablePlayers = [];

    internal static bool IsAvailable(ulong playerId, TradeLocation location) =>
        AvailablePlayers.TryGetValue(playerId, out TradeLocation current) && current == location;
    internal static TradeSessionSnapshot? CurrentSession { get; private set; }
    internal static string LastError { get; private set; } = string.Empty;
    internal static event Action? Changed;

    internal static void SetAvailability(ulong playerId, bool available, TradeLocation location)
    {
        if (available)
            AvailablePlayers[playerId] = location;
        else if (IsAvailable(playerId, location))
            AvailablePlayers.Remove(playerId);
        Changed?.Invoke();
    }

    internal static void SetSession(TradeSessionSnapshot snapshot)
    {
        if (!snapshot.Contains(TradeNetwork.LocalPlayerId))
            return;

        CurrentSession = snapshot.Clone();
        LastError = string.Empty;
        Changed?.Invoke();
    }

    internal static void ClearSession()
    {
        CurrentSession = null;
        LastError = string.Empty;
        Changed?.Invoke();
    }

    internal static void ApplyCommit(TradeSessionSnapshot snapshot)
    {
        TaskHelper.RunSafely(ApplyCommitAsync(snapshot));
    }

    internal static void MarkHostCommit(TradeSessionSnapshot snapshot)
    {
        if (snapshot.Location == TradeLocation.RestSite)
        {
            TradeRestSiteFlow.Complete(snapshot.PlayerA, success: true);
            TradeRestSiteFlow.Complete(snapshot.PlayerB, success: true);
        }
        if (snapshot.Contains(TradeNetwork.LocalPlayerId))
            CurrentSession = snapshot.Clone();
        Changed?.Invoke();
    }

    internal static void SetError(string error)
    {
        LastError = error;
        Changed?.Invoke();
    }

    internal static void Reset()
    {
        AvailablePlayers.Clear();
        CurrentSession = null;
        LastError = string.Empty;
        Changed?.Invoke();
    }

    private static async Task ApplyCommitAsync(TradeSessionSnapshot snapshot)
    {
        bool success = await TradeTransactionApplier.Apply(snapshot);
        if (snapshot.Location == TradeLocation.RestSite)
        {
            TradeRestSiteFlow.Complete(snapshot.PlayerA, success);
            TradeRestSiteFlow.Complete(snapshot.PlayerB, success);
        }
        if (snapshot.Contains(TradeNetwork.LocalPlayerId))
        {
            TradeSessionSnapshot local = snapshot.Clone();
            local.Status = success ? TradeSessionStatus.Committed : TradeSessionStatus.Canceled;
            CurrentSession = local;
            if (!success)
                LastError = ModText.Token(TextKey.TradeSyncFailed);
        }
        Changed?.Invoke();
    }
}
