using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataMaker.Forms.Signing;

/// <summary>
/// FOBO-issued certificate binding a publisher's Ed25519 signing public
/// key to their FOBO-verified email + Cognito sub. Issued by the sync
/// Lambda after the user PUTs their key with a valid bearer token; the
/// Lambda extracts the email + sub from the JWT and signs the binding
/// with FOBO's Ed25519 root key.
///
/// <para>
/// <b>Wire shape:</b> the payload is carried as an opaque UTF-8 JSON
/// string (<see cref="PayloadJson"/>) and the signature is over those
/// exact bytes — same trick as the legacy <c>SignedForm</c> envelope, for
/// the same reason: avoids cross-serializer canonicalisation footguns.
/// </para>
///
/// <para>
/// <b>Trust:</b> verifiers (<see cref="FoboTrustRoot.Verify"/>) check the
/// signature against FOBO's hardcoded root pubkey. A successful verify
/// promotes the claimed email from "self-asserted display text" to
/// "FOBO-verified anchor", which the trust prompt reflects with an
/// explicit "Verified by FOBO" badge.
/// </para>
/// </summary>
public sealed record FoboAttestation(
    [property: JsonPropertyName("payloadJson")]     string PayloadJson,
    [property: JsonPropertyName("signatureBase64")] string SignatureBase64)
{
    /// <summary>Parse the embedded payload. Throws if <see cref="PayloadJson"/> is malformed.</summary>
    public FoboAttestationPayload ParsePayload()
    {
        return JsonSerializer.Deserialize(PayloadJson, SigningJsonContext.Default.FoboAttestationPayload)
               ?? throw new InvalidDataException("Attestation payload deserialised to null.");
    }
}

/// <summary>
/// Inside-of-the-signature shape. The Lambda builds this object, JSON-
/// serialises it once, signs the UTF-8 bytes with FOBO root, and sends
/// both back to the publisher. The publisher caches it locally and
/// embeds it in every <c>.dmf</c> they sign while it's still valid.
/// </summary>
public sealed record FoboAttestationPayload(
    [property: JsonPropertyName("attestationVersion")] int AttestationVersion,
    [property: JsonPropertyName("subjectPublicKey")]   string SubjectPublicKeyBase64,
    [property: JsonPropertyName("subjectSub")]         string SubjectSub,
    [property: JsonPropertyName("subjectEmail")]       string? SubjectEmail,
    [property: JsonPropertyName("issuedAt")]           DateTimeOffset IssuedAt,
    [property: JsonPropertyName("expiresAt")]          DateTimeOffset ExpiresAt)
{
    /// <summary>Current attestation version. Bump only on payload-shape changes.</summary>
    public const int CurrentVersion = 1;

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
