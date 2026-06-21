namespace DataMaker.Schema.Layout;

using DataMaker.Schema.Forms;

/// <summary>
/// Cross-tab merge helpers for image columns. Image <see cref="ImageColumn.Align"/>
/// and <see cref="ImageColumn.MaxWidth"/> are authored only in the Styling tab's
/// image Size group — the Layout inspector has no control for them. But the Layout
/// editor rebuilds the whole form from its view-models (which still round-trip
/// those two props), so a Layout re-stage with a stale view-model would overwrite
/// a Styling edit (Placement=Right silently reverting to Fill on the next
/// structural change). The Layout editor runs its rebuilt form through
/// <see cref="RestoreStylingOwnedProps"/> so those two props always defer to the
/// live staged form — the same pass-through contract Form/element Style gets.
/// </summary>
public static class ImageColumnMerge
{
    /// <summary>
    /// Return <paramref name="rebuilt"/> with each image column's
    /// <see cref="ImageColumn.Align"/> + <see cref="ImageColumn.MaxWidth"/>
    /// replaced by the value from the matching (by <see cref="ImageColumn.Id"/>)
    /// column in <paramref name="live"/>. Image columns with no match in
    /// <paramref name="live"/> (e.g. just added in the Layout tab) keep their
    /// rebuilt values. Recurses into <see cref="GroupColumn"/> sub-grids.
    /// </summary>
    public static Form RestoreStylingOwnedProps(Form rebuilt, Form? live)
    {
        if (live is null) return rebuilt;

        var byId = new Dictionary<string, ImageColumn>(StringComparer.Ordinal);
        foreach (var step in live.Steps)
            foreach (var sec in step.Sections)
                Collect(sec.Rows, byId);
        if (byId.Count == 0) return rebuilt;

        return rebuilt with
        {
            Steps = rebuilt.Steps.Select(st => st with
            {
                Sections = st.Sections.Select(sec => sec with
                {
                    Rows = Restore(sec.Rows, byId)
                }).ToList()
            }).ToList()
        };
    }

    private static void Collect(IEnumerable<Row> rows, Dictionary<string, ImageColumn> sink)
    {
        foreach (var row in rows)
            foreach (var col in row.Columns)
            {
                if (col is ImageColumn ic) sink[ic.Id] = ic;
                else if (col is GroupColumn gc) Collect(gc.Rows, sink);
            }
    }

    private static List<Row> Restore(List<Row> rows, Dictionary<string, ImageColumn> live) =>
        rows.Select(row => row with
        {
            Columns = row.Columns.Select(col => col switch
            {
                ImageColumn ic when live.TryGetValue(ic.Id, out var lv) => ic with { Align = lv.Align, MaxWidth = lv.MaxWidth },
                GroupColumn gc => gc with { Rows = Restore(gc.Rows, live) },
                _ => col
            }).ToList()
        }).ToList();
}
