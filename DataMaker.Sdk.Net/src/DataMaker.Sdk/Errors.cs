namespace DataMaker.Sdk;

/// <summary>A single schema-validation problem for one field.</summary>
/// <param name="Field">Field key (storage name), or the offending key for unknown-field issues.</param>
/// <param name="Kind">Normalized field kind, or null when the key isn't a known field.</param>
/// <param name="Message">Human-readable reason.</param>
public sealed record ValidationIssue(string Field, string? Kind, string Message);

/// <summary>Base for all SDK errors. <see cref="Code"/> lets callers branch without string-matching.</summary>
public class DataMakerException : Exception
{
    public string Code { get; }

    public DataMakerException(string message, string code) : base(message) => Code = code;
}

/// <summary>.dmf bundle malformed, unsigned, tampered, or share-only (no recipient).</summary>
public sealed class DmfException : DataMakerException
{
    public DmfException(string message) : base(message, "DMF_INVALID") { }
}

/// <summary>Supplied values failed schema validation. See <see cref="Issues"/>.</summary>
public sealed class ValidationException : DataMakerException
{
    public IReadOnlyList<ValidationIssue> Issues { get; }

    public ValidationException(IReadOnlyList<ValidationIssue> issues)
        : base("submission values are invalid — " + string.Join("; ", issues.Select(i => $"{i.Field}: {i.Message}")),
               "VALIDATION_FAILED")
        => Issues = issues;
}

/// <summary>The /submissions endpoint returned a non-2xx response.</summary>
public sealed class SubmissionException : DataMakerException
{
    public int Status { get; }
    public string? Body { get; }

    public SubmissionException(int status, string? body)
        : base($"submission rejected by the server (HTTP {status})", "SUBMISSION_REJECTED")
    {
        Status = status;
        Body = body;
    }
}
