using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Binding for the <c>boolean</c> kind. CheckBox carries its own label so we
/// skip the binding's <see cref="FieldBinding.Label"/> — lets the check and
/// label sit on one row, which reads better for yes/no questions.
/// </summary>
internal sealed class BooleanBinding : FieldBinding
{
    private readonly CheckBox _field;

    public BooleanBinding(FieldDefinition definition, FormState state) : base(definition, state)
    {
        // CheckBox embeds its own label in the widget text — we can't split
        // it into two color spans, so the required marker rides inline,
        // uncoloured. A rare loss of consistency for a rare case (required
        // booleans aren't common).
        var text = definition.Required ? $"{definition.Label} (*)" : definition.Label;
        _field = new GroupScopedCheckBox(text, State.Get(definition.Name) is true)
        {
            Width = Dim.Fill(),
        };
        _field.WithStandardKeys(); // Enter toggles, like Space
        _field.Toggled += _ => State.Set(definition.Name, _field.Checked);

        // Seed the state with the initial value (false unless a default set it
        // true). A boolean always HAS an answer — "no" when untouched — so a
        // required yes/no is satisfied without flipping it, matching the JS/Uno
        // renderers where false is a valid value, not "empty".
        State.Set(definition.Name, _field.Checked);
    }

    public override View? Label => null;
    public override View Editor => _field;
    public override int EditorHeight => 1;
}
