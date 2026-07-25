using BetterMultiplayer.Trading;

namespace BetterMultiplayer.Tests;

public sealed class AssistSmithFlowTests : IDisposable
{
    public AssistSmithFlowTests() => AssistSmithFlow.Reset();

    public void Dispose() => AssistSmithFlow.Reset();

    [Fact]
    public async Task ResultReceivedBeforeWaitIsNotLost()
    {
        AssistSmithResult expected = new(true, 12, 7, "Card:Strike", 0, string.Empty);

        AssistSmithFlow.Complete(11, expected);
        AssistSmithResult actual = await AssistSmithFlow.WaitForResult(11);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task WaitingSelectionCompletesWhenResultArrives()
    {
        Task<AssistSmithResult> waiting = AssistSmithFlow.WaitForResult(11);
        AssistSmithResult expected = new(true, 12, 7, "Card:Strike", 0, string.Empty);

        AssistSmithFlow.Complete(11, expected);

        Assert.Equal(expected, await waiting);
    }

    [Fact]
    public async Task BeginningNewRestSiteCancelsOutstandingSelection()
    {
        Task<AssistSmithResult> waiting = AssistSmithFlow.WaitForResult(11);

        AssistSmithFlow.BeginRestSite();
        AssistSmithResult result = await waiting;

        Assert.False(result.Success);
        Assert.Equal(-1, result.CardIndex);
    }
}
