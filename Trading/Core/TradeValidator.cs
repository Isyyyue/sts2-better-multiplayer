using System;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using BetterMultiplayer.Config;
using BetterMultiplayer.Localization;

namespace BetterMultiplayer.Trading.Core;

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

    // 原版按 RelicModel.IsTradable 判定。玩家在设置里逐条放开限制后，
    // 这里补上玩家设置这一层。两处调用点（TryResolve 与 TradeOverlay 的
    // 选择格子）都走这个函数，所以只需要改这一处。
    //
    // 兜底：配置类继承自 BaseLib 的 SimpleModConfig，而清单里声明的 BaseLib
    // 最低版本（v3.3.7）早于本模组实际验证过的版本。若玩家装的 BaseLib 没有
    // 这套 Config API，加载该类型会抛 TypeLoadException。这里捕获后回落到
    // 原版行为——宁可设置页不可用，也不能让交易整个报错。
    // 回落目标是 relic.IsTradable 而不是 false：
    // 总开关关着时 CanTrade 本来就等于 IsTradable，
    // 所以"配置读不到"和"总开关没开"应该表现一致。
    internal static bool CanTradeRelic(RelicModel relic)
    {
        try
        {
            return BetterMultiplayerConfig.CanTrade(relic);
        }
        catch (Exception)
        {
            return relic.IsTradable;
        }
    }

    // 交易后是否允许某一方持有重复遗物。默认不允许（原版规则）。
    //
    // 兜底理由和 CanTradeRelic 一样：清单里声明的 BaseLib 最低版本早于实际
    // 验证过的版本，配置类型可能加载失败。捕获后回落 false = 原版行为，
    // 宁可这个开关用不了，也不能让交易校验整个报错。
    internal static bool DuplicateRelicsAllowed()
    {
        try
        {
            return BetterMultiplayerConfig.AllowDuplicateRelicsAfterTrade;
        }
        catch (Exception)
        {
            return false;
        }
    }

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

        // 本模组默认沿用原版的"不重复持有"约束，所以一笔会让某方撞上已有遗物的
        // 交易会被整笔拒绝。开关打开时跳过——注意两个 HashSet 的构造本身带副作用
        // （Add 会消耗元素），所以必须整段跳，不能只改 if 的条件。
        //
        // ★ 放行是安全的（2026-09-23 反编译核实，整条链路三处都不拦重复）：
        //     RelicCmd.Obtain          —— 没有重复检查
        //     Player.AddRelicInternal  —— 直接 _relics.Add，没有检查
        //     RelicGrabBag.Remove      —— 用 RemoveAll，幂等，找不到也不抛
        //   原版"不重复"的约束其实来自**抽取池和遗物选择界面**，不在 Obtain 这条路上。
        //   所以放行后拿到的是两个**独立实例**（各自 StackCount = 1）。
        //
        //   刻意不走 RelicModel.IncrementStackCount：它只对 IsStackable=true 的遗物
        //   有效（默认 false，只有 Circlet 之类少数几个可堆叠），对绝大多数遗物会
        //   直接抛 InvalidOperationException。别改。
        if (!DuplicateRelicsAllowed())
        {
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
        }

        error = string.Empty;
        return true;
    }
}
