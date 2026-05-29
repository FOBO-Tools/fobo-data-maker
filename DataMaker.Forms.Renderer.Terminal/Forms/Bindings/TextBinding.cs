using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Binding for single-line string kinds: text, email, phone, url. A format
/// expectation (email shape, URL scheme) is enforced later via the validation
/// rules on the <see cref="FieldDefinition"/>, not at widget level.
/// </summary>
internal sealed class TextBinding : FieldBinding
{
    private readonly View _label;
    private readonly Label? _asterisk;
    private readonly TextField _field;
    private readonly Label _errorIndicator = CreateErrorIndicator();

    public TextBinding(FieldDefinition definition, FormState state) : base(definition, state)
    {
        (_label, _asterisk) = RequiredLabel.Build(definition.Label, definition.Required);

        // Always fill the grid column. MaxLength is a content cap
        // (validated separately), not a visual cap — sizing the editor
        // to MaxLength made a 50-char field a stubby 52-cell input and
        // a no-MaxLength field stretched edge-to-edge. TextField scrolls
        // horizontally when text exceeds the visible width, so long
        // entries stay editable inside a fixed column box.
        _field = new TextField(State.Get(definition.Name)?.ToString() ?? "")
        {
            Width = Dim.Fill(),
        };

        _field.TextChanged += _ =>
        {
            var current = _field.Text.ToString() ?? "";
            State.Set(definition.Name, string.IsNullOrEmpty(current) ? null : current);
        };
    }

    public override View Label => _label;
    public override View Editor => _field;
    public override int EditorHeight => 1;
    public override Label? RequiredAsterisk => _asterisk;
    public override Label? ErrorIndicator => _errorIndicator;
}
