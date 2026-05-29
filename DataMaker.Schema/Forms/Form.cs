using DataMaker.Schema.Fields;
using DataMaker.Schema.Styling;

namespace DataMaker.Schema.Forms;

/// <summary>
/// Top-level form definition. One form = one records table in storage.
/// Fields are declared once in <see cref="Fields"/>; layout references them
/// by id from <see cref="Steps"/> → Sections → Rows → Columns. This separation
/// allows re-ordering layout without touching stored data.
/// </summary>
public sealed record Form
{
    /// <summary>Stable id; becomes the storage table seed. snake_case, SQL-safe.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// Bumped on every breaking change (field removal, kind change). Stored on
    /// each record at write time so migrations can locate outdated rows.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    public List<FieldDefinition> Fields { get; init; } = new();

    public List<FormStep> Steps { get; init; } = new();

    /// <summary>
    /// Form-level visual identity — palette (Light + Dark), typography
    /// defaults, spacing, shape, plus the same per-element style fields as
    /// <see cref="Schema.Styling.Style"/> for form-level chrome (background
    /// image / gradient / shadow). Themes are presets that get applied
    /// (copied) into this; once applied, the form renders without ever
    /// looking at the source theme. New forms initialize from
    /// <see cref="Schema.Styling.BuiltInThemes.DefaultFormStyle"/>.
    /// </summary>
    public FormStyle? Style { get; init; }

    /// <summary>
    /// Names of <see cref="FormStyle"/> properties the user has explicitly
    /// edited. When a theme is applied via the styling tab, listed properties
    /// are skipped so the user's intentional overrides survive theme swaps.
    /// </summary>
    public List<string> LockedStyleFields { get; init; } = new();

    /// <summary>
    /// Who's allowed to submit this form. Default <see cref="SubmitPolicy.Anonymous"/>
    /// matches the original public-survey semantics; setting
    /// <see cref="SubmitPolicy.Authenticated"/> forces submitters through a
    /// FOBO Cognito sign-in before they can POST and stamps their sub on
    /// the envelope as <c>SubmitterId</c>. See <see cref="Forms.SubmitPolicy"/>
    /// for the full enforcement story.
    /// </summary>
    public SubmitPolicy SubmitPolicy { get; init; } = SubmitPolicy.Anonymous;

    /// <summary>
    /// Form-level customizable messages, keyed by canonical slot id. Mirrors
    /// the per-field <see cref="FieldDefinition.Messages"/> pattern so every
    /// renderer (web / WP / Wasm / Uno / PDF) reads from the same source and
    /// can be translated without code changes.
    ///
    /// <para>
    /// Known slot ids (see <see cref="Schema.Validation.FormMessages"/>):
    /// <list type="bullet">
    ///   <item><c>"validationBanner"</c> — banner shown above the submit row
    ///         when at least one touched field is invalid.</item>
    /// </list>
    /// Missing key or empty value = engine default. Empty/whitespace falls
    /// through so a blanked entry doesn't silently kill the message.
    /// </para>
    /// </summary>
    public Dictionary<string, string>? Messages { get; init; }
}
