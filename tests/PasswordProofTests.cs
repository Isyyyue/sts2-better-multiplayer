using System.Security.Cryptography;
using BetterMultiplayer.Security;

namespace BetterMultiplayer.Tests;

public sealed class PasswordProofTests
{
    [Fact]
    public void SamePasswordAndSaltProduceSameKey()
    {
        byte[] salt = PasswordProof.CreateSalt();
        byte[] first = PasswordProof.DeriveKey("正确的密码-123", salt);
        byte[] second = PasswordProof.DeriveKey("正确的密码-123", salt);

        Assert.Equal(PasswordProof.SaltLength, salt.Length);
        Assert.Equal(PasswordProof.KeyLength, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ProofIsBoundToLobbyAndSteamMember()
    {
        byte[] salt = PasswordProof.CreateSalt();
        byte[] key = PasswordProof.DeriveKey("复杂密码-A9!", salt);
        string proof = Convert.ToBase64String(PasswordProof.CreateProof(key, 1001, 2002));

        Assert.True(PasswordProof.VerifyBase64Proof(key, 1001, 2002, proof));
        Assert.False(PasswordProof.VerifyBase64Proof(key, 1002, 2002, proof));
        Assert.False(PasswordProof.VerifyBase64Proof(key, 1001, 2003, proof));
    }

    [Fact]
    public void WrongPasswordAndMalformedProofAreRejected()
    {
        byte[] salt = PasswordProof.CreateSalt();
        byte[] correctKey = PasswordProof.DeriveKey("correct", salt);
        byte[] wrongKey = PasswordProof.DeriveKey("wrong", salt);
        string proof = Convert.ToBase64String(PasswordProof.CreateProof(correctKey, 1, 2));

        Assert.False(PasswordProof.VerifyBase64Proof(wrongKey, 1, 2, proof));
        Assert.False(PasswordProof.VerifyBase64Proof(correctKey, 1, 2, "not-base64"));
    }

    [Fact]
    public void InvalidSaltLengthIsRejected()
    {
        string shortSalt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(4));

        Assert.False(PasswordProof.TryDecodeSalt(shortSalt, out _));
        Assert.False(PasswordProof.TryDecodeSalt("%%%", out _));
    }

    [Fact]
    public void VerifierAcceptsOnlyThePasswordDerivedKey()
    {
        byte[] salt = PasswordProof.CreateSalt();
        byte[] correctKey = PasswordProof.DeriveKey("room-password", salt);
        byte[] wrongKey = PasswordProof.DeriveKey("other-password", salt);
        string verifier = Convert.ToBase64String(PasswordProof.CreateVerifier(correctKey));

        Assert.True(PasswordProof.VerifyBase64Verifier(correctKey, verifier));
        Assert.False(PasswordProof.VerifyBase64Verifier(wrongKey, verifier));
        Assert.False(PasswordProof.VerifyBase64Verifier(correctKey, "not-base64"));
    }
}
