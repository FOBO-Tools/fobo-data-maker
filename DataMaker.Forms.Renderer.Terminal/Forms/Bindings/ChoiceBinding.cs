using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Binding for the single-select <c>choice</c> kind. RadioGroup for ≤5 options
/// (browsable without a dropdown), ComboBox otherwise — matches what a native
/// form UI would pick. Stored value is the <see cref="Choice.Value"/> string
/// of the picked item, not its display label.
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
            var group = new RadioGroup(choices.Select(c => NStack.ustring.Make(c.Label)).ToArray())
            {
                Width = Dim.Fill(),
            };

            var selected = IndexOf(choices, existingValue);
            if (selected >= 0) group.SelectedItem = selected;

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
            var combo = new ComboBox
            {
                Width  = Dim.Fill(),
                Height = 4, // dropdown needs vertical room to show options
            };
            combo.SetSource(choices.Select(c => c.Label).ToList());

            var selected = IndexOf(choices, existingValue);
            if (selected >= 0) combo.SelectedItem = selected;

            combo.SelectedItemChanged += args =>
            {
                var idx = args.Item;
                State.Set(definition.Name, idx >= 0 && idx < choices.Count ? choices[idx].Value : null);
            };

            _field  = combo;
            _height = 4;
        }
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
