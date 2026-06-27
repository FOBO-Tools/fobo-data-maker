using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Binding for the <c>scale</c> kind (Likert / rating / NPS). A scale is just a
/// single integer between <see cref="ScaleOptions.Min"/> and
/// <see cref="ScaleOptions.Max"/>, so the terminal captures it as a RadioGroup
/// of the numeric points — one row per value, with the anchor labels folded
/// into the end points so their meaning survives. The star / shape figure row
/// from the richer renderers is purely cosmetic and is dropped here (the matrix
/// records this surface as <see cref="FieldSupport.Limited"/>); the captured
/// value is identical.
///
/// <para>
/// Stored as a <see cref="long"/> to match <c>RecordJson</c>'s scale coercion
/// and the evaluator's parameter type — same reasoning as
/// <see cref="NumberBinding"/>.
/// </para>
/// </summary>
internal sealed class ScaleBinding : FieldBinding
{
    private readonly View _label;
    private readonly Label? _asterisk;
    private readonly RadioGroup _field;
    private readonly int _height;
    private readonly IReadOnlyList<int> _points;

    public ScaleBinding(FieldDefinition definition, FormState state) : base(definition, state)
    {
        (_label, _asterisk) = RequiredLabel.Build(definition.Label, definition.Required);

        var sc  = definition.Scale ?? new ScaleOptions();
        var top = Math.Max(sc.Max, sc.Min + 1); // mirror the render-time clamp
        var points = new List<int>();
        for (var v = sc.Min; v <= top; v++) points.Add(v);
        _points = points;

        var labels = points.Select(v =>
        {
            if (v == sc.Min && !string.IsNullOrWhiteSpace(sc.MinLabel)) return $"{v} — {sc.MinLabel}";
            if (v == top    && !string.IsNullOrWhiteSpace(sc.MaxLabel)) return $"{v} — {sc.MaxLabel}";
            return v.ToString();
        }).ToArray();

        _field = new RadioGroup(labels.Select(NStack.ustring.Make).ToArray())
        {
            Width = Dim.Fill(),
        };

        var selected = IndexOf(state.Get(definition.Name));
        if (selected >= 0) _field.SelectedItem = selected;

        _field.SelectedItemChanged += args =>
        {
            var idx = args.SelectedItem;
            State.Set(definition.Name,
                idx >= 0 && idx < _points.Count ? (object)(long)_points[idx] : null);
        };

        _height = Math.Max(1, points.Count);
    }

    public override View Label => _label;
    public override View Editor => _field;
    public override int EditorHeight => _height;
    public override Label? RequiredAsterisk => _asterisk;

    /// <summary>Index of the point matching the stored value, or -1 if unset / off-scale.</summary>
    private int IndexOf(object? existing)
    {
        if (existing is null) return -1;
        if (!TryToInt(existing, out var value)) return -1;
        for (var i = 0; i < _points.Count; i++)
            if (_points[i] == value) return i;
        return -1;
    }

    private static bool TryToInt(object value, out int result)
    {
        switch (value)
        {
            case long l:    result = (int)l; return true;
            case int i:     result = i;      return true;
            case double d:  result = (int)d; return true;
            case decimal m: result = (int)m; return true;
            default:
                return int.TryParse(value.ToString(), out result);
        }
    }
}
