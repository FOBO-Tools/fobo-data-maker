namespace DataMaker.Schema.Backups;

/// <summary>
/// User-tweakable backup configuration. Persisted as JSON under
/// <c>{LocalAppData}/FOBO/DataMaker/backup-settings.json</c> — NOT in the
/// SQLite DB on purpose: if the DB is the thing being restored, the settings
/// must survive that restore. JSON-on-disk also makes a manual sanity-check
/// trivial during incident response.
/// </summary>
public sealed record BackupSettings
{
    /// <summary>Master switch for the scheduler timer.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Resolved absolute path of the folder where .dmbak files are written —
    /// the built-in <see cref="DefaultFolder"/> when <see cref="FolderMode"/> is
    /// <see cref="BackupFolderMode.Default"/>, otherwise <see cref="CustomFolder"/>.
    /// Kept as the effective path so the scheduler/service/cache read one field.
    /// Empty only when the user chose Custom but hasn't picked a folder yet; the
    /// scheduler is a no-op while empty.
    /// </summary>
    public string TargetFolder { get; init; } = "";

    /// <summary>Whether backups go to the built-in default folder or one the
    /// user picked.</summary>
    public BackupFolderMode FolderMode { get; init; } = BackupFolderMode.Default;

    /// <summary>The user's chosen folder, remembered even while Default is active
    /// so switching back to Custom restores it. Only takes effect when
    /// <see cref="FolderMode"/> is <see cref="BackupFolderMode.Custom"/>.</summary>
    public string CustomFolder { get; init; } = "";

    /// <summary>The built-in default backup folder:
    /// <c>{LocalAppData}/FOBO/DataMaker/backups</c>.</summary>
    public static string DefaultFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FOBO", "DataMaker", "backups");

    public BackupCadence Cadence { get; init; } = BackupCadence.Daily;

    /// <summary>
    /// Local-time hour for <see cref="BackupCadence.Daily"/> /
    /// <see cref="BackupCadence.Weekly"/>. 0–23.
    /// </summary>
    public int Hour { get; init; } = 2;

    /// <summary>Local-time minute. 0–59.</summary>
    public int Minute { get; init; }

    /// <summary>
    /// Day-of-week for <see cref="BackupCadence.Weekly"/>. Ignored otherwise.
    /// </summary>
    public DayOfWeek WeeklyDay { get; init; } = DayOfWeek.Sunday;

    /// <summary>
    /// Rotation policy: how many daily snapshots to keep. Older daily
    /// snapshots are deleted after each successful run.
    /// </summary>
    public int KeepDaily { get; init; } = 7;

    /// <summary>
    /// Rotation policy: how many additional weekly snapshots to keep. One
    /// snapshot per ISO week beyond the daily window is preserved; the rest
    /// are deleted.
    /// </summary>
    public int KeepWeekly { get; init; } = 4;

    /// <summary>
    /// UTC timestamp of the last successful run. Used by the scheduler to
    /// decide whether the next run is due and by the Home chip to render
    /// "Last backup: X ago".
    /// </summary>
    public string? LastRunUtc { get; init; }

    /// <summary>
    /// Error from the last attempted run, if it failed. Null on success.
    /// Surfaced in the settings UI so silent failures are visible.
    /// </summary>
    public string? LastRunError { get; init; }

    // EncryptionEnabled removed (#76(b)) — backups are always sealed with the
    // user's recovery code (RecoveryBundleFormat). There is no unencrypted
    // export mode. Old persisted settings carrying this field deserialize
    // fine (the extra property is ignored).

    /// <summary>
    /// Optional secondary destination uploaded right after every successful
    /// local snapshot. One provider at a time. <c>None</c> means
    /// "local-folder only" — the baseline behaviour before #33 closed.
    /// </summary>
    public CloudBackupConfig Cloud { get; init; } = new();
}

public enum BackupCadence
{
    Daily,
    Hourly,
    Weekly,
}

/// <summary>Where backups are written: the built-in default folder, or a
/// folder the user picks.</summary>
public enum BackupFolderMode
{
    Default,
    Custom,
}

/// <summary>
/// Where the cloud-upload step should push the <c>.dmbak</c> after a
/// successful local run. Credentials never live here — provider-specific
/// secrets stay in <see cref="DataMaker.KeyVault.IKeyVault"/> keyed by
/// <see cref="DataMaker.KeyVault.KeyVaultKeys"/> entries.
/// </summary>
public enum CloudBackupProvider
{
    /// <summary>Disabled (default). Only local-folder snapshots run.</summary>
    None,

    /// <summary>
    /// User-supplied S3-compatible bucket (AWS S3, Backblaze B2,
    /// Cloudflare R2, Wasabi, MinIO, etc.). Auth via access key + secret
    /// stored in the OS keychain.
    /// </summary>
    S3Compatible,

    /// <summary>
    /// FOBO-hosted bucket exposed via the DataMaker API (Cognito JWT).
    /// 1 GB hard cap per user; older snapshots rotate off when full.
    /// </summary>
    FoboCloud,

    /// <summary>
    /// SFTP over SSH. Covers rsync.net, Hetzner Storage Box, Synology,
    /// any vanilla SSH server. Auth = password or private key. Bundles
    /// land as plain files under <see cref="SftpBackupConfig.RemotePath"/>.
    /// </summary>
    Sftp,
}

public sealed record CloudBackupConfig
{
    public CloudBackupProvider Provider { get; init; } = CloudBackupProvider.None;

    /// <summary>Last cloud-upload outcome. Empty on a fresh install or after a clean run.</summary>
    public string? LastUploadError { get; init; }

    /// <summary>UTC timestamp of the last successful cloud upload.</summary>
    public string? LastUploadAtUtc { get; init; }

    /// <summary>S3-specific config; ignored when <see cref="Provider"/> != S3Compatible.</summary>
    public S3BackupConfig S3 { get; init; } = new();

    /// <summary>FOBO Cloud config; ignored when <see cref="Provider"/> != FoboCloud.</summary>
    public FoboCloudBackupConfig Fobo { get; init; } = new();

    /// <summary>SFTP config; ignored when <see cref="Provider"/> != Sftp.</summary>
    public SftpBackupConfig Sftp { get; init; } = new();
}

public sealed record S3BackupConfig
{
    /// <summary>
    /// HTTPS service endpoint. Empty falls back to AWS for the configured
    /// <see cref="Region"/>. Required for non-AWS S3-compatible vendors
    /// (e.g. <c>https://s3.us-west-002.backblazeb2.com</c>).
    /// </summary>
    public string Endpoint { get; init; } = "";

    public string Bucket { get; init; } = "";

    /// <summary>S3 region. Required by AWSSDK even for endpoint-overridden providers — pick any (e.g. <c>auto</c> for R2).</summary>
    public string Region { get; init; } = "us-east-1";

    /// <summary>Optional key prefix inside the bucket so multiple devices / apps can share a bucket without collisions.</summary>
    public string Prefix { get; init; } = "datamaker/";
}

public sealed record FoboCloudBackupConfig
{
    /// <summary>Bytes consumed in FOBO Cloud (sum of all snapshots for this user). Refreshed after every upload.</summary>
    public long UsedBytes { get; init; }

    /// <summary>Bytes the user is allowed. Hard cap for v1 = 1 GB; refreshed from <c>GET /backups/quota</c>.</summary>
    public long QuotaBytes { get; init; }
}

public sealed record SftpBackupConfig
{
    public string Host { get; init; } = "";

    public int Port { get; init; } = 22;

    public string Username { get; init; } = "";

    /// <summary>
    /// Absolute or home-relative remote path where <c>.dmbak</c> files
    /// land. Default lives under the user's home dir to avoid surprising
    /// the system admin.
    /// </summary>
    public string RemotePath { get; init; } = "datamaker-backups";

    public SftpAuthMode AuthMode { get; init; } = SftpAuthMode.Password;
}

public enum SftpAuthMode
{
    /// <summary>Username + password. Password stored in <see cref="DataMaker.KeyVault.IKeyVault"/>.</summary>
    Password,

    /// <summary>
    /// Username + private key (optionally encrypted). Private-key bytes
    /// + optional passphrase stored in <see cref="DataMaker.KeyVault.IKeyVault"/>.
    /// </summary>
    PrivateKey,
}
