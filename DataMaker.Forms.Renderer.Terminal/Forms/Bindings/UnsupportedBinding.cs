using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Placeholder binding for field kinds that don't have a first-class terminal
/// representation yet: <c>image</c>, <c>attachment</c>, <c>geo</c>,
/// <c>relation</c>. Renders a dim read-only hint so the user knows the field
/// exists and must be filled in the desktop app before submitting. The
/// existing field value (if any) flows through untouched — a submitter who
/// picked up a partially-filled form doesn't accidentally drop a picture.
/// </summary>
internal sealed class UnsupportedBinding : FieldBinding
{
    private readonly View _label;
    private readonly Label? _asterisk;
    private readonly Label _hint;

    public UnsupportedBinding(FieldDefinition definition, FormState state, string reason)
        : base(definition, state)
    {
        (_label, _asterisk) = RequiredLabel.Build(definition.Label, definition.Required);
        _hint  = new Label($"[{reason} — fill this field in the desktop app]")
        {
            Width = Dim.Fill(),
        };

        // Round-trip whatever was in the existing value (if the caller seeded one).
        // No user editing possible — so state just holds whatever it already had.
    }

    public override View Label => _label;
    public override View Editor => _hint;
    public override int EditorHeight => 1;
    public override Label? RequiredAsterisk => _asterisk;
}
