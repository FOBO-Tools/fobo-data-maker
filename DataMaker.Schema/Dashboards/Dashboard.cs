namespace DataMaker.Schema.Dashboards;

/// <summary>
/// Single install-wide home-screen dashboard. Hides behind a drag handle at
/// the bottom of the HomePage; the user pulls it up to reveal a grid of
/// tiles that pull data from any form.
///
/// <para>
/// One dashboard per install (id = "default"), persisted as a JSON blob in
/// the SQLite <c>dashboards</c> table. Tiles live in a flat list; the grid
/// is purely a 24-column layout reference (V1 — see <see cref="Version"/>)
/// resolved at render time from each tile's <c>X / Y / W / H</c>. V0 was
/// 12-col; HomeViewModel.LoadAsync auto-doubles V0 layouts on first read.
/// </para>
/// </summary>
public sealed record Dashboard
{
    /// <summary>"default" for the install-wide dashboard. Reserved for future per-form dashboards.</summary>
    public string Id { get; init; } = "default";

    public string Title { get; init; } = "Dashboard";

    public List<DashboardTile> Tiles { get; init; } = new();

    /// <summary>
    /// Last-applied open fraction of the drag handle (0 = fully collapsed,
    /// 1 = fully expanded). Persisted alongside tile layout so the slider
    /// position survives navigation + app restart. Drag-completion +
    /// tap-toggle write this; first-load applies it before the user sees
    /// the page.
    /// </summary>
    public double OpenFraction { get; init; }

    /// <summary>
    /// Layout schema version. 0 = pre-2026-05-21 "12-column × 80-dip-row"
    /// (later 40, then 20) grid. 1 = "24-column × 10-dip-row" grid where
    /// every tile X/Y/W/H was multiplied by 2 to preserve the same on-
    /// screen size while halving the drag-resize step.
    /// HomeViewModel.LoadAsync migrates Version 0 → 1 in-place on first
    /// read; default-zero on the JSON blob is what triggers it for
    /// existing dashboards.
    /// </summary>
    public int Version { get; init; }
}

/// <summary>
/// One cell in the dashboard grid. Each tile picks ONE payload slot via
/// <see cref="Kind"/>; the other payloads stay null. Same shape as
/// <c>FieldDefinition</c> (Kind + per-kind slot) so adding tile kinds later
/// only requires a new optional payload property.
/// </summary>
public sealed record DashboardTile
{
    /// <summary>GUID, stable across edits. Used as the key for re-render diffs.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("n");

    public DashboardTileKind Kind { get; init; } = DashboardTileKind.Markdown;

    /// <summary>0..23 grid column (Dashboard.Version >= 1).</summary>
    public int X { get; init; }
    /// <summary>0..(N-1) grid row.</summary>
    public int Y { get; init; }
    /// <summary>1..24 grid column span (Dashboard.Version >= 1).</summary>
    public int W { get; init; } = 8;
    /// <summary>1..N grid row span (Dashboard.Version >= 1).</summary>
    public int H { get; init; } = 4;

    public MarkdownTilePayload?      Markdown      { get; init; }
    public ChartTilePayload?         Chart         { get; init; }
    public LatestRecordsTilePayload? LatestRecords { get; init; }
}

public enum DashboardTileKind
{
    Markdown,
    Chart,
    LatestRecords,
}

/// <summary>Markdown / plain-text tile. Renders <see cref="Text"/> as-is for V1.</summary>
public sealed record MarkdownTilePayload(string Text);

/// <summary>
/// Reference to a chart from <see cref="Schema.Charts.ChartDefinition"/> by
/// id. The tile renders the chart inline; styling + chart definition are
/// owned by the chart store.
/// </summary>
public sealed record ChartTilePayload(string FormId, string ChartId);

/// <summary>
/// "Latest X records of form Y" table tile. No filter / sort surface in V1 —
/// just a top-N most-recent slice, oldest pinned at the bottom.
/// </summary>
public sealed record LatestRecordsTilePayload(string FormId, int Count = 10);
