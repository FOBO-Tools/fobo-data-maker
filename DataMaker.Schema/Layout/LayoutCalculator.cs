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

    /// <summary>
    /// Resolve a new span assignment when the column at <paramref name="dragIndex"/>
    /// is resized to <paramref name="desiredSpan"/> slots. Columns left of the drag
    /// are pinned; the dragged column grows or shrinks and the columns to its right
    /// absorb the difference — each kept at ≥ 1, their relative widths preserved —
    /// so the total never exceeds <paramref name="budget"/>.
    ///
    /// <para>
    /// This is the "move the divider" model used by the layout designer's drag
    /// handle: growing the left column shrinks its right neighbour rather than
    /// overflowing the row. With no columns to the right, the dragged column simply
    /// grows to fill the remaining budget.
    /// </para>
    /// </summary>
    /// <param name="currentSpans">Current span of each column in the row (≥ 1).</param>
    /// <param name="dragIndex">Index of the column being dragged.</param>
    /// <param name="desiredSpan">Span the pointer is asking the dragged column to take.</param>
    /// <param name="budget">Total slots in the row (<see cref="Row.ColumnsPerRow"/>).</param>
    public static int[] ResolveBoundaryResize(IReadOnlyList<int> currentSpans, int dragIndex, int desiredSpan, int budget)
    {
        var n = currentSpans.Count;
        budget = Math.Max(1, budget);
        var spans = new int[n];
        for (var i = 0; i < n; i++) spans[i] = Math.Max(1, currentSpans[i]);
        if (dragIndex < 0 || dragIndex >= n) return spans;

        var leftFixed = 0;
        for (var i = 0; i < dragIndex; i++) leftFixed += spans[i];

        var rightCount = n - dragIndex - 1;
        // Cap growth so every right column can stay at its 1-slot minimum.
        var maxSpan = Math.Max(1, budget - leftFixed - rightCount);
        spans[dragIndex] = Math.Clamp(desiredSpan, 1, maxSpan);

        if (rightCount > 0)
        {
            var rightBudget = budget - leftFixed - spans[dragIndex];   // ≥ rightCount
            var oldRight    = new int[rightCount];
            var oldTotal    = 0;
            for (var i = 0; i < rightCount; i++) { oldRight[i] = spans[dragIndex + 1 + i]; oldTotal += oldRight[i]; }

            var scaled = new int[rightCount];
            for (var i = 0; i < rightCount; i++)
                scaled[i] = Math.Max(1, (int)Math.Round((double)oldRight[i] * rightBudget / Math.Max(1, oldTotal)));

            // Drift correction — nudge the most-extreme column until the right
            // block sums to its budget exactly.
            var drift = rightBudget - scaled.Sum();
            var guard = 0;
            while (drift != 0 && guard++ < 256)
            {
                int pick;
                if (drift > 0)
                {
                    pick = 0;
                    for (var i = 1; i < rightCount; i++) if (scaled[i] > scaled[pick]) pick = i;
                }
                else
                {
                    pick = -1;
                    for (var i = 0; i < rightCount; i++) if (scaled[i] > 1 && (pick < 0 || scaled[i] > scaled[pick])) pick = i;
                    if (pick < 0) break;
                }
                scaled[pick] += Math.Sign(drift);
                drift = rightBudget - scaled.Sum();
            }
            for (var i = 0; i < rightCount; i++) spans[dragIndex + 1 + i] = scaled[i];
        }
        return spans;
    }
}

/// <summary>
/// Result of resolving a row at a given viewport width. <see cref="ColumnsPerRow"/>
/// is the grid slot count to render; <see cref="ChildSpans"/>[i] is the number
/// of slots child i occupies. Stacking is represented as <c>ColumnsPerRow = 1</c>
/// with every span = 1 (each child on its own visual row).
/// </summary>
public sealed record ResolvedRow(int ColumnsPerRow, int[] ChildSpans);
