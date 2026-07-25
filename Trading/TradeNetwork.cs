using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace BetterMultiplayer.Trading;

internal static class TradeNetwork
{
    internal static ulong LocalPlayerId => RunManager.Instance.NetService.NetId;
    internal static bool IsHost => RunManager.Instance.NetService.Type == NetGameType.Host;

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
