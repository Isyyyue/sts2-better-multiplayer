using System.Security.Cryptography;
using System.Text;

namespace BetterMultiplayer.Security;

public static class PasswordProof
{
    public const int SaltLength = 16;
    public const int KeyLength = 32;
    public const int Iterations = 120_000;
    private static readonly byte[] VerifierMessage = Encoding.UTF8.GetBytes("bettermp-password-verifier-v1");

    public static byte[] CreateSalt() => RandomNumberGenerator.GetBytes(SaltLength);

    public static byte[] DeriveKey(string password, ReadOnlySpan<byte> salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (salt.Length != SaltLength)
            throw new ArgumentException($"Salt must contain {SaltLength} bytes.", nameof(salt));

        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeyLength);
    }

    public static byte[] CreateProof(ReadOnlySpan<byte> key, ulong lobbyId, ulong memberId)
    {
        if (key.Length != KeyLength)
            throw new ArgumentException($"Key must contain {KeyLength} bytes.", nameof(key));

        Span<byte> message = stackalloc byte[24];
        BitConverter.TryWriteBytes(message[..8], lobbyId);
        BitConverter.TryWriteBytes(message.Slice(8, 8), memberId);
        BitConverter.TryWriteBytes(message.Slice(16, 8), 1UL);

        using HMACSHA256 hmac = new(key.ToArray());
        return hmac.ComputeHash(message.ToArray());
    }

    public static byte[] CreateVerifier(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeyLength)
            throw new ArgumentException($"Key must contain {KeyLength} bytes.", nameof(key));

        using HMACSHA256 hmac = new(key.ToArray());
        return hmac.ComputeHash(VerifierMessage);
    }

    public static bool VerifyBase64Proof(
        ReadOnlySpan<byte> key,
        ulong lobbyId,
        ulong memberId,
        string encodedProof)
    {
        byte[] supplied;
        try
        {
            supplied = Convert.FromBase64String(encodedProof);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] expected = CreateProof(key, lobbyId, memberId);
        try
        {
            return supplied.Length == expected.Length &&
                   CryptographicOperations.FixedTimeEquals(supplied, expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(supplied);
        }
    }

    public static bool VerifyBase64Verifier(ReadOnlySpan<byte> key, string encodedVerifier)
    {
        byte[] supplied;
        try
        {
            supplied = Convert.FromBase64String(encodedVerifier);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] expected = CreateVerifier(key);
        try
        {
            return supplied.Length == expected.Length &&
                   CryptographicOperations.FixedTimeEquals(supplied, expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(supplied);
        }
    }

    public static bool TryDecodeSalt(string encodedSalt, out byte[] salt)
    {
        try
        {
            salt = Convert.FromBase64String(encodedSalt);
            return salt.Length == SaltLength;
        }
        catch (FormatException)
        {
            salt = [];
            return false;
        }
    }
}
