using DataMaker.Schema.Styling;
using Terminal.Gui;
using TgAttribute = Terminal.Gui.Attribute;

namespace DataMaker.Forms.Renderer.Terminal.Forms;

/// <summary>
/// Maps a resolved <see cref="Style"/> onto a Terminal.Gui
/// <see cref="ColorScheme"/>. Only the subset of style properties a TUI can
/// honor passes through:
/// <list type="bullet">
///   <item><see cref="Style.TextColor"/> → foreground</item>
///   <item><see cref="Style.BackgroundColor"/> → background</item>
///   <item><see cref="Style.FontWeight"/> Bold/SemiBold → nearest bright color (approximates bold in fixed-attribute terminals)</item>
/// </list>
///
/// Everything else (font family/size, shadows, gradients, padding, corner
/// radius, opacity, letter-spacing) is a terminal no-op and ignored.
///
/// <para>
/// Hex colors snap to the nearest of the 16 ANSI colors by Euclidean RGB
/// distance. This is a lossy but predictable mapping — a hot-pink field won't
/// render as hot pink in a terminal, but it'll land on BrightMagenta which is
/// the closest honest approximation.
/// </para>
/// </summary>
internal static class StyleToColorScheme
{
    /// <summary>Resolve a style chain root-to-leaf and build a ColorScheme from it.</summary>
    public static ColorScheme Build(ColorScheme fallback, params Style?[] chain)
    {
        var resolved = StyleResolver.Resolve(chain);

        var fg = ResolveColor(resolved.TextColor,       fallback.Normal.Foreground);
        var bg = ResolveColor(resolved.BackgroundColor, fallback.Normal.Background);

        if (resolved.FontWeight is StyleFontWeight.Bold or StyleFontWeight.Bold)
            fg = Brighten(fg);

        // Focus inverts fg/bg — same convention Terminal.Gui ships with.
        // HotNormal/HotFocus picks a contrasting accent so the button hotkey
        // letter (e.g. the 'S' in "_Submit") is visibly distinct from the
        // rest of the button text. v1 has no separate underline attribute —
        // colour is the entire visual cue.
        var hot        = RequiredLabel.HotKeyAccent(bg);
        var hotOnFocus = RequiredLabel.HotKeyAccent(fg);
        return new ColorScheme
        {
            Normal    = new TgAttribute(fg,         bg),
            Focus     = new TgAttribute(bg,         fg),
            HotNormal = new TgAttribute(hot,        bg),
            HotFocus  = new TgAttribute(hotOnFocus, fg),
            Disabled  = new TgAttribute(Color.DarkGray, bg),
        };
    }

    private static Color ResolveColor(string? hex, Color fallback) =>
        TryParseHex(hex, out var r, out var g, out var b) ? Nearest(r, g, b) : fallback;

    private static bool TryParseHex(string? value, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var s = value.TrimStart('#');
        // Accept #rrggbb and #aarrggbb (alpha dropped — TUI has no alpha).
        if (s.Length == 8) s = s[2..];
        if (s.Length != 6) return false;

        try
        {
            r = Convert.ToByte(s[0..2], 16);
            g = Convert.ToByte(s[2..4], 16);
            b = Convert.ToByte(s[4..6], 16);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Map an RGB color to the nearest of the 16 ANSI colors Terminal.Gui exposes.</summary>
    private static Color Nearest(byte r, byte g, byte b)
    {
        var best       = Color.White;
        var bestDist   = int.MaxValue;
        foreach (var (color, cr, cg, cb) in Palette)
        {
            var dr = r - cr;
            var dg = g - cg;
            var db = b - cb;
            var d  = dr * dr + dg * dg + db * db;
            if (d < bestDist)
            {
                bestDist = d;
                best     = color;
            }
        }
        return best;
    }

    /// <summary>Bold approximation: snap a color to its bright variant where one exists.</summary>
    private static Color Brighten(Color color) => color switch
    {
        Color.Black   => Color.DarkGray,
        Color.Blue    => Color.BrightBlue,
        Color.Green   => Color.BrightGreen,
        Color.Cyan    => Color.BrightCyan,
        Color.Red     => Color.BrightRed,
        Color.Magenta => Color.BrightMagenta,
        Color.Brown   => Color.BrightYellow,
        Color.Gray    => Color.White,
        _             => color, // already bright
    };

    // ANSI 16-color palette approximate RGB values. Not precise (terminals
    // theme their own palettes), but close enough for nearest-neighbour matching.
    private static readonly (Color Color, byte R, byte G, byte B)[] Palette =
    [
        (Color.Black,         0,   0,   0),
        (Color.Blue,          0,   0, 170),
        (Color.Green,         0, 170,   0),
        (Color.Cyan,          0, 170, 170),
        (Color.Red,         170,   0,   0),
        (Color.Magenta,     170,   0, 170),
        (Color.Brown,       170,  85,   0),
        (Color.Gray,        170, 170, 170),
        (Color.DarkGray,     85,  85,  85),
        (Color.BrightBlue,   85,  85, 255),
        (Color.BrightGreen,  85, 255,  85),
        (Color.BrightCyan,   85, 255, 255),
        (Color.BrightRed,   255,  85,  85),
        (Color.BrightMagenta,255, 85, 255),
        (Color.BrightYellow,255, 255,  85),
        (Color.White,       255, 255, 255),
    ];
}
