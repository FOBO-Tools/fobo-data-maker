using System.Text.RegularExpressions;
using DataMaker.Expressions.Engine;
using DataMaker.Forms.Runtime;
using DataMaker.Schema.Fields;
using DataMaker.Schema.Forms;
using DataMaker.Schema.Layout;
using DataMaker.Schema.Validation;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms;

/// <summary>
/// Glue between <see cref="FormEvaluator"/> (expression engine) and the
/// Terminal.Gui view tree produced by <see cref="FormRenderer"/>. Owns the
/// single live <see cref="FormEvaluator"/> for the session and keeps it in
/// lockstep with <see cref="FormState"/>: every user edit re-fires visibility
/// evaluation, recomputes calculated fields, and updates the TUI without
/// rebuilding views.
///
/// <para>
/// Dynamic visibility leaves a gap in the vertical layout where a hidden
/// field would sit — flowing the layout back together on every toggle would
/// require re-running the whole walker and losing scroll + cursor position.
/// Accepted for the first cut; reflow is a future enhancement.
/// </para>
/// </summary>
internal sealed class FormRuntime : IDisposable
{
    private readonly Form _form;
    private readonly FormState _state;
    private readonly FormEvaluator _evaluator;
    private readonly Dictionary<string, List<View>> _viewsByField = new(StringComparer.Ordinal);
    private readonly Dictionary<GroupColumn, View> _viewsByGroup = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, FieldBinding> _bindingsByField = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FieldDefinition> _fieldsByName;
    private readonly Dictionary<string, string?> _fieldErrors = new(StringComparer.Ordinal);
    private bool _reevaluating;

    /// <summary>
    /// Fires when a field gains focus. Args: (fieldName, currentError).
    /// FormWindow subscribes to update the status bar.
    /// </summary>
    public event Action<string, string?>? FieldFocused;

    public FormRuntime(ExpressionEngine engine, Form form, FormState state)
    {
        _form      = form;
        _state     = state;
        _evaluator = new FormEvaluator(engine, form);
        _fieldsByName = form.Fields.ToDictionary(f => f.Name, StringComparer.Ordinal);

        // Seed evaluator with whatever's already in state (defaults applied by renderer).
        foreach (var f in form.Fields)
            _evaluator.SetValue(f.Name, _state.Get(f.Name));

        _state.ValueChanged += OnStateChanged;
    }

    /// <summary>
    /// Detach from <see cref="FormState.ValueChanged"/>. Called when the
    /// window tears down its view subtree (e.g. rebuilding on resize) so
    /// the old runtime stops reacting to state changes the new runtime is
    /// already handling.
    /// </summary>
    public void Dispose()
    {
        _state.ValueChanged -= OnStateChanged;
    }

    /// <summary>Called by the renderer for every field it emits, after the views exist.</summary>
    public void RegisterField(string fieldName, FieldBinding binding, IEnumerable<View> views)
    {
        _bindingsByField[fieldName] = binding;
        if (!_viewsByField.TryGetValue(fieldName, out var list))
            _viewsByField[fieldName] = list = new List<View>();
        list.AddRange(views);

        // Wire focus/blur for per-field validation feedback. Leave = user
        // tabbed out → validate this field and sync its indicator. Enter =
        // user entered field → surface any existing error in the status bar.
        var editor = binding.Editor;
        editor.Leave += _ => OnFieldBlurred(fieldName, binding);
        editor.Enter += _ => FieldFocused?.Invoke(
            fieldName,
            _fieldErrors.TryGetValue(fieldName, out var e) ? e : null);
    }

    /// <summary>The editor View for a field, or null if it has none. Used by
    /// the window to scroll a newly-focused field into the viewport.</summary>
    public View? EditorFor(string fieldName) =>
        _bindingsByField.TryGetValue(fieldName, out var b) ? b.Editor : null;

    /// <summary>Called by the renderer for every group it emits.</summary>
    public void RegisterGroup(GroupColumn group, View frame) =>
        _viewsByGroup[group] = frame;

    /// <summary>
    /// Evaluate visibility + calculated fields once the full view tree is in
    /// place. Call this exactly once after the renderer finishes.
    /// </summary>
    public void InitialEvaluate() => Reevaluate(triggeredBy: null);

    /// <summary>Returns true if the field would render visibly given current state.</summary>
    public bool IsFieldVisible(string fieldName) => _evaluator.IsFieldVisible(fieldName);

    /// <summary>
    /// Run intrinsic + user validation over every visible field. Returns a
    /// field-name → first-error map; empty = submission OK.
    /// </summary>
    public IReadOnlyDictionary<string, string> Validate()
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in _form.Fields)
        {
            if (!_evaluator.IsFieldVisible(field.Name)) continue;

            var err = FirstError(field);
            if (err is not null) errors[field.Name] = err;
        }
        return errors;
    }

    /// <summary>
    /// Re-run validation for every field and sync each binding's error
    /// indicator visibility. Called by Submit so all invalid fields light up
    /// at once (rather than only the ones the user happened to tab through).
    /// </summary>
    public void SyncAllErrorIndicators()
    {
        foreach (var field in _form.Fields)
        {
            if (!_evaluator.IsFieldVisible(field.Name))
            {
                _fieldErrors[field.Name] = null;
                if (_bindingsByField.TryGetValue(field.Name, out var b) && b.ErrorIndicator is { } ind && ind.Visible)
                    ind.Visible = false;
                continue;
            }

            var err = FirstError(field);
            _fieldErrors[field.Name] = err;
            if (_bindingsByField.TryGetValue(field.Name, out var binding) && binding.ErrorIndicator is { } indicator)
            {
                var shouldShow = err is not null;
                if (indicator.Visible != shouldShow)
                {
                    indicator.Visible = shouldShow;
                    indicator.SetNeedsDisplay();
                }
            }
        }
    }

    /// <summary>
    /// Validate only the named fields (one wizard step) and sync their error
    /// indicators — the gate the Next button runs before advancing. Returns
    /// name→error for the fields that failed (empty = the step may advance).
    /// </summary>
    public IReadOnlyDictionary<string, string> ValidateStep(IEnumerable<string> fieldNames)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in fieldNames)
        {
            if (!_fieldsByName.TryGetValue(name, out var field)) continue;

            var err = _evaluator.IsFieldVisible(name) ? FirstError(field) : null;
            _fieldErrors[name] = err;
            SyncIndicator(name, err);
            if (err is not null) errors[name] = err;
        }
        return errors;
    }

    /// <summary>Show or hide a field's inline error indicator to match its current error.</summary>
    private void SyncIndicator(string fieldName, string? err)
    {
        if (!_bindingsByField.TryGetValue(fieldName, out var binding) || binding.ErrorIndicator is not { } indicator)
            return;
        var shouldShow = err is not null;
        if (indicator.Visible != shouldShow)
        {
            indicator.Visible = shouldShow;
            indicator.SetNeedsDisplay();
        }
    }

    private void OnFieldBlurred(string fieldName, FieldBinding binding)
    {
        if (!_fieldsByName.TryGetValue(fieldName, out var def)) return;

        var err = _evaluator.IsFieldVisible(fieldName) ? FirstError(def) : null;
        _fieldErrors[fieldName] = err;

        if (binding.ErrorIndicator is { } indicator)
        {
            var shouldShow = err is not null;
            if (indicator.Visible != shouldShow)
            {
                indicator.Visible = shouldShow;
                indicator.SetNeedsDisplay();
            }
        }
    }

    // ── Change propagation ────────────────────────────────────────────

    private void OnStateChanged(string fieldName)
    {
        if (_reevaluating) return;
        _evaluator.SetValue(fieldName, _state.Get(fieldName));
        Reevaluate(triggeredBy: fieldName);
    }

    private void Reevaluate(string? triggeredBy)
    {
        _reevaluating = true;
        try
        {
            // Visibility cascade for fields.
            foreach (var (name, views) in _viewsByField)
            {
                var visible = _evaluator.IsFieldVisible(name);
                foreach (var v in views)
                {
                    if (v.Visible != visible) v.Visible = visible;
                }
            }

            // Visibility for groups.
            foreach (var (group, frame) in _viewsByGroup)
            {
                var visible = _evaluator.IsGroupVisible(group);
                if (frame.Visible != visible) frame.Visible = visible;
            }

            // Calculated field recompute — runs regardless of visibility since
            // other fields may depend on these. Match Uno FormInstance's semantics.
            foreach (var field in _form.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.CalculatedExpression)) continue;

                var computed = _evaluator.GetCalculatedValue(field.Name);
                if (!Equals(_state.Get(field.Name), computed))
                {
                    _state.Set(field.Name, computed);
                    _evaluator.SetValue(field.Name, computed);
                }

                if (_bindingsByField.TryGetValue(field.Name, out var binding))
                    binding.OnComputedValueUpdated(computed);
            }

            // Don't call Application.Refresh() here: when Reevaluate is
            // triggered from inside a widget callback (e.g. ComboBox's
            // SelectedItemChanged), a synchronous refresh can try to redraw
            // that widget before its driver-owned attributes are wired up,
            // throwing "Attributes must be initialized by a driver". Every
            // view we touched above called SetNeedsDisplay already; the main
            // loop will repaint on the next tick, which is what we want.
        }
        finally { _reevaluating = false; }
    }

    // ── Validation ────────────────────────────────────────────────────

    private string? FirstError(FieldDefinition field)
    {
        var value = _state.Get(field.Name);

        foreach (var intrinsic in IntrinsicValidators.GetFor(field))
        {
            var err = intrinsic.Validate(value, field);
            if (err is not null) return err;
        }

        for (var i = 0; i < field.Validation.Count; i++)
        {
            var rule = field.Validation[i];
            if (!_evaluator.ShouldApplyRule(rule.When)) continue;

            var err = EvaluateRule(field, value, rule, i);
            if (err is not null) return err;
        }

        return null;
    }

    private string? EvaluateRule(FieldDefinition field, object? value, ValidationRule rule, int ruleIndex) => rule switch
    {
        RequiredRule    => IsEmpty(value) ? (rule.Message ?? $"{field.Label} is required.") : null,

        MinLengthRule r => value is string s && s.Length < r.Length
                           ? (rule.Message ?? $"Minimum {r.Length} characters.") : null,

        MaxLengthRule r => value is string s && s.Length > r.Length
                           ? (rule.Message ?? $"Maximum {r.Length} characters.") : null,

        PatternRule r   => value is string s && !string.IsNullOrEmpty(s) &&
                           !Regex.IsMatch(s, r.Regex)
                           ? (rule.Message ?? "Value does not match the required pattern.") : null,

        MinValueRule r  => TryDecimal(value, out var d) && d < r.Value
                           ? (rule.Message ?? $"Minimum value is {r.Value}.") : null,

        MaxValueRule r  => TryDecimal(value, out var d) && d > r.Value
                           ? (rule.Message ?? $"Maximum value is {r.Value}.") : null,

        ExpressionRule  => _evaluator.EvaluateExpressionRule(field.Name, ruleIndex)
                           ? null : (rule.Message ?? "Value is not valid."),

        _               => null,
    };

    private static bool IsEmpty(object? value) => value switch
    {
        null                                      => true,
        string s when string.IsNullOrEmpty(s)     => true,
        System.Collections.ICollection c
                    when c.Count == 0             => true,
        _                                         => false,
    };

    private static bool TryDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case null:      result = 0; return false;
            case decimal m: result = m; return true;
            case double d:  result = (decimal)d; return true;
            case long l:    result = l; return true;
            case int i:     result = i; return true;
            case string s when decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out var parsed):
                result = parsed; return true;
            default:        result = 0; return false;
        }
    }
}
