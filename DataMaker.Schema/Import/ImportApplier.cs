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
    /// Coerce a flat string value to the CLR shape the record store expects for
    /// <paramref name="kind"/>. Number→long, Decimal/Money→decimal, Boolean→bool,
    /// collections→list of string. Text-family, date and signature kinds keep the
    /// string — RecordJson coerces a "data:" string for a signature into a
    /// SignatureRef.
    /// </summary>
    public static bool TryCoerce(string raw, string kind, out object? value)
    {
        value = null;
        var t = raw.Trim();

        switch (kind)
        {
            case FieldTypes.Number:
            case FieldTypes.Scale:
                if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                {
                    value = l; return true;
                }
                if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var dl)
                    && decimal.Truncate(dl) == dl)
                {
                    value = (long)dl; return true;
                }
                return false;

            case FieldTypes.Decimal:
            case FieldTypes.Money:
                if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                {
                    value = d; return true;
                }
                return false;

            case FieldTypes.Boolean:
                return TryCoerceBool(t, out value);

            case FieldTypes.MultiChoice:
            case FieldTypes.List:
                value = t.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .ToList();
                return true;

            default:
                // text / long-text / rich-text / email / phone / url / choice /
                // relation / date / datetime / signature / initials — keep as-is.
                value = raw;
                return true;
        }
    }

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
