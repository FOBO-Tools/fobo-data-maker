using System.Text.Json.Serialization;
using DataMaker.Schema.Styling;
using DataMaker.Schema.Validation;

namespace DataMaker.Schema.Fields;

/// <summary>
/// Definition of a single field on a Form. Flat shape (kind-specific option
/// blocks are nullable) to keep JSON serialization straightforward — no
/// discriminator hassles, trivial round-trip.
///
/// <para>
/// <b>Name</b> is the persistent storage key. Must be a valid snake_case SQL
/// identifier: it becomes the JSONB key and the generated-column name in the
/// records table, so renames are migrations, not cosmetic edits.
/// </para>
/// </summary>
public sealed record FieldDefinition
{
    /// <summary>Stable id (GUID/string). Survives rename of <see cref="Name"/>.</summary>
    public required string Id { get; init; }

    /// <summary>Storage key + generated-column name. snake_case, SQL-safe.</summary>
    public required string Name { get; init; }

    public required string Label { get; init; }

    /// <summary>Helper text shown beneath the label. Renderers display it as italic 12pt muted ink. Null = no helper text.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Hint text shown inside the empty input. Honoured by single-input
    /// kinds (text / long-text / rich-text / number / decimal / money /
    /// email / phone / url / relation / list-chip-input). Bool / choice /
    /// multi-choice / date / datetime / image / attachment / geo ignore it
    /// — those kinds don't have a "blank state" the placeholder fits into,
    /// or use a kind-specific empty visual instead (e.g. "Click to upload").
    /// </summary>
    public string? Placeholder { get; init; }

    /// <summary>
    /// HTML autocomplete token rendered as the input's <c>autocomplete</c>
    /// attribute. Drives browser autofill — accepted values follow the
    /// WHATWG spec (<c>"email"</c>, <c>"tel"</c>, <c>"given-name"</c>,
    /// <c>"family-name"</c>, <c>"name"</c>, <c>"street-address"</c>,
    /// <c>"address-line1"</c>, <c>"address-line2"</c>, <c>"postal-code"</c>,
    /// <c>"address-level1"</c>, <c>"address-level2"</c>, <c>"country"</c>,
    /// <c>"organization"</c>, <c>"organization-title"</c>, <c>"username"</c>,
    /// <c>"bday"</c>, <c>"sex"</c>, <c>"off"</c>, …). Null = leave the attr
    /// unset so the browser falls back to heuristics on <see cref="Name"/>.
    /// </summary>
    public string? Autocomplete { get; init; }

    /// <summary>
    /// Canonical field-type id — one of the constants in <see cref="FieldTypes"/>
    /// for built-ins, or a host-registered custom id. See <see cref="FieldTypeRegistry"/>.
    /// </summary>
    [JsonConverter(typeof(FieldKindJsonConverter))]
    public required string Kind { get; init; }

    public bool Required { get; init; }

    /// <summary>Default value, JSON-serialized. Null = no default.</summary>
    public object? DefaultValue { get; init; }

    /// <summary>
    /// DataMaker.Expressions expression. When set the field is read-only in the
    /// UI and its value is recomputed from other fields on save.
    /// </summary>
    public string? CalculatedExpression { get; init; }

    /// <summary>
    /// Optional boolean expression (DataMaker.Expressions). When set, the field
    /// only renders in the record-entry UI when the expression evaluates true.
    /// Null = always visible.
    ///
    /// <para>
    /// <b>Runtime semantics when this is false:</b> the field is hidden AND
    /// all its validation (intrinsic + user rules) is skipped — a user can't
    /// be required to fill in what they never saw. Calculated fields still
    /// compute regardless of visibility (other fields may depend on them).
    /// Defaults apply when visibility flips back true if the field is still empty.
    /// </para>
    /// </summary>
    public string? VisibleWhen { get; init; }

    /// <summary>When true, this field is used as the record's display title.</summary>
    public bool IsPrimaryDisplay { get; init; }

    /// <summary>When true, storage builds an index on the generated column.</summary>
    public bool Indexed { get; init; }

    public List<ValidationRule> Validation { get; init; } = new();

    /// <summary>
    /// Custom error messages keyed by check id. Missing key = engine default.
    /// Slot ids are canonical strings: <c>"required"</c>, <c>"email"</c>,
    /// <c>"url"</c>, <c>"phone"</c>, <c>"number"</c>, <c>"decimal"</c>,
    /// <c>"money"</c>, <c>"date"</c>, <c>"datetime"</c>, <c>"boolean"</c>,
    /// <c>"choice"</c>, <c>"multichoice"</c>, <c>"geo.lat"</c>, <c>"geo.lng"</c>,
    /// <c>"geo"</c>, <c>"attachment.ext"</c>, <c>"text.minLength"</c>,
    /// <c>"text.maxLength"</c>, <c>"text.pattern"</c>, <c>"relation"</c>.
    /// Authored-rule messages still live on each <see cref="ValidationRule"/>.
    /// </summary>
    public Dictionary<string, string>? Messages { get; init; }

    // ── Kind-specific option blocks (exactly one non-null for kinds that need it) ──
    public TextOptions?       Text       { get; init; }
    public NumberOptions?     Number     { get; init; }
    public MoneyOptions?      Money      { get; init; }
    public ChoiceOptions?     Choice     { get; init; }
    public ScaleOptions?      Scale      { get; init; }
    public RelationOptions?   Relation   { get; init; }
    public AttachmentOptions? Attachment { get; init; }
    public DateOptions?       Date       { get; init; }

    /// <summary>Field-level style overrides. Null properties inherit from the nearest ancestor.</summary>
    public Style? Style { get; init; }
}
