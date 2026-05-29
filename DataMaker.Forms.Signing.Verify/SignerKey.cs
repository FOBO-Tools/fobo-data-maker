using System.Security.Cryptography;

namespace DataMaker.Forms.Signing;

/// <summary>
/// Verify-side helpers for an Ed25519 signer public key. The signing/keygen
/// counterpart (<c>SigningKeyPair</c>, with the private half) lives in the
/// server-only <c>DataMaker.Forms.Signing</c> assembly — clients only need to
/// size and fingerprint a public key, never to generate or hold a secret one.
/// </summary>
public static class SignerKey
{
    public const int PublicKeyLength = 32;

    /// <summary>
    /// Short, stable, human-readable fingerprint of a public key: first 8 bytes
    /// of SHA-256 as colon-separated hex pairs (e.g. <c>a1:b2:c3:d4:e5:f6:07:18</c>).
    /// Used by the terminal's trust-on-first-use prompt.
    /// </summary>
    public static string FingerprintOf(byte[] publicKey)
    {
        if (publicKey.Length != PublicKeyLength)
            throw new ArgumentException($"Public key must be {PublicKeyLength} bytes.", nameof(publicKey));
        var hash = SHA256.HashData(publicKey);
        return string.Join(':', hash.Take(8).Select(b => b.ToString("x2")));
    }
}
