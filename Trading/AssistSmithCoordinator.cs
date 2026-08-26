using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using BetterMultiplayer.Trading.Messages;
using BetterMultiplayer.Localization;
using BetterMultiplayer.Diagnostics;

namespace BetterMultiplayer.Trading;

internal static class AssistSmithCoordinator
{
    private readonly record struct PendingRequest(
        bool Canceled,
        ulong TargetId,
        int CardIndex,
        string CardId,
        int UpgradeLevel);

    private static readonly HashSet<ulong> ActivePlayers = [];
    private static readonly Dictionary<ulong, PendingRequest> PendingRequests = [];

    internal static void Register(ulong playerId)
    {
        if (TradeNetwork.IsHost)
            Register(playerId, Broadcast);
    }

    internal static void Register(
        ulong playerId,
        Action<ulong, AssistSmithResult> broadcast)
    {
        ActivePlayers.Add(playerId);
        DiagnosticRecorder.RecordAssistSmith(
            "registered",
            playerId,
            0,
            -1,
            string.Empty,
            0,
            success: true,
            "accepted");
        if (PendingRequests.Remove(playerId, out PendingRequest pending))
        {
            DiagnosticRecorder.RecordAssistSmith(
                "request_replayed",
                playerId,
                pending.TargetId,
                pending.CardIndex,
                pending.CardId,
                pending.UpgradeLevel,
                success: true,
                "accepted");
            Resolve(
                playerId,
                pending.Canceled,
                pending.TargetId,
                pending.CardIndex,
                pending.CardId,
                pending.UpgradeLevel,
                broadcast);
        }
    }

    internal static void Resolve(
        ulong senderId,
        bool canceled,
        ulong targetId,
        int cardIndex,
        string cardId,
        int upgradeLevel)
    {
        Resolve(
            senderId,
            canceled,
            targetId,
            cardIndex,
            cardId,
            upgradeLevel,
            Broadcast);
    }

    internal static void Resolve(
        ulong senderId,
        bool canceled,
        ulong targetId,
        int cardIndex,
        string cardId,
        int upgradeLevel,
        Action<ulong, AssistSmithResult> broadcast)
    {
        if (!ActivePlayers.Remove(senderId))
        {
            PendingRequests[senderId] = new PendingRequest(
                canceled,
                targetId,
                cardIndex,
                cardId,
                upgradeLevel);
            DiagnosticRecorder.RecordAssistSmith(
                "request_pending",
                senderId,
                targetId,
                cardIndex,
                cardId,
                upgradeLevel,
                success: false,
                "not_available");
            return;
        }

        DiagnosticRecorder.RecordAssistSmith(
            "request_received",
            senderId,
            targetId,
            cardIndex,
            cardId,
            upgradeLevel,
            success: !canceled,
            canceled ? "canceled" : "accepted");

        AssistSmithResult result;
        string reason;
        if (canceled)
        {
            result = Failure(string.Empty);
            reason = "canceled";
        }
        else
        {
            Player? owner = RunManager.Instance.State?.GetPlayer(senderId);
            Player? target = RunManager.Instance.State?.GetPlayer(targetId);
            if (owner is null || target is null)
            {
                result = Failure(ModText.Token(TextKey.AssistSmithPlayerLeft));
                reason = "player_missing";
            }
            else if (!AssistSmithSelection.TryResolve(
                         owner,
                         target,
                         cardIndex,
                         cardId,
                         upgradeLevel,
                         out _,
                         out string error))
            {
                result = Failure(error);
                reason = "validation_failed";
            }
            else
            {
                result = new AssistSmithResult(true, targetId, cardIndex, cardId, upgradeLevel, string.Empty);
                reason = "completed";
            }
        }

        DiagnosticRecorder.RecordAssistSmith(
            "result_broadcast",
            senderId,
            result.TargetId == 0 ? targetId : result.TargetId,
            result.CardIndex < 0 ? cardIndex : result.CardIndex,
            result.CardId.Length == 0 ? cardId : result.CardId,
            result.UpgradeLevel,
            result.Success,
            reason);
        broadcast(senderId, result);
    }

    internal static void PlayerDisconnected(ulong playerId)
    {
        if (TradeNetwork.IsHost)
            PlayerDisconnected(playerId, Broadcast);
    }

    internal static void PlayerDisconnected(
        ulong playerId,
        Action<ulong, AssistSmithResult> broadcast)
    {
        bool pending = PendingRequests.Remove(playerId);
        bool active = ActivePlayers.Remove(playerId);
        if (pending || active)
        {
            DiagnosticRecorder.RecordAssistSmith(
                "disconnected",
                playerId,
                0,
                -1,
                string.Empty,
                0,
                success: false,
                "disconnected");
        }
        if (active)
            broadcast(playerId, Failure(ModText.Token(TextKey.AssistSmithPlayerDisconnected)));
    }

    internal static void BeginRestSite()
    {
        ActivePlayers.Clear();
        PendingRequests.Clear();
        AssistSmithFlow.BeginRestSite();
    }

    internal static void Reset()
    {
        ActivePlayers.Clear();
        PendingRequests.Clear();
        AssistSmithFlow.Reset();
    }

    private static AssistSmithResult Failure(string error) =>
        new(false, 0, -1, string.Empty, 0, error);

    private static void Broadcast(ulong playerId, AssistSmithResult result) =>
        TradeNetwork.Broadcast(new AssistSmithResultEvent
        {
            PlayerId = playerId,
            Success = result.Success,
            TargetId = result.TargetId,
            CardIndex = result.CardIndex,
            CardId = result.CardId,
            UpgradeLevel = result.UpgradeLevel,
            Error = result.Error
        });
}

internal static class AssistSmithSelection
{
    internal static bool TryResolve(
        Player owner,
        Player target,
        int cardIndex,
        string cardId,
        int upgradeLevel,
        out CardModel? card,
        out string error)
    {
        card = null;
        error = string.Empty;
        if (owner == target)
        {
            error = ModText.Token(TextKey.AssistSmithOtherPlayersOnly);
            return false;
        }
        if (cardIndex < 0 || cardIndex >= target.Deck.Cards.Count)
        {
            error = ModText.Token(TextKey.AssistSmithDeckChanged);
            return false;
        }

        CardModel candidate = target.Deck.Cards[cardIndex];
        if (!string.Equals(candidate.Id.ToString(), cardId, StringComparison.Ordinal) ||
            candidate.CurrentUpgradeLevel != upgradeLevel)
        {
            error = ModText.Token(TextKey.AssistSmithDeckChanged);
            return false;
        }
        if (!candidate.IsUpgradable)
        {
            error = ModText.Token(TextKey.AssistSmithCardNotUpgradable);
            return false;
        }

        card = candidate;
        return true;
    }
}
