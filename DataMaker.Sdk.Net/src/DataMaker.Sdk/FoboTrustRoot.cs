using System.Text;
using System.Text.Json;

namespace DataMaker.Sdk;

/// <summary>
/// Verifies a publisher's FOBO attestation — the Ed25519-signed binding of
/// their signing key to a FOBO-verified email (+ admin-verified company) — so
/// a sender can show "Verified by FOBO" / where a submission's recipient is.
/// Mirrors the desktop's DataMaker.Forms.Signing.FoboTrustRoot; the root pubkey
/// is duplicated here intentionally (sender-side, offline-verifiable).
/// </summary>
public static class FoboTrustRoot
{
    /// <summary>FOBO root Ed25519 pubkey (base64). Fingerprint 50:be:66:c4:fb:a6:b0:12.</summary>
    public const string PublicKeyBase64 = "K2XDkrQ3vn5FSzbohodSMGiSrfomXg9/bgfczFxEGh4=";

    private const int MinVersion = 1;
    private const int MaxVersion = 2;

    /// <summary>
    /// Verify the manifest's <c>signer.foboAttestation</c> object against the
    /// root pubkey. Returns the attested identity on success, null otherwise
    /// (bad signature, wrong/unknown version, pubkey mismatch, expired, or
    /// malformed). Failure is not fatal — the form is still self-signed-usable.
    /// </summary>
    public static FoboAttestation? Verify(JsonElement attestation, string signerPublicKeyBase64, DateTimeOffset? now = null)
    {
        if (attestation.ValueKind != JsonValueKind.Object) return null;
        var payloadJson = Str(attestation, "payloadJson");
        var sigB64      = Str(attestation, "signatureBase64");
        if (payloadJson is null || sigB64 is null) return null;

        byte[] sig;
        try { sig = Convert.FromBase64String(sigB64); }
        catch { return null; }

        bool ok;
        try { ok = SealedBox.VerifyEd25519(Encoding.UTF8.GetBytes(payloadJson), sig, PublicKeyBase64); }
        catch { return null; }
        if (!ok) return null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(payloadJson); }
        catch { return null; }

        using (doc)
        {
            var p = doc.RootElement;
            var ver = p.TryGetProperty("attestationVersion", out var v) && v.TryGetInt32(out var n) ? n : 0;
            if (ver < MinVersion || ver > MaxVersion) return null;

            // FOBO must vouch for the same key that signed the form.
            if (!string.Equals(Str(p, "subjectPublicKey"), signerPublicKeyBase64, StringComparison.Ordinal))
                return null;

            DateTimeOffset? expires = p.TryGetProperty("expiresAt", out var e) && e.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(e.GetString(), out var dt) ? dt : null;
            if (expires is { } x && (now ?? DateTimeOffset.UtcNow) >= x) return null;

            return new FoboAttestation(
                IsVerified: true,
                Email:      Str(p, "subjectEmail"),
                Company:    Str(p, "subjectCompany"),
                Sub:        Str(p, "subjectSub"),
                ExpiresAt:  expires);
        }
    }

    private static string? Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
