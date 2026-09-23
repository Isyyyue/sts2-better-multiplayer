using System.Globalization;
using BetterMultiplayer.Localization;

namespace BetterMultiplayer.Trading.View;

internal static class TradeGoldInput
{
    internal static int Adjust(int current, int delta, int availableGold) =>
        Math.Clamp(current + delta, 0, Math.Max(0, availableGold));

    internal static int Max(int availableGold) => Math.Max(0, availableGold);

    internal static bool TryParse(string text, int availableGold, out int amount, out string error)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out amount))
        {
            error = ModText.Get(TextKey.InvalidGoldAmount);
            return false;
        }
        if (amount < 0 || amount > availableGold)
        {
            error = ModText.Get(TextKey.GoldAmountRange, availableGold);
            return false;
        }

        error = string.Empty;
        return true;
    }
}

