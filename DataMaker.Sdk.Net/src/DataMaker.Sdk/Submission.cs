using System.Text.Json.Serialization;

namespace DataMaker.Sdk;

/// <summary>
/// Plaintext routing metadata + sealed ciphertext, byte-exact to the desktop's
/// DataMaker.Sync.Shared.SubmissionEnvelope. submitterId is null for anonymous
/// submissions; editToken/receivedAt are omitted on create.
/// </summary>
public sealed record SubmissionEnvelope(
    [property: JsonPropertyName("submissionId")]    string SubmissionId,
    [property: JsonPropertyName("formId")]          string FormId,
    [property: JsonPropertyName("recipientUserId")] string RecipientUserId,
    [property: JsonPropertyName("submitterId")]     string? SubmitterId,
    [property: JsonPropertyName("ciphertext")]      string Ciphertext);

/// <summary>
/// What the envelope's ciphertext encrypts, byte-exact to
/// DataMaker.Sync.Shared.SubmissionPayload. formSchema is a legacy field no
/// current receiver reads; left empty. SubmittedAt is ISO-8601 UTC.
/// </summary>
public sealed record SubmissionPayload(
    [property: JsonPropertyName("values")]       IReadOnlyDictionary<string, object?> Values,
    [property: JsonPropertyName("submittedAt")]  string SubmittedAt,
    [property: JsonPropertyName("formVersion")]  int FormVersion,
    [property: JsonPropertyName("formSchema")]   string FormSchema,
    [property: JsonPropertyName("mode")]         string Mode);

/// <summary>Server response to POST /submissions.</summary>
internal sealed record SubmissionResponse(
    [property: JsonPropertyName("submissionId")] string? SubmissionId,
    [property: JsonPropertyName("editToken")]    string? EditToken);

/// <summary>Options for building/posting a submission.</summary>
public sealed class SubmitOptions
{
    /// <summary>Validate + coerce values before sealing (default true).</summary>
    public bool Validate { get; init; } = true;

    /// <summary>Reject unknown value keys instead of throwing (default false → reject).</summary>
    public bool AllowUnknown { get; init; }

    /// <summary>Submitter id; null = anonymous (the public-survey default).</summary>
    public string? SubmitterId { get; init; }
}

/// <summary>A built-but-unsent submission.</summary>
public sealed record BuiltSubmission(
    string SubmissionId,
    SubmissionEnvelope Envelope,
    SubmissionPayload Payload,
    IReadOnlyDictionary<string, object?> Values);
