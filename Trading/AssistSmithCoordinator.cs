using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using BetterMultiplayer.Trading.Messages;
using BetterMultiplayer.Localization;

namespace BetterMultiplayer.Trading;

internal static class AssistSmithCoordinator
{
    private static readonly HashSet<ulong> ActivePlayers = [];

    internal static void Register(ulong playerId)
    {
        if (TradeNetwork.IsHost)
            ActivePlayers.Add(playerId);
    }

    internal static void Resolve(
        ulong senderId,
        bool canceled,
        ulong targetId,
        int cardIndex,
        string cardId,
        int upgradeLevel)
    {
        if (!ActivePlayers.Remove(senderId))
            return;

        AssistSmithResult result;
        if (canceled)
        {
            result = Failure(string.Empty);
        }
        else
        {
            Player? owner = RunManager.Instance.State?.GetPlayer(senderId);
            Player? target = RunManager.Instance.State?.GetPlayer(targetId);
            if (owner is null || target is null)
                result = Failure(ModText.Token(TextKey.AssistSmithPlayerLeft));
            else if (!AssistSmithSelection.TryResolve(
                         owner,
                         target,
                         cardIndex,
                         cardId,
                         upgradeLevel,
                         out _,
                         out string error))
                result = Failure(error);
            else
                result = new AssistSmithResult(true, targetId, cardIndex, cardId, upgradeLevel, string.Empty);
        }

        Broadcast(senderId, result);
    }

    internal static void PlayerDisconnected(ulong playerId)
    {
        if (TradeNetwork.IsHost && ActivePlayers.Remove(playerId))
            Broadcast(playerId, Failure(ModText.Token(TextKey.AssistSmithPlayerDisconnected)));
    }

    internal static void BeginRestSite()
    {
        ActivePlayers.Clear();
        AssistSmithFlow.BeginRestSite();
    }

    internal static void Reset()
    {
        ActivePlayers.Clear();
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
