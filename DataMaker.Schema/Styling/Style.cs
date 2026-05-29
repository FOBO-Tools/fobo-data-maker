using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataMaker.Schema.Styling;

/// <summary>
/// Cascading visual style for form elements. Attached optionally to
/// <c>Form</c>, <c>Section</c>, <c>GroupColumn</c>, and <c>FieldDefinition</c>.
/// Every property is nullable — null means "inherit from the nearest
/// ancestor that specifies a non-null value". Resolution is handled by
/// <see cref="StyleResolver"/>.
///
/// <para>
/// Only the <b>record-entry runtime</b> and the <b>preview window</b> consume
/// resolved styles; the layout designer canvas stays neutral (so user-chosen
/// fonts or backgrounds never break the designer chrome).
/// </para>
///
/// <para>
/// Colors are hex strings (e.g. <c>"#5b8cff"</c> or <c>"#aarrggbb"</c>) so the
/// JSON is human-readable and easy to hand-edit. The runtime parses them into
/// <c>Color</c>s; invalid values are ignored silently (design-time validator
/// will surface them later).
/// </para>
/// </summary>
public record Style
{
    // ── Tier 1: Typography ────────────────────────────────────────────
    public string?           FontFamily  { get; init; }
    public double?           FontSize    { get; init; }
    public StyleFontWeight?  FontWeight  { get; init; }
    public string?           TextColor   { get; init; }

    // ── Tier 2: Backgrounds & borders ─────────────────────────────────
    public string?  BackgroundColor { get; init; }
    public string?  BorderColor     { get; init; }
    public double?  BorderThickness { get; init; }
    public double?  CornerRadius    { get; init; }
    // Padding / Margin: uniform single value used by the simple inspector
    // view. Advanced view authors per-side via the four *Top/*Right/*Bottom/
    // *Left fields below — any non-null per-side wins over the uniform
    // value for that side at apply-time. Existing single-value data keeps
    // working unchanged.
    public double?  Padding         { get; init; }
    public double?  Margin          { get; init; }
    public double?  PaddingTop      { get; init; }
    public double?  PaddingRight    { get; init; }
    public double?  PaddingBottom   { get; init; }
    public double?  PaddingLeft     { get; init; }
    public double?  MarginTop       { get; init; }
    public double?  MarginRight     { get; init; }
    public double?  MarginBottom    { get; init; }
    public double?  MarginLeft      { get; init; }

    // ── Tier 3: Background image ──────────────────────────────────────
    public StyleImage? BackgroundImage { get; init; }

    // ── Tier 4: Advanced ──────────────────────────────────────────────
    public StyleGradient?  BackgroundGradient { get; init; }
    public StyleShadow?    Shadow        { get; init; }
    public StyleAlignment? TextAlignment { get; init; }
    public double?         LineHeight    { get; init; }
    public double?         LetterSpacing { get; init; }
    public double?         Opacity       { get; init; }
}

[JsonConverter(typeof(StyleFontWeightConverter))]
public enum StyleFontWeight { Normal, Bold }
public enum StyleAlignment  { Left, Center, Right, Justify }

/// <summary>
/// Per-form preferred display mode. Persisted on
/// <see cref="FormStyle.PreferredMode"/> so the designer's Light/Dark
/// toggle survives save/reopen (the toggle is form-level state, not a
/// per-user editor preference — same form opens in the same mode on
/// every machine). Null = "no preference; default to Light".
/// </summary>
public enum StyleMode { Light, Dark }

/// <summary>
/// Tolerant JsonConverter for <see cref="StyleFontWeight"/>. Accepts the
/// canonical PascalCase names (<c>"Normal"</c>, <c>"Bold"</c>),
/// case-insensitive CSS weight names (<c>thin</c>..<c>black</c>),
/// CSS numeric weights as numbers OR numeric strings (100..900), and
/// the raw enum ordinal. Threshold ≥ 600 → Bold, else Normal — matches
/// CSS's "bolder" boundary so <c>"semibold"</c> + <c>600</c> both land as
/// Bold instead of throwing. Writes the canonical name on serialize so
/// hand-edited files round-trip cleanly.
///
/// <para>
/// Attached at the enum so every deserialization path (agent tool host,
/// <c>.dmf</c> loader, theme import) benefits — strict canonical names
/// still work, so this is purely additive over the default behaviour.
/// </para>
/// </summary>
public sealed class StyleFontWeightConverter : JsonConverter<StyleFontWeight>
{
    public override StyleFontWeight Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var n))
                    return FromNumber(n);
                if (reader.TryGetDouble(out var d))
                    return FromNumber((int)d);
                throw new JsonException("Unsupported numeric token for StyleFontWeight.");

            case JsonTokenType.String:
                var s = reader.GetString() ?? "";
                if (int.TryParse(s, System.Globalization.NumberStyles.Integer,
                                 System.Globalization.CultureInfo.InvariantCulture, out var ns))
                    return FromNumber(ns);
                return FromName(s);

            default:
                throw new JsonException($"Unexpected token {reader.TokenType} for StyleFontWeight.");
        }
    }

    public override void Write(Utf8JsonWriter writer, StyleFontWeight value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());

    private static StyleFontWeight FromNumber(int n) =>
        n >= 600 || n == 1 ? StyleFontWeight.Bold : StyleFontWeight.Normal;

    private static StyleFontWeight FromName(string s) => s.Trim().ToLowerInvariant() switch
    {
        "normal" or "regular" or "thin" or "extralight" or "ultralight" or "light" or "book"
            => StyleFontWeight.Normal,
        "medium"
            => StyleFontWeight.Normal,
        "bold" or "semibold" or "demibold" or "extrabold" or "ultrabold" or "black" or "heavy"
            => StyleFontWeight.Bold,
        _   => throw new JsonException($"Unknown font weight '{s}'. Expected Normal/Bold or a CSS weight name/number."),
    };
}

/// <summary>
/// Optional background image. <see cref="Source"/> is a URL or local path.
/// Same typed-record + converter pattern as <c>Geo</c>: ensures future
/// migration paths (e.g. BLOB-referencing source) don't break existing data.
/// </summary>
public sealed record StyleImage(string Source, StyleImageFit Fit = StyleImageFit.UniformToFill);

public enum StyleImageFit { None, Fill, Uniform, UniformToFill }

public sealed record StyleGradient(
    GradientDirection Direction,
    string            StartColor,
    string            EndColor);

public enum GradientDirection { Vertical, Horizontal, DiagonalDown, DiagonalUp }

public sealed record StyleShadow(
    string Color,
    double Blur    = 4,
    double OffsetX = 0,
    double OffsetY = 2);
