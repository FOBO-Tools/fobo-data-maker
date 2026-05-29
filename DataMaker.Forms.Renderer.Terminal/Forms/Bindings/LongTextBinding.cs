using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>Binding for the <c>long-text</c> kind — multi-line text area.</summary>
internal sealed class LongTextBinding : FieldBinding
{
    private const int DefaultRows = 5;

    private readonly View _label;
    private readonly Label? _asterisk;
    private readonly TextView _field;
    private readonly Label _errorIndicator = CreateErrorIndicator();

    public LongTextBinding(FieldDefinition definition, FormState state) : base(definition, state)
    {
        (_label, _asterisk) = RequiredLabel.Build(definition.Label, definition.Required);

        _field = new TextView
        {
            Width  = Dim.Fill(),
            Height = DefaultRows,
            Text   = State.Get(definition.Name)?.ToString() ?? "",
        };

        // v1 gotcha: TextView.TextChanged fires only on programmatic setter.
        // ContentsChanged is the event that fires on user input.
        _field.ContentsChanged += _ =>
        {
            var current = _field.Text.ToString() ?? "";
            State.Set(definition.Name, string.IsNullOrEmpty(current) ? null : current);
        };
    }

    public override View Label => _label;
    public override View Editor => _field;
    public override int EditorHeight => DefaultRows;
    public override Label? RequiredAsterisk => _asterisk;
    public override Label? ErrorIndicator => _errorIndicator;
}
