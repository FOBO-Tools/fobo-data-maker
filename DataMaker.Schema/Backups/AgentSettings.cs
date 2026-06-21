namespace DataMaker.Schema.Backups;

/// <summary>
/// User-tweakable background-agent configuration. Persisted as JSON under
/// <c>{LocalAppData}/FOBO/DataMaker/agent-settings.json</c>, sibling of
/// <see cref="BackupSettings"/>.
///
/// <para>
/// Kept in its own file (not folded into <see cref="BackupSettings"/>) so
/// the agent and backup features can evolve independently — e.g. someone
/// who only wants offline submission delivery shouldn't also need to
/// configure a backup folder.
/// </para>
/// </summary>
public sealed record AgentSettings
{
    /// <summary>
    /// Master switch. When true the agent is registered with the OS scheduler;
    /// when false the registration is removed. Defaults <c>true</c>: the agent
    /// powers backups, submission drain + cloud uploads while the app is closed,
    /// so it's provisioned by default (see <c>AgentProvisioner</c>). The only
    /// thing that suppresses auto-install is the user turning this off in
    /// Settings → Background, which persists <c>false</c>.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Wake interval in minutes. Each tick runs sync drain + backup-due
    /// check. Lower = lower submission latency when the app is closed,
    /// higher = lower battery / CPU cost.
    /// </summary>
    public int IntervalMinutes { get; init; } = 5;

    /// <summary>
    /// Whether the agent should poll the FOBO sync API. False = agent only
    /// runs backup-due-check, never touches submissions. Defaults true so
    /// "enable the agent" gets the obvious user expectation (catch
    /// submissions while app closed).
    /// </summary>
    public bool SyncEnabled { get; init; } = true;
}
