using DataMaker.Schema.Fields;

namespace DataMaker.Schema.Import;

/// <summary>
/// Which DataMaker field a given source field kind may map onto — the structural
/// half of the hard type block (no disappearing data). A pairing the matrix
/// forbids is not selectable in the dialog and is a Save-gating error; a pairing
/// it allows can still fail per-value at apply (see <see cref="ImportApplier"/>).
/// Source-agnostic: used by both the PDF importer and the spreadsheet importer.
/// </summary>
public static class ImportTypeCompatibility
{
    // A flat source has no numeric/date type — everything is text, checkbox,
    // choice or signature — so a text source legitimately feeds ANY kind whose
    // value coercion accepts a string (see ImportApplier.TryCoerce): the scalars,
    // booleans ("yes"/"true"/…), and the collection kinds (a delimited string
    // explodes to a list / multi-choice). Coercion validates the actual value
    // later — an offered pairing can still fail per-value at apply. Geo is handled
    // separately (PDF-only) because resolving a typed address needs the
    // single-record picker. Image / attachment never accept a flat value.
    private static readonly string[] TextTargets =
    {
        FieldTypes.Text, FieldTypes.LongText, FieldTypes.RichText,
        FieldTypes.Email, FieldTypes.Phone, FieldTypes.Url,
        FieldTypes.Number, FieldTypes.Decimal, FieldTypes.Money, FieldTypes.Scale,
        FieldTypes.Boolean, FieldTypes.Date, FieldTypes.DateTime,
        FieldTypes.Choice, FieldTypes.MultiChoice, FieldTypes.List, FieldTypes.Relation,
    };

    private static readonly string[] CheckboxTargets =
    {
        FieldTypes.Boolean, FieldTypes.Choice,
    };

    private static readonly string[] ChoiceTargets =
    {
        FieldTypes.Choice, FieldTypes.MultiChoice, FieldTypes.List,
        FieldTypes.Text, FieldTypes.LongText,
    };

    private static readonly string[] SignatureTargets =
    {
        FieldTypes.Signature, FieldTypes.Initials,
    };

    /// <summary>Target kinds that can never receive an imported value, whatever
    /// the source: typed/owned refs and geo (no string round-trip from a flat
    /// value).</summary>
    private static readonly HashSet<string> NeverTargetKinds = new(StringComparer.Ordinal)
    {
        FieldTypes.Image, FieldTypes.Attachment, FieldTypes.Geo,
    };

    /// <summary>Allowed target kinds for a source field kind. Unknown is treated
    /// as text (permissive) — many foreign sources omit type info.</summary>
    public static IReadOnlyList<string> AllowedTargetKinds(ImportFieldKind source) => source switch
    {
        ImportFieldKind.Checkbox  => CheckboxTargets,
        ImportFieldKind.Choice    => ChoiceTargets,
        ImportFieldKind.Signature => SignatureTargets,
        _                         => TextTargets, // Text + Unknown
    };

    /// <summary>
    /// True when <paramref name="source"/> may map onto <paramref name="target"/>:
    /// the target is a real, writable field (not calculated, not a never-target
    /// kind) and its kind is in the source's allowed set. Source-kind-agnostic —
    /// excludes geo (use the <see cref="IsCompatible(ImportFieldKind,FieldDefinition,ImportSourceKind)"/>
    /// overload to allow geo for the PDF single-record flow).
    /// </summary>
    public static bool IsCompatible(ImportFieldKind source, FieldDefinition target)
    {
        if (target is null) return false;
        if (!IsMappableTarget(target)) return false;
        return AllowedTargetKinds(source).Contains(target.Kind, StringComparer.Ordinal);
    }

    /// <summary>
    /// As <see cref="IsCompatible(ImportFieldKind,FieldDefinition)"/>, but also
    /// allows a geo target for a <see cref="ImportSourceKind.Pdf"/> source: a PDF
    /// is one record, so a typed address can be geocoded with the ambiguity picker
    /// at import. A spreadsheet imports many rows and can't prompt per row, so geo
    /// stays unmappable there — exactly as image / attachment do everywhere.
    /// </summary>
    public static bool IsCompatible(ImportFieldKind source, FieldDefinition target, ImportSourceKind sourceKind)
    {
        if (target is null) return false;
        if (!string.IsNullOrWhiteSpace(target.CalculatedExpression)) return false;

        if (target.Kind == FieldTypes.Geo)
            return sourceKind == ImportSourceKind.Pdf
                && source is ImportFieldKind.Text or ImportFieldKind.Unknown;

        return IsCompatible(source, target);
    }

    /// <summary>A field is a mappable target unless it's computed (its value is
    /// derived, never written) or a kind that can't accept a flat value.</summary>
    public static bool IsMappableTarget(FieldDefinition target)
    {
        if (target is null) return false;
        if (!string.IsNullOrWhiteSpace(target.CalculatedExpression)) return false;
        return !NeverTargetKinds.Contains(target.Kind);
    }
}
