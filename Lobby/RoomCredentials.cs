using System.Security.Cryptography;
using BetterMultiplayer.Security;

namespace BetterMultiplayer.Lobby;

internal static class RoomCredentials
{
    internal static bool Matches(RoomRecord room, string roomName, string password)
    {
        string normalizedName = RoomText.NormalizeRoomName(roomName);
        if (!string.Equals(room.Name, normalizedName, StringComparison.Ordinal) ||
            password.Length == 0 ||
            !RoomText.IsValidPassword(password) ||
            !PasswordProof.TryDecodeSalt(room.EncodedSalt, out byte[] salt))
        {
            return false;
        }

        byte[] key = PasswordProof.DeriveKey(password, salt);
        try
        {
            return PasswordProof.VerifyBase64Verifier(key, room.EncodedVerifier);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(salt);
        }
    }
}
