namespace DataMaker.Schema.Layout;

/// <summary>
/// Clones a step's layout (sections → rows → columns, including groups and design
/// elements) while <b>dropping every <see cref="FieldColumn"/></b> and minting a
/// fresh id for each cloned node. Backs the layout editor's "Copy step" action: a
/// field lives on exactly one step, so the copy is a reusable skeleton the user
/// re-populates with fields. All other content — titles, styling, group nesting,
/// headings/dividers/spacers/images/buttons/rich-text — is preserved verbatim.
/// Records are immutable, so each clone is a <c>with</c> that overrides only the id
/// (and the recursively-rebuilt child list).
/// </summary>
public static class StepLayoutCloner
{
    public static List<Section> CloneSectionsLayoutOnly(IEnumerable<Section> sections) =>
        sections.Select(CloneSection).ToList();

    private static Section CloneSection(Section s) => s with
    {
        Id   = $"sec_{FreshId()[..8]}",
        Rows = s.Rows.Select(CloneRow).ToList(),
    };

    private static Row CloneRow(Row r) => r with
    {
        Id      = FreshId(),
        Columns = r.Columns.Where(c => c is not FieldColumn).Select(CloneColumn).ToList(),
    };

    private static Column CloneColumn(Column c) => c switch
    {
        GroupColumn g    => g with { Id = FreshId(), Rows = g.Rows.Select(CloneRow).ToList() },
        RichTextColumn x => x with { Id = FreshId() },
        ImageColumn x    => x with { Id = FreshId() },
        DividerColumn x  => x with { Id = FreshId() },
        HeadingColumn x  => x with { Id = FreshId() },
        SpacerColumn x   => x with { Id = FreshId() },
        ButtonColumn x   => x with { Id = FreshId() },
        _                => c,   // FieldColumn already filtered out in CloneRow
    };

    private static string FreshId() => Guid.NewGuid().ToString("N");
}
