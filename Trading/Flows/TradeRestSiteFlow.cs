namespace BetterMultiplayer.Trading.Flows;

internal static class TradeRestSiteFlow
{
    private static readonly object Gate = new();
    private static readonly Dictionary<ulong, TaskCompletionSource<bool>> Waiters = [];

    internal static async Task<bool> WaitForResult(ulong playerId)
    {
        TaskCompletionSource<bool> waiter;
        lock (Gate)
        {
            // ★ 每次都换新的 waiter，绝不复用字典里那个。
            //   上一次交易留下的 waiter 可能已经完成（Complete 只 TrySetResult、
            //   不摘除条目），复用它会让本次等待瞬间返回——界面刚打开，
            //   休息处那边就已经把选项判定成结束了。
            waiter = new TaskCompletionSource<bool>();
            Waiters[playerId] = waiter;
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
