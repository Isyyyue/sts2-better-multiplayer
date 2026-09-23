using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BetterMultiplayer.Trading.Sync;

internal static class TradeTransactionApplier
{
    internal static int ResolveAvailableGold(int playerGold) => playerGold;

    // The network snapshot may carry a client-reported balance for diagnostics,
    // but settlement must always use the authoritative run-state balance.
    internal static int ResolveAvailableGold(int playerGold, int reportedGold) => playerGold;

    private sealed record TransferPayload(
        List<SerializableCard> Cards,
        List<SerializableRelic> Relics,
        List<SerializablePotion> Potions,
        int Gold);

    private static readonly HashSet<ulong> AppliedTransactions = [];

    internal static async Task<bool> Apply(TradeSessionSnapshot snapshot)
    {
        lock (AppliedTransactions)
        {
            if (!AppliedTransactions.Add(snapshot.SessionId))
                return true;
        }

        RunState? state = null;
        Player? playerA = null;
        Player? playerB = null;
        ResolvedOffer? offerA = null;
        ResolvedOffer? offerB = null;
        TransferPayload? payloadA = null;
        TransferPayload? payloadB = null;
        bool removedA = false;
        bool removedB = false;

        try
        {
            state = RunManager.Instance.State ??
                throw new InvalidOperationException("No run is currently active.");
            playerA = state.GetPlayer(snapshot.PlayerA);
            playerB = state.GetPlayer(snapshot.PlayerB);
            if (playerA is null || playerB is null)
                throw new InvalidOperationException("Trade participant is missing from the run.");

            int availableGoldA = ResolveAvailableGold(playerA.Gold, snapshot.GoldA);
            int availableGoldB = ResolveAvailableGold(playerB.Gold, snapshot.GoldB);

            if (!TradeValidator.TryResolvePair(
                    playerA,
                    snapshot.OfferA,
                    playerB,
                    snapshot.OfferB,
                    snapshot.Location,
                    availableGoldA,
                    availableGoldB,
                    out offerA,
                    out offerB,
                    out string error))
            {
                throw new InvalidOperationException(error);
            }

            payloadA = Capture(playerA, offerA!);
            payloadB = Capture(playerB, offerB!);

            int finalGoldA = playerA.Gold;
            int finalGoldB = playerB.Gold;
            if (snapshot.Location == TradeLocation.Merchant &&
                (!TradeGoldBalance.TryCalculateFinal(
                    availableGoldA,
                    payloadA.Gold,
                    payloadB.Gold,
                    out finalGoldA) ||
                 !TradeGoldBalance.TryCalculateFinal(
                    availableGoldB,
                    payloadB.Gold,
                    payloadA.Gold,
                    out finalGoldB)))
            {
                throw new InvalidOperationException("Gold trade would produce an invalid balance.");
            }

            // ★ 先把两边的东西都取下来，再给对方——顺序不能反。
            //   药水必须提前腾出槽位，TryToProcure 才有地方放；
            //   而且 Remove 走的是实例引用，比 Add（要反序列化、要占槽）可靠得多。
            //   代价是这一步之后如果 Add 失败，东西已经在半空中了，
            //   所以下面的 catch 必须把它们还回去。
            await Remove(playerA, offerA!);
            removedA = true;
            await Remove(playerB, offerB!);
            removedB = true;

            if (snapshot.Location == TradeLocation.Merchant)
            {
                playerA.Gold = finalGoldA;
                playerB.Gold = finalGoldB;
            }

            await Add(state, playerB, payloadA);
            await Add(state, playerA, payloadB);
            if (snapshot.Location == TradeLocation.RestSite)
            {
                TradeUsageTracker.Mark(snapshot.PlayerA);
                TradeUsageTracker.Mark(snapshot.PlayerB);
            }
            BetterMultiplayerMod.Logger.Info($"Trade {snapshot.SessionId} committed");
            return true;
        }
        catch (Exception ex)
        {
            BetterMultiplayerMod.Logger.Error($"Applying trade {snapshot.SessionId} failed: {ex}");

            // ★ 已经把东西取下来了就必须还回去。
            //   不还的话就是玩家报告的那个现象：交易报错，遗物也跟着没了——
            //   东西已经 Remove，Add 却没跑到。
            if (state is not null)
            {
                if (removedA && playerA is not null && payloadA is not null)
                    await Restore(state, playerA, payloadA);
                if (removedB && playerB is not null && payloadB is not null)
                    await Restore(state, playerB, payloadB);
            }

            // 失败就得把幂等标记撤掉，否则同一次交易再也不会被真正重试。
            lock (AppliedTransactions)
                AppliedTransactions.Remove(snapshot.SessionId);
            return false;
        }
    }

    internal static void Reset()
    {
        lock (AppliedTransactions)
            AppliedTransactions.Clear();
    }

    private static TransferPayload Capture(Player source, ResolvedOffer offer)
    {
        return new TransferPayload(
            offer.Cards.Select(card => card.ToSerializable()).ToList(),
            offer.Relics.Select(relic => relic.ToSerializable()).ToList(),
            offer.Potions.Select(potion => potion.ToSerializable(source.GetPotionSlotIndex(potion))).ToList(),
            offer.Gold);
    }

    private static async Task Remove(Player player, ResolvedOffer offer)
    {
        if (offer.Cards.Count > 0)
            await CardPileCmd.RemoveFromDeck(offer.Cards, showPreview: false);

        foreach (RelicModel relic in offer.Relics)
            await RelicCmd.Remove(relic);

        foreach (PotionModel potion in offer.Potions)
            await PotionCmd.Discard(potion);
    }

    private static async Task Add(RunState state, Player target, TransferPayload payload)
    {
        foreach (SerializableCard serialized in payload.Cards)
        {
            CardModel card = state.LoadCard(serialized, target);
            await CardPileCmd.Add(
                card,
                PileType.Deck,
                CardPilePosition.Bottom,
                clonedBy: null,
                skipVisuals: true);
        }

        foreach (SerializableRelic serialized in payload.Relics)
            await RelicCmd.Obtain(RelicModel.FromSerializable(serialized), target);

        foreach (SerializablePotion serialized in payload.Potions)
            await PotionCmd.TryToProcure(PotionModel.FromSerializable(serialized), target);
    }

    // 回滚：把之前从这个玩家身上取下来的东西原样放回去。
    // payload 是取下来之前抓的完整快照，加回去就是还原。
    //
    // 刻意吞掉异常：回滚跑在已经失败的路径上，让它再抛会把真正的失败原因盖掉，
    // 而且此时也没有第二次补救的机会了。
    private static async Task Restore(RunState state, Player player, TransferPayload payload)
    {
        try
        {
            await Add(state, player, payload);
            BetterMultiplayerMod.Logger.Warn(
                $"Rolled back the trade payload taken from player {player.NetId}.");
        }
        catch (Exception ex)
        {
            BetterMultiplayerMod.Logger.Error(
                $"Rolling back the trade payload for player {player.NetId} failed: {ex}");
        }
    }
}

