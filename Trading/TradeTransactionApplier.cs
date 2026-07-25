using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BetterMultiplayer.Trading;

internal static class TradeTransactionApplier
{
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

        try
        {
            RunState state = RunManager.Instance.State ??
                throw new InvalidOperationException("No run is currently active.");
            Player? playerA = state.GetPlayer(snapshot.PlayerA);
            Player? playerB = state.GetPlayer(snapshot.PlayerB);
            if (playerA is null || playerB is null)
                throw new InvalidOperationException("Trade participant is missing from the run.");

            int availableGoldA = snapshot.Location == TradeLocation.Merchant ? snapshot.GoldA : playerA.Gold;
            int availableGoldB = snapshot.Location == TradeLocation.Merchant ? snapshot.GoldB : playerB.Gold;

            if (!TradeValidator.TryResolvePair(
                    playerA,
                    snapshot.OfferA,
                    playerB,
                    snapshot.OfferB,
                    snapshot.Location,
                    availableGoldA,
                    availableGoldB,
                    out ResolvedOffer? offerA,
                    out ResolvedOffer? offerB,
                    out string error))
            {
                throw new InvalidOperationException(error);
            }

            TransferPayload payloadA = Capture(playerA, offerA!);
            TransferPayload payloadB = Capture(playerB, offerB!);

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

            await Remove(playerA, offerA!);
            await Remove(playerB, offerB!);

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
}
