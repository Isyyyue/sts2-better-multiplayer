using System.Security.Cryptography;
using BetterMultiplayer.Security;
using BetterMultiplayer.Localization;
using Steamworks;

namespace BetterMultiplayer.Lobby;

internal static class JoinContext
{
    private static ulong? _lobbyId;
    private static byte[]? _key;

    internal static bool Begin(RoomRecord room, string password, out string error)
    {
        Clear();

        if (password.Length == 0 || !RoomText.IsValidPassword(password))
        {
            error = ModText.Token(TextKey.RoomNotFound);
            return false;
        }
        if (!PasswordProof.TryDecodeSalt(room.EncodedSalt, out byte[] salt))
        {
            error = ModText.Token(TextKey.RoomNotFound);
            return false;
        }

        try
        {
            _key = PasswordProof.DeriveKey(password, salt);
            if (!PasswordProof.VerifyBase64Verifier(_key, room.EncodedVerifier))
            {
                Clear();
                error = ModText.Token(TextKey.RoomNotFound);
                return false;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
        }

        _lobbyId = room.LobbyId;
        error = string.Empty;
        return true;
    }

    internal static bool SetMemberProof(ulong lobbyId, ulong memberId)
    {
        if (_lobbyId != lobbyId)
            return false;

        string proof = string.Empty;
        if (_key is not null)
        {
            byte[] proofBytes = PasswordProof.CreateProof(_key, lobbyId, memberId);
            try
            {
                proof = Convert.ToBase64String(proofBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(proofBytes);
            }
        }

        CSteamID steamLobbyId = new(lobbyId);
        CSteamID steamMemberId = new(memberId);
        SteamMatchmaking.SetLobbyMemberData(steamLobbyId, RoomSession.KeyProof, proof);
        return SteamMatchmaking.GetLobbyMemberData(steamLobbyId, steamMemberId, RoomSession.KeyProof) == proof;
    }

    internal static void Clear()
    {
        if (_key is not null)
            CryptographicOperations.ZeroMemory(_key);
        _key = null;
        _lobbyId = null;
    }
}
