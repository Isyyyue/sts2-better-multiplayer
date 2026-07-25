namespace BetterMultiplayer.Trading;

internal sealed record AssistSmithResult(
    bool Success,
    ulong TargetId,
    int CardIndex,
    string CardId,
    int UpgradeLevel,
    string Error);

internal static class AssistSmithFlow
{
    private static readonly object Gate = new();
    private static readonly Dictionary<ulong, TaskCompletionSource<AssistSmithResult>> Waiters = [];
    private static readonly Dictionary<ulong, AssistSmithResult> PendingResults = [];

    internal static Task<AssistSmithResult> WaitForResult(ulong playerId)
    {
        lock (Gate)
        {
            if (PendingResults.Remove(playerId, out AssistSmithResult? pending))
                return Task.FromResult(pending);

            if (!Waiters.TryGetValue(playerId, out TaskCompletionSource<AssistSmithResult>? waiter))
            {
                waiter = new TaskCompletionSource<AssistSmithResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                Waiters[playerId] = waiter;
            }
            return waiter.Task;
        }
    }

    internal static void Complete(ulong playerId, AssistSmithResult result)
    {
        lock (Gate)
        {
            if (Waiters.Remove(playerId, out TaskCompletionSource<AssistSmithResult>? waiter))
                waiter.TrySetResult(result);
            else
                PendingResults[playerId] = result;
        }
    }

    internal static void BeginRestSite()
    {
        AssistSmithResult canceled = new(false, 0, -1, string.Empty, 0, string.Empty);
        lock (Gate)
        {
            foreach (TaskCompletionSource<AssistSmithResult> waiter in Waiters.Values)
                waiter.TrySetResult(canceled);
            Waiters.Clear();
            PendingResults.Clear();
        }
    }

    internal static void Reset() => BeginRestSite();
}
