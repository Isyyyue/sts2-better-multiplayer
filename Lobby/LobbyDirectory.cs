using MegaCrit.Sts2.Core.Multiplayer.Transport.Steam;
using MegaCrit.Sts2.Core.Platform.Steam;
using Steamworks;
using BetterMultiplayer.Localization;

namespace BetterMultiplayer.Lobby;

public sealed record RoomRecord(
    ulong LobbyId,
    string Name,
    string OwnerName,
    bool Locked,
    int PlayerCount,
    int Capacity,
    string EncodedSalt,
    string EncodedVerifier);

internal static class LobbyDirectory
{
    internal static async Task<IReadOnlyList<RoomRecord>> FindByName(
        string roomName,
        CancellationToken cancellationToken = default)
    {
        if (!SteamInitializer.Initialized)
            return [];

        string normalizedName = RoomText.NormalizeRoomName(roomName);

        SteamMatchmaking.AddRequestLobbyListStringFilter(
            RoomSession.KeyProtocol,
            RoomSession.ProtocolVersion,
            ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter(
            RoomSession.KeyOpen,
            "1",
            ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter(
            RoomSession.KeyName,
            normalizedName,
            ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
        SteamMatchmaking.AddRequestLobbyListResultCountFilter(100);

        SteamAPICall_t call = SteamMatchmaking.RequestLobbyList();
        using SteamCallResult<LobbyMatchList_t> result = new(call, cancellationToken);
        LobbyMatchList_t response = await result.Task;

        List<RoomRecord> rooms = new((int)response.m_nLobbiesMatching);
        for (int i = 0; i < response.m_nLobbiesMatching; i++)
        {
            CSteamID lobbyId = SteamMatchmaking.GetLobbyByIndex(i);
            string name = RoomText.NormalizeRoomName(
                SteamMatchmaking.GetLobbyData(lobbyId, RoomSession.KeyName));
            if (name.Length == 0)
                continue;

            CSteamID ownerId = SteamMatchmaking.GetLobbyOwner(lobbyId);
            string ownerName = SteamFriends.GetFriendPersonaName(ownerId);
            if (string.IsNullOrWhiteSpace(ownerName) || ownerName == "[unknown]")
                ownerName = ModText.Get(TextKey.SteamPlayer);

            rooms.Add(new RoomRecord(
                lobbyId.m_SteamID,
                name,
                ownerName,
                SteamMatchmaking.GetLobbyData(lobbyId, RoomSession.KeyLocked) == "1",
                SteamMatchmaking.GetNumLobbyMembers(lobbyId),
                SteamMatchmaking.GetLobbyMemberLimit(lobbyId),
                SteamMatchmaking.GetLobbyData(lobbyId, RoomSession.KeySalt),
                SteamMatchmaking.GetLobbyData(lobbyId, RoomSession.KeyVerifier)));
        }

        return rooms
            .OrderByDescending(room => room.PlayerCount)
            .ThenBy(room => room.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    internal static async Task<RoomRecord?> FindMatching(
        string roomName,
        string password,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RoomRecord> rooms = await FindByName(roomName, cancellationToken);
        return rooms.FirstOrDefault(room => RoomCredentials.Matches(room, roomName, password));
    }

    internal static async Task<bool> HasCredentialCollision(
        string roomName,
        string password,
        CancellationToken cancellationToken = default)
    {
        return await FindMatching(roomName, password, cancellationToken) is not null;
    }
}
