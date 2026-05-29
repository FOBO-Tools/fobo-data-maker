using System.Globalization;
using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Read-only display for a field with a <see cref="FieldDefinition.CalculatedExpression"/>.
/// The user never types here — the <see cref="FormRuntime"/> writes the
/// computed value into <see cref="FormState"/> on every change and calls
/// <see cref="FieldBinding.OnComputedValueUpdated"/> so this binding can
/// repaint its display label. Matches the Uno renderer's behavior: calculated
/// fields still produce a value regardless of visibility (other fields may
/// depend on them).
/// </summary>
internal sealed class CalculatedFieldBinding : FieldBinding
{
    private readonly View _label;
    private readonly Label? _asterisk;
    private readonly Label _display;

    public CalculatedFieldBinding(FieldDefinition definition, FormState state)
        : base(definition, state)
    {
        // Calculated fields that are marked required still get the asterisk —
        // downstream validation relies on the value being non-empty when required.
        (_label, _asterisk) = RequiredLabel.Build($"{definition.Label}  (calculated)", definition.Required);

        _display = new Label(FormatValue(state.Get(definition.Name)))
        {
            Width = Dim.Fill(),
        };
    }

    public override View Label => _label;
    public override View Editor => _display;
    public override int EditorHeight => 1;
    public override Label? RequiredAsterisk => _asterisk;

    public override void OnComputedValueUpdated(object? value) =>
        _display.Text = FormatValue(value);

    private static string FormatValue(object? value) => value switch
    {
        null       => "(not yet computed)",
        decimal d  => d.ToString(CultureInfo.CurrentCulture),
        double  d  => d.ToString(CultureInfo.CurrentCulture),
        bool    b  => b ? "true" : "false",
        DateTime t => t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        _          => value.ToString() ?? "",
    };
}
