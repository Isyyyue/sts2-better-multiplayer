using MegaCrit.Sts2.Core.Helpers;
using BetterMultiplayer.Localization;
using BetterMultiplayer.Diagnostics;

namespace BetterMultiplayer.Trading.Core;

internal static class TradeStateStore
{
    private static readonly Dictionary<ulong, TradeLocation> AvailablePlayers = [];

    internal static bool IsAvailable(ulong playerId, TradeLocation location) =>
        AvailablePlayers.TryGetValue(playerId, out TradeLocation current) && current == location;
    internal static TradeSessionSnapshot? CurrentSession { get; private set; }
    internal static string LastError { get; private set; } = string.Empty;
    internal static event Action? Changed;

    internal static bool SetAvailability(ulong playerId, bool available, TradeLocation location)
    {
        bool changed;
        if (available)
        {
            changed = !IsAvailable(playerId, location);
            AvailablePlayers[playerId] = location;
        }
        else if (IsAvailable(playerId, location))
        {
            changed = true;
            AvailablePlayers.Remove(playerId);
        }
        else
        {
            changed = false;
        }

        if (changed)
            Changed?.Invoke();
        return changed;
    }

    internal static void SetSession(TradeSessionSnapshot snapshot)
    {
        if (!snapshot.Contains(TradeNetwork.LocalPlayerId))
            return;

        // ★ 只在【换了一笔交易】时清空错误，不能无条件清。
        //   房主拒绝报价时发的是「错误 + 快照」两条消息，快照往往后到；
        //   无条件清空会把刚弹出的失败原因立刻擦掉，玩家只看到"点了没反应"。
        bool sameSession = CurrentSession?.SessionId == snapshot.SessionId;
        CurrentSession = snapshot.Clone();
        if (!sameSession)
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
        DiagnosticRecorder.RecordSessionSnapshot(snapshot, "commit_applied");
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
            DiagnosticRecorder.RecordSessionSnapshot(
                local,
                success ? "commit_applied" : "commit_failed");
        }
        Changed?.Invoke();
    }
}
