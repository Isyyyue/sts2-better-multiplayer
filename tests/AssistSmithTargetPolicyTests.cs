using System.Runtime.CompilerServices;
using BetterMultiplayer.Trading;
using Godot;
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

    [Fact]
    public void OverlappingCharacterControlsPassMouseThroughAndTrackTheirOriginalFilters()
    {
        AssistSmithTargetControlInput[] controls =
        [
            new(IsHitbox: true, IntersectsHitbox: true, Control.MouseFilterEnum.Pass),
            new(IsHitbox: false, IntersectsHitbox: true, Control.MouseFilterEnum.Stop),
            new(IsHitbox: false, IntersectsHitbox: true, Control.MouseFilterEnum.Pass),
            new(IsHitbox: false, IntersectsHitbox: true, Control.MouseFilterEnum.Ignore),
            new(IsHitbox: false, IntersectsHitbox: false, Control.MouseFilterEnum.Stop)
        ];

        AssistSmithTargetControlPlan[] plan = AssistSmithTargetInputPolicy.BuildPlan(controls);

        Assert.Equal(
            [
                Control.MouseFilterEnum.Stop,
                Control.MouseFilterEnum.Ignore,
                Control.MouseFilterEnum.Ignore,
                Control.MouseFilterEnum.Ignore,
                Control.MouseFilterEnum.Stop
            ],
            plan.Select(item => item.ActiveMouseFilter));
        Assert.Equal(
            controls.Select(item => item.OriginalMouseFilter),
            plan.Select(item => item.OriginalMouseFilter));
    }

    [Fact]
    public void TemporaryMouseFilterIsRestoredWhenItStillHasTheActiveValue()
    {
        Assert.True(AssistSmithTargetInputPolicy.ShouldRestore(
            Control.MouseFilterEnum.Stop,
            Control.MouseFilterEnum.Ignore,
            Control.MouseFilterEnum.Ignore));
    }

    [Fact]
    public void MouseFilterChangedByAnotherModIsNotOverwrittenDuringCleanup()
    {
        Assert.False(AssistSmithTargetInputPolicy.ShouldRestore(
            Control.MouseFilterEnum.Stop,
            Control.MouseFilterEnum.Ignore,
            Control.MouseFilterEnum.Pass));
    }

    private static Player NewPlayerStub() =>
        (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
}
