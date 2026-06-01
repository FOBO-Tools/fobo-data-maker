using System.Text;

namespace DataMaker.Forms.Signing;

/// <summary>
/// FOBO's Ed25519 root pubkey, baked into the app + terminal at build
/// time. The corresponding private key lives in AWS Secrets Manager and
/// only ever leaves the sync Lambda's process memory; there's no way to
/// issue a valid <see cref="FoboAttestation"/> without it.
///
/// <para>
/// Verifiers use <see cref="Verify"/> to promote a publisher's claimed
/// email from "self-asserted" to "FOBO-verified". Failing the verify is
/// not fatal — the form still renders under self-signed semantics — but
/// the trust prompt UI must NOT show the "Verified by FOBO" badge unless
/// this method returns true.
/// </para>
///
/// <para>
/// <b>Rotating this pubkey is a breaking change:</b> a new root means
/// previously issued attestations stop verifying. Plan for a transition
/// window where the verifier accepts both keys.
/// </para>
/// </summary>
public static class FoboTrustRoot
{
    /// <summary>
    /// FOBO root pubkey (32-byte Ed25519, base64). The corresponding
    /// private key lives only in AWS Secrets Manager under
    /// <c>DataMaker/FoboRoot</c> (eu-west-1) and is fetched by the sync
    /// Lambda at cold-start via <c>SecretsService</c>; it MUST NOT live
    /// anywhere else in the deployment — not env vars, not the SAM
    /// template, not a repo file. Rotate by generating a fresh pair
    /// (rerun <c>bootstrap-fobo-root-secret</c> after deleting the old
    /// secret), baking the new pubkey here, and deploying apps + Lambda
    /// together. Old attestations stop verifying on rotation — plan a
    /// transition window if that ever matters.
    ///
    /// <para>
    /// Fingerprint: <c>50:be:66:c4:fb:a6:b0:12</c> (verify locally with
    /// <c>SigningKeyPair.FingerprintOf</c> after base64-decoding).
    /// </para>
    /// </summary>
    public const string PublicKeyBase64 = "K2XDkrQ3vn5FSzbohodSMGiSrfomXg9/bgfczFxEGh4=";

    /// <summary>
    /// Verify a <see cref="FoboAttestation"/> against the embedded root
    /// pubkey. Returns the parsed payload on success; null otherwise.
    /// Reasons for failure (signature mismatch, expired, payload won't
    /// parse) are NOT separated out — the consumer only needs to know
    /// whether to render the "Verified by FOBO" badge.
    /// </summary>
    public static FoboAttestationPayload? Verify(
        FoboAttestation attestation,
        string subjectPublicKeyBase64,
        DateTimeOffset? now = null)
    {
        var rootPubKey = Convert.FromBase64String(PublicKeyBase64);
        if (IsZero(rootPubKey)) return null; // sentinel — no attestation auth in this build

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(attestation.SignatureBase64);
        }
        catch (FormatException)
        {
            return null;
        }

        var payloadBytes = Encoding.UTF8.GetBytes(attestation.PayloadJson);

        bool ok;
        try
        {
            ok = Sodium.PublicKeyAuth.VerifyDetached(signature, payloadBytes, rootPubKey);
        }
        catch
        {
            return null;
        }

        if (!ok) return null;

        FoboAttestationPayload payload;
        try
        {
            payload = attestation.ParsePayload();
        }
        catch
        {
            return null;
        }

        // Accept any known version in [MinSupported, Current]. A v1 payload
        // simply carries no company; a version above what we understand is
        // rejected rather than trusted blind.
        if (payload.AttestationVersion is < FoboAttestationPayload.MinSupportedVersion
                                        or > FoboAttestationPayload.CurrentVersion)
            return null;

        // Subject pubkey in the attestation must match the publisher
        // we're verifying — otherwise FOBO is vouching for a different
        // key than the one that signed the form.
        if (!string.Equals(payload.SubjectPublicKeyBase64, subjectPublicKeyBase64, StringComparison.Ordinal))
            return null;

        if (payload.IsExpired(now ?? DateTimeOffset.UtcNow)) return null;

        return payload;
    }

    private static bool IsZero(byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
            if (bytes[i] != 0) return false;
        return true;
    }
}
