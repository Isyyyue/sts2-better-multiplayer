using BetterMultiplayer.Diagnostics;

namespace BetterMultiplayer.Tests;

[Collection("TradeState")]
public sealed class TradeCoordinatorStateTests : IDisposable
{
    public TradeCoordinatorStateTests() => TradeCoordinator.EndLocation(TradeLocation.Merchant);

    public void Dispose() => TradeCoordinator.EndLocation(TradeLocation.Merchant);

    [Fact]
    public void DuplicateBeginLocationPreservesAvailability()
    {
        TradeCoordinator.BeginLocation(TradeLocation.Merchant);
        Assert.True(TradeStateStore.SetAvailability(12, available: true, TradeLocation.Merchant));

        TradeCoordinator.BeginLocation(TradeLocation.Merchant);

        Assert.True(TradeStateStore.IsAvailable(12, TradeLocation.Merchant));
    }

    [Fact]
    public void EndLocationAllowsTheNextVisitToReinitialize()
    {
        TradeCoordinator.BeginLocation(TradeLocation.Merchant);
        Assert.True(TradeStateStore.SetAvailability(12, available: true, TradeLocation.Merchant));

        TradeCoordinator.EndLocation(TradeLocation.Merchant);
        TradeCoordinator.BeginLocation(TradeLocation.Merchant);

        Assert.False(TradeStateStore.IsAvailable(12, TradeLocation.Merchant));
    }

    [Fact]
    public void DuplicateRestSiteInitializationPreservesUsedPlayers()
    {
        TradeCoordinator.BeginLocation(TradeLocation.RestSite);
        TradeUsageTracker.Mark(12);

        try
        {
            TradeCoordinator.BeginLocation(TradeLocation.RestSite);

            Assert.True(TradeUsageTracker.HasUsed(12));
        }
        finally
        {
            TradeCoordinator.EndLocation(TradeLocation.RestSite);
            TradeUsageTracker.Reset();
        }
    }

    [Fact]
    public void LateWithdrawalDoesNotReopenAClosedLocation()
    {
        long sequence = DiagnosticRecorder.Snapshot().LastOrDefault()?.Sequence ?? 0;
        TradeCoordinator.EndLocation(TradeLocation.Merchant);

        TradeCoordinator.SetAvailable(
            playerId: 12,
            available: false,
            TradeLocation.Merchant,
            reportedGold: 0);

        DiagnosticEntry entry = Assert.Single(
            DiagnosticRecorder.Snapshot(),
            item => item.Sequence > sequence);
        Assert.Equal(DiagnosticEventCode.TradeAvailabilityHandled, entry.Code);
        Assert.Equal("no_active_location", entry.Facts?.Reason);
    }

    [Fact]
    public void LateAnnouncementDoesNotReopenAClosedLocation()
    {
        long sequence = DiagnosticRecorder.Snapshot().LastOrDefault()?.Sequence ?? 0;
        TradeCoordinator.EndLocation(TradeLocation.Merchant);

        TradeCoordinator.SetAvailable(
            playerId: 12,
            available: true,
            TradeLocation.Merchant,
            reportedGold: 0);

        DiagnosticEntry entry = Assert.Single(
            DiagnosticRecorder.Snapshot(),
            item => item.Sequence > sequence);
        Assert.Equal(DiagnosticEventCode.TradeAvailabilityHandled, entry.Code);
        Assert.Equal("no_active_location", entry.Facts?.Reason);
        Assert.False(TradeStateStore.IsAvailable(12, TradeLocation.Merchant));
    }
}
