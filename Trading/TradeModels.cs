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
    public bool LockedA { get; set; }
    public bool LockedB { get; set; }
    public int OfferRevisionA { get; set; }
    public int OfferRevisionB { get; set; }
    public int LockedRevisionA { get; set; } = -1;
    public int LockedRevisionB { get; set; } = -1;
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
    public bool IsLocked(ulong playerId) => playerId == PlayerA ? LockedA : LockedB;
    public bool IsOtherLocked(ulong playerId) => playerId == PlayerA ? LockedB : LockedA;
    public int OfferRevisionFor(ulong playerId) => playerId == PlayerA ? OfferRevisionA : OfferRevisionB;
    public int LockedRevisionFor(ulong playerId) => playerId == PlayerA ? LockedRevisionA : LockedRevisionB;
    public void BumpOfferRevision(ulong playerId)
    {
        if (playerId == PlayerA) OfferRevisionA++;
        else if (playerId == PlayerB) OfferRevisionB++;
        else throw new ArgumentOutOfRangeException(nameof(playerId));
    }
    public void SetLocked(ulong playerId, bool locked, int revision)
    {
        if (playerId == PlayerA)
        {
            LockedA = ConfirmedA = locked;
            LockedRevisionA = locked ? revision : -1;
        }
        else if (playerId == PlayerB)
        {
            LockedB = ConfirmedB = locked;
            LockedRevisionB = locked ? revision : -1;
        }
        else throw new ArgumentOutOfRangeException(nameof(playerId));
    }

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
        LockedA = LockedA,
        LockedB = LockedB,
        OfferRevisionA = OfferRevisionA,
        OfferRevisionB = OfferRevisionB,
        LockedRevisionA = LockedRevisionA,
        LockedRevisionB = LockedRevisionB,
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
        writer.WriteBool(LockedA || ConfirmedA);
        writer.WriteBool(LockedB || ConfirmedB);
        writer.WriteInt(OfferRevisionA);
        writer.WriteInt(OfferRevisionB);
        writer.WriteInt(LockedRevisionA);
        writer.WriteInt(LockedRevisionB);
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
        LockedA = ConfirmedA = reader.ReadBool();
        LockedB = ConfirmedB = reader.ReadBool();
        OfferRevisionA = reader.ReadInt();
        OfferRevisionB = reader.ReadInt();
        LockedRevisionA = reader.ReadInt();
        LockedRevisionB = reader.ReadInt();
        Status = TradePacketCodec.ReadStatus(reader);
        Location = TradePacketCodec.ReadLocation(reader);
    }
}

