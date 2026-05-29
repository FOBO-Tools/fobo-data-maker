namespace DataMaker.Schema.Backups;

/// <summary>
/// Metadata embedded as <c>manifest.json</c> inside every .dmbak snapshot zip.
/// Lets the restore flow preview a snapshot (counts, version, age) before
/// overwriting the live DB, and gives a SHA-256 to detect tampering or
/// truncated downloads when cloud-sync arrives.
/// </summary>
/// <param name="ManifestVersion">
/// Format version of the manifest itself — separate from app/schema versions
/// so we can extend manifest fields without bumping the bundle format.
/// </param>
/// <param name="AppVersion">
/// `AssemblyName.Version` of the DataMaker shell that wrote the snapshot.
/// </param>
/// <param name="SchemaVersion">
/// SQLite `PRAGMA user_version` at the time of backup. Restore refuses to
/// overwrite a newer DB with an older snapshot unless the user opts in.
/// </param>
/// <param name="CreatedAtUtc">ISO-8601 timestamp of the backup run.</param>
/// <param name="Trigger">
/// "manual" for ad-hoc, "scheduled" for timer-driven runs. Drives the UI
/// label in the snapshot list.
/// </param>
/// <param name="FormCount">Total rows in <c>forms</c> at backup time.</param>
/// <param name="RecordCount">Total rows across every per-form record table.</param>
/// <param name="ThemeCount">Total rows in <c>themes</c>.</param>
/// <param name="ChartCount">Total rows in <c>charts</c>.</param>
/// <param name="DashboardCount">Total rows in <c>dashboards</c>.</param>
/// <param name="DbSizeBytes">Byte size of the snapshotted .db inside the zip.</param>
/// <param name="DbSha256">Lower-case hex SHA-256 of the .db file before zipping.</param>
public sealed record BackupManifest(
    int      ManifestVersion,
    string   AppVersion,
    long     SchemaVersion,
    string   CreatedAtUtc,
    string   Trigger,
    int      FormCount,
    int      RecordCount,
    int      ThemeCount,
    int      ChartCount,
    int      DashboardCount,
    long     DbSizeBytes,
    string   DbSha256);
