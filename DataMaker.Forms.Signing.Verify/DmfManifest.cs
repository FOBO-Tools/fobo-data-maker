using System.Text.Json.Serialization;

namespace DataMaker.Forms.Signing;

/// <summary>
/// The signed payload of a <c>.dmf</c> bundle. One UTF-8 JSON file,
/// stored as <see cref="DmfFormat.ManifestEntryName"/> in the zip; its
/// exact bytes are what the publisher's Ed25519 key signs over. Every
/// other file in the bundle is bound by appearing in <see cref="Files"/>
/// with its SHA-256 — tamper with any covered byte and the verifier
/// rejects the bundle before it reaches the form parser.
/// </summary>
public sealed record DmfManifest(
    [property: JsonPropertyName("envelopeVersion")] int EnvelopeVersion,
    [property: JsonPropertyName("signedAt")]        DateTimeOffset SignedAt,
    [property: JsonPropertyName("signer")]          DmfSigner Signer,
    [property: JsonPropertyName("recipient")]       DmfRecipient? Recipient,
    [property: JsonPropertyName("files")]           IReadOnlyList<DmfFileEntry> Files);

/// <summary>
/// Signer block. <see cref="PublicKeyBase64"/> is the only mandatory
/// field — that's the Ed25519 key the manifest signature verifies
/// against. Identity (<see cref="SignerIdentity"/>) is claimed unless
/// chained to FOBO via <see cref="FoboAttestation"/>; in the latter case
/// <c>Identity.Email</c> is promoted to "verified" by the trust prompt.
/// </summary>
public sealed record DmfSigner(
    [property: JsonPropertyName("publicKey")]       string PublicKeyBase64,
    [property: JsonPropertyName("identity")]        SignerIdentity? Identity,
    [property: JsonPropertyName("foboAttestation")] FoboAttestation? FoboAttestation);

/// <summary>
/// Optional recipient block. When present, the form is intended for
/// sealed-box submissions back to the listed FOBO user — submitters
/// (terminal, future web client) encrypt against
/// <see cref="PublicKeyBase64"/> and route via <see cref="UserId"/>.
/// When absent, the form is share-only (template / preview / archive).
/// </summary>
public sealed record DmfRecipient(
    [property: JsonPropertyName("publicKey")] string PublicKeyBase64,
    [property: JsonPropertyName("userId")]    string UserId);

/// <summary>
/// One covered file. The verifier reads the matching zip entry, hashes
/// it, and compares to <see cref="Sha256Hex"/>; mismatch fails the
/// bundle. Paths are stored verbatim — keep them simple, lowercase, no
/// leading slash, forward-slash separators.
/// </summary>
public sealed record DmfFileEntry(
    [property: JsonPropertyName("path")]   string Path,
    [property: JsonPropertyName("sha256")] string Sha256Hex,
    [property: JsonPropertyName("size")]   long Size);
