using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using BetterMultiplayer.Localization;

namespace BetterMultiplayer.Trading;

internal sealed record ResolvedOffer(
    IReadOnlyList<CardModel> Cards,
    IReadOnlyList<RelicModel> Relics,
    IReadOnlyList<PotionModel> Potions,
    int Gold);

internal static class TradeValidator
{
    internal const int MaxCards = 3;
    internal const int MaxRelics = 1;
    internal const int MaxPotions = 1;

    private static readonly HashSet<string> BoundCards = new(StringComparer.Ordinal)
    {
        "AscendersBane",
        "CurseOfTheBell",
        "Necronomicurse"
    };

    internal static bool TryResolve(
        Player player,
        TradeOffer rawOffer,
        TradeLocation location,
        out ResolvedOffer? resolved,
        out string error) =>
        TryResolve(player, rawOffer, location, player.Gold, out resolved, out error);

    internal static bool TryResolve(
        Player player,
        TradeOffer rawOffer,
        TradeLocation location,
        int availableGold,
        out ResolvedOffer? resolved,
        out string error)
    {
        TradeOffer offer = rawOffer.Normalized();
        resolved = null;

        if (!TradeShapeRules.TryValidate(
                location,
                offer.CardIndices.Count,
                offer.RelicIndices.Count,
                offer.PotionSlotIndices.Count,
                offer.Gold,
                out error))
        {
            return false;
        }

        if (!TradeGoldBalance.TryValidateOffer(availableGold, offer.Gold, out error))
        {
            return false;
        }

        if (offer.CardIndices.Any(index => index < 0 || index >= player.Deck.Cards.Count) ||
            offer.RelicIndices.Any(index => index < 0 || index >= player.Relics.Count) ||
            offer.PotionSlotIndices.Any(index => index < 0 || index >= player.PotionSlots.Count))
        {
            error = ModText.Token(TextKey.OfferedItemMissing);
            return false;
        }

        List<CardModel> cards = offer.CardIndices.Select(index => player.Deck.Cards[index]).ToList();
        if (cards.Any(card => BoundCards.Contains(card.GetType().Name)))
        {
            error = ModText.Token(TextKey.BoundCurseNotTradable);
            return false;
        }

        List<RelicModel> relics = offer.RelicIndices.Select(index => player.Relics[index]).ToList();
        if (relics.Any(relic => !CanTradeRelic(relic)))
        {
            error = ModText.Token(TextKey.RelicNotTradable);
            return false;
        }

        if (offer.PotionSlotIndices.Count > 0 && !GameApiCompatibility.CanRemovePotions(player))
        {
            error = ModText.Token(TextKey.PotionCannotBeRemoved);
            return false;
        }

        List<PotionModel> potions = [];
        foreach (int slot in offer.PotionSlotIndices)
        {
            PotionModel? potion = player.GetPotionAtSlotIndex(slot);
            if (potion is null)
            {
                error = ModText.Token(TextKey.OfferedPotionMissing);
                return false;
            }
            potions.Add(potion);
        }

        resolved = new ResolvedOffer(cards, relics, potions, offer.Gold);
        error = string.Empty;
        return true;
    }

    internal static bool CanTradeCard(CardModel card) => !BoundCards.Contains(card.GetType().Name);

    internal static bool CanTradeRelic(RelicModel relic) => relic.IsTradable;

    internal static bool TryResolvePair(
        Player playerA,
        TradeOffer offerA,
        Player playerB,
        TradeOffer offerB,
        TradeLocation location,
        out ResolvedOffer? resolvedA,
        out ResolvedOffer? resolvedB,
        out string error) =>
        TryResolvePair(
            playerA,
            offerA,
            playerB,
            offerB,
            location,
            playerA.Gold,
            playerB.Gold,
            out resolvedA,
            out resolvedB,
            out error);

    internal static bool TryResolvePair(
        Player playerA,
        TradeOffer offerA,
        Player playerB,
        TradeOffer offerB,
        TradeLocation location,
        int availableGoldA,
        int availableGoldB,
        out ResolvedOffer? resolvedA,
        out ResolvedOffer? resolvedB,
        out string error)
    {
        resolvedB = null;
        if (!TryResolve(playerA, offerA, location, availableGoldA, out resolvedA, out error) ||
            !TryResolve(playerB, offerB, location, availableGoldB, out resolvedB, out error))
        {
            return false;
        }

        if (resolvedA!.Cards.Count + resolvedA.Relics.Count + resolvedA.Potions.Count + resolvedA.Gold == 0 &&
            resolvedB!.Cards.Count + resolvedB.Relics.Count + resolvedB.Potions.Count + resolvedB.Gold == 0)
        {
            error = location == TradeLocation.Merchant
                ? ModText.Token(TextKey.NoGoldOffered)
                : ModText.Token(TextKey.NoItemsOffered);
            return false;
        }

        ResolvedOffer validA = resolvedA!;
        ResolvedOffer validB = resolvedB!;
        int finalPotionsA = playerA.Potions.Count() - validA.Potions.Count + validB.Potions.Count;
        int finalPotionsB = playerB.Potions.Count() - validB.Potions.Count + validA.Potions.Count;
        if (finalPotionsA > playerA.MaxPotionCount || finalPotionsB > playerB.MaxPotionCount)
        {
            error = ModText.Token(TextKey.NotEnoughPotionSlots);
            return false;
        }

        HashSet<ModelId> finalRelicsA = playerA.Relics
            .Except(validA.Relics)
            .Select(relic => relic.Id)
            .ToHashSet();
        HashSet<ModelId> finalRelicsB = playerB.Relics
            .Except(validB.Relics)
            .Select(relic => relic.Id)
            .ToHashSet();

        if (validB.Relics.Any(relic => !finalRelicsA.Add(relic.Id)) ||
            validA.Relics.Any(relic => !finalRelicsB.Add(relic.Id)))
        {
            error = ModText.Token(TextKey.DuplicateRelicAfterTrade);
            return false;
        }

        error = string.Empty;
        return true;
    }
}
