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

    /// <summary>
    /// Form-wide default style for every field's label (font / size / weight /
    /// colour / margin / alignment). A field's <see cref="DataMaker.Schema.Fields.FieldDefinition.LabelStyle"/>
    /// merges on top per-property (the heading-defaults pattern). Null = no
    /// opinion; renderers fall back to <see cref="LabelFontSize"/> then their
    /// built-in label defaults. <see cref="Style.FontSize"/> here, when set,
    /// supersedes the legacy <see cref="LabelFontSize"/>.
    /// </summary>
    public Style? LabelStyle { get; init; }

    // ── Spacing defaults ──
    public double? FieldSpacing   { get; init; }
    public double? SectionSpacing { get; init; }

    // ── Shape defaults ──
    public double? FieldBorderThickness { get; init; }

    // ── Field label placement ──
    //
    // Where a field's label sits relative to its input. Top = stacked (default,
    // current behaviour). Left = inline label column. Floating = label rides
    // inside the field and lifts on focus/fill (Material-style). Null = Top.
    public FieldLabelPosition? LabelPosition { get; init; }

    // ── Field-state shape ──

    /// <summary>Focus ring thickness in px. Null = renderer default (~2).</summary>
    public double? FocusRingWidth { get; init; }

    /// <summary>Marker drawn after required-field labels. Null = "*". Empty string hides it.</summary>
    public string? RequiredMarkerGlyph { get; init; }

    // ── Layout ──

    /// <summary>Max width of the form card in px (content is centred). Null = renderer default.</summary>
    public double? MaxWidth { get; init; }

    /// <summary>
    /// Control density — scales field padding + row spacing as one knob.
    /// Compact / Comfortable / Spacious. Null = Comfortable (current spacing).
    /// </summary>
    public FormDensity? Density { get; init; }

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

    /// <summary>Color of the number / check drawn inside the current + completed badges. Null = palette paper.</summary>
    public string? OnActiveColor { get; init; }

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

/// <summary>Where a field's label sits relative to its input control.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FieldLabelPosition>))]
public enum FieldLabelPosition { Top, Left, Floating }

/// <summary>Control-density preset — one knob over field padding + row spacing.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FormDensity>))]
public enum FormDensity { Compact, Comfortable, Spacious }

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

    // ── Field-state tokens (mode-specific) ────────────────────────────
    //
    // Each is an optional override on top of a renderer fallback so a theme
    // only ships what it wants to brand. Null → the renderer derives a sane
    // value (focus = accent, placeholder = muted ink, required = error, etc.)
    // so older themes keep their look.

    /// <summary>Placeholder text colour. Null → falls back to MutedInkColor.</summary>
    public string? PlaceholderColor { get; init; }

    /// <summary>Input focus ring / focus border colour. Null → AccentColor.</summary>
    public string? FocusRingColor { get; init; }

    /// <summary>Field border on hover (pre-focus affordance). Null → AccentColor.</summary>
    public string? FieldHoverBorderColor { get; init; }

    /// <summary>Required-field marker (asterisk) colour. Null → ErrorColor.</summary>
    public string? RequiredMarkerColor { get; init; }

    /// <summary>Disabled / read-only field fill. Null → FieldFillColor.</summary>
    public string? DisabledFillColor { get; init; }

    /// <summary>Disabled / read-only field text. Null → MutedInkColor.</summary>
    public string? DisabledInkColor { get; init; }

    // ── Layout + extra colours ────────────────────────────────────────

    /// <summary>Page surface behind the form card. Null → PaperColor (card == page).</summary>
    public string? PageBackgroundColor { get; init; }

    /// <summary>Success / valid-state colour (validation OK ticks, success banners). Null → renderer green.</summary>
    public string? SuccessColor { get; init; }

    /// <summary>Hyperlink colour in rich-text + descriptions. Null → AccentColor.</summary>
    public string? LinkColor { get; init; }
}
