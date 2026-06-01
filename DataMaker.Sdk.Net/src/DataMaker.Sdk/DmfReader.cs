using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DataMaker.Sdk;

/// <summary>
/// Reader/verifier for the .dmf signed-bundle format
/// (DataMaker.Forms.Signing.DmfFormat). A .dmf is a ZIP of manifest.json (the
/// Ed25519-signed bytes), signature.bin, form.json, plus optional render assets
/// bound by SHA-256 in manifest.files[]. Verifies the manifest signature and
/// that form.json matches its signed hash, then returns a descriptor to submit
/// against.
/// </summary>
public static class DmfReader
{
    private const string ManifestEntry = "manifest.json";
    private const string SignatureEntry = "signature.bin";
    private const string FormEntry = "form.json";
    private const string CompiledEntry = "compiled.json";
    private const string ElementCssEntry = "elementCss.json";
    private const string PaletteEntry = "palette.css";
    private const string FontsEntry = "fonts.css";

    /// <summary>
    /// Parse + verify a .dmf. <paramref name="verify"/> gates the Ed25519 +
    /// form-hash checks (disable only for trusted local fixtures).
    /// <paramref name="includeRenderBundle"/> also reads compiled/elementCss/
    /// palette/fonts entries for the ASP.NET renderer.
    /// </summary>
    public static FormDescriptor Read(byte[] dmfBytes, bool verify = true, bool includeRenderBundle = false)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(new MemoryStream(dmfBytes), ZipArchiveMode.Read);
        }
        catch (Exception e)
        {
            throw new DmfException($"not a readable .dmf archive: {e.Message}");
        }

        using (zip)
        {
            var manifestBytes = ReadEntry(zip, ManifestEntry) ?? throw new DmfException(".dmf is missing manifest.json");
            var formBytes = ReadEntry(zip, FormEntry) ?? throw new DmfException(".dmf is missing form.json");

            using var manifestDoc = ParseJson(manifestBytes, "manifest.json");
            var manifest = manifestDoc.RootElement;

            if (verify)
            {
                var signatureBytes = ReadEntry(zip, SignatureEntry) ?? throw new DmfException(".dmf is missing signature.bin");
                VerifySignature(manifest, manifestBytes, signatureBytes);
                VerifyFormHash(manifest, formBytes);
            }

            using var formDoc = ParseJson(formBytes, "form.json");
            var form = formDoc.RootElement;

            var recipient = manifest.TryGetProperty("recipient", out var r) && r.ValueKind == JsonValueKind.Object ? r : (JsonElement?)null;
            var signer = manifest.TryGetProperty("signer", out var s) && s.ValueKind == JsonValueKind.Object ? s : (JsonElement?)null;

            // FOBO attestation (optional) — its own chain, verified against the
            // root pubkey independent of the manifest-signature `verify` flag.
            // Null when absent or it fails to verify.
            FoboAttestation? fobo = null;
            if (signer is { } sgFobo && GetString(sgFobo, "publicKey") is { } signerPub
                && sgFobo.TryGetProperty("foboAttestation", out var att) && att.ValueKind == JsonValueKind.Object)
            {
                fobo = FoboTrustRoot.Verify(att, signerPub);
            }

            RenderBundle? bundle = null;
            if (includeRenderBundle)
            {
                bundle = new RenderBundle
                {
                    FormJson = Encoding.UTF8.GetString(formBytes),
                    CompiledJson = ReadEntryText(zip, CompiledEntry),
                    ElementCssJson = ReadEntryText(zip, ElementCssEntry),
                    PaletteCss = ReadEntryText(zip, PaletteEntry),
                    FontsCss = ReadEntryText(zip, FontsEntry),
                };
            }

            return new FormDescriptor
            {
                FormId = GetString(form, "id") ?? "",
                Name = GetString(form, "name") ?? "",
                Description = GetString(form, "description"),
                SchemaVersion = GetInt(form, "schemaVersion") ?? 1,
                SubmitPolicy = GetString(form, "submitPolicy"),
                RecipientUserId = recipient is { } rec ? GetString(rec, "userId") : null,
                RecipientPublicKey = recipient is { } rec2 ? GetString(rec2, "publicKey") : null,
                Signer = signer is { } sg
                    ? new SignerInfo(
                        GetString(sg, "publicKey") ?? "",
                        sg.TryGetProperty("identity", out var id) && id.ValueKind == JsonValueKind.Object ? GetString(id, "email") : null,
                        sg.TryGetProperty("identity", out var id2) && id2.ValueKind == JsonValueKind.Object ? GetString(id2, "name") : null,
                        sg.TryGetProperty("identity", out var id3) && id3.ValueKind == JsonValueKind.Object ? GetString(id3, "company") : null)
                    : null,
                FoboVerification = fobo,
                EnvelopeVersion = GetInt(manifest, "envelopeVersion") ?? 0,
                Fields = ExtractFields(form),
                Verified = verify,
                RenderBundle = bundle,
            };
        }
    }

    private static void VerifySignature(JsonElement manifest, byte[] manifestBytes, byte[] signatureBytes)
    {
        if (!manifest.TryGetProperty("signer", out var signer) || GetString(signer, "publicKey") is not { } pub)
            throw new DmfException(".dmf manifest.signer.publicKey is missing — cannot verify");
        bool ok;
        try { ok = SealedBox.VerifyEd25519(manifestBytes, signatureBytes, pub); }
        catch (Exception e) { throw new DmfException(e.Message); }
        if (!ok)
            throw new DmfException(".dmf signature is invalid — bundle was tampered with or signer key is wrong");
    }

    private static void VerifyFormHash(JsonElement manifest, byte[] formBytes)
    {
        var digest = SealedBox.Sha256Hex(formBytes);
        if (!manifest.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            throw new DmfException(".dmf manifest does not list files — bundle is malformed");
        foreach (var f in files.EnumerateArray())
        {
            if (GetString(f, "path") == FormEntry)
            {
                if (GetString(f, "sha256") != digest)
                    throw new DmfException(".dmf form.json hash mismatch — bundle was tampered with");
                return;
            }
        }
        throw new DmfException(".dmf manifest does not list form.json — bundle is malformed");
    }

    private static IReadOnlyList<FieldDescriptor> ExtractFields(JsonElement form)
    {
        var list = new List<FieldDescriptor>();
        if (!form.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var f in fields.EnumerateArray())
        {
            var name = GetString(f, "name") ?? "";
            List<ChoiceOption>? choices = null;
            var allowCustom = false;
            if (f.TryGetProperty("choice", out var choice) && choice.ValueKind == JsonValueKind.Object)
            {
                allowCustom = choice.TryGetProperty("allowCustom", out var ac) && ac.ValueKind == JsonValueKind.True;
                if (choice.TryGetProperty("choices", out var cs) && cs.ValueKind == JsonValueKind.Array)
                {
                    choices = new List<ChoiceOption>();
                    foreach (var c in cs.EnumerateArray())
                    {
                        var v = GetString(c, "value") ?? "";
                        choices.Add(new ChoiceOption(v, GetString(c, "label") ?? v));
                    }
                }
            }

            list.Add(new FieldDescriptor
            {
                Id = GetString(f, "id") ?? "",
                Key = name,
                Name = name,
                Label = GetString(f, "label") ?? name,
                Kind = GetString(f, "kind") ?? "",
                Required = f.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.True,
                Description = GetString(f, "description"),
                Placeholder = GetString(f, "placeholder"),
                Choices = choices,
                AllowCustom = allowCustom,
            });
        }
        return list;
    }

    private static byte[]? ReadEntry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry is null) return null;
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string? ReadEntryText(ZipArchive zip, string name)
    {
        var bytes = ReadEntry(zip, name);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    private static JsonDocument ParseJson(byte[] bytes, string label)
    {
        try { return JsonDocument.Parse(bytes); }
        catch (Exception e) { throw new DmfException($".dmf {label} is not valid JSON: {e.Message}"); }
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
}
