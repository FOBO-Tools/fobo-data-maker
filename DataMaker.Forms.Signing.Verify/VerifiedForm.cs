using DataMaker.Schema.Forms;

namespace DataMaker.Forms.Signing;

/// <summary>
/// Successful return of <see cref="DmfReader.Read(byte[])"/>. Carries
/// the parsed form plus everything callers need to surface a trust
/// prompt and (when <see cref="RecipientPublicKey"/> is set) submit a
/// sealed-box reply.
///
/// <para>
/// <see cref="IsFoboVerified"/> is the load-bearing trust signal — the
/// publisher's claimed email is only treated as authoritative when
/// <see cref="FoboVerification"/> is non-null. Everything else on
/// <see cref="SignerIdentity"/> is display sugar.
/// </para>
/// </summary>
public sealed record VerifiedForm(
    Form Form,
    byte[] SignerPublicKey,
    string SignerFingerprint,
    SignerIdentity SignerIdentity,
    FoboAttestationPayload? FoboVerification,
    byte[]? RecipientPublicKey,
    string? RecipientUserId,
    DateTimeOffset SignedAt)
{
    /// <summary>True when a valid FOBO attestation chains the signer's pubkey to a verified email.</summary>
    public bool IsFoboVerified => FoboVerification is not null;

    /// <summary>The trust-bearing email when FOBO-verified, otherwise the claimed (unverified) email.</summary>
    public string? DisplayEmail => FoboVerification?.SubjectEmail ?? SignerIdentity.Email;

    /// <summary>True if the form expects sealed-box submissions back to a FOBO user.</summary>
    public bool AcceptsSubmissions => RecipientPublicKey is not null && !string.IsNullOrEmpty(RecipientUserId);

    /// <summary>
    /// Verified, hash-checked auxiliary entries shipped alongside <c>form.json</c>
    /// in the bundle (envelope v3+). Keyed by manifest path — e.g.
    /// <see cref="DmfFormat.CompiledEntryName"/> carries the JS-compiled
    /// expression map, <see cref="DmfFormat.ElementCssEntryName"/> carries the
    /// per-element resolved-style CSS map. Empty for v2 bundles.
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> Extras { get; init; }
        = new Dictionary<string, byte[]>();
}
