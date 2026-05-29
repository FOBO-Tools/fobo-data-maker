using DataMaker.Expressions.Engine;
using DataMaker.Schema.Fields;
using DataMaker.Schema.Forms;
using DataMaker.Schema.Layout;
using DataMaker.Schema.Validation;
using DynamicExpresso;

namespace DataMaker.Forms.Runtime;

/// <summary>
/// Runtime glue between <see cref="ExpressionEngine"/> and a specific
/// <see cref="Form"/>. Compiles every per-field <c>VisibleWhen</c> /
/// <c>CalculatedExpression</c> plus every rule-level <c>Expression</c> and
/// <c>When</c> once, then evaluates them on demand against a value snapshot
/// maintained by <see cref="SetValue"/>.
///
/// <para>
/// This is the plug-in point called out in the deferred backlog — the
/// <c>DesignValidator</c> can later accept a <see cref="FormEvaluator"/> to
/// close the "expression rule vs default" design-time check from the same
/// seam.
/// </para>
///
/// <para>
/// Malformed expressions are swallowed at compile time; the design-time
/// validator is responsible for surfacing those before a form gets saved.
/// A null compiled <see cref="Lambda"/> is treated as "always true" so one
/// broken expression doesn't make the whole form unusable at runtime.
/// </para>
/// </summary>
public sealed class FormEvaluator
{
    private readonly ExpressionEngine _engine;
    private readonly Parameter[] _parameters;
    private readonly Dictionary<string, Lambda?> _visibleWhenByField = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Lambda?> _calculatedByField  = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Lambda?> _whenCache          = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Field, int RuleIndex), Lambda?> _ruleExpressions = new();
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    /// <summary>
    /// For each field, the compiled VisibleWhen lambdas of every <see cref="GroupColumn"/>
    /// that is an ancestor of that field in the layout tree. Null entries are groups
    /// without a VisibleWhen (always visible). Fields not appearing in any group
    /// have no entry here; their visibility is own-VisibleWhen only.
    /// </summary>
    private readonly Dictionary<string, List<Lambda?>> _ancestorGroupVisibilityByField = new(StringComparer.Ordinal);

    /// <summary>
    /// Per-column compiled VisibleWhen, keyed by schema instance reference
    /// (stable across evaluations — the tree isn't rewritten at runtime).
    /// Covers <see cref="GroupColumn"/>, <see cref="RichTextColumn"/>,
    /// <see cref="ImageColumn"/> and <see cref="DividerColumn"/> uniformly so
    /// the renderer can ask <see cref="IsColumnVisible"/> for any decorative
    /// column. Null entries are columns without a VisibleWhen (always visible).
    /// </summary>
    private readonly Dictionary<Column, Lambda?> _columnVisibilityLambdas = new(ReferenceEqualityComparer.Instance);

    public Form Form { get; }

    public FormEvaluator(ExpressionEngine engine, Form form)
    {
        _engine = engine;
        Form    = form;
        _parameters = form.Fields
            .Select(f => new Parameter(f.Name, ParameterTypeFor(f.Kind)))
            .ToArray();

        foreach (var f in form.Fields)
        {
            if (!string.IsNullOrWhiteSpace(f.VisibleWhen))
                _visibleWhenByField[f.Name] = TryCompile(f.VisibleWhen!);

            if (!string.IsNullOrWhiteSpace(f.CalculatedExpression))
                _calculatedByField[f.Name] = TryCompile(f.CalculatedExpression!);

            for (var i = 0; i < f.Validation.Count; i++)
            {
                if (f.Validation[i] is ExpressionRule er)
                    _ruleExpressions[(f.Name, i)] = TryCompile(er.Expression);
            }
        }

        // Walk the layout tree once, recording for every FieldColumn the chain of
        // GroupColumn.VisibleWhen lambdas from the root down. Depth is unbounded
        // (schema-level); the designer's 2-level cap is enforced elsewhere.
        var fieldsById = form.Fields.ToDictionary(f => f.Id, StringComparer.Ordinal);
        foreach (var step in form.Steps)
            foreach (var section in step.Sections)
                foreach (var row in section.Rows)
                    WalkRow(row, ancestors: Array.Empty<Lambda?>(), fieldsById);
    }

    private void WalkRow(Row row, IReadOnlyList<Lambda?> ancestors, Dictionary<string, FieldDefinition> fieldsById)
    {
        foreach (var col in row.Columns)
        {
            switch (col)
            {
                case FieldColumn fc:
                    if (fieldsById.TryGetValue(fc.FieldId, out var field) && ancestors.Count > 0)
                        _ancestorGroupVisibilityByField[field.Name] = ancestors.ToList();
                    break;
                case GroupColumn gc:
                    var groupLambda = string.IsNullOrWhiteSpace(gc.VisibleWhen)
                        ? null
                        : TryCompile(gc.VisibleWhen!);
                    _columnVisibilityLambdas[gc] = groupLambda;
                    var deeper = new List<Lambda?>(ancestors.Count + 1);
                    deeper.AddRange(ancestors);
                    deeper.Add(groupLambda);
                    foreach (var innerRow in gc.Rows)
                        WalkRow(innerRow, deeper, fieldsById);
                    break;
                case RichTextColumn rt:
                    _columnVisibilityLambdas[rt] = string.IsNullOrWhiteSpace(rt.VisibleWhen)
                        ? null
                        : TryCompile(rt.VisibleWhen!);
                    break;
                case ImageColumn ic:
                    _columnVisibilityLambdas[ic] = string.IsNullOrWhiteSpace(ic.VisibleWhen)
                        ? null
                        : TryCompile(ic.VisibleWhen!);
                    break;
                case DividerColumn dc:
                    _columnVisibilityLambdas[dc] = string.IsNullOrWhiteSpace(dc.VisibleWhen)
                        ? null
                        : TryCompile(dc.VisibleWhen!);
                    break;
                case SpacerColumn sp:
                    _columnVisibilityLambdas[sp] = string.IsNullOrWhiteSpace(sp.VisibleWhen)
                        ? null
                        : TryCompile(sp.VisibleWhen!);
                    break;
            }
        }
    }

    // ── Value snapshot ─────────────────────────────────────────────────

    public void SetValue(string fieldName, object? value) =>
        _values[fieldName] = value;

    public object? GetValue(string fieldName) =>
        _values.TryGetValue(fieldName, out var v) ? v : null;

    public IReadOnlyDictionary<string, object?> Snapshot => _values;

    // ── Evaluation ─────────────────────────────────────────────────────

    /// <summary>
    /// True when the field's own <c>VisibleWhen</c> evaluates true (or is
    /// absent) AND every ancestor <see cref="GroupColumn"/>'s <c>VisibleWhen</c>
    /// evaluates true (or is absent). Validation cascades: a field hidden by
    /// an ancestor group skips all its validation rules, just like a field
    /// hidden by its own <c>VisibleWhen</c>.
    /// </summary>
    public bool IsFieldVisible(string fieldName)
    {
        if (_visibleWhenByField.TryGetValue(fieldName, out var own) && own is not null)
        {
            if (!(Invoke<bool?>(own) ?? false)) return false;
        }

        if (_ancestorGroupVisibilityByField.TryGetValue(fieldName, out var ancestors))
        {
            foreach (var lambda in ancestors)
            {
                if (lambda is null) continue;   // group without VisibleWhen = always visible
                if (!(Invoke<bool?>(lambda) ?? false)) return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when a column has no <c>VisibleWhen</c> or when its expression
    /// evaluates true. Works uniformly for <see cref="GroupColumn"/> and the
    /// decorative columns (<see cref="RichTextColumn"/>, <see cref="ImageColumn"/>,
    /// <see cref="DividerColumn"/>). For groups, validation cascade for
    /// descendant fields is handled independently through
    /// <see cref="IsFieldVisible"/>.
    /// </summary>
    public bool IsColumnVisible(Column column)
    {
        if (!_columnVisibilityLambdas.TryGetValue(column, out var lambda) || lambda is null)
            return true;
        return Invoke<bool?>(lambda) ?? false;
    }

    /// <summary>Convenience overload for the group case (the only call-site that pre-existed the decorative-column gap).</summary>
    public bool IsGroupVisible(GroupColumn group) => IsColumnVisible(group);

    /// <summary>Evaluate a calculated field's expression. Returns null if no expression is defined or it fails.</summary>
    public object? GetCalculatedValue(string fieldName)
    {
        if (!_calculatedByField.TryGetValue(fieldName, out var lambda) || lambda is null)
            return null;
        return Invoke<object>(lambda);
    }

    /// <summary>Evaluate a rule's <c>When</c> clause. Rules without a When always apply.</summary>
    public bool ShouldApplyRule(string? whenExpression)
    {
        if (string.IsNullOrWhiteSpace(whenExpression)) return true;
        if (!_whenCache.TryGetValue(whenExpression, out var lambda))
            _whenCache[whenExpression] = lambda = TryCompile(whenExpression);
        if (lambda is null) return true;
        return Invoke<bool?>(lambda) ?? false;
    }

    /// <summary>Evaluate a previously-compiled <see cref="ExpressionRule"/>. Returns true when the rule passes.</summary>
    public bool EvaluateExpressionRule(string fieldName, int ruleIndex)
    {
        if (!_ruleExpressions.TryGetValue((fieldName, ruleIndex), out var lambda) || lambda is null)
            return true;
        return Invoke<bool?>(lambda) ?? false;
    }

    // ── Internals ──────────────────────────────────────────────────────

    private Lambda? TryCompile(string expression)
    {
        try { return _engine.Compile(expression, _parameters); }
        catch { return null; }
    }

    private T? Invoke<T>(Lambda lambda)
    {
        var args = new object?[_parameters.Length];
        for (var i = 0; i < _parameters.Length; i++)
            args[i] = _values.TryGetValue(_parameters[i].Name, out var v) ? v : null;
        return _engine.Evaluate<T>(lambda, args);
    }

    /// <summary>
    /// C# type used for a field's expression parameter. Delegates to
    /// <see cref="FieldTypeRegistry"/> so custom field types declare their own
    /// parameter type. Unknown ids fall back to <c>string</c> — safe because
    /// everything coerces through ToString for equality checks.
    /// </summary>
    private static Type ParameterTypeFor(string kind) =>
        FieldTypeRegistry.Get(kind)?.ExpressionParameterType ?? typeof(string);
}
