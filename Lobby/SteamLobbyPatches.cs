using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Transport.Steam;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Runs;
using Steamworks;
using StsSteamClient = MegaCrit.Sts2.Core.Multiplayer.Transport.Steam.SteamClient;
using BetterMultiplayer.Trading;

namespace BetterMultiplayer.Lobby;

[HarmonyPatch(typeof(SteamHost), nameof(SteamHost.StartHost))]
internal static class SteamHostStartPatch
{
    [HarmonyPostfix]
    private static void Postfix(SteamHost __instance, ref Task<NetErrorInfo?> __result)
    {
        if (RoomSession.HasPending)
            __result = CompleteStart(__result, __instance);
    }

    private static async Task<NetErrorInfo?> CompleteStart(Task<NetErrorInfo?> original, SteamHost host)
    {
        NetErrorInfo? error = await original;
        if (!error.HasValue && host.LobbyId.HasValue)
            RoomSession.HostStarted(host.LobbyId.Value);
        else
            RoomSession.HostFailed();
        return error;
    }
}

[HarmonyPatch(typeof(SteamHost), nameof(SteamHost.SetHostIsClosed))]
internal static class SteamHostVisibilityPatch
{
    [HarmonyPrefix]
    private static bool Prefix(SteamHost __instance, bool isClosed)
    {
        if (!__instance.LobbyId.HasValue || RoomSession.ActiveLobbyId != __instance.LobbyId.Value.m_SteamID)
            return true;

        RoomSession.SetClosed(__instance.LobbyId.Value, isClosed);
        return false;
    }
}

[HarmonyPatch(typeof(SteamHost), nameof(SteamHost.StopHost))]
internal static class SteamHostStopPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        PasswordGatePatch.Reset();
        RoomSession.Clear();
    }
}

[HarmonyPatch(typeof(StsSteamClient), nameof(StsSteamClient.ConnectToLobby))]
internal static class SteamClientProofPatch
{
    [HarmonyPostfix]
    private static void Postfix(StsSteamClient __instance, ulong lobbyId, ref Task<NetErrorInfo?> __result)
    {
        __result = SetProofAfterConnection(__result, __instance, lobbyId);
    }

    private static async Task<NetErrorInfo?> SetProofAfterConnection(
        Task<NetErrorInfo?> original,
        StsSteamClient client,
        ulong lobbyId)
    {
        NetErrorInfo? error = await original;
        if (!error.HasValue && !JoinContext.SetMemberProof(lobbyId, client.NetId))
            BetterMultiplayerMod.Logger.Warn($"Could not write the password proof to Steam lobby {lobbyId}");
        return error;
    }
}

[HarmonyPatch(typeof(StartRunLobby), "HandleClientLobbyJoinRequestMessage")]
internal static class PasswordGatePatch
{
    private const float ProofWaitSeconds = 5f;
    private static readonly HashSet<ulong> PendingPlayers = [];
    private static readonly HashSet<ulong> AuthorizedPlayers = [];
    private static CancellationTokenSource _lifetime = new();

    [HarmonyPrefix]
    private static bool Prefix(
        StartRunLobby __instance,
        ClientLobbyJoinRequestMessage message,
        ulong senderId)
    {
        if (__instance.NetService is not NetHostGameService host ||
            host.NetHost is not SteamHost steamHost ||
            !steamHost.LobbyId.HasValue ||
            RoomSession.ActiveLobbyId != steamHost.LobbyId.Value.m_SteamID)
        {
            return true;
        }

        if (AuthorizedPlayers.Remove(senderId))
            return true;

        if (RoomSession.VerifyMember(steamHost.LobbyId.Value, senderId))
            return true;

        if (PendingPlayers.Add(senderId))
        {
            BetterMultiplayerMod.Logger.Info($"Waiting for room password proof from player {senderId}");
            TaskHelper.RunSafely(WaitForProof(
                __instance,
                host,
                steamHost.LobbyId.Value,
                message,
                senderId,
                _lifetime.Token));
        }
        return false;
    }

    internal static void Reset()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _lifetime = new CancellationTokenSource();
        PendingPlayers.Clear();
        AuthorizedPlayers.Clear();
    }

    private static async Task WaitForProof(
        StartRunLobby lobby,
        NetHostGameService host,
        CSteamID lobbyId,
        ClientLobbyJoinRequestMessage message,
        ulong senderId,
        CancellationToken cancellationToken)
    {
        try
        {
            float elapsed = 0f;
            while (elapsed < ProofWaitSeconds && IsConnected(host, senderId))
            {
                if (RoomSession.VerifyMember(lobbyId, senderId))
                {
                    PendingPlayers.Remove(senderId);
                    AuthorizedPlayers.Add(senderId);
                    BetterMultiplayerMod.Logger.Info($"Room password verification succeeded for player {senderId}");
                    AccessTools.Method(typeof(StartRunLobby), "HandleClientLobbyJoinRequestMessage")
                        .Invoke(lobby, [message, senderId]);
                    return;
                }

                NGame? game = NGame.Instance;
                if (game is null)
                    break;
                elapsed += await game.AwaitProcessFrame(cancellationToken);
            }

            if (IsConnected(host, senderId))
            {
                BetterMultiplayerMod.Logger.Warn($"Room password verification timed out or failed for player {senderId}");
                host.DisconnectClient(senderId, NetError.InvalidJoin, now: true);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            PendingPlayers.Remove(senderId);
            AuthorizedPlayers.Remove(senderId);
        }
    }

    private static bool IsConnected(NetHostGameService host, ulong playerId) =>
        host.ConnectedPeers.Any(peer => peer.peerId == playerId);
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
internal static class MultiplayerStateCleanupPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        JoinContext.Clear();
        PasswordGatePatch.Reset();
        RoomSession.Clear();
        TradeCoordinator.Reset();
    }
}

[HarmonyPatch(typeof(RunLobby), "OnDisconnectedFromClientAsHost")]
internal static class TradeDisconnectCleanupPatch
{
    [HarmonyPostfix]
    private static void Postfix(ulong playerId)
    {
        if (TradeNetwork.IsHost)
            TradeCoordinator.PlayerDisconnected(playerId);
    }
}
