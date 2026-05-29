using Terminal.Gui;
using TgAttribute = Terminal.Gui.Attribute;

namespace DataMaker.Forms.Renderer.Terminal.Forms;

/// <summary>
/// Built-in color presets a user can force via <c>--template &lt;name&gt;</c>.
/// When a template is active the form's own <c>Style</c> cascade is ignored —
/// every view uses the template's <see cref="ColorScheme"/>, including the
/// Window chrome and Terminal.Gui's default <see cref="Colors.Dialog"/> /
/// <see cref="Colors.Base"/> schemes so dialogs blend in.
///
/// <para>
/// <c>HotNormal</c>/<c>HotFocus</c> get a contrasting accent (via
/// <see cref="RequiredLabel.HotKeyAccent"/>) so the button hotkey letter
/// (<c>_S</c>ubmit, <c>_C</c>ancel) is visibly differentiated from the rest
/// of the button text. Terminal.Gui v1 has no underline attribute — the
/// colour change is the whole visual cue.
/// </para>
/// </summary>
internal static class Templates
{
    public static readonly IReadOnlyDictionary<string, ColorScheme> Presets = Build();

    private static Dictionary<string, ColorScheme> Build()
    {
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["dark"]  = MakeScheme(fg: Color.White,        bg: Color.Black, disabledFg: Color.DarkGray),
            ["green"] = MakeScheme(fg: Color.BrightGreen,  bg: Color.Black, disabledFg: Color.Green),
            ["light"] = MakeScheme(fg: Color.Black,        bg: Color.White, disabledFg: Color.DarkGray),
        };
    }

    /// <summary>
    /// Build a full ColorScheme from a foreground/background pair, with
    /// Focus inverted and HotNormal/HotFocus picking a contrasting accent.
    /// </summary>
    private static ColorScheme MakeScheme(Color fg, Color bg, Color disabledFg)
    {
        var accent = RequiredLabel.HotKeyAccent(bg);
        var accentOnFocus = RequiredLabel.HotKeyAccent(fg); // background on focus = original fg

        return new ColorScheme
        {
            Normal    = new TgAttribute(fg,     bg),
            Focus     = new TgAttribute(bg,     fg),
            HotNormal = new TgAttribute(accent, bg),
            HotFocus  = new TgAttribute(accentOnFocus, fg),
            Disabled  = new TgAttribute(disabledFg, bg),
        };
    }

    public static bool TryResolve(string name, out ColorScheme scheme) =>
        Presets.TryGetValue(name, out scheme!);

    public static string Known => string.Join(", ", Presets.Keys);
}
