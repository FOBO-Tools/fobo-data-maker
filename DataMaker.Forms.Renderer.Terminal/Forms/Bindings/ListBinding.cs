using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Binding for the <c>list</c> kind — a newline-separated list of strings. Same
/// convention as the Uno <c>ListFieldEditor</c>: one item per line, empty
/// lines dropped on commit.
/// </summary>
internal sealed class ListBinding : FieldBinding
{
    private const int DefaultRows = 5;

    private readonly View _label;
    private readonly Label? _asterisk;
    private readonly TextView _field;
    private readonly Label _errorIndicator = CreateErrorIndicator();

    public ListBinding(FieldDefinition definition, FormState state) : base(definition, state)
    {
        (_label, _asterisk) = RequiredLabel.Build($"{definition.Label}  (one per line)", definition.Required);

        _field = new TextView
        {
            Width  = Dim.Fill(),
            Height = DefaultRows,
            Text   = JoinExisting(State.Get(definition.Name)),
        };

        // v1 gotcha: TextView.TextChanged fires only on programmatic setter.
        // ContentsChanged is the event that fires on user input.
        _field.ContentsChanged += _ =>
        {
            var raw = _field.Text.ToString() ?? "";
            var items = raw
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            State.Set(definition.Name, items.Length == 0 ? null : items);
        };
    }

    public override View Label => _label;
    public override View Editor => _field;
    public override int EditorHeight => DefaultRows;
    public override Label? RequiredAsterisk => _asterisk;
    public override Label? ErrorIndicator => _errorIndicator;

    private static string JoinExisting(object? existing)
    {
        if (existing is null) return "";
        if (existing is System.Collections.IEnumerable enumerable && existing is not string)
        {
            return string.Join('\n', enumerable.Cast<object?>()
                .Where(x => x is not null)
                .Select(x => x!.ToString()));
        }
        return existing.ToString() ?? "";
    }
}
