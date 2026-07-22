using System.Globalization;
using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Binding for <c>number</c>, <c>decimal</c>, and <c>money</c> kinds. The
/// widget is a plain <see cref="TextField"/> that rejects non-numeric input at
/// write time and keeps the typed value in <see cref="FormState"/> as a
/// <see cref="decimal"/> — so JSON serialization downstream gets a number, not
/// a string, without caring which kind drove it.
///
/// <para>
/// <b>Why write <c>null</c> on unparseable input</b> rather than leaving the
/// last good value: it matches the Uno renderer's behavior and keeps "empty
/// the field" a valid operation. Validation rules surface the error at submit
/// time.
/// </para>
/// </summary>
internal sealed class NumberBinding : FieldBinding
{
    private readonly View _label;
    private readonly Label? _asterisk;
    private readonly TextField _field;
    private readonly Label _errorIndicator = CreateErrorIndicator();

    public NumberBinding(FieldDefinition definition, FormState state) : base(definition, state)
    {
        (_label, _asterisk) = RequiredLabel.Build(LabelWithUnit(definition), definition.Required);

        _field = new TextField(FormatExisting(State.Get(definition.Name)))
        {
            Width = Dim.Fill(),
        };

        _field.TextChanged += _ =>
        {
            var raw = _field.Text.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(raw))
            {
                State.Set(definition.Name, null);
            }
            // NumberStyles.Float (NOT .Any) — no thousands-group separator. With
            // .Any a nl user's "23.67" read the "." as a group sep and collapsed
            // to 2367. Without group support the nl parse rejects "." (nl decimal
            // is ","), then the invariant pass reads "." as the decimal point → 23.67.
            else if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
                  || decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                // Coerce per kind to match the evaluator's parameter
                // type. Number = long?, Decimal/Money = decimal?. Without
                // the coercion DynamicExpresso throws inside Invoke on
                // the type mismatch, the evaluator swallows the throw,
                // and any downstream calculated field silently returns
                // null. Mirrors the Uno records-grid fix
                // (feedback_datamaker_inline_edit_coerce).
                object boxed = definition.Kind == FieldTypes.Number
                    ? (object)(long)parsed
                    : parsed;
                State.Set(definition.Name, boxed);
            }
            else
            {
                // Keep the user's raw text in the field (so they can keep typing)
                // but flag state as null — submit will show a validation error.
                State.Set(definition.Name, null);
            }
        };
    }

    public override View Label => _label;
    public override View Editor => _field;
    public override int EditorHeight => 1;
    public override Label? RequiredAsterisk => _asterisk;
    public override Label? ErrorIndicator => _errorIndicator;

    private static string LabelWithUnit(FieldDefinition d)
    {
        if (d.Kind == FieldTypes.Money && !string.IsNullOrWhiteSpace(d.Money?.Currency))
            return $"{d.Label}  ({d.Money!.Currency})";
        return d.Label;
    }

    private static string FormatExisting(object? existing) => existing switch
    {
        null                 => "",
        decimal d            => d.ToString(CultureInfo.CurrentCulture),
        double  d            => d.ToString(CultureInfo.CurrentCulture),
        float   f            => f.ToString(CultureInfo.CurrentCulture),
        long    l            => l.ToString(CultureInfo.CurrentCulture),
        int     i            => i.ToString(CultureInfo.CurrentCulture),
        _                    => existing.ToString() ?? "",
    };
}
