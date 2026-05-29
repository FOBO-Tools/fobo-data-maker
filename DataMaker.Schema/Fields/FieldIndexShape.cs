namespace DataMaker.Schema.Fields;

/// <summary>
/// Central policy for "what does indexing mean per field kind". Both the
/// SQLite schema builder and the form editor consult this — keeping the
/// rule in one place is the only way to be sure the generated column,
/// the index, the form-editor UI and the JSON serialiser agree.
///
/// <para>
/// <b>Two classes of fields:</b>
/// </para>
/// <list type="bullet">
///   <item><b>Always indexed</b> — primitive kinds (Text, Email, Phone,
///     Url, Number, Decimal, Money, Boolean, Choice, Date, DateTime).
///     The SQL grid sorts and filters on these by default; users don't
///     get to disable that, because the cost is small (one btree on a
///     column that's likely under 100 bytes) and the value is large
///     (sub-second sort and filter on 1M rows). The form editor shows
///     an "indexed" badge but no toggle.</item>
///   <item><b>Opt-in indexed</b> — composite kinds whose value is a
///     compound JSON object (Geo, Image, Attachment). For these the
///     index is on a single human-readable sub-path (Geo's
///     formattedAddress, Image/Attachment's fileName). The form editor
///     surfaces a toggle so users decide whether the column is worth
///     the index cost.</item>
/// </list>
///
/// <para>
/// <b>Not indexed</b> — LongText (high-cardinality, low-value as a
/// btree key) and MultiChoice / List (need denormalisation that's
/// deferred to a later phase). These kinds never carry an index in
/// V1; sort and filter UI is hidden for them in the records grid.
/// </para>
/// </summary>
public static class FieldIndexShape
{
    /// <summary>
    /// True when records of this kind are always indexed regardless of
    /// the per-field <see cref="FieldDefinition.Indexed"/> flag. The
    /// flag is honoured for opt-in kinds, ignored here.
    /// </summary>
    public static bool IsAlwaysIndexed(string kind) => kind switch
    {
        FieldTypes.Text  or FieldTypes.Email or FieldTypes.Phone or FieldTypes.Url
            or FieldTypes.Number or FieldTypes.Decimal or FieldTypes.Money
            or FieldTypes.Boolean or FieldTypes.Choice
            or FieldTypes.Date    or FieldTypes.DateTime => true,
        _                                                => false,
    };

    /// <summary>
    /// True when the user can opt this kind in or out of indexing via
    /// the form editor. Composite kinds qualify when a useful index
    /// shape exists:
    /// <list type="bullet">
    ///   <item>Geo / Image / Attachment — index a single human-readable
    ///     sub-path (formattedAddress / fileName).</item>
    ///   <item>MultiChoice / List — index the bare JSON value
    ///     (lexical sort/filter on the array text). Good enough for
    ///     "group records by exact interest set"; richer per-element
    ///     sort would need a denormalised text sibling and is deferred.</item>
    /// </list>
    /// </summary>
    public static bool CanOptInIndexed(string kind) => kind switch
    {
        FieldTypes.Geo or FieldTypes.Image or FieldTypes.Attachment
            or FieldTypes.MultiChoice or FieldTypes.List => true,
        _                                                  => false,
    };

    /// <summary>
    /// Effective "is this field indexed" — combines
    /// <see cref="IsAlwaysIndexed"/> (kind-policy) with
    /// <see cref="FieldDefinition.Indexed"/> (per-field opt-in).
    /// </summary>
    public static bool IsIndexed(FieldDefinition field) =>
        IsAlwaysIndexed(field.Kind) || (CanOptInIndexed(field.Kind) && field.Indexed);

    /// <summary>
    /// JSON sub-path under <c>$."fieldName"</c> for the indexable
    /// surrogate value, or empty string when the bare value is itself
    /// what gets indexed. The double-quote wrapping mirrors what the
    /// rest of <c>json_extract</c> path-building uses to stay safe
    /// even though field names are validated to a tight charset.
    /// </summary>
    public static string IndexJsonSubPath(string kind) => kind switch
    {
        FieldTypes.Geo                            => ".\"formattedAddress\"",
        FieldTypes.Image or FieldTypes.Attachment => ".\"fileName\"",
        _                                          => "",
    };
}
