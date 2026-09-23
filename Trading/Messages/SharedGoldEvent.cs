using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace BetterMultiplayer.Trading.Messages;

/// <summary>
/// 房主 → 全体：惊喜模式下全队统一的那个金币数。
///
/// 只带一个 int，因为"共享"就意味着所有人持有同一个数。
///
/// ★ 这条消息现在是**兜底**，不是唯一真相来源：两端都会自己算
///   （见 SharedGoldSync 的 Postfix 与 ShouldSync）。它的作用是在某一端
///   因为状态差异算出不一样的值时，把结果拉回房主那一份。
///   （2026-09-23 修正：原文写的是"客户端收到后照抄，权威只在房主手里"，
///    那是 e480ccb 改成"客户端自己算"之前的旧描述，注释漏改了。）
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
