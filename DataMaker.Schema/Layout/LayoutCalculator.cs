namespace DataMaker.Schema.Layout;

/// <summary>
/// Pure responsive-layout math. Given a <see cref="Row"/> and a viewport
/// width, decides whether the row renders as a grid (honoring
/// <see cref="Row.ColumnsPerRow"/> + each column's <see cref="Column.Span"/>)
/// or collapses into a stacked single-column list.
///
/// <para>
/// <b>V1 policy:</b> whole-row stacking. If <i>any</i> column's
/// <see cref="Column.StackBelowPx"/> is greater than the viewport width, the
/// entire row stacks. Per-column independent stacking is intentionally
/// deferred — it creates "half-collapsed" layouts that almost always look
/// worse than a clean stack.
/// </para>
///
/// <para>
/// This logic is kept pure (no Uno/XAML dependency) so the custom
/// <c>ResponsiveRow</c> panel delegates here and tests stay fast.
/// </para>
/// </summary>
public static class LayoutCalculator
{
    public static ResolvedRow Resolve(Row row, double viewportWidth)
    {
        if (row.Columns.Count == 0)
            return new ResolvedRow(ColumnsPerRow: 1, ChildSpans: Array.Empty<int>());

        var needsStack = row.Columns.Any(c =>
            c.StackBelowPx is int threshold && viewportWidth < threshold);

        if (needsStack)
        {
            var stacked = new int[row.Columns.Count];
            Array.Fill(stacked, 1);
            return new ResolvedRow(ColumnsPerRow: 1, ChildSpans: stacked);
        }

        var perRow = row.ColumnsPerRow <= 0 ? 12 : row.ColumnsPerRow;
        var spans = row.Columns
            .Select(c => Math.Clamp(c.Span, 1, perRow))
            .ToArray();

        return new ResolvedRow(ColumnsPerRow: perRow, ChildSpans: spans);
    }
}

/// <summary>
/// Result of resolving a row at a given viewport width. <see cref="ColumnsPerRow"/>
/// is the grid slot count to render; <see cref="ChildSpans"/>[i] is the number
/// of slots child i occupies. Stacking is represented as <c>ColumnsPerRow = 1</c>
/// with every span = 1 (each child on its own visual row).
/// </summary>
public sealed record ResolvedRow(int ColumnsPerRow, int[] ChildSpans);
