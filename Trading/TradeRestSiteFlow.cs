namespace BetterMultiplayer.Trading;

internal static class TradeRestSiteFlow
{
    private static readonly object Gate = new();
    private static readonly Dictionary<ulong, TaskCompletionSource<bool>> Waiters = [];

    internal static async Task<bool> WaitForResult(ulong playerId)
    {
        TaskCompletionSource<bool> waiter;
        lock (Gate)
        {
            if (!Waiters.TryGetValue(playerId, out waiter!))
            {
                waiter = new TaskCompletionSource<bool>();
                Waiters[playerId] = waiter;
            }
        }

        try
        {
            return await waiter.Task;
        }
        finally
        {
            lock (Gate)
            {
                if (Waiters.TryGetValue(playerId, out TaskCompletionSource<bool>? current) &&
                    ReferenceEquals(current, waiter))
                {
                    Waiters.Remove(playerId);
                }
            }
        }
    }

    internal static void Complete(ulong playerId, bool success)
    {
        lock (Gate)
        {
            if (Waiters.TryGetValue(playerId, out TaskCompletionSource<bool>? waiter))
                waiter.TrySetResult(success);
        }
    }

    internal static void BeginRestSite()
    {
        lock (Gate)
        {
            foreach (TaskCompletionSource<bool> waiter in Waiters.Values)
                waiter.TrySetResult(false);
            Waiters.Clear();
        }
    }

    internal static void Reset() => BeginRestSite();
}
