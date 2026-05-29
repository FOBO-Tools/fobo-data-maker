using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Binding for the <c>multi-choice</c> kind. v1 has no multi-select dropdown,
/// so we render a vertical stack of checkboxes — same UX as a well-designed
/// web form when there are few options. Stored value is a
/// <see cref="string"/>[] of picked <see cref="Choice.Value"/> strings.
/// </summary>
internal sealed class MultiChoiceBinding : FieldBinding
{
    private readonly View _label;
    private readonly Label? _asterisk;
    private readonly View _container;
    private readonly List<CheckBox> _checkBoxes;
    private readonly IReadOnlyList<Choice> _choices;
    private readonly HashSet<string> _selected;

    public MultiChoiceBinding(FieldDefinition definition, FormState state) : base(definition, state)
    {
        (_label, _asterisk) = RequiredLabel.Build(definition.Label, definition.Required);
        _choices  = definition.Choice?.Choices ?? Array.Empty<Choice>();
        _selected = ExistingSet(state.Get(definition.Name));

        _container = new View
        {
            Width  = Dim.Fill(),
            Height = Math.Max(1, _choices.Count),
        };
        _checkBoxes = new List<CheckBox>(_choices.Count);

        for (var i = 0; i < _choices.Count; i++)
        {
            var choice = _choices[i];
            var box = new CheckBox(choice.Label, _selected.Contains(choice.Value))
            {
                X     = 0,
                Y     = i,
                Width = Dim.Fill(),
            };
            box.Toggled += _ =>
            {
                if (box.Checked) _selected.Add(choice.Value);
                else             _selected.Remove(choice.Value);
                Flush();
            };
            _checkBoxes.Add(box);
            _container.Add(box);
        }
    }

    public override View Label => _label;
    public override View Editor => _container;
    public override int EditorHeight => Math.Max(1, _choices.Count);
    public override Label? RequiredAsterisk => _asterisk;

    private void Flush()
    {
        if (_selected.Count == 0) State.Set(Definition.Name, null);
        else                      State.Set(Definition.Name, _selected.ToArray());
    }

    private static HashSet<string> ExistingSet(object? existing)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (existing is null) return set;

        if (existing is System.Collections.IEnumerable enumerable && existing is not string)
        {
            foreach (var item in enumerable)
                if (item is not null) set.Add(item.ToString()!);
        }
        else
        {
            set.Add(existing.ToString()!);
        }
        return set;
    }
}
