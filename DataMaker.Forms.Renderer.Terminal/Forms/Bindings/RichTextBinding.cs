using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Binding for the <c>rich-text</c> kind. Body is Markdown across every
/// renderer; in the terminal we hand the user a multi-line TextView so
/// they can edit the raw Markdown source. Downstream renderers (web,
/// uno, pdf) format the same Markdown on display.
///
/// <para>Slightly taller default than <see cref="LongTextBinding"/> —
/// rich-text fields tend to carry paragraph-scale prose, not single
/// notes. Otherwise identical wiring (ContentsChanged → state, same
/// asterisk + error indicator pattern).</para>
/// </summary>
internal sealed class RichTextBinding : FieldBinding
{
    private const int DefaultRows = 8;

    private readonly View _label;
    private readonly Label? _asterisk;
    private readonly TextView _field;
    private readonly Label _hint;
    private readonly Label _errorIndicator = CreateErrorIndicator();

    public RichTextBinding(FieldDefinition definition, FormState state) : base(definition, state)
    {
        (_label, _asterisk) = RequiredLabel.Build($"{definition.Label}  (Markdown)", definition.Required);

        _field = new TextView
        {
            Width  = Dim.Fill(),
            Height = DefaultRows,
            Text   = State.Get(definition.Name)?.ToString() ?? "",
            // Tab moves to the next field instead of inserting a tab char —
            // Enter still inserts newlines (multi-line markdown body).
            AllowsTab = false,
        };

        _field.ContentsChanged += _ =>
        {
            var current = _field.Text.ToString() ?? "";
            State.Set(definition.Name, string.IsNullOrEmpty(current) ? null : current);
        };

        // One-line hint sits below the editor so the user knows the body
        // is Markdown (downstream renderers format **bold**, # headers,
        // etc. — the terminal keeps it raw on purpose).
        _hint = new Label("Tip: ** bold **, _italic_, # heading, - bullet — formatted by other renderers.")
        {
            Width = Dim.Fill(),
        };
    }

    public override View Label => _label;
    public override View Editor => _field;
    public override int EditorHeight => DefaultRows;
    public override Label? RequiredAsterisk => _asterisk;
    public override Label? ErrorIndicator => _errorIndicator;
}
