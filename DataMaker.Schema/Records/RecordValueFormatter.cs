using System.Globalization;
using DataMaker.Schema.Fields;

namespace DataMaker.Schema.Records;

/// <summary>
/// Localized fallback labels the value formatter substitutes when a value has
/// no natural text (a captured signature with no typed name, an image with no
/// filename, a geo point with no resolved address, …). Each surface supplies
/// its own set: the grid maps these to <c>Loc.Get(...)</c>, the PDF generator
/// maps them off <c>RecordPdfStrings</c>. Defaults are English so headless /
/// test / SDK callers still render something sensible.
/// </summary>
public sealed record RecordValueLabels
{
    public string Yes            { get; init; } = "Yes";
    public string No             { get; init; } = "No";
    /// <summary>Shown for a geo point that resolved no address. Empty means
    /// "fall back to lat/lng coordinates" instead.</summary>
    public string UnknownAddress { get; init; } = "";
    public string ImageLabel     { get; init; } = "(image)";
    public string FileLabel      { get; init; } = "(file)";
    public string SignatureLabel { get; init; } = "Signed";

    public static RecordValueLabels Default { get; } = new();
}

/// <summary>
/// The single, surface-agnostic stringifier for a record field's raw stored
/// value. The grid (inline + CSV/XLSX export), the PDF record generator, and
/// the search filter all route through here so they cannot drift — previously
/// the grid and PDF carried near-identical hand-copied switches and the PDF
/// copy had already lost the <see cref="SignatureRef"/> arm (it printed the
/// struct type name for signed fields).
///
/// <para>
/// Loc-free by design: it lives in Schema (reachable by every consumer) and
/// takes its fallback strings via <see cref="RecordValueLabels"/> rather than
/// touching the app's localization. Boolean / MultiChoice / Image / RichText
/// are drawn specially on some surfaces (the PDF draws image + signature ink,
/// renders choice lists itself) — those callers route around this for the
/// special kinds and only use it for the wrapped-text kinds.
/// </para>
/// </summary>
public static class RecordValueFormatter
{
    public static string? FormatForDisplay(object? value, FieldDefinition field, RecordValueLabels labels) => value switch
    {
        null                   => null,
        bool b                 => b ? labels.Yes : labels.No,
        // Dates are persisted as ISO strings (lex-sortable in SQL). When a raw
        // string reaches us for a date kind, parse + culture-format it rather
        // than leak the ISO round-trip text (e.g. "2026-04-17T20:36:18+02:00").
        string str when field.Kind is FieldTypes.Date or FieldTypes.DateTime
                               => FormatDateString(str, field.Kind),
        string s               => s,
        string[] arr           => string.Join(", ", arr),
        IEnumerable<string> en => string.Join(", ", en),
        DateTime dt            => FormatDate(dt, field.Kind),
        DateTimeOffset dto     => FormatDate(dto, field.Kind),
        decimal d when field.Kind == FieldTypes.Money
                               => $"{field.Money?.Currency ?? ""} {d:N2}".Trim(),
        decimal d              => d.ToString("G", CultureInfo.CurrentCulture),
        double dbl             => dbl.ToString("G", CultureInfo.CurrentCulture),
        long l                 => l.ToString(CultureInfo.CurrentCulture),
        int i                  => i.ToString(CultureInfo.CurrentCulture),
        // Geo: prefer the resolved address; else the localized "unknown
        // address" label, or — when that label is empty — raw coordinates.
        Geo g                  => g.FormattedAddress
                                  ?? (string.IsNullOrEmpty(labels.UnknownAddress)
                                        ? $"{g.Lat:F5}, {g.Lng:F5}"
                                        : labels.UnknownAddress),
        ImageRef img           => img.FileName ?? labels.ImageLabel,
        AttachmentRef a        => a.FileName ?? labels.FileLabel,
        // Signature / initials: the typed name if the signer typed one, else a
        // generic "Signed" label; a blank ref reads as empty.
        SignatureRef sig       => sig.TypedName ?? (sig.IsBlank ? null : labels.SignatureLabel),
        _                      => value.ToString(),
    };

    /// <summary>
    /// Format a stored timestamp for display in the user's culture + timezone.
    /// A <c>datetime</c> field is an instant → convert UTC-stored values to the
    /// device-local timezone and show the culture's short date+time (<c>"g"</c>).
    /// A <c>date</c> field is a tz-naive calendar date (a birthday must not
    /// shift across midnight by timezone) → show the culture's short date
    /// (<c>"d"</c>) with NO timezone conversion.
    /// </summary>
    public static string FormatDate(DateTimeOffset dto, string kind) =>
        kind == FieldTypes.DateTime
            ? dto.ToLocalTime().ToString("g")
            : dto.ToString("d");

    public static string FormatDate(DateTime dt, string kind)
    {
        if (kind != FieldTypes.DateTime) return dt.ToString("d");
        var local = dt.Kind switch
        {
            DateTimeKind.Utc   => dt.ToLocalTime(),
            DateTimeKind.Local => dt,
            // Unspecified — records are persisted UTC (DateTimeOffset.UtcNow),
            // so treat a kind-less value as UTC before converting to local.
            _                  => DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime(),
        };
        return local.ToString("g");
    }

    /// <summary>Parse an ISO date/datetime string and route it through
    /// <see cref="FormatDate(DateTimeOffset,string)"/>; falls back to the raw
    /// string if it isn't a parseable timestamp.</summary>
    public static string FormatDateString(string s, string kind) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var dto)
                ? FormatDate(dto, kind)
                : s;
}
