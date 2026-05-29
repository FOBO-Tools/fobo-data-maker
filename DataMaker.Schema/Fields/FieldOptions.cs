namespace DataMaker.Schema.Fields;

/// <summary>Kind-specific settings. Nullable on the field; only the relevant block is populated.</summary>
public sealed record TextOptions
{
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public string? Pattern { get; init; }
}

public sealed record NumberOptions
{
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }
    public int? DecimalPlaces { get; init; }
    public string? Format { get; init; }
}

public sealed record MoneyOptions
{
    public string Currency { get; init; } = "EUR";
    public int DecimalPlaces { get; init; } = 2;
}

public sealed record ChoiceOptions
{
    public IReadOnlyList<Choice> Choices { get; init; } = Array.Empty<Choice>();
    public bool AllowCustom { get; init; }
}

public sealed record RelationOptions
{
    /// <summary>Id of the target Form whose records this field references.</summary>
    public required string TargetFormId { get; init; }
    /// <summary>Field Id on the target form used as the human-readable display value.</summary>
    public string? DisplayFieldId { get; init; }
    /// <summary>True for many-to-many / one-to-many; stored as array of target record ids.</summary>
    public bool Multiple { get; init; }
}

public sealed record AttachmentOptions
{
    public string[] AcceptedExtensions { get; init; } = Array.Empty<string>();
    public long? MaxSizeBytes { get; init; }
}

/// <summary>
/// Display-format options for <c>date</c> / <c>datetime</c> fields.
/// Storage is always ISO 8601 (yyyy-MM-dd for Date, full ISO for DateTime).
/// Rendering always uses the viewer's
/// <see cref="System.Globalization.CultureInfo.CurrentCulture"/> short
/// date/time pattern — a German user sees 22.04.2026, an American sees
/// 4/22/2026, and the stored value stays identical.
/// </summary>
public sealed record DateOptions
{
    /// <summary>Earliest acceptable value (ISO 8601 string). Null = no lower bound.</summary>
    public string? Min { get; init; }
    /// <summary>Latest acceptable value (ISO 8601 string). Null = no upper bound.</summary>
    public string? Max { get; init; }
}
