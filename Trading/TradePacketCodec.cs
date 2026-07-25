using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace BetterMultiplayer.Trading;

internal static class TradePacketCodec
{
    internal static void WriteLocation(PacketWriter writer, TradeLocation location)
    {
        if (!Enum.IsDefined(location))
            throw new InvalidDataException($"Unknown trade location: {location}");
        writer.WriteByte((byte)location);
    }

    internal static TradeLocation ReadLocation(PacketReader reader) => reader.ReadByte() switch
    {
        (byte)TradeLocation.RestSite => TradeLocation.RestSite,
        (byte)TradeLocation.Merchant => TradeLocation.Merchant,
        byte value => throw new InvalidDataException($"Unknown trade location value: {value}")
    };

    internal static void WriteStatus(PacketWriter writer, TradeSessionStatus status)
    {
        if (!Enum.IsDefined(status))
            throw new InvalidDataException($"Unknown trade status: {status}");
        writer.WriteByte((byte)status);
    }

    internal static TradeSessionStatus ReadStatus(PacketReader reader) => reader.ReadByte() switch
    {
        (byte)TradeSessionStatus.Pending => TradeSessionStatus.Pending,
        (byte)TradeSessionStatus.Active => TradeSessionStatus.Active,
        (byte)TradeSessionStatus.Committing => TradeSessionStatus.Committing,
        (byte)TradeSessionStatus.Committed => TradeSessionStatus.Committed,
        (byte)TradeSessionStatus.Canceled => TradeSessionStatus.Canceled,
        byte value => throw new InvalidDataException($"Unknown trade status value: {value}")
    };
}
