namespace BetterMultiplayer.Trading;

internal static class TradeUsageTracker
{
    private static readonly HashSet<ulong> UsedPlayers = [];

    internal static bool HasUsed(ulong playerId) => UsedPlayers.Contains(playerId);

    internal static void Mark(ulong playerId) => UsedPlayers.Add(playerId);

    internal static void BeginRestSite() => UsedPlayers.Clear();

    internal static void Reset() => BeginRestSite();
}
