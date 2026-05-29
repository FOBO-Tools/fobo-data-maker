using System.Security.Cryptography;
using Sodium;

namespace DataMaker.Sdk;

/// <summary>
/// Sealed-box crypto for Data Maker submissions, mirroring the desktop's
/// DataMaker.Sync.Shared.SealedBox (libsodium crypto_box_seal — X25519 +
/// XSalsa20-Poly1305) so the form owner decrypts with their X25519 private
/// half. Base64 here is standard-with-padding, matching libsodium's ORIGINAL
/// variant and the JS/Python SDKs.
/// </summary>
public static class SealedBox
{
    private const int PublicKeyBytes = 32;
    private const int SignPublicKeyBytes = 32;
    private const int SignatureBytes = 64;

    /// <summary>Sealed-box encrypt <paramref name="plaintext"/> to the recipient X25519 public key (base64). Returns std-base64 ciphertext.</summary>
    public static string Seal(byte[] plaintext, string recipientPublicKeyBase64)
    {
        var pk = Convert.FromBase64String(recipientPublicKeyBase64);
        if (pk.Length != PublicKeyBytes)
            throw new ArgumentException($"recipient pubkey must be {PublicKeyBytes} bytes, got {pk.Length}");
        return Convert.ToBase64String(SealedPublicKeyBox.Create(plaintext, pk));
    }

    /// <summary>Verify a detached Ed25519 signature over <paramref name="message"/>.</summary>
    public static bool VerifyEd25519(byte[] message, byte[] signature, string signerPublicKeyBase64)
    {
        var pub = Convert.FromBase64String(signerPublicKeyBase64);
        if (pub.Length != SignPublicKeyBytes)
            throw new ArgumentException($"signer.publicKey must be {SignPublicKeyBytes} bytes, got {pub.Length}");
        if (signature.Length != SignatureBytes)
            throw new ArgumentException($"signature must be {SignatureBytes} bytes, got {signature.Length}");
        return PublicKeyAuth.VerifyDetached(signature, message, pub);
    }

    /// <summary>Lowercase-hex SHA-256.</summary>
    public static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
