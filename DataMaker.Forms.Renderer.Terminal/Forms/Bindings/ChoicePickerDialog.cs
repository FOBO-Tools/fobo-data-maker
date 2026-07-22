using System;
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Modal single-select list picker for <see cref="ChoiceBinding"/> when there
/// are too many options for an inline radio group. A plain <see cref="ListView"/>
/// gives a reliable keyboard model — ↑/↓ to move, Enter to pick, Esc to cancel —
/// unlike Terminal.Gui's ComboBox (whose collapsed search field lets arrow keys
/// escape to the page buttons).
/// </summary>
internal sealed class ChoicePickerDialog : Dialog
{
    private readonly ListView _list;

    /// <summary>Index the user committed, or null if they cancelled.</summary>
    public int? CommittedIndex { get; private set; }

    public ChoicePickerDialog(string title, IReadOnlyList<string> labels, int selected)
        : base(title, Width(labels), Height(labels))
    {
        _list = new ListView(labels.ToList())
        {
            X = 0, Y = 0,
            Width  = Dim.Fill(),
            Height = Dim.Fill(1), // leave the button row
        };
        if (selected >= 0 && selected < labels.Count) _list.SelectedItem = selected;

        // Enter on a row picks it immediately.
        _list.OpenSelectedItem += _ => Commit();
        Add(_list);

        var ok = new Button("OK", is_default: true);
        ok.Clicked += Commit;
        var cancel = new Button("Cancel");
        cancel.Clicked += () => { CommittedIndex = null; Application.RequestStop(); };
        AddButton(ok);
        AddButton(cancel);
    }

    private void Commit()
    {
        CommittedIndex = _list.SelectedItem;
        Application.RequestStop();
    }

    // Esc cancels the picker (the form's own Esc handler is suspended while this
    // modal owns the run loop).
    public override bool ProcessKey(KeyEvent kb)
    {
        if (kb.Key == Key.Esc)
        {
            CommittedIndex = null;
            Application.RequestStop();
            return true;
        }
        return base.ProcessKey(kb);
    }

    private static int Width(IReadOnlyList<string> labels)
    {
        var longest = labels.Count == 0 ? 0 : labels.Max(l => l?.Length ?? 0);
        return Math.Clamp(longest + 6, 32, 72);
    }

    private static int Height(IReadOnlyList<string> labels) => Math.Clamp(labels.Count + 4, 8, 22);
}
