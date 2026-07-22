using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Binding for <see cref="FieldTypes.Signature"/> + <see cref="FieldTypes.Initials"/>.
/// A terminal has no ink surface, so the signer <b>types their name</b> (stored
/// as <see cref="SignatureRef.TypedName"/>).
///
/// <para>An earlier version also let the signer upload a signature image file,
/// but the desktop editor doesn't support image signatures — so the terminal
/// only produces the typed-name shape, which renders everywhere.</para>
/// </summary>
internal sealed class SignatureBinding : FieldBinding
{
    private readonly View _label;
    private readonly Label? _asterisk;
    private readonly TextField _nameField;
    private readonly Label _errorIndicator = CreateErrorIndicator();

    public SignatureBinding(FieldDefinition definition, FormState state) : base(definition, state)
    {
        (_label, _asterisk) = RequiredLabel.Build(definition.Label, definition.Required);

        var initial = State.Get(definition.Name) as SignatureRef?;
        _nameField = new TextField(initial?.TypedName ?? "")
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = 1,
        };
        _nameField.TextChanged += _ =>
        {
            var typed = _nameField.Text.ToString() ?? "";
            State.Set(definition.Name, string.IsNullOrWhiteSpace(typed)
                ? null
                : new SignatureRef(TypedName: typed));
        };
    }

    public override View Label => _label;
    public override View Editor => _nameField;
    public override int EditorHeight => 1;
    public override Label? RequiredAsterisk => _asterisk;
    public override Label? ErrorIndicator => _errorIndicator;
}
