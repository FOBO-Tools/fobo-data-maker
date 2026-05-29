using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms;

/// <summary>
/// A modal dialog that replaces <see cref="MessageBox"/> for multi-line
/// content. <see cref="MessageBox"/> centers text and packs it tight against
/// the title bar — unreadable for the validation error list or a per-field
/// state dump. This helper lays the body out as a left-aligned label
/// starting one row below the title, with an OK button pinned to the bottom.
///
/// <para>
/// Sizes itself to content with sensible caps so a 30-field error list
/// doesn't blow past the screen. Returns after the user acknowledges.
/// </para>
/// </summary>
internal static class InfoDialog
{
    public static void Show(string title, string body, bool isError = false)
    {
        var outerW = Application.Top?.Frame.Width  ?? 80;
        var outerH = Application.Top?.Frame.Height ?? 24;

        // Width fits the longest line (plus frame + a couple of cols of padding),
        // capped at 90% of the screen.
        var lines = body.Split('\n');
        var longest = lines.Length == 0 ? 0 : lines.Max(l => l.Length);
        var width  = Math.Min(outerW * 9 / 10, Math.Max(40, longest + 6));
        var height = Math.Min(outerH * 9 / 10, lines.Length + 6);

        var ok = new Button("_OK") { IsDefault = true };
        var dialog = new Dialog(title, width, height, ok);

        var label = new Label(body)
        {
            X             = 1,
            Y             = 1,   // one row below the frame title
            Width         = Dim.Fill(1),
            Height        = Dim.Fill(2),
            TextAlignment = TextAlignment.Left,
        };
        dialog.Add(label);

        ok.Clicked += () => Application.RequestStop();

        // Tint the dialog's frame + button accent red for error variants so it
        // stays visually consistent with the rest of the template/cascade.
        if (isError) dialog.ColorScheme = Colors.Error;

        Application.Run(dialog);
    }
}
