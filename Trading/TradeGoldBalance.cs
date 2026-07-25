namespace BetterMultiplayer.Trading;

using BetterMultiplayer.Localization;

internal static class TradeGoldBalance
{
    internal static bool TryValidateOffer(int reportedBalance, int offeredGold, out string error)
    {
        if (reportedBalance < 0 || offeredGold < 0 || offeredGold > reportedBalance)
        {
            error = ModText.Token(TextKey.InvalidGoldOffer);
            return false;
        }

        error = string.Empty;
        return true;
    }

    internal static bool TryCalculateFinal(
        int reportedBalance,
        int offeredGold,
        int receivedGold,
        out int finalBalance)
    {
        finalBalance = 0;
        if (!TryValidateOffer(reportedBalance, offeredGold, out _) || receivedGold < 0)
            return false;

        long result = (long)reportedBalance - offeredGold + receivedGold;
        if (result > int.MaxValue)
            return false;

        finalBalance = (int)result;
        return true;
    }
}
