using System.Security.Cryptography;
using BetterMultiplayer.Security;
using BetterMultiplayer.Localization;
using Steamworks;

namespace BetterMultiplayer.Lobby;

internal static class RoomSession
{
    internal const string ProtocolVersion = "6";
    internal const string KeyProtocol = "bettermp.protocol";
    internal const string KeyName = "bettermp.name";
    internal const string KeyLocked = "bettermp.locked";
    internal const string KeySalt = "bettermp.salt";
    internal const string KeyVerifier = "bettermp.verifier";
    internal const string KeyOpen = "bettermp.open";
    internal const string KeyModVersion = "bettermp.version";
    internal const string KeyProof = "bettermp.proof";

    private sealed record HostConfig(string Name, byte[] Salt, byte[] Key)
    {
        public bool Locked => Key.Length > 0;
    }

    private static HostConfig? _pending;
    private static HostConfig? _active;

    internal static ulong? ActiveLobbyId { get; private set; }
    internal static bool HasPending => _pending is not null;

    internal static bool BeginHosting(string roomName, string password, out string error)
    {
        string normalizedName = RoomText.NormalizeRoomName(roomName);
        if (normalizedName.Length == 0)
        {
            error = ModText.Token(TextKey.EnterRoomName);
            return false;
        }

        if (password.Length == 0)
        {
            error = ModText.Token(TextKey.EnterRoomPassword);
            return false;
        }

        if (!RoomText.IsValidPassword(password))
        {
            error = ModText.Token(TextKey.InvalidPassword);
            return false;
        }

        ClearConfig(ref _pending);
        byte[] salt = PasswordProof.CreateSalt();
        byte[] key = PasswordProof.DeriveKey(password, salt);
        _pending = new HostConfig(normalizedName, salt, key);
        error = string.Empty;
        return true;
    }

    internal static void HostStarted(CSteamID lobbyId)
    {
        if (_pending is null)
            return;

        ClearConfig(ref _active);
        _active = _pending;
        _pending = null;
        ActiveLobbyId = lobbyId.m_SteamID;

        SteamMatchmaking.SetLobbyType(lobbyId, ELobbyType.k_ELobbyTypePublic);
        SteamMatchmaking.SetLobbyJoinable(lobbyId, true);
        SteamMatchmaking.SetLobbyData(lobbyId, KeyProtocol, ProtocolVersion);
        SteamMatchmaking.SetLobbyData(lobbyId, KeyName, _active.Name);
        SteamMatchmaking.SetLobbyData(lobbyId, KeyLocked, _active.Locked ? "1" : "0");
        SteamMatchmaking.SetLobbyData(lobbyId, KeySalt, Convert.ToBase64String(_active.Salt));
        byte[] verifier = PasswordProof.CreateVerifier(_active.Key);
        try
        {
            SteamMatchmaking.SetLobbyData(lobbyId, KeyVerifier, Convert.ToBase64String(verifier));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verifier);
        }
        SteamMatchmaking.SetLobbyData(lobbyId, KeyOpen, "1");
        SteamMatchmaking.SetLobbyData(lobbyId, KeyModVersion, BetterMultiplayerMod.Version);
    }

    internal static void SetClosed(CSteamID lobbyId, bool closed)
    {
        if (ActiveLobbyId != lobbyId.m_SteamID)
            return;

        SteamMatchmaking.SetLobbyData(lobbyId, KeyOpen, closed ? "0" : "1");
        SteamMatchmaking.SetLobbyJoinable(lobbyId, !closed);
        SteamMatchmaking.SetLobbyType(
            lobbyId,
            closed ? ELobbyType.k_ELobbyTypePrivate : ELobbyType.k_ELobbyTypePublic);
    }

    internal static bool VerifyMember(CSteamID lobbyId, ulong memberId)
    {
        if (ActiveLobbyId != lobbyId.m_SteamID || _active is null)
            return true;
        if (!_active.Locked)
            return true;

        string proof = SteamMatchmaking.GetLobbyMemberData(lobbyId, new CSteamID(memberId), KeyProof);
        return PasswordProof.VerifyBase64Proof(_active.Key, lobbyId.m_SteamID, memberId, proof);
    }

    internal static void HostFailed()
    {
        ClearConfig(ref _pending);
    }

    internal static void CancelPending()
    {
        ClearConfig(ref _pending);
    }

    internal static void Clear()
    {
        ClearConfig(ref _pending);
        ClearConfig(ref _active);
        ActiveLobbyId = null;
    }

    private static void ClearConfig(ref HostConfig? config)
    {
        if (config is not null)
        {
            CryptographicOperations.ZeroMemory(config.Key);
            CryptographicOperations.ZeroMemory(config.Salt);
        }
        config = null;
    }
}
