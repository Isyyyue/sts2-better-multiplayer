using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace BetterMultiplayer.Trading;

internal static class TradeNetwork
{
    internal static ulong LocalPlayerId => RunManager.Instance.NetService.NetId;

    /// <summary>
    /// 是否房主。
    ///
    /// ★ 这里必须兜底，不能裸访问。原因：
    /// SharedGoldSync 挂在 Player.Gold 的 setter 上，而金币在关卡加载、
    /// 玩家初始化时就会被赋值——那时 RunManager / NetService 可能还没就绪。
    /// 裸访问会抛异常，异常从 setter 里冒出去就会打断游戏的加载流程，
    /// 表现就是"开了惊喜模式，联机进去卡死"。
    ///
    /// 读的是同一个东西的 DiagnosticRecorder.CanReadHost 一直带着 try/catch，
    /// 这里与它保持一致。异常时返回 false，让调用方走"非房主"分支。
    /// </summary>
    internal static bool IsHost
    {
        get
        {
            try
            {
                return RunManager.Instance.NetService.Type == NetGameType.Host;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static bool IsHostSender(ulong senderId)
    {
        INetGameService service = RunManager.Instance.NetService;
        return service.Type switch
        {
            NetGameType.Host => senderId == service.NetId,
            NetGameType.Client when service is NetClientGameService client => senderId == client.HostNetId,
            _ => false
        };
    }

    internal static void SendRequest(ICustomMessage request)
    {
        if (IsHost)
            request.HandleMessage(LocalPlayerId);
        else
            CustomMessageWrapper.Send(request);
    }

    internal static void Broadcast(ICustomMessage message, bool applyLocally = true)
    {
        if (!IsHost)
            throw new InvalidOperationException("Only the host may broadcast trade events.");

        if (applyLocally)
            message.HandleMessage(LocalPlayerId);
        RunManager.Instance.NetService.SendMessage(new CustomMessageWrapper { Message = message });
    }
}
