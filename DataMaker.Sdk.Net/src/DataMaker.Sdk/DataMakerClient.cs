using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace DataMaker.Sdk;

/// <summary>
/// Submit records to a Data Maker form. Read a .dmf, validate + seal values
/// client-side, and POST the ciphertext to the public submissions endpoint —
/// everything but the final POST runs offline.
///
/// <code>
/// var client = new DataMakerClient();
/// var form   = DataMakerClient.ReadForm(File.ReadAllBytes("contact.dmf"));
/// var result = await client.SubmitAsync(form, new Dictionary&lt;string, object?&gt;
/// {
///     ["email"] = "ada@example.com",
///     ["full_name"] = "Ada",
/// });
/// </code>
/// </summary>
public sealed class DataMakerClient
{
    public const string DefaultApiBaseUrl = "https://datamaker-api.fobo-tools.com";

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public DataMakerClient(HttpClient? http = null, string apiBaseUrl = DefaultApiBaseUrl)
    {
        _http = http ?? new HttpClient();
        _baseUrl = apiBaseUrl.TrimEnd('/');
    }

    /// <summary>Read + verify a .dmf into a descriptor. <paramref name="verify"/> gates signature/hash checks.</summary>
    public static FormDescriptor ReadForm(byte[] dmfBytes, bool verify = true, bool includeRenderBundle = false)
        => DmfReader.Read(dmfBytes, verify, includeRenderBundle);

    /// <summary>RFC 4122 v4, dashes stripped — matches the desktop's GUID "N" idempotency key.</summary>
    public static string NewSubmissionId() => Guid.NewGuid().ToString("N");

    /// <summary>Validate + seal a submission without sending.</summary>
    public static BuiltSubmission BuildSubmission(FormDescriptor form, IReadOnlyDictionary<string, object?> values, SubmitOptions? options = null)
    {
        options ??= new SubmitOptions();
        if (string.IsNullOrEmpty(form.RecipientPublicKey))
            throw new DataMakerException("form has no recipient public key — share-only bundles cannot receive submissions", "NO_RECIPIENT");

        IReadOnlyDictionary<string, object?> outValues = values;
        if (options.Validate)
        {
            var (coerced, issues) = Validation.Validate(form.Fields, values, options.AllowUnknown);
            if (issues.Count > 0) throw new ValidationException(issues);
            outValues = coerced;
        }

        var payload = new SubmissionPayload(
            Values: outValues,
            SubmittedAt: DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            FormVersion: form.SchemaVersion,
            FormSchema: "",
            Mode: "Create");

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, DataMakerJsonContext.Default.SubmissionPayload);
        var ciphertext = SealedBox.Seal(plaintext, form.RecipientPublicKey!);
        var id = NewSubmissionId();

        var envelope = new SubmissionEnvelope(id, form.FormId, form.RecipientPublicKey!, options.SubmitterId, ciphertext);
        return new BuiltSubmission(id, envelope, payload, outValues);
    }

    /// <summary>POST a built envelope to /submissions.</summary>
    public async Task<SubmitResult> PostAsync(SubmissionEnvelope envelope, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync($"{_baseUrl}/submissions", envelope, DataMakerJsonContext.Default.SubmissionEnvelope, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new SubmissionException((int)resp.StatusCode, body);

        SubmissionResponse? parsed;
        try { parsed = JsonSerializer.Deserialize(body, DataMakerJsonContext.Default.SubmissionResponse); }
        catch { throw new SubmissionException((int)resp.StatusCode, $"unparseable response body: {body}"); }

        return new SubmitResult(parsed?.SubmissionId ?? envelope.SubmissionId, parsed?.EditToken, envelope.FormId);
    }

    /// <summary>Validate, seal, and post against a pre-read form.</summary>
    public async Task<SubmitResult> SubmitAsync(FormDescriptor form, IReadOnlyDictionary<string, object?> values, SubmitOptions? options = null, CancellationToken ct = default)
    {
        var built = BuildSubmission(form, values, options);
        return await PostAsync(built.Envelope, ct).ConfigureAwait(false);
    }

    /// <summary>Read a .dmf, then validate, seal, and post.</summary>
    public Task<SubmitResult> SubmitAsync(byte[] dmfBytes, IReadOnlyDictionary<string, object?> values, SubmitOptions? options = null, bool verify = true, CancellationToken ct = default)
        => SubmitAsync(ReadForm(dmfBytes, verify), values, options, ct);
}
