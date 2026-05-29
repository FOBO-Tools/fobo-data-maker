namespace DataMaker.Forms.Renderer.Terminal.Forms;

/// <summary>
/// Mutable in-memory backing for a single form-filling session. Keyed by
/// <c>FieldDefinition.Name</c> — the same shape the submission payload's
/// <c>values</c> map uses — so there's no translation step when we pack the
/// submission.
///
/// <para>
/// Raises <see cref="ValueChanged"/> on every <see cref="Set"/> so the
/// <see cref="FormRuntime"/> can react: update the expression evaluator,
/// recompute calculated fields, and flip visibility for fields whose
/// <c>VisibleWhen</c> now resolves differently.
/// </para>
/// </summary>
internal sealed class FormState
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    /// <summary>
    /// Fires after a <see cref="Set"/> changes a value. Argument is the field
    /// name. Subscribers must be idempotent — the runtime may write back
    /// (e.g. calculated fields) during reevaluation.
    /// </summary>
    public event Action<string>? ValueChanged;

    public object? Get(string name) =>
        _values.TryGetValue(name, out var v) ? v : null;

    public void Set(string name, object? value)
    {
        var existed = _values.TryGetValue(name, out var previous);
        if (value is null)
        {
            if (!existed) return;
            _values.Remove(name);
        }
        else
        {
            if (existed && Equals(previous, value)) return; // no-op: don't flap reeval
            _values[name] = value;
        }
        ValueChanged?.Invoke(name);
    }

    public IReadOnlyDictionary<string, object?> Snapshot() =>
        _values.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
}
