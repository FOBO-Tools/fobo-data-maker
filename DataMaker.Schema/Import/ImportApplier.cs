using System.Globalization;
using DataMaker.Schema.Fields;
using DataMaker.Schema.Forms;

namespace DataMaker.Schema.Import;

/// <summary>
/// Applies an <see cref="ImportMapping"/> to a set of source field values,
/// producing the record value dictionary — or, when any mapped value can't be
/// coerced to its target kind, a non-empty failure list. The caller saves a
/// record only on success: a partial single-record import that silently drops
/// values is never produced (hard block). Source-agnostic — PDF and spreadsheet
/// importers both call this.
/// </summary>
public static class ImportApplier
{
    public static ImportApplyResult Apply(
        Form form, IReadOnlyList<ImportField> fields, ImportMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(mapping);

        var bySource   = new Dictionary<string, ImportField>(StringComparer.Ordinal);
        foreach (var f in fields) bySource[f.Name] = f;
        var byFormName = form.Fields.ToDictionary(f => f.Name, StringComparer.Ordinal);

        var values   = new Dictionary<string, object?>(StringComparer.Ordinal);
        var failures = new List<ImportFailure>();

        foreach (var wire in mapping.Mappings)
        {
            // A mapping referencing a since-removed field (form edited after the
            // mapping was saved) is tolerated — skip it.
            if (!byFormName.TryGetValue(wire.FormFieldName, out var target)) continue;
            if (!bySource.TryGetValue(wire.SourceFieldName, out var source))  continue;

            var raw = source.Value;
            if (string.IsNullOrEmpty(raw)) continue; // empty source → no value, not a failure

            if (TryCoerce(raw, target.Kind, out var coerced))
                values[target.Name] = coerced;
            else
                failures.Add(new ImportFailure(
                    wire.SourceFieldName, wire.FormFieldName, target.Kind, raw));
        }

        return new ImportApplyResult(values, failures);
    }

    /// <summary>
    /// Coerce exact-name imported values — the DataMaker-generated PDF fast path,
    /// where the source field names already equal the form's field names, so there
    /// is no mapping step. Every imported key that matches a form field is coerced
    /// to its target CLR shape. Non-string values (booleans and choice arrays the
    /// importer already shaped) pass through untouched; empty strings are skipped.
    /// A value that can't be coerced is omitted from <see cref="ImportApplyResult.Values"/>
    /// (never stored as raw text the record store would mis-read) and reported as a
    /// failure so the caller can surface it.
    /// </summary>
    public static ImportApplyResult CoerceByFieldName(
        Form form, IReadOnlyDictionary<string, object?> imported)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(imported);

        var byName   = form.Fields.ToDictionary(f => f.Name, StringComparer.Ordinal);
        var values   = new Dictionary<string, object?>(StringComparer.Ordinal);
        var failures = new List<ImportFailure>();

        foreach (var (name, raw) in imported)
        {
            if (!byName.TryGetValue(name, out var field)) continue;
            if (raw is not string s) { values[name] = raw; continue; }
            if (string.IsNullOrEmpty(s)) continue;

            if (TryCoerce(s, field.Kind, out var coerced))
                values[name] = coerced;
            else
                failures.Add(new ImportFailure(name, name, field.Kind, s));
        }

        return new ImportApplyResult(values, failures);
    }

    /// <summary>
    /// Coerce a flat string value to the CLR shape the record store expects for
    /// <paramref name="kind"/>. Number→long, Decimal/Money→decimal, Boolean→bool,
    /// Date/DateTime→DateTimeOffset, collections→list of string. Other text-family
    /// and signature kinds keep the string — RecordJson coerces a "data:" string
    /// for a signature into a SignatureRef.
    ///
    /// <para>
    /// Numbers and dates arrive as <b>human-typed, culture-formatted</b> strings:
    /// a PDF/CSV field has no number or date picker, so a Dutch filler types
    /// "22,34" (comma = decimal) and "24-05-1986" (dd-MM-yyyy). Parsing those with
    /// the invariant culture silently mangles them ("22,34" → 2234; the date
    /// fails outright). Parse in <see cref="CultureInfo.CurrentCulture"/> first —
    /// the app sets it from the user's locale — then fall back to the invariant
    /// culture for data authored elsewhere.
    /// </para>
    /// </summary>
    public static bool TryCoerce(string raw, string kind, out object? value)
    {
        value = null;
        var t = raw.Trim();

        switch (kind)
        {
            case FieldTypes.Number:
            case FieldTypes.Scale:
                foreach (var c in ParseCultures())
                {
                    if (long.TryParse(t, IntStyles, c, out var l))
                    {
                        value = l; return true;
                    }
                    if (decimal.TryParse(t, DecimalStyles, c, out var dl)
                        && decimal.Truncate(dl) == dl)
                    {
                        value = (long)dl; return true;
                    }
                }
                return false;

            case FieldTypes.Decimal:
            case FieldTypes.Money:
                foreach (var c in ParseCultures())
                    if (decimal.TryParse(t, DecimalStyles | NumberStyles.AllowCurrencySymbol, c, out var d))
                    {
                        value = d; return true;
                    }
                return false;

            case FieldTypes.Boolean:
                return TryCoerceBool(t, out value);

            case FieldTypes.Date:
            case FieldTypes.DateTime:
                foreach (var c in ParseCultures())
                    if (DateTimeOffset.TryParse(t, c, DateTimeStyles.AssumeLocal, out var dto))
                    {
                        value = dto; return true;
                    }
                return false;

            case FieldTypes.MultiChoice:
            case FieldTypes.List:
                // Explode a delimited string into items. DataMaker's own export
                // joins list values with newlines; a foreign/flat source typically
                // comma-separates ("Test, Newline, Test2") — accept both so a
                // comma-delimited text column maps cleanly onto a list.
                value = t.Split(new[] { '\n', ',' },
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .ToList();
                return true;

            default:
                // long-text / rich-text / email / phone / url / choice /
                // relation / signature / initials — keep as-is.
                value = raw;
                return true;
        }
    }

    /// <summary>
    /// Cultures to try when parsing a human-typed number or date, in order: the
    /// caller's current culture (set by the app from the user's locale) first,
    /// then the invariant culture for data authored on a differently-configured
    /// machine. Computed per call so a mid-session locale switch is respected.
    /// </summary>
    private static IEnumerable<CultureInfo> ParseCultures()
    {
        yield return CultureInfo.CurrentCulture;
        if (!CultureInfo.CurrentCulture.Equals(CultureInfo.InvariantCulture))
            yield return CultureInfo.InvariantCulture;
    }

    // Deliberately NO AllowThousands. A grouping separator in one culture is the
    // decimal separator in another ("." groups in nl-NL, decimal in en-US), so
    // allowing it lets a comma-decimal "22,34" be silently read as 2234 — the
    // exact corruption this coercion exists to prevent. We try the current
    // culture then the invariant one instead; a single hand-typed field with a
    // grouping separator is rare and inherently ambiguous, so we'd rather fail
    // than 100× a value.
    private const NumberStyles IntStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

    private const NumberStyles DecimalStyles = IntStyles | NumberStyles.AllowDecimalPoint;

    /// <summary>
    /// Required fields that would land empty for the given resolved values —
    /// returns their labels (or names). Fields that can't be captured from a
    /// flat source (calculated, image/attachment/geo) are excluded;
    /// <paramref name="exempt"/> drops source-specific "couldn't be filled"
    /// fields (e.g. the PDF exporter's dropped fields).
    /// </summary>
    public static IReadOnlyList<string> MissingRequired(
        Form form,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlySet<string>? exempt = null)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(values);

        var missing = new List<string>();
        foreach (var f in form.Fields)
        {
            if (!f.Required) continue;
            if (!ImportTypeCompatibility.IsMappableTarget(f)) continue;
            if (exempt is not null && exempt.Contains(f.Name)) continue;
            if (IsBlank(values.TryGetValue(f.Name, out var v) ? v : null))
                missing.Add(string.IsNullOrWhiteSpace(f.Label) ? f.Name : f.Label!);
        }
        return missing;
    }

    private static bool IsBlank(object? v) => v switch
    {
        null                             => true,
        string s                         => string.IsNullOrWhiteSpace(s),
        System.Collections.ICollection c => c.Count == 0,
        _                                => false, // bool false / 0 count as answered
    };

    private static bool TryCoerceBool(string t, out object? value)
    {
        value = null;
        switch (t.ToLowerInvariant())
        {
            case "yes" or "on" or "true" or "1" or "checked" or "x" or "y":
                value = true; return true;
            case "no" or "off" or "false" or "0" or "unchecked" or "n" or "":
                value = false; return true;
            default:
                return false;
        }
    }
}

/// <summary>Outcome of <see cref="ImportApplier.Apply"/>. When
/// <see cref="Failures"/> is empty the values are safe to save; otherwise the
/// caller decides (single-record: block; multi-row: skip the failing rows).</summary>
public sealed record ImportApplyResult(
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyList<ImportFailure>         Failures)
{
    public bool Ok => Failures.Count == 0;
}

/// <summary>A mapped value that could not be coerced to its target kind.</summary>
public sealed record ImportFailure(
    string SourceFieldName,
    string FormFieldName,
    string TargetKind,
    string RawValue);
