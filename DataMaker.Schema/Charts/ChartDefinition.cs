using System.Text.Json.Serialization;

namespace DataMaker.Schema.Charts;

/// <summary>
/// Saved chart definition for a single form. Renders against the form's
/// records via the LiveCharts2 control. Pivot-table style: axis fields,
/// optional series-split field, value fields with aggregates, and arbitrary
/// filter rows. Stored as JSON in a per-form-keyed table (see
/// <c>SqliteChartStore</c>); round-trips through <c>SchemaJsonContext</c>.
///
/// <para>
/// Per-kind validation lives on the save path (see chart designer):
/// <list type="bullet">
///   <item><b>Line / Bar</b> — <see cref="Axis"/> may be empty (single
///   "(all)" bucket) or carry one or more categorical fields (nested
///   <c>GROUP BY</c>); <see cref="Values"/> may be many; <see cref="SeriesSplit"/>
///   optional. Each (split-value × Value-spec) becomes one rendered series.</item>
///   <item><b>Pie</b> — exactly one <see cref="Axis"/> field, exactly one
///   <see cref="Values"/> entry; <see cref="SeriesSplit"/> ignored.</item>
///   <item><b>Scatter</b> — exactly one numeric <see cref="Axis"/> field
///   (X), exactly one <see cref="Values"/> entry (Y, raw); optional
///   <see cref="SeriesSplit"/> for color-coding groups.</item>
///   <item><b>Map</b> — plots every record's location fields as point
///   markers on an OpenStreetMap base. Carries no pivot config: <see cref="Axis"/>,
///   <see cref="SeriesSplit"/> and <see cref="Values"/> stay empty. The
///   builder auto-collects all Geo fields on the form; a record with N
///   location fields contributes N markers, one coloured per field index
///   off the shared chart palette. <see cref="Filters"/> still apply.</item>
/// </list>
/// </para>
/// </summary>
public sealed record ChartDefinition
{
    public required string Id { get; init; }
    public required string FormId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required ChartKind Kind { get; init; }

    /// <summary>
    /// X / category fields, in the order they should be nested. Multiple
    /// entries collapse to a composite axis label per (cat1 / cat2 / …)
    /// tuple; one entry behaves like a flat X axis.
    /// </summary>
    public List<string> Axis { get; init; } = new();

    /// <summary>
    /// Optional date granularity per <see cref="Axis"/> slot. <c>null</c> /
    /// missing entry = group by the raw field value (default). Only honoured
    /// when the matching field is Date / DateTime. The storage layer wraps
    /// the generated column in the corresponding SQLite <c>strftime</c>
    /// expression so buckets actually collapse instead of producing one
    /// row per distinct instant.
    /// </summary>
    public List<DateGranularity?> AxisTransforms { get; init; } = new();

    /// <summary>
    /// Optional split-by field. When set, the result is pivoted: one
    /// rendered series per distinct value of this field, per
    /// <see cref="Values"/> entry.
    /// </summary>
    public string? SeriesSplit { get; init; }

    /// <summary>Optional date granularity for <see cref="SeriesSplit"/>; same
    /// rules as <see cref="AxisTransforms"/>.</summary>
    public DateGranularity? SeriesSplitTransform { get; init; }

    public List<ChartValue> Values { get; init; } = new();

    public List<ChartFilter> Filters { get; init; } = new();

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public enum ChartKind { Line, Bar, Pie, Scatter, Map }

/// <summary>
/// Warehouse-style date dimensions applied to a Date / DateTime field used
/// as a chart group-by. Lets a user roll up timestamps into year / month /
/// week / etc. buckets without precomputing a derived field.
///
/// <para>
/// The first five (<see cref="Year"/>..<see cref="Day"/>) are ordered
/// time buckets — labels are ISO-prefixed strings (e.g. <c>2026-05</c>) so
/// SQLite's lexicographic ORDER BY matches chronological order naturally.
/// The last three collapse across years and sort numerically (Jan=1..Dec=12,
/// Sun=0..Sat=6, 0..23) — useful for seasonality / time-of-day plots.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DateGranularity>))]
public enum DateGranularity
{
    Year,
    Quarter,
    Month,
    Week,
    Day,
    MonthOfYear,
    DayOfWeek,
    HourOfDay,
}

/// <summary>None = raw values (scatter only). Other ops fold many records into one axis bucket.</summary>
public enum ChartAggregate { None, Count, Sum, Avg, Min, Max }

/// <summary>One Y-axis value spec — a numeric field plus the aggregate that folds it.</summary>
public sealed record ChartValue
{
    public required string Field { get; init; }
    public string? Label { get; init; }
    public ChartAggregate Aggregate { get; init; } = ChartAggregate.Sum;

    /// <summary>
    /// Plot this value as a cumulative running total: each axis bucket shows
    /// the sum of itself plus all prior buckets. Applied client-side after the
    /// ordered aggregate query (rows arrive sorted by axis), so it needs no
    /// SQL change. Line/Bar only — meaningless for Pie/Scatter. Defaults off;
    /// older saved charts deserialise with this false.
    /// </summary>
    public bool RunningTotal { get; init; }
}

/// <summary>Filter op set. Mirrors the storage layer's FilterOp; the designer
/// surfaces a kind-appropriate subset per field (e.g. only comparison ops
/// for numeric / date fields, only Contains/Equals for text).</summary>
public enum ChartFilterOp
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
    Contains,
    StartsWith,
    EndsWith,
    In,
    IsNull,
    IsNotNull,
}

/// <summary>One predicate row. <c>Value</c> is JSON-shaped (string / number / bool /
/// array for <see cref="ChartFilterOp.In"/>); the SQL layer rebinds to the right
/// generated-column type.</summary>
public sealed record ChartFilter
{
    public required string Field { get; init; }
    public required ChartFilterOp Op { get; init; }
    public object? Value { get; init; }
}
