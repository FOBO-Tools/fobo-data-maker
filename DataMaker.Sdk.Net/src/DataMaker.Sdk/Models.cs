namespace DataMaker.Sdk;

/// <summary>A choice option (value submitted, label shown).</summary>
public sealed record ChoiceOption(string Value, string Label);

/// <summary>Publisher identity from the .dmf manifest signer block.</summary>
public sealed record SignerInfo(string PublicKey, string? Email, string? Name, string? Company);

/// <summary>
/// A verified FOBO attestation: the publisher's signing key chained to a
/// FOBO-verified email (and, when present, an admin-verified company). Only
/// returned when the signature, version, key match and expiry all check out —
/// so <c>IsVerified</c> is always true on a non-null instance.
/// </summary>
public sealed record FoboAttestation(bool IsVerified, string? Email, string? Company, string? Sub, DateTimeOffset? ExpiresAt);

/// <summary>One submittable field, flattened from form.json.</summary>
public sealed class FieldDescriptor
{
    public required string Id { get; init; }
    /// <summary>Storage key (the field's name) — what the receiver indexes on.</summary>
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Label { get; init; }
    public required string Kind { get; init; }
    public bool Required { get; init; }
    public string? Description { get; init; }
    public string? Placeholder { get; init; }
    public IReadOnlyList<ChoiceOption>? Choices { get; init; }
    public bool AllowCustom { get; init; }
}

/// <summary>
/// Raw .dmf render-bundle entries, present when the bundle was read with
/// <c>includeRenderBundle: true</c>. The ASP.NET renderer assembles the
/// browser form-bundle JSON ({ form, compiled, elementCss, paletteCss }) from
/// these without needing the desktop's FormBundleBuilder.
/// </summary>
public sealed class RenderBundle
{
    public required string FormJson { get; init; }
    public string? CompiledJson { get; init; }
    public string? ElementCssJson { get; init; }
    public string? PaletteCss { get; init; }
    public string? FontsCss { get; init; }
}

/// <summary>A verified .dmf form, ready to submit against.</summary>
public sealed class FormDescriptor
{
    public required string FormId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public string? SubmitPolicy { get; init; }
    public string? RecipientUserId { get; init; }
    public string? RecipientPublicKey { get; init; }
    public SignerInfo? Signer { get; init; }
    /// <summary>The FOBO attestation when present + valid; null = self-signed (no FOBO chain).</summary>
    public FoboAttestation? FoboVerification { get; init; }
    /// <summary>True when a valid FOBO attestation chains the signer's key to a verified email.</summary>
    public bool IsFoboVerified => FoboVerification is not null;
    public int EnvelopeVersion { get; init; }
    public required IReadOnlyList<FieldDescriptor> Fields { get; init; }
    public bool Verified { get; init; }
    public RenderBundle? RenderBundle { get; init; }
}

/// <summary>Result of a successful submission.</summary>
public sealed record SubmitResult(string SubmissionId, string? EditToken, string FormId);
