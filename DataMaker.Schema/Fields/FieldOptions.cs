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

/// <summary>
/// Settings for a <c>scale</c> field — a single-pick rating / Likert / NPS
/// question rendered as a horizontal row of figures (reusing the step bar's
/// <see cref="DataMaker.Schema.Styling.StepFigureShape"/> so the two share one
/// visual language). The stored value is the chosen integer point (e.g. 4 on a
/// 1–5 scale); validation treats it like a number, so it's usable in
/// expressions and aggregations.
/// </summary>
public sealed record ScaleOptions
{
    /// <summary>Lowest point. 1 for a Likert scale, 0 for NPS.</summary>
    public int Min { get; init; } = 1;

    /// <summary>Highest point. 5 for a Likert scale, 10 for NPS. Clamped to ≥ Min + 1 at render.</summary>
    public int Max { get; init; } = 5;

    /// <summary>Anchor label under the lowest point (e.g. "Strongly disagree"). Null = none.</summary>
    public string? MinLabel { get; init; }

    /// <summary>Anchor label under the highest point (e.g. "Strongly agree"). Null = none.</summary>
    public string? MaxLabel { get; init; }

    /// <summary>Figure drawn for each point — shared with the wizard step bar (Circle / Square / Rounded / Diamond / Star).</summary>
    public DataMaker.Schema.Styling.StepFigureShape Shape { get; init; } = DataMaker.Schema.Styling.StepFigureShape.Circle;

    /// <summary>
    /// True = fill every point up to and including the selection (a star-rating
    /// feel). False = highlight only the selected point (a Likert radio feel).
    /// </summary>
    public bool Cumulative { get; init; }

    /// <summary>Fill colour (hex) of the selected / highlighted point(s). Null = the form's accent colour.</summary>
    public string? HighlightColor { get; init; }

    /// <summary>Outline / fill colour (hex) of the un-selected points. Null = the form's muted-ink colour.</summary>
    public string? UnselectedColor { get; init; }

    /// <summary>Colour (hex) of the field's own label — independent of the figure colours. Null = the form's ink colour.</summary>
    public string? LabelColor { get; init; }

    /// <summary>Colour (hex) of the low-end (Min) anchor label. Null = the form's muted-ink colour.</summary>
    public string? MinLabelColor { get; init; }

    /// <summary>Colour (hex) of the high-end (Max) anchor label. Null = the form's muted-ink colour.</summary>
    public string? MaxLabelColor { get; init; }

    /// <summary>Horizontal placement of the point row within the field. Null = Left.</summary>
    public ScaleAlignment? Alignment { get; init; }

    /// <summary>Gap (px) between points. Null = renderer default (~6).</summary>
    public double? Spacing { get; init; }
}

/// <summary>Horizontal placement of a <see cref="ScaleOptions"/> point row.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ScaleAlignment>))]
public enum ScaleAlignment { Left, Center, Right }

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
