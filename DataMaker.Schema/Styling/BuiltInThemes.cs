namespace DataMaker.Schema.Styling;

/// <summary>
/// Built-in themes seeded into the theme store on install. The store's
/// SeedAsync upsert leaves user-created themes alone; built-in updates
/// replace any built-in with the same id (so DataMaker can ship palette
/// tweaks across versions without dropping user themes).
///
/// <para>
/// "Default" is the FOBO baseline every new form starts from — the
/// renderer expects every form to have both Light and Dark palettes
/// populated, so a freshly created form deep-copies <see cref="Default"/>
/// into its <see cref="FormStyle"/>.
/// </para>
/// </summary>
public static class BuiltInThemes
{
    /// <summary>FOBO baseline — natural white paper, cyan accent, modest borders.</summary>
    public static ThemePreset Default { get; } = new()
    {
        Id          = "default",
        Name        = "Default",
        Description = "FOBO baseline — paper-white surface, cyan accent, balanced spacing.",
        IsBuiltIn   = true,
        Style       = DefaultFormStyle(),
    };

    // Declared after Default so the static initializer sees a real instance —
    // C# static field init runs top-to-bottom; forward refs come out as
    // default(T) (i.e. null), which exploded SeedAsync on first run. The store's
    // SeedAsync prunes any built-in row whose id is no longer in this list, so
    // retiring a preset here removes it from existing libraries on next launch.
    public static IReadOnlyList<ThemePreset> All { get; } = new[]
    {
        Default,
    };

    /// <summary>Public so the renderer + form bootstrap can deep-copy these
    /// values when a new form is created.</summary>
    public static FormStyle DefaultFormStyle() => new()
    {
        Colors = new StylePalette
        {
            AccentColor          = "#04C8FF",
            ErrorColor           = "#c92a2a",
            PaperColor           = "#FFFFFF",
            InkColor             = "#000000",
            MutedInkColor        = "#5C5C5C",
            FieldFillColor       = "#F9F9F9",
            FieldBorderColor     = "#D0D0D0",
            GroupSurfaceColor    = "#FFFFFF",
            GroupBorderColor     = "#D0D0D0",
            DividerColor         = "#E5E5E5",
            AddonBackgroundColor = "#f1f3f5",
        },
        DarkColors = new StylePalette
        {
            AccentColor          = "#04C8FF",
            ErrorColor           = "#ff6b6b",
            PaperColor           = "#1E1E1E",
            InkColor             = "#E6E6E6",
            MutedInkColor        = "#9A9A9A",
            FieldFillColor       = "#2A2A2A",
            FieldBorderColor     = "#3D3D3D",
            GroupSurfaceColor    = "#252525",
            GroupBorderColor     = "#3D3D3D",
            DividerColor         = "#2F2F2F",
            AddonBackgroundColor = "#232323",
        },
        FontFamily           = "inter",   // DataMakerFontCatalog.DefaultFamilyId — bundled body face.
        FontSize             = 14,
        FontWeight           = StyleFontWeight.Normal,
        TextAlignment        = StyleAlignment.Left,
        LineHeight           = 1.5,
        CornerRadius         = 4,
        FieldBorderThickness = 1,
        Padding              = 20,
        Margin               = 0,
        FieldSpacing         = 4,
        SectionSpacing       = 20,
        LabelFontSize        = 14,
        DescriptionFontSize  = 12,
        // TextColor intentionally null — palette Colors.InkColor +
        // DarkColors.InkColor drive body text per mode through the
        // renderer's DmFormInkBrush. Setting Style.TextColor here would
        // shadow the palette and lock both Light + Dark to a single
        // color, which breaks dark-mode legibility.
        Heading1Style = new Style { FontSize = 28, FontWeight = StyleFontWeight.Bold },
        Heading2Style = new Style { FontSize = 22, FontWeight = StyleFontWeight.Bold },
        Heading3Style = new Style { FontSize = 18, FontWeight = StyleFontWeight.Bold },
        Heading4Style = new Style { FontSize = 14, FontWeight = StyleFontWeight.Bold },

        // Button defaults — single source of truth for both the Uno
        // designer (reads PrimaryButtonDefaults at render time) and the
        // .dmf bundle (FormBundleBuilder emits `button/<id>` element CSS
        // merged onto these). Hardcodes the FOBO Default accent (#04C8FF);
        // when the user changes accent in the styling tab they currently
        // have to update these manually — see backlog for auto-recolour.
        PrimaryButtonDefaults = new ButtonDefaults
        {
            Base = new Style
            {
                BackgroundColor = "#04C8FF",
                TextColor       = "#FFFFFF",
                BorderColor     = "#04C8FF",
                BorderThickness = 1,
                CornerRadius    = 6,
                FontSize        = 14,
                FontWeight      = StyleFontWeight.Bold,
                PaddingTop      = 8,  PaddingBottom = 8,
                PaddingLeft     = 16, PaddingRight  = 16,
            },
            Hover   = new Style { BackgroundColor = "#1CD0FF", BorderColor = "#1CD0FF" },
            Pressed = new Style { BackgroundColor = "#00B0E0", BorderColor = "#00B0E0" },
        },
        SecondaryButtonDefaults = new ButtonDefaults
        {
            Base = new Style
            {
                BackgroundColor = "#00000000",
                TextColor       = "#04C8FF",
                BorderColor     = "#04C8FF",
                BorderThickness = 1,
                CornerRadius    = 6,
                FontSize        = 14,
                FontWeight      = StyleFontWeight.Bold,
                PaddingTop      = 8,  PaddingBottom = 8,
                PaddingLeft     = 16, PaddingRight  = 16,
            },
            Hover   = new Style { BackgroundColor = "#1404C8FF" },
            Pressed = new Style { BackgroundColor = "#2904C8FF" },
        },
        SubtleButtonDefaults = new ButtonDefaults
        {
            Base = new Style
            {
                BackgroundColor = "#00000000",
                TextColor       = "#04C8FF",
                BorderColor     = "#00000000",
                BorderThickness = 0,
                CornerRadius    = 6,
                FontSize        = 14,
                FontWeight      = StyleFontWeight.Bold,
                PaddingTop      = 8,  PaddingBottom = 8,
                PaddingLeft     = 16, PaddingRight  = 16,
            },
            Hover   = new Style { BackgroundColor = "#1A04C8FF" },
            Pressed = new Style { BackgroundColor = "#3304C8FF" },
        },
    };

}
