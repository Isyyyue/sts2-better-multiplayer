using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace BetterMultiplayer.Trading.Messages;

public sealed class AssistSmithRequest : ICustomMessage
{
    public bool Canceled { get; set; }
    public ulong TargetId { get; set; }
    public int CardIndex { get; set; }
    public string CardId { get; set; } = string.Empty;
    public int UpgradeLevel { get; set; }
    public bool ShouldBroadcast => false;

    public void HandleMessage(ulong senderId)
    {
        if (TradeNetwork.IsHost)
            AssistSmithCoordinator.Resolve(
                senderId,
                Canceled,
                TargetId,
                CardIndex,
                CardId,
                UpgradeLevel);
    }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteBool(Canceled);
        writer.WriteULong(TargetId);
        writer.WriteInt(CardIndex);
        writer.WriteString(CardId.Length > 120 ? CardId[..120] : CardId);
        writer.WriteInt(UpgradeLevel);
    }

    public void Deserialize(PacketReader reader)
    {
        Canceled = reader.ReadBool();
        TargetId = reader.ReadULong();
        CardIndex = reader.ReadInt();
        CardId = reader.ReadString();
        UpgradeLevel = reader.ReadInt();
    }
}

public sealed class AssistSmithResultEvent : ICustomMessage
{
    public ulong PlayerId { get; set; }
    public bool Success { get; set; }
    public ulong TargetId { get; set; }
    public int CardIndex { get; set; }
    public string CardId { get; set; } = string.Empty;
    public int UpgradeLevel { get; set; }
    public string Error { get; set; } = string.Empty;
    public bool ShouldBroadcast => false;

    public void HandleMessage(ulong senderId)
    {
        if (!TradeNetwork.IsHostSender(senderId))
            return;

        AssistSmithFlow.Complete(
            PlayerId,
            new AssistSmithResult(
                Success,
                TargetId,
                CardIndex,
                CardId,
                UpgradeLevel,
                Error));
    }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(PlayerId);
        writer.WriteBool(Success);
        writer.WriteULong(TargetId);
        writer.WriteInt(CardIndex);
        writer.WriteString(CardId.Length > 120 ? CardId[..120] : CardId);
        writer.WriteInt(UpgradeLevel);
        writer.WriteString(Error.Length > 160 ? Error[..160] : Error);
    }

    public void Deserialize(PacketReader reader)
    {
        PlayerId = reader.ReadULong();
        Success = reader.ReadBool();
        TargetId = reader.ReadULong();
        CardIndex = reader.ReadInt();
        CardId = reader.ReadString();
        UpgradeLevel = reader.ReadInt();
        Error = reader.ReadString();
    }
}
