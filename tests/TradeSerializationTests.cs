using BetterMultiplayer.Trading;
using BetterMultiplayer.Trading.Messages;
using BetterMultiplayer.Localization;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace BetterMultiplayer.Tests;

public sealed class TradeSerializationTests
{
    [Theory]
    [InlineData(TradeLocation.RestSite)]
    [InlineData(TradeLocation.Merchant)]
    public void AvailabilityRequestRoundTrips(TradeLocation location)
    {
        AvailabilityRequest original = new()
        {
            Available = true,
            Location = location,
            ReportedGold = 568
        };

        AvailabilityRequest copy = RoundTrip<AvailabilityRequest>(original);

        Assert.True(copy.Available);
        Assert.Equal(location, copy.Location);
        Assert.Equal(568, copy.ReportedGold);
    }

    [Theory]
    [InlineData(TradeSessionStatus.Pending, TradeLocation.RestSite)]
    [InlineData(TradeSessionStatus.Active, TradeLocation.Merchant)]
    [InlineData(TradeSessionStatus.Committing, TradeLocation.RestSite)]
    [InlineData(TradeSessionStatus.Committed, TradeLocation.Merchant)]
    [InlineData(TradeSessionStatus.Canceled, TradeLocation.RestSite)]
    public void SessionSnapshotRoundTrips(TradeSessionStatus status, TradeLocation location)
    {
        TradeSessionSnapshot original = new()
        {
            SessionId = 42,
            PlayerA = 11,
            PlayerB = 12,
            Revision = 3,
            GoldA = 568,
            GoldB = 53,
            ConfirmedA = true,
            Status = status,
            Location = location,
            OfferA = new TradeOffer { CardIndices = [1, 4], Gold = 25 },
            OfferB = new TradeOffer { RelicIndices = [2], PotionSlotIndices = [0] }
        };

        TradeSessionSnapshot copy = RoundTrip<TradeSessionSnapshot>(original);

        Assert.Equal(original.SessionId, copy.SessionId);
        Assert.Equal(original.PlayerA, copy.PlayerA);
        Assert.Equal(original.PlayerB, copy.PlayerB);
        Assert.Equal(original.Revision, copy.Revision);
        Assert.Equal(568, copy.GoldFor(11));
        Assert.Equal(53, copy.GoldFor(12));
        Assert.Equal(original.ConfirmedA, copy.ConfirmedA);
        Assert.Equal(original.ConfirmedB, copy.ConfirmedB);
        Assert.Equal(status, copy.Status);
        Assert.Equal(location, copy.Location);
        Assert.Equal([1, 4], copy.OfferA.CardIndices);
        Assert.Equal(25, copy.OfferA.Gold);
        Assert.Equal([2], copy.OfferB.RelicIndices);
        Assert.Equal([0], copy.OfferB.PotionSlotIndices);
    }

    [Fact]
    public void AvailabilityRequestRejectsUnknownLocation()
    {
        PacketWriter writer = new();
        writer.WriteBool(true);
        writer.WriteByte(byte.MaxValue);
        PacketReader reader = new();
        reader.Reset(writer.Buffer);

        Assert.Throws<InvalidDataException>(() => new AvailabilityRequest().Deserialize(reader));
    }

    [Fact]
    public void SessionSnapshotRejectsUnknownStatus()
    {
        PacketWriter writer = new();
        writer.WriteULong(1);
        writer.WriteULong(2);
        writer.WriteULong(3);
        writer.WriteInt(0);
        writer.WriteInt(0);
        writer.WriteInt(0);
        writer.Write(new TradeOffer());
        writer.Write(new TradeOffer());
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteByte(byte.MaxValue);
        writer.WriteByte((byte)TradeLocation.RestSite);
        PacketReader reader = new();
        reader.Reset(writer.Buffer);

        Assert.Throws<InvalidDataException>(() => new TradeSessionSnapshot().Deserialize(reader));
    }

    [Fact]
    public void MerchantRequestsRoundTripOwnerReportedGold()
    {
        InviteResponseRequest response = RoundTrip<InviteResponseRequest>(new InviteResponseRequest
        {
            SessionId = 7,
            Accepted = true,
            ReportedGold = 53
        });
        OfferUpdateRequest offer = RoundTrip<OfferUpdateRequest>(new OfferUpdateRequest
        {
            SessionId = 7,
            Offer = new TradeOffer { Gold = 150 },
            ReportedGold = 568
        });
        ConfirmRequest confirmation = RoundTrip<ConfirmRequest>(new ConfirmRequest
        {
            SessionId = 7,
            Revision = 4,
            Confirmed = true,
            ReportedGold = 568
        });

        Assert.Equal(53, response.ReportedGold);
        Assert.Equal(568, offer.ReportedGold);
        Assert.Equal(568, confirmation.ReportedGold);
    }

    [Fact]
    public void AssistSmithRequestRoundTripsSelectionIdentity()
    {
        AssistSmithRequest copy = RoundTrip<AssistSmithRequest>(new AssistSmithRequest
        {
            TargetId = 12,
            CardIndex = 7,
            CardId = "Card:Strike",
            UpgradeLevel = 1
        });

        Assert.False(copy.Canceled);
        Assert.Equal((ulong)12, copy.TargetId);
        Assert.Equal(7, copy.CardIndex);
        Assert.Equal("Card:Strike", copy.CardId);
        Assert.Equal(1, copy.UpgradeLevel);
    }

    [Fact]
    public void AssistSmithResultRoundTripsFailure()
    {
        AssistSmithResultEvent copy = RoundTrip<AssistSmithResultEvent>(new AssistSmithResultEvent
        {
            PlayerId = 11,
            CardIndex = -1,
            Error = ModText.Token(TextKey.AssistSmithDeckChanged)
        });

        Assert.False(copy.Success);
        Assert.Equal((ulong)11, copy.PlayerId);
        Assert.Equal(-1, copy.CardIndex);
        Assert.Equal(ModText.Token(TextKey.AssistSmithDeckChanged), copy.Error);
    }

    private static T RoundTrip<T>(T original) where T : IPacketSerializable, new()
    {
        PacketWriter writer = new();
        original.Serialize(writer);
        PacketReader reader = new();
        reader.Reset(writer.Buffer);
        return reader.Read<T>();
    }
}
