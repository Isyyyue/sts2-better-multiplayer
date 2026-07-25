namespace BetterMultiplayer.Trading;

using BetterMultiplayer.Localization;

internal static class TradeShapeRules
{
    internal static bool TryValidate(
        TradeLocation location,
        int cardCount,
        int relicCount,
        int potionCount,
        int gold,
        out string error)
    {
        if (!Enum.IsDefined(location))
        {
            error = ModText.Token(TextKey.InvalidTradeLocation);
            return false;
        }
        if (cardCount < 0 || relicCount < 0 || potionCount < 0 || gold < 0)
        {
            error = ModText.Token(TextKey.NegativeOfferAmount);
            return false;
        }
        if (location == TradeLocation.RestSite && gold != 0)
        {
            error = ModText.Token(TextKey.GoldOnlyAtMerchant);
            return false;
        }
        if (location == TradeLocation.Merchant &&
            (cardCount > 0 || relicCount > 0 || potionCount > 0))
        {
            error = ModText.Token(TextKey.ItemsNotAllowedAtMerchant);
            return false;
        }
        if (cardCount > TradeValidator.MaxCards ||
            relicCount > TradeValidator.MaxRelics ||
            potionCount > TradeValidator.MaxPotions)
        {
            error = ModText.Token(TextKey.OfferLimitExceeded);
            return false;
        }

        error = string.Empty;
        return true;
    }
}
