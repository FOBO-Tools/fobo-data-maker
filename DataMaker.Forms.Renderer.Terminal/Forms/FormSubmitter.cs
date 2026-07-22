using System.Buffers;
using System.Text.Json;
using System.Net.Http.Headers;
using DataMaker.Forms.Signing;
using DataMaker.Schema;
using DataMaker.Schema.Fields;
using DataMaker.Sdk;
using FOBO.Auth;

namespace DataMaker.Forms.Renderer.Terminal.Forms;

/// <summary>
/// Packs a completed form-fill into a submission and POSTs it to the configured
/// sync API — through the public <see cref="DataMakerClient"/> SDK, not the
/// server-shared internals. The terminal is just another SDK client, so its
/// submit path is the same subset any third-party integrator ships.
///
/// <para>
/// Encryption is sealed-box: the submitter only encrypts against the
/// recipient's X25519 public key carried in the signed form envelope, never
/// learning the private key. The wire format is identical to every other
/// submitter (web renderer, JS/Python SDKs).
/// </para>
/// </summary>
internal static class FormSubmitter
{
    public static async Task<SubmitResult> SubmitAsync(
        VerifiedForm form,
        FormState state,
        string submitEndpoint,
        HttpClient http,
        TokenSet? bearer = null,
        CancellationToken ct = default)
    {
        // The UI gates submit on a recipient being present; treat its absence
        // as a programming error surfaced as a user-visible failure.
        if (form.RecipientPublicKey is null || form.RecipientUserId is null)
            return SubmitResult.Fail("Form has no recipient — submissions are not supported.");

        // FormVersion MUST match the .dmf the submitter actually filled — the
        // receiver looks the version up in form_versions and quarantines (#18b)
        // on a miss. Fields are left empty because validation already ran in the
        // UI (OnSubmit); we seal the snapshot as-is via SubmitOptions.Validate=false.
        var descriptor = new FormDescriptor
        {
            FormId = form.Form.Id,
            Name = form.Form.Name ?? string.Empty,
            SchemaVersion = form.Form.SchemaVersion,
            RecipientPublicKey = Convert.ToBase64String(form.RecipientPublicKey),
            RecipientUserId = form.RecipientUserId,
            Fields = Array.Empty<FieldDescriptor>(),
        };

        // Authenticated forms carry a FOBO bearer — apply it to the HttpClient
        // the SDK posts with (the SDK takes the client, not a per-call header).
        if (bearer is not null)
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer.AccessToken);

        var client = new DataMakerClient(http, submitEndpoint);
        try
        {
            var result = await client.SubmitAsync(descriptor, NormalizeForWire(state.Snapshot()), new SubmitOptions { Validate = false }, ct);
            return SubmitResult.Ok(result.SubmissionId);
        }
        catch (SubmissionException ex)
        {
            var snippet = ex.Body is { Length: > 200 } b ? b[..200] + "…" : ex.Body;
            return SubmitResult.Fail($"HTTP {ex.Status}: {snippet}");
        }
        catch (DataMakerException ex)
        {
            return SubmitResult.Fail($"Failed to build submission: {ex.Message}");
        }
        catch (Exception ex)
        {
            return SubmitResult.Fail($"Network error: {ex.Message}");
        }
    }

    // Schema value types (SignatureRef / ImageRef / AttachmentRef / Geo) aren't
    // known to the standalone SDK's source-gen JSON context, so handing one to
    // the SDK throws "no JsonTypeInfo metadata for SignatureRef". Pre-serialise
    // each via its own converter to a JsonElement (which the SDK context now
    // accepts) — so the wire JSON is exactly the shape the receiver parses.
    private static readonly SignatureRefJsonConverter  SigConv = new();
    private static readonly ImageRefJsonConverter      ImgConv = new();
    private static readonly AttachmentRefJsonConverter AttConv = new();
    private static readonly JsonSerializerOptions      WireOpts = new(JsonSerializerDefaults.Web);

    private static IReadOnlyDictionary<string, object?> NormalizeForWire(IReadOnlyDictionary<string, object?> snapshot)
    {
        Dictionary<string, object?>? copy = null;
        foreach (var kv in snapshot)
        {
            var wire = ToWire(kv.Value);
            if (!ReferenceEquals(wire, kv.Value))
                (copy ??= new Dictionary<string, object?>(snapshot))[kv.Key] = wire;
        }
        return copy ?? snapshot;
    }

    private static object? ToWire(object? v) => v switch
    {
        SignatureRef s  => Element(w => SigConv.Write(w, s, WireOpts)),
        ImageRef i      => Element(w => ImgConv.Write(w, i, WireOpts)),
        AttachmentRef a => Element(w => AttConv.Write(w, a, WireOpts)),
        Geo g           => Element(w => JsonSerializer.Serialize(w, g, SchemaJsonContext.Default.Geo)),
        _               => v, // primitives / string / string[] — the SDK serialises these directly
    };

    private static JsonElement Element(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) write(writer);
        using var doc = JsonDocument.Parse(buffer.WrittenMemory);
        return doc.RootElement.Clone();
    }
}

internal sealed record SubmitResult(bool Success, string? SubmissionId, string? Error)
{
    public static SubmitResult Ok(string id)      => new(true,  id,   null);
    public static SubmitResult Fail(string error) => new(false, null, error);
}
