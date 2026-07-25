using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace BetterMultiplayer.Trading;

public enum TradeLocation : byte
{
    RestSite,
    Merchant
}

public enum TradeSessionStatus : byte
{
    Pending,
    Active,
    Committing,
    Committed,
    Canceled
}

public sealed class TradeOffer : IPacketSerializable
{
    public List<int> CardIndices { get; set; } = [];
    public List<int> RelicIndices { get; set; } = [];
    public List<int> PotionSlotIndices { get; set; } = [];
    public int Gold { get; set; }

    public TradeOffer Clone() => new()
    {
        CardIndices = [.. CardIndices],
        RelicIndices = [.. RelicIndices],
        PotionSlotIndices = [.. PotionSlotIndices],
        Gold = Gold
    };

    public TradeOffer Normalized() => new()
    {
        CardIndices = CardIndices.Distinct().Order().ToList(),
        RelicIndices = RelicIndices.Distinct().Order().ToList(),
        PotionSlotIndices = PotionSlotIndices.Distinct().Order().ToList(),
        Gold = Gold
    };

    public void Serialize(PacketWriter writer)
    {
        WriteIndices(writer, CardIndices);
        WriteIndices(writer, RelicIndices);
        WriteIndices(writer, PotionSlotIndices);
        writer.WriteInt(Gold);
    }

    public void Deserialize(PacketReader reader)
    {
        CardIndices = ReadIndices(reader);
        RelicIndices = ReadIndices(reader);
        PotionSlotIndices = ReadIndices(reader);
        Gold = reader.ReadInt();
    }

    private static void WriteIndices(PacketWriter writer, IReadOnlyList<int> indices)
    {
        writer.WriteByte((byte)Math.Min(indices.Count, byte.MaxValue));
        foreach (int index in indices.Take(byte.MaxValue))
            writer.WriteUShort(checked((ushort)Math.Clamp(index, 0, ushort.MaxValue)));
    }

    private static List<int> ReadIndices(PacketReader reader)
    {
        int count = reader.ReadByte();
        List<int> indices = new(count);
        for (int i = 0; i < count; i++)
            indices.Add(reader.ReadUShort());
        return indices;
    }
}

public sealed class TradeSessionSnapshot : IPacketSerializable
{
    public ulong SessionId { get; set; }
    public ulong PlayerA { get; set; }
    public ulong PlayerB { get; set; }
    public int Revision { get; set; }
    public int GoldA { get; set; }
    public int GoldB { get; set; }
    public TradeOffer OfferA { get; set; } = new();
    public TradeOffer OfferB { get; set; } = new();
    public bool ConfirmedA { get; set; }
    public bool ConfirmedB { get; set; }
    public TradeSessionStatus Status { get; set; }
    public TradeLocation Location { get; set; }

    public bool Contains(ulong playerId) => PlayerA == playerId || PlayerB == playerId;
    public ulong OtherPlayer(ulong playerId) => playerId == PlayerA ? PlayerB : PlayerA;
    public TradeOffer OfferFor(ulong playerId) => playerId == PlayerA ? OfferA : OfferB;
    public TradeOffer OtherOffer(ulong playerId) => playerId == PlayerA ? OfferB : OfferA;
    public int GoldFor(ulong playerId) => playerId == PlayerA ? GoldA : GoldB;
    public void SetGold(ulong playerId, int gold)
    {
        if (playerId == PlayerA)
            GoldA = gold;
        else if (playerId == PlayerB)
            GoldB = gold;
        else
            throw new ArgumentOutOfRangeException(nameof(playerId));
    }
    public bool IsConfirmed(ulong playerId) => playerId == PlayerA ? ConfirmedA : ConfirmedB;
    public bool IsOtherConfirmed(ulong playerId) => playerId == PlayerA ? ConfirmedB : ConfirmedA;

    public TradeSessionSnapshot Clone() => new()
    {
        SessionId = SessionId,
        PlayerA = PlayerA,
        PlayerB = PlayerB,
        Revision = Revision,
        GoldA = GoldA,
        GoldB = GoldB,
        OfferA = OfferA.Clone(),
        OfferB = OfferB.Clone(),
        ConfirmedA = ConfirmedA,
        ConfirmedB = ConfirmedB,
        Status = Status,
        Location = Location
    };

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(SessionId);
        writer.WriteULong(PlayerA);
        writer.WriteULong(PlayerB);
        writer.WriteInt(Revision);
        writer.WriteInt(GoldA);
        writer.WriteInt(GoldB);
        writer.Write(OfferA);
        writer.Write(OfferB);
        writer.WriteBool(ConfirmedA);
        writer.WriteBool(ConfirmedB);
        TradePacketCodec.WriteStatus(writer, Status);
        TradePacketCodec.WriteLocation(writer, Location);
    }

    public void Deserialize(PacketReader reader)
    {
        SessionId = reader.ReadULong();
        PlayerA = reader.ReadULong();
        PlayerB = reader.ReadULong();
        Revision = reader.ReadInt();
        GoldA = reader.ReadInt();
        GoldB = reader.ReadInt();
        OfferA = reader.Read<TradeOffer>();
        OfferB = reader.Read<TradeOffer>();
        ConfirmedA = reader.ReadBool();
        ConfirmedB = reader.ReadBool();
        Status = TradePacketCodec.ReadStatus(reader);
        Location = TradePacketCodec.ReadLocation(reader);
    }
}
