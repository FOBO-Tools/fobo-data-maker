using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms;

/// <summary>
/// Shared layout for a "value text + action button" editor (Date "Set",
/// DateTime "Set", Geo "Search…"). Mirrors the choice dropdown's editor exactly:
/// the value sits on its own row, the button a row below it, left-aligned under
/// the label (X=0) — so every action button starts at the SAME column regardless
/// of its label width, and a long value can use the full width without colliding
/// with the button.
/// </summary>
internal static class ActionFieldRow
{
    /// <summary>Editor height these bindings must report from <c>EditorHeight</c>.</summary>
    public const int Height = 3;

    public static (View Editor, Label Display, Button Button) Build(
        string displayText, string buttonText, Action onClick)
    {
        var editor = new View { Width = Dim.Fill(), Height = Height };

        var display = new Label(displayText)
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = 1,
        };

        var button = new Button(buttonText) { X = 0, Y = 2, Height = 1 };
        // No letter hotkey — Ctrl+S is Submit, and "Set"/"Search…" would grab S.
        // Open via Enter/Space/click. Clearing HotKeyPos also stops TG colouring
        // the first uppercase letter. (Same treatment as the dropdown's button.)
        button.HotKey = Key.Null;
        button.TextFormatter.HotKeyPos = -1;
        button.Clicked += () => onClick();

        editor.Add(display, button);
        return (editor, display, button);
    }
}
