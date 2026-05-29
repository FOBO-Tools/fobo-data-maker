namespace DataMaker.Schema.Backups;

/// <summary>
/// Wire shapes for the <c>/backups/*</c> endpoints on DataMaker.Lambda.Api.
/// Shared between desktop client + Lambda so a schema drift is a compile
/// error, not a runtime 400.
/// </summary>
public sealed record CloudBackupListItem(
    string   FileName,
    long     SizeBytes,
    DateTime LastModifiedUtc);

public sealed record CloudBackupListResponse(
    IReadOnlyList<CloudBackupListItem> Items,
    long                                UsedBytes,
    long                                QuotaBytes);

public sealed record CloudBackupQuotaResponse(
    long UsedBytes,
    long QuotaBytes);

public sealed record CloudBackupDownloadResponse(
    string Url,
    int    ExpiresInSeconds);
