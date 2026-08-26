using BetterMultiplayer.Diagnostics;
using BetterMultiplayer.Trading;

namespace BetterMultiplayer.Tests;

public sealed class AssistSmithFlowTests : IDisposable
{
    public AssistSmithFlowTests()
    {
        AssistSmithFlow.Reset();
        DiagnosticRecorder.ResetForTests();
    }

    public void Dispose()
    {
        AssistSmithFlow.Reset();
        DiagnosticRecorder.ResetForTests();
    }

    [Fact]
    public async Task ResultReceivedBeforeWaitIsNotLost()
    {
        AssistSmithResult expected = new(true, 12, 7, "Card:Strike", 0, string.Empty);

        AssistSmithFlow.Complete(11, expected);
        AssistSmithResult actual = await AssistSmithFlow.WaitForResult(11);

        Assert.Equal(expected, actual);
        DiagnosticEntry entry = Assert.Single(DiagnosticRecorder.Snapshot(), candidate =>
            candidate.Code == DiagnosticEventCode.AssistSmithChanged);
        Assert.Equal("result_received", entry.Facts?.Stage);
        Assert.Equal("11", entry.Facts?.ActorId);
        Assert.Equal("12", entry.Facts?.TargetId);
        Assert.Equal(7, entry.Facts?.CardIndex);
        Assert.Equal("Card:Strike", entry.Facts?.ItemId);
        Assert.True(entry.Facts?.Success);
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
