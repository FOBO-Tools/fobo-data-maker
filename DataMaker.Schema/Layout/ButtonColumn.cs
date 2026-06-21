using System.Text.Json.Serialization;
using DataMaker.Schema.Styling;

namespace DataMaker.Schema.Layout;

/// <summary>
/// A clickable action button placed inside a <see cref="Row"/>. Like
/// <see cref="HeadingColumn"/> / <see cref="DividerColumn"/> / <see cref="ImageColumn"/>
/// it carries no record data — instead, clicks raise <c>FormInstance.ActionInvoked</c>
/// with this button's <see cref="Name"/>. Hosting pages decide what the event
/// means (save record, POST to webhook, navigate, agent call, etc.); the
/// schema only persists the button's identity and look.
///
/// <para>
/// <b>Variant cascade:</b> each form-style defines defaults for Primary /
/// Secondary / Subtle via <see cref="FormStyle.PrimaryButtonDefaults"/> et al.
/// The button picks a variant; its per-instance <see cref="Style"/> /
/// <see cref="HoverStyle"/> / <see cref="PressedStyle"/> merge on top of the
/// variant defaults field-by-field (null falls through).
/// </para>
/// </summary>
public sealed record ButtonColumn : Column
{
    /// <summary>Stable id, mirrors <see cref="GroupColumn.Id"/>.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Event identifier emitted on click. snake_case; unique within the form.</summary>
    public required string Name { get; init; }

    /// <summary>Display text on the button.</summary>
    public required string Label { get; init; }

    /// <summary>Which variant default set to inherit from.</summary>
    public ButtonVariant Variant { get; init; } = ButtonVariant.Primary;

    /// <summary>
    /// Built-in action triggered on click. <see cref="ButtonAction.None"/>
    /// (default) just raises <c>FormInstance.ActionInvoked</c> so a custom
    /// host can wire arbitrary logic on <see cref="Name"/>; the named actions
    /// execute their effect through <c>FormInstance</c> and ALSO raise
    /// ActionInvoked afterwards (or before, on Reset) so hosts can observe.
    ///
    /// <para>
    /// Implementation is per-renderer / per-host: the desktop editor writes the
    /// record locally, while a published web bundle posts it to the submissions
    /// endpoint — same schema action, different runtime side effect.
    /// </para>
    /// </summary>
    public ButtonAction Action { get; init; } = ButtonAction.None;

    /// <summary>Optional Font Awesome glyph rendered next to the label.</summary>
    public string? IconGlyph { get; init; }

    /// <summary>Side the icon sits on. Defaults to Left if not set in variant defaults either.</summary>
    public ButtonIconPosition? IconPosition { get; init; }

    /// <summary>
    /// Optional inline image (data: URI or HTTP(S) URL) shown next to the
    /// label — separate from <see cref="Style"/>'s <c>BackgroundImage</c>.
    /// Use for branded buttons ("Sign in with X").
    /// </summary>
    public string? InlineImageSrc { get; init; }

    /// <summary>Side the inline image sits on. Defaults to Left.</summary>
    public ButtonIconPosition? InlineImagePosition { get; init; }

    /// <summary>Optional gating expression. Null = always visible.</summary>
    public string? VisibleWhen { get; init; }

    /// <summary>Per-instance base-state override on top of the variant default's base style.</summary>
    public Style? Style { get; init; }

    /// <summary>Per-instance :hover-state override on top of the variant default's hover style.</summary>
    public Style? HoverStyle { get; init; }

    /// <summary>Per-instance :active-state override on top of the variant default's pressed style.</summary>
    public Style? PressedStyle { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ButtonVariant>))]
public enum ButtonVariant
{
    Primary   = 0,
    Secondary = 1,
    Subtle    = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<ButtonIconPosition>))]
public enum ButtonIconPosition
{
    Left  = 0,
    Right = 1,
}

/// <summary>
/// Built-in side effect executed when a <see cref="ButtonColumn"/> is clicked.
/// The schema declares intent; the renderer / host wires the actual
/// implementation through <c>FormInstance</c> and the configured record store.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ButtonAction>))]
public enum ButtonAction
{
    /// <summary>No built-in action — just raises ActionInvoked so a custom host can react.</summary>
    None    = 0,

    /// <summary>Final submit: validate + persist (insert or update) + raise the Submitted event so the host can close / navigate.</summary>
    Submit  = 1,

    /// <summary>Persist current values without finalizing. Same record store call as Submit; no Submitted event.</summary>
    Save    = 2,

    /// <summary>Reset every visible field to its DefaultValue (or empty when no default).</summary>
    Reset   = 3,

    /// <summary>Multi-step wizard: advance to the next step (validating the current step first). No-op on a single-step form.</summary>
    NextStep = 4,

    /// <summary>Multi-step wizard: go back to the previous step. No-op on the first step / a single-step form.</summary>
    PrevStep = 5,
}
