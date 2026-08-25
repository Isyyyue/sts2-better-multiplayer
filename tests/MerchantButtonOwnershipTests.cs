using BetterMultiplayer.Trading;

namespace BetterMultiplayer.Tests;

public sealed class MerchantButtonOwnershipTests
{
    [Fact]
    public void SupersededRoomCannotReleaseCurrentRoomsButton()
    {
        const ulong firstRoomOwner = 1001;
        const ulong currentRoomOwner = 2002;
        MerchantTradePatch.ClaimButtonOwner(firstRoomOwner, () => { });
        MerchantTradePatch.ClaimButtonOwner(currentRoomOwner, () => { });

        Assert.False(MerchantTradePatch.ReleaseButtonOwner(firstRoomOwner));
        Assert.True(MerchantTradePatch.ReleaseButtonOwner(currentRoomOwner));
    }

    [Fact]
    public void SupersededButtonCleanupRunsOnceBeforeOldRoomExit()
    {
        const ulong firstRoomOwner = 3003;
        const ulong currentRoomOwner = 4004;
        int firstCleanupCount = 0;
        int currentCleanupCount = 0;
        Action firstCleanup = CountOnce(() => firstCleanupCount++);
        Action currentCleanup = CountOnce(() => currentCleanupCount++);

        Assert.True(MerchantTradePatch.ClaimButtonOwner(firstRoomOwner, firstCleanup));
        Assert.Equal(0, firstCleanupCount);

        Assert.True(MerchantTradePatch.ClaimButtonOwner(currentRoomOwner, currentCleanup));
        Assert.Equal(1, firstCleanupCount);
        Assert.Equal(0, currentCleanupCount);

        Assert.False(MerchantTradePatch.ReleaseButtonOwner(firstRoomOwner));
        firstCleanup();
        Assert.Equal(1, firstCleanupCount);

        Assert.True(MerchantTradePatch.ReleaseButtonOwner(currentRoomOwner));
        currentCleanup();
        currentCleanup();
        Assert.Equal(1, currentCleanupCount);
    }

    private static Action CountOnce(Action cleanup)
    {
        MerchantButtonCleanupState state = new();
        return () =>
        {
            if (state.TryBeginCleanup())
                cleanup();
        };
    }
}
