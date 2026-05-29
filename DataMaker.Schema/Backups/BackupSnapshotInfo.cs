namespace DataMaker.Schema.Backups;

/// <summary>
/// Runtime view of a .dmbak file discovered in the target folder. Built by
/// reading just the manifest entry from the zip; no full unzip.
/// </summary>
public sealed record BackupSnapshotInfo(
    string          FilePath,
    string          FileName,
    long            FileSizeBytes,
    BackupManifest? Manifest);
