using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace BetterMultiplayer.Trading.Messages;

/// <summary>
/// 房主 → 全体：惊喜模式下全队统一的那个金币数。
///
/// 只带一个 int，因为"共享"就意味着所有人持有同一个数。
/// 客户端收到后照抄，不自己判断惊喜模式开没开——权威只在房主手里，
/// 两边设置不一致也不会让金币跑偏。
/// </summary>
public sealed class SharedGoldEvent : ICustomMessage
{
    public int Gold { get; set; }

    public bool ShouldBroadcast => false;

    public void HandleMessage(ulong senderId)
    {
        if (TradeNetwork.IsHostSender(senderId))
            SharedGoldSync.ApplyFromHost(Gold);
    }

    public void Serialize(PacketWriter writer) => writer.WriteInt(Gold);

    public void Deserialize(PacketReader reader) => Gold = reader.ReadInt();
}
