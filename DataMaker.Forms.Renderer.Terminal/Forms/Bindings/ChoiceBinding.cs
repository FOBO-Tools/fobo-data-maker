using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Binding for the single-select <c>choice</c> kind. RadioGroup for ≤5 options
/// (browsable inline), otherwise a display + "Select" button that opens a modal
/// list picker (<see cref="ChoicePickerDialog"/>). Stored value is the
/// <see cref="Choice.Value"/> string of the picked item, not its display label.
/// </summary>
internal sealed class ChoiceBinding : FieldBinding
{
    private const int RadioThreshold = 5;

    private readonly View _label;
    private readonly Label? _asterisk;
    private readonly View _field;
    private readonly int _height;

    public ChoiceBinding(FieldDefinition definition, FormState state) : base(definition, state)
    {
        (_label, _asterisk) = RequiredLabel.Build(definition.Label, definition.Required);

        var choices = definition.Choice?.Choices ?? Array.Empty<Choice>();
        var existingValue = state.Get(definition.Name)?.ToString();

        if (choices.Count <= RadioThreshold)
        {
            // Unique per-option hotkeys — otherwise "Very satisfied" / "Very
            // dissatisfied" both auto-grab V and collide.
            var labels = Keys.AssignUniqueHotkeys(choices.Select(c => c.Label).ToList());
            var group = new FocusScopedRadioGroup(labels.Select(NStack.ustring.Make).ToArray())
            {
                Width = Dim.Fill(),
            };
            group.WithStandardKeys(); // Enter selects (like Space) + Down-arrow moves

            // -1 when there's no stored value → nothing pre-selected. Terminal.Gui
            // defaults a RadioGroup to index 0, which renders the first option as
            // chosen even though the user never picked it (and the value stays
            // empty) — so a required field shows a filled radio yet still fails
            // validation. Set the real selection (or none) before wiring the
            // change handler so this initial state doesn't write to FormState.
            var selected = IndexOf(choices, existingValue);
            group.SelectedItem = selected;

            group.SelectedItemChanged += args =>
            {
                var idx = args.SelectedItem;
                State.Set(definition.Name, idx >= 0 && idx < choices.Count ? choices[idx].Value : null);
            };

            _field  = group;
            _height = Math.Max(1, choices.Count);
        }
        else
        {
            // Too many options for an inline radio group. Show the current
            // choice + a "Select" button that opens a modal list picker. The
            // ComboBox was replaced because its collapsed search field let the
            // arrow keys escape to the page buttons — the picker's ListView has
            // a clean ↑/↓ + Enter + Esc keyboard model.
            var labels = choices.Select(c => c.Label).ToList();

            // Value on its own row; the Select button sits a row below it,
            // left-aligned under the label (not floated off to the right).
            var editor  = new View { Width = Dim.Fill(), Height = 3 };
            var display = new Label(DisplayLabel(choices, existingValue))
            {
                X = 0, Y = 0, Width = Dim.Fill(), Height = 1,
            };
            var pick = new Button("Select") { X = 0, Y = 2 };
            // No 'S' hotkey — Ctrl+S is Submit; open via Enter/Space/click. Clear
            // both the binding AND the drawn hotkey position (TG sets HotKeyPos
            // from the first uppercase letter when the text is parsed, and that's
            // what colours the 'S' — HotKey=Null alone doesn't reset it).
            pick.HotKey = Key.Null;
            pick.TextFormatter.HotKeyPos = -1;
            pick.Clicked += () =>
            {
                var current = IndexOf(choices, State.Get(definition.Name)?.ToString());
                var dlg     = new ChoicePickerDialog(definition.Label, labels, current);
                Application.Run(dlg);
                if (dlg.CommittedIndex is int idx && idx >= 0 && idx < choices.Count)
                {
                    State.Set(definition.Name, choices[idx].Value);
                    display.Text = choices[idx].Label;
                }
            };
            editor.Add(display, pick);

            _field  = editor;
            _height = 3;
        }
    }

    private static string DisplayLabel(IReadOnlyList<Choice> choices, string? value)
    {
        var idx = IndexOf(choices, value);
        return idx >= 0 ? choices[idx].Label : "(none)";
    }

    public override View Label => _label;
    public override View Editor => _field;
    public override int EditorHeight => _height;
    public override Label? RequiredAsterisk => _asterisk;

    private static int IndexOf(IReadOnlyList<Choice> choices, string? value)
    {
        if (value is null) return -1;
        for (var i = 0; i < choices.Count; i++)
            if (choices[i].Value == value) return i;
        return -1;
    }
}
