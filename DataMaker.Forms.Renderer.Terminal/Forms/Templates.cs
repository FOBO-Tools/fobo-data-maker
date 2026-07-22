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
    // Plain colour pairs — NO Terminal.Gui.Attribute here. Attributes are made
    // by the driver (Attribute.Make returns an uninitialized value when
    // Application.Driver is null), so building a ColorScheme before
    // Application.Init() poisons it: any later redraw throws "Attributes must be
    // initialized by a driver". The ComboBox dropdown was the first view to hit
    // it. So this table stays attribute-free and is safe to touch pre-Init for
    // name validation; the scheme itself is built on demand in TryResolve, which
    // the caller must invoke AFTER Application.Init().
    private static readonly IReadOnlyDictionary<string, (Color Fg, Color Bg, Color DisabledFg)> Palettes =
        new Dictionary<string, (Color, Color, Color)>(StringComparer.OrdinalIgnoreCase)
        {
            ["dark"]  = (Color.White,       Color.Black, Color.DarkGray),
            ["green"] = (Color.BrightGreen, Color.Black, Color.Green),
            ["light"] = (Color.Black,       Color.White, Color.DarkGray),
        };

    /// <summary>True if <paramref name="name"/> is a known template. Safe to call before Application.Init() — touches no Attributes.</summary>
    public static bool IsKnown(string name) => Palettes.ContainsKey(name);

    /// <summary>
    /// Build the ColorScheme for <paramref name="name"/>. MUST run after
    /// <see cref="Application.Init()"/> — it makes driver attributes.
    /// </summary>
    public static bool TryResolve(string name, out ColorScheme scheme)
    {
        if (!Palettes.TryGetValue(name, out var p))
        {
            scheme = null!;
            return false;
        }
        scheme = MakeScheme(p.Fg, p.Bg, p.DisabledFg);
        return true;
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

    public static string Known => string.Join(", ", Palettes.Keys);
}
