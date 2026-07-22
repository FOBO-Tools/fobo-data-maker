using Terminal.Gui;
using TgAttribute = Terminal.Gui.Attribute;

namespace DataMaker.Forms.Renderer.Terminal.Forms;

/// <summary>
/// Builds a field label as either a plain <see cref="Label"/> or a
/// container holding the label plus a red <c>(*)</c> marker for required
/// fields. Red is the one colour every template keeps — even on the light
/// template it reads as a warning, which is the point of the marker.
///
/// <para>
/// The asterisk's scheme is finalised by <see cref="FormRenderer"/> after
/// the field's base scheme is known: it keeps the inherited background and
/// flips the foreground to <see cref="Color.BrightRed"/> via
/// <see cref="AccentScheme"/>. That way the asterisk reads on navy, green,
/// brown, or white without any knowledge at construction time.
/// </para>
/// </summary>
internal static class RequiredLabel
{
    public static (View Container, Label? Asterisk) Build(string text, bool required)
    {
        // Collapse line breaks to spaces — a stray \r renders as a literal ␍
        // glyph in Terminal.Gui, and a single-line label can't show breaks.
        text = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

        if (!required)
        {
            // AutoSize=true lets the label size to its text instead of
            // Dim.Fill()'ing the row — keeps labels visually tight and lets
            // other views sit next to them without fighting for space.
            return (new Label(text) { AutoSize = true }, null);
        }

        // Container sizes itself to its children (AutoSize on children + a
        // Dim.Fill/Height). Inner labels use AutoSize=true so the main label
        // claims exactly its text length and the asterisk lands right after it.
        var container = new View { Width = Dim.Fill(), Height = 1 };
        var main      = new Label(text)    { X = 0, Y = 0, AutoSize = true };
        var asterisk  = new Label(" (*)")  { X = Pos.Right(main), Y = 0, AutoSize = true };
        container.Add(main, asterisk);
        return (container, asterisk);
    }

    /// <summary>Scheme with foreground <paramref name="accent"/> but background inherited from <paramref name="baseScheme"/>.</summary>
    public static ColorScheme AccentScheme(ColorScheme baseScheme, Color accent)
    {
        var bg = baseScheme.Normal.Background;
        var fg = accent;
        return new ColorScheme
        {
            Normal    = new TgAttribute(fg, bg),
            Focus     = new TgAttribute(fg, bg),
            HotNormal = new TgAttribute(fg, bg),
            HotFocus  = new TgAttribute(fg, bg),
            Disabled  = new TgAttribute(fg, bg),
        };
    }

    /// <summary>
    /// Pick a high-contrast accent for a given background so the hotkey
    /// letter in a Button/MenuItem stands out from the main text. Terminal.Gui
    /// v1 has no separate underline attribute — the hotkey is visualised
    /// purely by color difference between Normal and HotNormal.
    /// </summary>
    public static Color HotKeyAccent(Color bg) => bg switch
    {
        Color.White or Color.Gray or
        Color.BrightYellow or Color.BrightCyan or Color.BrightGreen or Color.BrightMagenta
                      => Color.BrightRed,   // dark accent on light backgrounds
        _             => Color.BrightYellow, // light accent on dark backgrounds
    };
}
