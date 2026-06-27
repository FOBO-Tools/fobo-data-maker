namespace DataMaker.Schema.Dashboards;

/// <summary>
/// Pure tile-grid geometry for the Home dashboard — collision tests, free-slot
/// packing, and pixel→cell translation. Extracted from <c>HomePage</c>
/// code-behind (audit #81g) so the placement math is unit-testable in isolation
/// from the WinUI <c>Grid</c> it drives. No UI types here; the view passes in its
/// host metrics and applies the integer results.
/// </summary>
public static class DashboardTileLayout
{
    /// <summary>The dashboard grid is a fixed 24-column track; tiles span whole
    /// columns. Row count grows on demand and is owned by the view.</summary>
    public const int Columns = 24;

    /// <summary>
    /// True when the rectangle <c>(x, y, w, h)</c> overlaps <paramref name="tile"/>
    /// in cell space. Half-open intervals — touching edges don't count as overlap.
    /// </summary>
    public static bool Overlaps(DashboardTile tile, int x, int y, int w, int h) =>
        x < tile.X + tile.W && x + w > tile.X &&
        y < tile.Y + tile.H && y + h > tile.Y;

    /// <summary>
    /// True when placing a tile at <c>(x, y, w, h)</c> would overlap any tile in
    /// <paramref name="tiles"/> other than the one being moved
    /// (<paramref name="movingId"/>). The moving tile is skipped so a drag that
    /// doesn't actually move still validates.
    /// </summary>
    public static bool Collides(
        IEnumerable<DashboardTile> tiles, string movingId, int x, int y, int w, int h)
    {
        foreach (var t in tiles)
        {
            if (t.Id == movingId) continue;
            if (Overlaps(t, x, y, w, h)) return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="tile"/> references <paramref name="formId"/>.
    /// Chart and latest-records tiles carry a FormId; markdown tiles don't and
    /// are never matched.
    /// </summary>
    public static bool ReferencesForm(DashboardTile tile, string formId) =>
        tile.Chart?.FormId == formId || tile.LatestRecords?.FormId == formId;

    /// <summary>
    /// All tiles except those referencing <paramref name="formId"/>. Used when a
    /// form is deleted to drop its now-orphan dashboard tiles. Returns a new list;
    /// the input is unchanged.
    /// </summary>
    public static List<DashboardTile> WithoutForm(
        IEnumerable<DashboardTile> tiles, string formId) =>
        tiles.Where(t => !ReferencesForm(t, formId)).ToList();

    /// <summary>
    /// First free top-left cell for a <c>w×h</c> tile, scanning row-major over the
    /// occupied region. Falls back to the row just below everything when no gap
    /// fits, so a new tile always lands somewhere valid.
    /// </summary>
    public static (int x, int y) FindFreeSlot(IReadOnlyList<DashboardTile> existing, int w, int h)
    {
        var bottom = existing.Count == 0 ? 0 : existing.Max(t => t.Y + t.H);
        for (var y = 0; y <= bottom; y++)
        {
            for (var x = 0; x + w <= Columns; x++)
            {
                var collides = false;
                foreach (var t in existing)
                {
                    if (Overlaps(t, x, y, w, h)) { collides = true; break; }
                }
                if (!collides) return (x, y);
            }
        }
        return (0, bottom);
    }

    /// <summary>
    /// Convert a pixel translation into integer cell-deltas for the current
    /// tile-host metrics. Returns (0,0) when the host hasn't laid out yet
    /// (non-positive cell width) so a no-op drag doesn't reshape the tile.
    /// </summary>
    /// <remarks>
    /// The host is <see cref="Columns"/> columns plus <c>Columns - 1</c> gaps of
    /// <paramref name="columnSpacing"/> wide; each row is <paramref name="rowHeight"/>
    /// tall plus a <paramref name="rowSpacing"/> gap. The spacing must be folded
    /// into the cell pitch or dragging one tile-height steps short by the spacing —
    /// the round trip would only reach the next cell at +cellH +rowSpacing. Keep
    /// <paramref name="rowHeight"/> in sync with the <c>GridLength</c> the view uses
    /// for its row definitions.
    /// </remarks>
    public static (int dx, int dy) CellDeltaFromTranslation(
        double hostWidth, double columnSpacing, double rowSpacing,
        double px, double py, double rowHeight = 10.0)
    {
        var cellW = (hostWidth - (Columns - 1) * columnSpacing) / Columns + columnSpacing;
        var cellH = rowHeight + rowSpacing;
        // hostWidth <= 0 is the real pre-layout signal (the original cellW <= 0
        // guard is unreachable for valid spacing — folding columnSpacing back in
        // keeps cellW positive for any non-negative width).
        if (hostWidth <= 0 || cellW <= 0) return (0, 0);
        return ((int)Math.Round(px / cellW), (int)Math.Round(py / cellH));
    }
}
