namespace DataMaker.Forms.Signing;

/// <summary>
/// On-disk constants for the <c>.dmf</c> (DataMakerForm) signed-bundle
/// format. A <c>.dmf</c> is a ZIP archive containing at least three
/// entries:
///
/// <list type="bullet">
///   <item><see cref="ManifestEntryName"/> — the manifest, listing every
///     other entry with its SHA-256 hash plus signer/recipient metadata.
///     The manifest's UTF-8 bytes are what the publisher's Ed25519 key
///     signs over (see <c>DmfWriter</c> in the server-side signing assembly).</item>
///   <item><see cref="SignatureEntryName"/> — 64 raw bytes of detached
///     Ed25519 signature, no encoding.</item>
///   <item><see cref="FormEntryName"/> — UTF-8 JSON serialization of the
///     <see cref="DataMaker.Schema.Forms.Form"/>. Future bundles can carry
///     more entries (assets, localizations, fonts) — the manifest's file
///     list is the source of truth for what's covered by the signature.</item>
/// </list>
///
/// <para>
/// The signing scheme intentionally signs the manifest, not the zip's
/// central directory: zip layout is producer-dependent (timestamps,
/// compression level, ordering) and not byte-stable, while the manifest
/// JSON is a single canonical artefact under our control. Every other
/// file in the bundle is bound by being listed in the manifest with its
/// SHA-256 — tamper with any covered file and the hash check fails before
/// the signature is even consulted.
/// </para>
/// </summary>
public static class DmfFormat
{
    /// <summary>File extension used when writing <c>.dmf</c> bundles to disk.</summary>
    public const string FileExtension = ".dmf";

    /// <summary>MIME type for HTTP transport of <c>.dmf</c> bundles.</summary>
    public const string MimeType = "application/vnd.datamaker.form";

    /// <summary>Current envelope version. Bump on breaking format changes.</summary>
    public const int CurrentEnvelopeVersion = 3;

    public const string ManifestEntryName   = "manifest.json";
    public const string SignatureEntryName  = "signature.bin";
    public const string FormEntryName       = "form.json";
    public const string CompiledEntryName   = "compiled.json";
    public const string ElementCssEntryName = "elementCss.json";
    public const string PaletteEntryName    = "palette.css";
    public const string FontsEntryName      = "fonts.css";

    /// <summary>SHA-256 produces 32 bytes; we serialise as lowercase hex (64 chars).</summary>
    public const int Sha256HexLength = 64;
}
