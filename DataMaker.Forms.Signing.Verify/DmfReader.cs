using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DataMaker.Schema;
using DataMaker.Schema.Forms;
using DataMaker.Schema.Layout;

namespace DataMaker.Forms.Signing;

/// <summary>
/// Thrown when a <c>.dmf</c> bundle fails verification or structural
/// validation. Callers should surface this as a clear "this form was
/// not signed by who it claims" message — distinct from generic file
/// I/O or zip corruption.
/// </summary>
public sealed class InvalidDmfException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Verifies a <c>.dmf</c> bundle and returns the typed
/// <see cref="VerifiedForm"/> inside. The verification chain is:
/// <list type="number">
///   <item>Open the zip; require manifest, signature, and form entries.</item>
///   <item>Parse the manifest just to read the signer pubkey + envelope
///         version. (We never trust deserialised values for crypto —
///         the signed bytes are the source of truth, but we need the
///         pubkey out of them somehow.)</item>
///   <item>Verify the Ed25519 signature against the manifest's
///         <i>raw zip-entry bytes</i> using the publisher's pubkey.</item>
///   <item>For every file in the manifest's <c>files</c> list, hash the
///         matching zip entry and compare to the manifest's value.</item>
///   <item>If a <see cref="FoboAttestation"/> is present, run it
///         through <see cref="FoboTrustRoot.Verify"/> and surface the
///         result on <see cref="VerifiedForm.IsFoboVerified"/>.</item>
///   <item>Deserialise <c>form.json</c> and return.</item>
/// </list>
/// </summary>
public static class DmfReader
{
    /// <summary>
    /// Form-deserialization options. Source-gen <see cref="SchemaJsonContext"/>
    /// is the resolver (so the trimmer keeps every Form/Field/Layout accessor
    /// statically), and <see cref="ColumnJsonConverter"/> handles the
    /// polymorphic field/group split — its own implementation calls
    /// <c>options.GetTypeInfo</c>, which finds the source-gen entries
    /// registered on this options instance.
    /// </summary>
    private static readonly JsonSerializerOptions FormOpts = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = SchemaJsonContext.Default,
        Converters       = { new ColumnJsonConverter() },
    };

    private static JsonTypeInfo<Form> FormTypeInfo =>
        (JsonTypeInfo<Form>)FormOpts.GetTypeInfo(typeof(Form));

    public static VerifiedForm Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var ms = new MemoryStream(bytes);
        return Read(ms);
    }

    public static VerifiedForm Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDmfException("File is not a valid zip archive — is this really a .dmf?", ex);
        }

        using (zip)
        {
            var manifestBytes  = ReadEntryBytes(zip, DmfFormat.ManifestEntryName);
            var signatureBytes = ReadEntryBytes(zip, DmfFormat.SignatureEntryName);

            // Parse the manifest — but only to extract the signer pubkey
            // and the file list. The actual signed payload is the raw
            // bytes; we never compute the signature against deserialised
            // structure (that would invite canonicalisation issues).
            DmfManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize(manifestBytes, SigningJsonContext.Default.DmfManifest)
                           ?? throw new InvalidDmfException("Manifest deserialised to null.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDmfException("Manifest is not valid JSON.", ex);
            }

            if (manifest.EnvelopeVersion != DmfFormat.CurrentEnvelopeVersion)
                throw new InvalidDmfException(
                    $"Unsupported envelope version {manifest.EnvelopeVersion}; this client understands v{DmfFormat.CurrentEnvelopeVersion}.");

            byte[] signerPubKey;
            try
            {
                signerPubKey = Convert.FromBase64String(manifest.Signer.PublicKeyBase64);
            }
            catch (FormatException ex)
            {
                throw new InvalidDmfException("Signer public key isn't valid base64.", ex);
            }

            if (signerPubKey.Length != SignerKey.PublicKeyLength)
                throw new InvalidDmfException(
                    $"Signer public key must be {SignerKey.PublicKeyLength} bytes, got {signerPubKey.Length}.");

            // ── Signature check over the manifest's raw bytes.
            bool sigOk;
            try
            {
                sigOk = Sodium.PublicKeyAuth.VerifyDetached(signatureBytes, manifestBytes, signerPubKey);
            }
            catch (Exception ex)
            {
                throw new InvalidDmfException("Signature verification threw.", ex);
            }
            if (!sigOk)
                throw new InvalidDmfException("Manifest signature is invalid — bytes were tampered with or the signer key doesn't match.");

            // ── Verify every covered file's SHA-256. form.json gets pulled
            //    aside for the typed parse below; every other manifest entry
            //    (envelope v3+ ships compiled.json + elementCss.json) is
            //    handed back to the caller as a verified bytes blob.
            byte[]? formBytes = null;
            var extras = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var file in manifest.Files)
            {
                var fileBytes = ReadEntryBytes(zip, file.Path);
                if (fileBytes.LongLength != file.Size)
                    throw new InvalidDmfException(
                        $"File '{file.Path}' has size {fileBytes.LongLength}, manifest declared {file.Size}.");

                var actualHex = ToHex(SHA256.HashData(fileBytes));
                if (!string.Equals(actualHex, file.Sha256Hex, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDmfException(
                        $"File '{file.Path}' SHA-256 doesn't match the manifest. Bundle was tampered with.");

                if (file.Path == DmfFormat.FormEntryName) formBytes = fileBytes;
                else                                       extras[file.Path] = fileBytes;
            }

            if (formBytes is null)
                throw new InvalidDmfException(
                    $"Manifest doesn't declare a '{DmfFormat.FormEntryName}' entry — bundle has no form payload.");

            // ── FOBO attestation chain (optional).
            FoboAttestationPayload? foboPayload = null;
            if (manifest.Signer.FoboAttestation is { } attestation)
                foboPayload = FoboTrustRoot.Verify(attestation, manifest.Signer.PublicKeyBase64);

            // ── Parse the form last. Order matters: a corrupted form is
            //    a different error from a tampered bundle, and the
            //    crypto checks above must succeed first to give a
            //    meaningful message.
            Form form;
            try
            {
                form = JsonSerializer.Deserialize(formBytes, FormTypeInfo)
                       ?? throw new InvalidDmfException($"{DmfFormat.FormEntryName} deserialised to null.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDmfException($"{DmfFormat.FormEntryName} is not a valid Form payload.", ex);
            }

            byte[]? recipientPubKey = null;
            if (manifest.Recipient is { } r)
            {
                try { recipientPubKey = Convert.FromBase64String(r.PublicKeyBase64); }
                catch (FormatException ex) { throw new InvalidDmfException("Recipient public key isn't valid base64.", ex); }
            }

            return new VerifiedForm(
                Form:              form,
                SignerPublicKey:   signerPubKey,
                SignerFingerprint: SignerKey.FingerprintOf(signerPubKey),
                SignerIdentity:    manifest.Signer.Identity ?? SignerIdentity.Empty,
                FoboVerification:  foboPayload,
                RecipientPublicKey: recipientPubKey,
                RecipientUserId:   manifest.Recipient?.UserId,
                SignedAt:          manifest.SignedAt)
            {
                Extras = extras,
            };
        }
    }

    /// <summary>
    /// Non-throwing variant. Returns false + an error message on any
    /// crypto / format failure; structurally-bad input (non-zip, missing
    /// entries) and tampered bundles are both surfaced through the same
    /// channel since the user's recovery is the same: get a fresh copy.
    /// </summary>
    public static bool TryRead(byte[] bytes, out VerifiedForm? form, out string? error)
    {
        try
        {
            form  = Read(bytes);
            error = null;
            return true;
        }
        catch (InvalidDmfException ex)
        {
            form  = null;
            error = ex.Message;
            return false;
        }
    }

    // ── helpers ────────────────────────────────────────────────────

    private static byte[] ReadEntryBytes(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path)
                    ?? throw new InvalidDmfException($"Bundle is missing required entry '{path}'.");
        using var stream = entry.Open();
        using var ms = new MemoryStream(checked((int)entry.Length));
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        for (var i = 0; i < bytes.Length; i++)
            sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }
}
