using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace BetterMultiplayer.Trading.Messages;

public sealed class AvailabilityRequest : ICustomMessage
{
    public bool Available { get; set; }
    public TradeLocation Location { get; set; }
    public int ReportedGold { get; set; }
    public bool ShouldBroadcast => false;

    public void HandleMessage(ulong senderId)
    {
        if (TradeNetwork.IsHost)
            TradeCoordinator.SetAvailable(senderId, Available, Location, ReportedGold);
    }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteBool(Available);
        TradePacketCodec.WriteLocation(writer, Location);
        writer.WriteInt(ReportedGold);
    }

    public void Deserialize(PacketReader reader)
    {
        Available = reader.ReadBool();
        Location = TradePacketCodec.ReadLocation(reader);
        ReportedGold = reader.ReadInt();
    }
}

public sealed class InviteRequest : ICustomMessage
{
    public ulong TargetId { get; set; }
    public bool ShouldBroadcast => false;

    public void HandleMessage(ulong senderId)
    {
        if (TradeNetwork.IsHost)
            TradeCoordinator.Invite(senderId, TargetId);
    }

    public void Serialize(PacketWriter writer) => writer.WriteULong(TargetId);
    public void Deserialize(PacketReader reader) => TargetId = reader.ReadULong();
}

public sealed class InviteResponseRequest : ICustomMessage
{
    public ulong SessionId { get; set; }
    public bool Accepted { get; set; }
    public int ReportedGold { get; set; }
    public bool ShouldBroadcast => false;

    public void HandleMessage(ulong senderId)
    {
        if (TradeNetwork.IsHost)
            TradeCoordinator.RespondToInvite(senderId, SessionId, Accepted, ReportedGold);
    }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(SessionId);
        writer.WriteBool(Accepted);
        writer.WriteInt(ReportedGold);
    }

    public void Deserialize(PacketReader reader)
    {
        SessionId = reader.ReadULong();
        Accepted = reader.ReadBool();
        ReportedGold = reader.ReadInt();
    }
}

public sealed class OfferUpdateRequest : ICustomMessage
{
    public ulong SessionId { get; set; }
    public TradeOffer Offer { get; set; } = new();
    public int ReportedGold { get; set; }
    public bool ShouldBroadcast => false;

    public void HandleMessage(ulong senderId)
    {
        if (TradeNetwork.IsHost)
            TradeCoordinator.UpdateOffer(senderId, SessionId, Offer, ReportedGold);
    }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(SessionId);
        writer.Write(Offer);
        writer.WriteInt(ReportedGold);
    }

    public void Deserialize(PacketReader reader)
    {
        SessionId = reader.ReadULong();
        Offer = reader.Read<TradeOffer>();
        ReportedGold = reader.ReadInt();
    }
}

public sealed class ConfirmRequest : ICustomMessage
{
    public ulong SessionId { get; set; }
    public int Revision { get; set; }
    public bool Confirmed { get; set; }
    public int ReportedGold { get; set; }
    public bool ShouldBroadcast => false;

    public void HandleMessage(ulong senderId)
    {
        if (TradeNetwork.IsHost)
            TradeCoordinator.Confirm(senderId, SessionId, Revision, Confirmed, ReportedGold);
    }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(SessionId);
        writer.WriteInt(Revision);
        writer.WriteBool(Confirmed);
        writer.WriteInt(ReportedGold);
    }

    public void Deserialize(PacketReader reader)
    {
        SessionId = reader.ReadULong();
        Revision = reader.ReadInt();
        Confirmed = reader.ReadBool();
        ReportedGold = reader.ReadInt();
    }
}

public sealed class CancelTradeRequest : ICustomMessage
{
    public ulong SessionId { get; set; }
    public bool ShouldBroadcast => false;

    public void HandleMessage(ulong senderId)
    {
        if (TradeNetwork.IsHost)
            TradeCoordinator.Cancel(senderId, SessionId);
    }

    public void Serialize(PacketWriter writer) => writer.WriteULong(SessionId);
    public void Deserialize(PacketReader reader) => SessionId = reader.ReadULong();
}

public sealed class AvailabilityEvent : ICustomMessage
{
    public ulong PlayerId { get; set; }
    public bool Available { get; set; }
    public TradeLocation Location { get; set; }
    public bool ShouldBroadcast => false;

    public void HandleMessage(ulong senderId)
    {
        if (TradeNetwork.IsHostSender(senderId))
        {
            if (!Available && Location == TradeLocation.RestSite)
                TradeRestSiteFlow.Complete(PlayerId, success: false);
            TradeStateStore.SetAvailability(PlayerId, Available, Location);
        }
    }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(PlayerId);
        writer.WriteBool(Available);
        TradePacketCodec.WriteLocation(writer, Location);
    }

    public void Deserialize(PacketReader reader)
    {
        PlayerId = reader.ReadULong();
        Available = reader.ReadBool();
        Location = TradePacketCodec.ReadLocation(reader);
    }
}

public sealed class SessionEvent : ICustomMessage
{
    public TradeSessionSnapshot Snapshot { get; set; } = new();
    public bool ShouldBroadcast => false;

    public void HandleMessage(ulong senderId)
    {
        if (TradeNetwork.IsHostSender(senderId))
        {
            TradeStateStore.SetSession(Snapshot);
        }
    }

    public void Serialize(PacketWriter writer) => writer.Write(Snapshot);
    public void Deserialize(PacketReader reader) => Snapshot = reader.Read<TradeSessionSnapshot>();
}

public sealed class CommitEvent : ICustomMessage
{
    public TradeSessionSnapshot Snapshot { get; set; } = new();
    public bool ShouldBroadcast => false;

    public void HandleMessage(ulong senderId)
    {
        if (TradeNetwork.IsHostSender(senderId))
            TradeStateStore.ApplyCommit(Snapshot);
    }

    public void Serialize(PacketWriter writer) => writer.Write(Snapshot);
    public void Deserialize(PacketReader reader) => Snapshot = reader.Read<TradeSessionSnapshot>();
}

public sealed class TradeErrorEvent : ICustomMessage
{
    public ulong TargetId { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool ShouldBroadcast => false;

    public void HandleMessage(ulong senderId)
    {
        if (TradeNetwork.IsHostSender(senderId) && TargetId == TradeNetwork.LocalPlayerId)
            TradeStateStore.SetError(Message);
    }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(TargetId);
        writer.WriteString(Message.Length > 160 ? Message[..160] : Message);
    }

    public void Deserialize(PacketReader reader)
    {
        TargetId = reader.ReadULong();
        Message = reader.ReadString();
    }
}
