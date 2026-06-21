namespace DataMaker.Schema.Coachmarks;

/// <summary>
/// "Don't show again" memory for first-run coachmark overlays. Persisted as
/// JSON under <c>{LocalAppData}/FOBO/DataMaker/coachmarks.json</c>, a sibling
/// of <see cref="Backups.AgentSettings"/> / <see cref="Backups.BackupSettings"/>
/// — kept OUT of the encrypted DB on purpose so a dismissed hint stays
/// dismissed across a <c>.dmbak</c> restore, exactly like the other settings
/// files.
/// </summary>
public sealed record CoachmarkSettings
{
    /// <summary>
    /// Keys of coachmark overlays the user has explicitly dismissed via their
    /// "don't show again" link. A flat list keeps adding new coachmarks a
    /// one-string change with no schema churn.
    /// </summary>
    public List<string> Dismissed { get; init; } = new();

    /// <summary>
    /// Id of the one form tagged for the first-run onboarding coachmark chain
    /// (build → style → save → publish). Stamped once when the user creates
    /// their first-ever form; the coachmarks only ever appear on this form, so
    /// they can't reappear on later forms. Null = not yet stamped / cleared.
    /// </summary>
    public string? OnboardingFormId { get; init; }
}
