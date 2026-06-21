using System.Text.Json.Serialization;

namespace DataMaker.Schema.Styling;

/// <summary>
/// Form-level style record. Extends per-element <see cref="Style"/> with the
/// extra surface a form needs to look complete on its own: a Light + Dark
/// color palette, typography defaults beyond the base font, structural
/// spacing, and a field-border thickness.
///
/// <para>
/// Themes are NOT a runtime concern — they're stored presets that get *applied*
/// (copied) into a form's <see cref="FormStyle"/> by the styling tab. Once
/// applied the values live here and the form renders without ever looking at
/// the source theme. See <see cref="ThemePreset"/> for the wire format.
/// </para>
///
/// <para>
/// Cascade for per-element styling stays the same:
/// <c>FormStyle (typography base) → Section.Style → Group.Style → Field.Style</c>.
/// Palette tokens (<see cref="Colors"/> / <see cref="DarkColors"/>) feed the
/// renderer's resource injection so system controls (TextBox, ComboBox, etc.)
/// pick up the form's brand without per-control overrides.
/// </para>
/// </summary>
public sealed record FormStyle : Style
{
    /// <summary>Light-mode palette. Always populated for new forms (FOBO default).</summary>
    public StylePalette? Colors { get; init; }

    /// <summary>Dark-mode palette. Always populated for new forms (FOBO default).</summary>
    public StylePalette? DarkColors { get; init; }

    /// <summary>
    /// Form's default display mode. Set by the Styling tab's Light/Dark
    /// toggle so the choice survives save/reopen. Null = no opinion;
    /// renderers fall back to Light (matches pre-field behaviour). Future
    /// runtime work can layer "follow system theme" on top.
    /// </summary>
    public StyleMode? PreferredMode { get; init; }

    // ── Typography defaults (above + beyond Style.FontFamily/FontSize/FontWeight) ──
    public double? LabelFontSize       { get; init; }
    public double? DescriptionFontSize { get; init; }

    // ── Spacing defaults ──
    public double? FieldSpacing   { get; init; }
    public double? SectionSpacing { get; init; }

    // ── Shape defaults ──
    public double? FieldBorderThickness { get; init; }

    // ── Heading defaults (per level, 1 = largest, 4 = smallest) ──
    //
    // These are the theme-supplied baseline for each HeadingColumn level.
    // A HeadingColumn's per-instance Style merges field-by-field on top —
    // null props on the instance fall through to the corresponding level
    // default, then through the rest of the FormStyle cascade. Null at this
    // level means "no theme opinion": the renderer falls back to a built-in
    // sensible default per level (defined in the renderer, not the schema,
    // so themes don't have to ship all four to be valid).
    public Style? Heading1Style { get; init; }
    public Style? Heading2Style { get; init; }
    public Style? Heading3Style { get; init; }
    public Style? Heading4Style { get; init; }

    // ── Button-variant defaults ──
    //
    // Each variant ships Base / Hover / Pressed style slices plus optional
    // default icon. A ButtonColumn instance picks a variant (Primary / Secondary
    // / Subtle) and inherits the corresponding bundle; per-instance Style /
    // HoverStyle / PressedStyle / icon fields override field-by-field. Null at
    // this level means "no theme opinion" — the renderer falls back to a
    // built-in baseline per variant (accent-fill / outline / text-only).
    public ButtonDefaults? PrimaryButtonDefaults   { get; init; }
    public ButtonDefaults? SecondaryButtonDefaults { get; init; }
    public ButtonDefaults? SubtleButtonDefaults    { get; init; }

    // ── Step bar (multi-step wizard) ──
    //
    // Visual styling for the numbered step bar a multi-step form renders —
    // position (top / bottom), badge figure shape, colors, font. Null = use the
    // palette/theme defaults (accent fill for current + completed badges, muted
    // outline for upcoming, ink labels). Ignored by single-step forms.
    public StepBarStyle? StepBar { get; init; }

    /// <summary>
    /// Project this form-level style down to a per-element <see cref="Style"/>
    /// carrying ONLY the properties that should cascade — typography + text
    /// formatting. Container chrome (background, border, padding, margin) is
    /// intentionally excluded; those are own-level at every cascade tier.
    /// Used by the renderer as the form-base entry in the per-element resolve
    /// chain so form-level TextColor / FontFamily / etc. flow into sections,
    /// groups, fields, and headings.
    /// </summary>
    public Style ToBaseStyle() => new()
    {
        FontFamily    = FontFamily,
        FontSize      = FontSize,
        FontWeight    = FontWeight,
        TextColor     = TextColor,
        TextAlignment = TextAlignment,
        LineHeight    = LineHeight,
        LetterSpacing = LetterSpacing,
    };
}

/// <summary>
/// Visual styling for the multi-step wizard's numbered step bar. Every member is
/// an optional override on top of the palette/theme defaults — null means "use
/// the renderer default" (current/completed = accent fill, upcoming = muted
/// outline, labels in ink). Lives on <see cref="FormStyle.StepBar"/>.
/// </summary>
public sealed record StepBarStyle
{
    /// <summary>Where the bar renders relative to the step content. Null = Top.</summary>
    public StepBarPosition? Position { get; init; }

    /// <summary>Badge shape around each step number. Null = Circle.</summary>
    public StepFigureShape? Shape { get; init; }

    /// <summary>Fill for the current + completed badges. Null = palette accent.</summary>
    public string? ActiveColor { get; init; }

    /// <summary>Outline + text for upcoming badges/labels. Null = palette muted ink.</summary>
    public string? InactiveColor { get; init; }

    /// <summary>Connector line color. Null = falls back to InactiveColor / muted ink.</summary>
    public string? ConnectorColor { get; init; }

    /// <summary>Active step's label text color. Null = palette ink.</summary>
    public string? LabelColor { get; init; }

    /// <summary>Label + number font family. Null = inherit the form font.</summary>
    public string? FontFamily { get; init; }

    /// <summary>Label font size in px. Null = renderer default.</summary>
    public double? FontSize { get; init; }

    /// <summary>Gap (px) between the bar and the step content. Null = renderer default (~4).</summary>
    public double? Margin { get; init; }

    /// <summary>Render the step bar at all. Null = true; false hides it entirely (steps still navigate via Back/Next).</summary>
    public bool? ShowBar { get; init; }

    /// <summary>Draw connector lines between badges. Null = true.</summary>
    public bool? ShowConnectors { get; init; }

    /// <summary>Show step titles next to badges. Null = true (false = badges only).</summary>
    public bool? ShowLabels { get; init; }
}

/// <summary>Where the wizard step bar sits relative to the active step's content.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StepBarPosition>))]
public enum StepBarPosition { Top, Bottom }

/// <summary>Figure drawn around each step number on the wizard step bar.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StepFigureShape>))]
public enum StepFigureShape { Circle, Square, Rounded, Diamond, Star }

/// <summary>
/// Color palette for one mode (light or dark). Hex strings so the JSON stays
/// human-readable and easy to hand-edit; the renderer parses them.
/// </summary>
public sealed record StylePalette
{
    /// <summary>Primary brand/interactive color. Drives focus borders, toggle/checkbox fills, submit button.</summary>
    public string? AccentColor { get; init; }

    /// <summary>Validation error color for text + error-state borders.</summary>
    public string? ErrorColor { get; init; }

    /// <summary>Form background / document surface.</summary>
    public string? PaperColor { get; init; }

    /// <summary>Primary text color.</summary>
    public string? InkColor { get; init; }

    /// <summary>Secondary/muted text (descriptions, placeholders).</summary>
    public string? MutedInkColor { get; init; }

    /// <summary>Input field background fill.</summary>
    public string? FieldFillColor { get; init; }

    /// <summary>Input field border at rest.</summary>
    public string? FieldBorderColor { get; init; }

    /// <summary>Group card background.</summary>
    public string? GroupSurfaceColor { get; init; }

    /// <summary>Group card border (falls back to FieldBorderColor in renderer).</summary>
    public string? GroupBorderColor { get; init; }

    /// <summary>Horizontal rule / divider color.</summary>
    public string? DividerColor { get; init; }

    /// <summary>Input-group addon background (e.g. currency prefix).</summary>
    public string? AddonBackgroundColor { get; init; }

    // ── Per-level heading text colors (mode-specific) ─────────────────
    //
    // FormStyle.HeadingNStyle carries typography (size/weight/spacing) that's
    // the same in Light + Dark. Color must differ per mode, so it lives on the
    // palette like InkColor. Cascade at render time:
    //   HeadingColumn instance Style.TextColor
    //   → FormStyle.HeadingNStyle.TextColor (theme-level override)
    //   → palette.HeadingNColor       ← this token
    //   → palette.InkColor            (existing fallback)
    public string? Heading1Color { get; init; }
    public string? Heading2Color { get; init; }
    public string? Heading3Color { get; init; }
    public string? Heading4Color { get; init; }
}
