using System.Text.Json.Serialization;

namespace DataMaker.Forms.Signing;

/// <summary>
/// Human-readable identity claimed by the publisher of a <c>.dmf</c> form.
/// Embedded in the signed manifest, so a given keypair can't have its
/// claimed identity swapped without invalidating the signature — but the
/// identity itself is still self-asserted unless paired with a
/// <see cref="FoboAttestation"/> chaining the publisher's pubkey to a
/// FOBO-verified email.
///
/// <para>
/// <see cref="Email"/> is the only field that carries trust weight, and
/// only when accompanied by a valid attestation. <see cref="Name"/> and
/// <see cref="Company"/> are display sugar — useful for the trust-prompt
/// UI, not security-bearing.
/// </para>
/// </summary>
public sealed record SignerIdentity(
    [property: JsonPropertyName("name")]    string? Name,
    [property: JsonPropertyName("company")] string? Company,
    [property: JsonPropertyName("email")]   string? Email)
{
    public static SignerIdentity Empty { get; } = new(null, null, null);

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Name)    &&
        string.IsNullOrWhiteSpace(Company) &&
        string.IsNullOrWhiteSpace(Email);
}
