using System.Linq;
using System.Reflection;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal;

/// <summary>
/// Startup splash: the embedded ASCII logo, centered on its own Toplevel.
/// Dismisses on any key (or auto-continues after a short beat) and then the
/// form window takes over. No-op if the logo resource is missing.
/// </summary>
internal static class SplashScreen
{
    private const string ResourceName = "DataMaker.Forms.Renderer.Terminal.Assets.logo.txt";

    public static void Show(ColorScheme? template = null)
    {
        var art = LoadLogo();
        if (string.IsNullOrWhiteSpace(art)) return; // nothing to show — skip

        // Drop blank lines at the top/bottom so the logo hugs the top row
        // instead of floating a couple of rows down.
        art = TrimBlankEdges(art);

        // Fit the art to the terminal height — the full logo is ~43 rows, taller
        // than most terminals, so it would spill over the hint. Evenly drop rows
        // to leave one line at the bottom for the hint.
        var rows    = Application.Driver?.Rows ?? 40;
        var maxRows = Math.Max(1, rows - 1);
        art = FitRows(art, maxRows);

        // Honour the active template (green logo in the green template, etc).
        // No template → white on the terminal's own black, never Terminal.Gui blue.
        var attr   = template is not null
            ? template.Normal
            : Application.Driver.MakeAttribute(Color.White, Color.Black);
        var scheme = new ColorScheme { Normal = attr, Focus = attr, HotNormal = attr, HotFocus = attr, Disabled = attr };

        var top = new Toplevel
        {
            X = 0, Y = 0,
            Width  = Dim.Fill(),
            Height = Dim.Fill(),
            ColorScheme = scheme,
        };

        var logo = new Label(art)
        {
            X        = Pos.Center(),
            Y        = 0,
            AutoSize = true,
            ColorScheme = scheme,
        };
        var hint = new Label("press any key")
        {
            X = Pos.Center(),
            Y = Pos.AnchorEnd(1),
            ColorScheme = scheme,
        };
        top.Add(logo, hint);

        // Any key dismisses immediately; otherwise auto-continue after a beat
        // so an unattended run still proceeds.
        top.KeyPress += _ => Application.RequestStop();
        var timeout = Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(2200), _ =>
        {
            Application.RequestStop();
            return false; // one-shot
        });

        Application.Run(top);
        Application.MainLoop.RemoveTimeout(timeout);
    }

    /// <summary>Strip whitespace-only lines from the top and bottom of the art.</summary>
    private static string TrimBlankEdges(string art)
    {
        var lines = art.Replace("\r", "").Split('\n').ToList();
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines);
    }

    /// <summary>Evenly downsample the art's rows so it's at most <paramref name="maxRows"/> tall (no-op if it already fits).</summary>
    private static string FitRows(string art, int maxRows)
    {
        var lines = art.Replace("\r", "").Split('\n');
        if (lines.Length <= maxRows) return art;

        var kept = new string[maxRows];
        for (var i = 0; i < maxRows; i++)
            kept[i] = lines[(int)((long)i * lines.Length / maxRows)];
        return string.Join("\n", kept);
    }

    private static string? LoadLogo()
    {
        var asm = typeof(SplashScreen).Assembly;
        // Exact name first; fall back to any manifest resource ending in
        // logo.txt so an MSBuild naming quirk doesn't silently drop the splash.
        var name = asm.GetManifestResourceNames().Contains(ResourceName)
            ? ResourceName
            : asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("logo.txt", StringComparison.Ordinal));
        if (name is null) return null;

        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
