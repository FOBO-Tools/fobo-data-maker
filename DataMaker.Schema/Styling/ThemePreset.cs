namespace DataMaker.Schema.Styling;

/// <summary>
/// Shippable theme — palette + typography + spacing + shape, packaged as a
/// preset that lives in the database and gets *applied* (copied) into a
/// form's <see cref="FormStyle"/>. Themes are not live-linked: once applied,
/// edits to the theme have no effect on existing forms; the form keeps its
/// snapshot.
///
/// <para>
/// DataMaker ships with built-in themes (<see cref="BuiltInThemes"/>) that
/// are seeded into the local store on first run. Authors can save their own
/// form's <see cref="FormStyle"/> as a new theme via the styling tab's
/// "Save as theme…" action.
/// </para>
/// </summary>
public sealed record ThemePreset
{
    public required string Id          { get; init; }
    public required string Name        { get; init; }
    public string? Description         { get; init; }
    public required FormStyle Style    { get; init; }
    public DateTimeOffset CreatedAt    { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>True for themes seeded by DataMaker on install. UI gates
    /// destructive actions (rename, delete) on built-ins.</summary>
    public bool IsBuiltIn { get; init; }
}
