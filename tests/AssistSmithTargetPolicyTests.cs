using System.Runtime.CompilerServices;
using BetterMultiplayer.Trading;
using MegaCrit.Sts2.Core.Entities.Players;

namespace BetterMultiplayer.Tests;

public sealed class AssistSmithTargetPolicyTests
{
    [Fact]
    public void RemotePlayerRemainsSelectableWhenNoUpgradableCardsAreKnown()
    {
        Player owner = NewPlayerStub();
        Player teammateWithoutLoadedDeck = NewPlayerStub();

        Assert.True(AssistSmithTargetPolicy.CanTarget(owner, teammateWithoutLoadedDeck));
    }

    [Fact]
    public void MissingOrOwningPlayerIsNotSelectable()
    {
        Player owner = NewPlayerStub();

        Assert.False(AssistSmithTargetPolicy.CanTarget(owner, null));
        Assert.False(AssistSmithTargetPolicy.CanTarget(owner, owner));
    }

    private static Player NewPlayerStub() =>
        (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
}
