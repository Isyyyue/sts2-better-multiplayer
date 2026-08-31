using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using BetterMultiplayer.Trading;

namespace BetterMultiplayer.Tests;

public sealed class GameApiCompatibilityTests
{
    [Fact]
    public void ProductionAssemblyDoesNotBindVersionSpecificConnectedPeersGetter()
    {
        using FileStream stream = File.OpenRead(typeof(GameApiCompatibility).Assembly.Location);
        using PEReader peReader = new(stream);
        MetadataReader metadata = peReader.GetMetadataReader();
        string[] referencedMembers = metadata.MemberReferences
            .Select(handle => metadata.GetString(metadata.GetMemberReference(handle).Name))
            .ToArray();

        Assert.DoesNotContain("get_ConnectedPeers", referencedMembers);
        Assert.Contains("get_ConnectedPeerIds", referencedMembers);
    }

    [Fact]
    public void ConnectedPeerLookupMatchesExactPlayerAcrossPartySizes()
    {
        Assert.False(GameApiCompatibility.ContainsConnectedPeer([], 22));
        Assert.True(GameApiCompatibility.ContainsConnectedPeer([22], 22));
        Assert.False(GameApiCompatibility.ContainsConnectedPeer([22], 33));

        ulong[] threeRemotePeers = [11, 22, 33];
        Assert.True(GameApiCompatibility.ContainsConnectedPeer(threeRemotePeers, 22));
        Assert.False(GameApiCompatibility.ContainsConnectedPeer(threeRemotePeers, 44));
    }

    [Fact]
    public void ReadsLegacyPotionCapabilityProperty()
    {
        Assert.True(GameApiCompatibility.TryReadBooleanProperty(
            new LegacyPotionApi { CanRemovePotions = true },
            "CanRemovePotions") == true);
    }

    [Fact]
    public void ReadsBetaPotionCapabilityProperty()
    {
        Assert.False(GameApiCompatibility.TryReadBooleanProperty(
            new BetaPotionApi { CanUseOrRemovePotions = false },
            "CanUseOrRemovePotions") == true);
    }

    [Fact]
    public void MissingOrNonBooleanPropertyReturnsNull()
    {
        Assert.Null(GameApiCompatibility.TryReadBooleanProperty(new object(), "Missing"));
        Assert.Null(GameApiCompatibility.TryReadBooleanProperty(new NonBooleanApi(), "Value"));
    }

    [Fact]
    public void ControllerDetectionSupportsLegacyAndBetaProperties()
    {
        Assert.True(GameApiCompatibility.IsUsingController(
            new LegacyControllerApi { IsUsingController = true }));
        Assert.True(GameApiCompatibility.IsUsingController(
            new BetaControllerApi { InputType = "Controller" }));
        Assert.False(GameApiCompatibility.IsUsingController(
            new BetaControllerApi { InputType = "MouseAndKeyboard" }));
    }

    private sealed class LegacyPotionApi
    {
        public bool CanRemovePotions { get; init; }
    }

    private sealed class BetaPotionApi
    {
        public bool CanUseOrRemovePotions { get; init; }
    }

    private sealed class NonBooleanApi
    {
        public string Value => "not a bool";
    }

    private sealed class LegacyControllerApi
    {
        public bool IsUsingController { get; init; }
    }

    private sealed class BetaControllerApi
    {
        public string InputType { get; init; } = string.Empty;
    }
}
