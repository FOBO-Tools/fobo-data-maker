namespace DataMaker.Schema.Fields;

/// <summary>
/// Per-type metadata registered in <see cref="FieldTypeRegistry"/>. Describes
/// the pieces of behaviour that consumers (expression engine, storage, editor)
/// look up by type id so they don't have to switch on a closed enum.
///
/// <para>
/// Keep this descriptor lean — only facts that don't require the full
/// <see cref="FieldDefinition"/> to compute. Per-field logic (intrinsic
/// validators, canonicalization of raw values) stays in its own module and
/// switches on <see cref="Id"/> directly; adding it here would drag storage /
/// validation dependencies into Schema.
/// </para>
/// </summary>
public sealed record FieldTypeDescriptor(
    /// <summary>Canonical type id — the string persisted to form JSON.</summary>
    string Id,

    /// <summary>Designer-facing label (e.g. "Long text" for "long-text").</summary>
    string Label,

    /// <summary>
    /// CLR type used as the expression-engine parameter for values of this
    /// type. Nullable value types so missing values don't crash expressions;
    /// object-typed fields (Geo, etc.) keep their real type so property
    /// access works from user-authored expressions.
    /// </summary>
    Type ExpressionParameterType,

    /// <summary>SQLite column affinity for the generated value column.</summary>
    string SqliteType);
