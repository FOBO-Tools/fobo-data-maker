using System.Text.RegularExpressions;
using DataMaker.Schema.Fields;

namespace DataMaker.Schema.Validation;

/// <summary>
/// Design-time "does this field make sense?" check. Same principle as the old
/// Chivan.Leads.Backend: reuse the runtime validators as design-time validators
/// by pointing them at the field's <see cref="FieldDefinition.DefaultValue"/>
/// instead of a user-submitted record value.
///
/// <para>
/// The payoff is zero duplication — tighten <see cref="IntrinsicValidators"/>
/// once and the designer starts flagging out-of-spec defaults automatically.
/// </para>
///
/// <para>
/// Scope of V1: all checks that don't need an expression engine. Expression-rule
/// evaluation against the default is orthogonal scaffolding and lives in the UI
/// layer where the engine is registered; see <c>FieldDefinitionViewModel</c> for
/// how it plugs in.
/// </para>
/// </summary>
public static class DesignValidator
{
    public static IReadOnlyList<DesignIssue> ValidateField(FieldDefinition field)
    {
        var issues = new List<DesignIssue>();
        AddIntrinsicIssues(field, issues);
        AddUserRuleIssues(field, issues);
        AddInternalConsistencyIssues(field, issues);
        return issues;
    }

    // ── Default vs intrinsic validators ─────────────────────────────

    private static void AddIntrinsicIssues(FieldDefinition field, List<DesignIssue> issues)
    {
        if (field.DefaultValue is null) return;

        foreach (var intrinsic in IntrinsicValidators.GetFor(field))
        {
            var error = intrinsic.Validate(field.DefaultValue, field);
            if (error is not null)
                issues.Add(new DesignIssue(DesignSeverity.Warning,
                    "Default", $"Default value violates: {intrinsic.Description}"));
        }
    }

    // ── Default vs user-authored rules ──────────────────────────────

    private static void AddUserRuleIssues(FieldDefinition field, List<DesignIssue> issues)
    {
        if (field.DefaultValue is null) return;

        for (int i = 0; i < field.Validation.Count; i++)
        {
            var rule = field.Validation[i];
            var err = CheckRuleAgainstDefault(rule, field.DefaultValue);
            if (err is not null)
                issues.Add(new DesignIssue(DesignSeverity.Warning,
                    $"Rule {i + 1}", $"Default value violates rule: {err}"));
        }
    }

    /// <summary>Run one rule against the default. Returns null if the rule passes or doesn't apply to the default.</summary>
    private static string? CheckRuleAgainstDefault(ValidationRule rule, object defaultValue) => rule switch
    {
        // Required is about user input, not about having a default — skip.
        RequiredRule         => null,

        MinLengthRule r  when defaultValue is string s && s.Length < r.Length
            => $"Default length {s.Length} < MinLength({r.Length}).",
        MaxLengthRule r  when defaultValue is string s && s.Length > r.Length
            => $"Default length {s.Length} > MaxLength({r.Length}).",
        PatternRule r    when defaultValue is string s && !TryRegexMatch(r.Regex, s)
            => $"Default '{Truncate(s)}' does not match pattern.",
        MinValueRule r   when TryDecimal(defaultValue, out var d) && d < r.Value
            => $"Default {d} < Min({r.Value}).",
        MaxValueRule r   when TryDecimal(defaultValue, out var d) && d > r.Value
            => $"Default {d} > Max({r.Value}).",
        // Expression rules need the engine + sibling context — validated in the UI layer.
        ExpressionRule       => null,
        _                    => null,
    };

    // ── Internal consistency (options contradict each other) ────────

    private static void AddInternalConsistencyIssues(FieldDefinition field, List<DesignIssue> issues)
    {
        // Text: MinLength > MaxLength.
        if (field.Text is { MinLength: { } minL, MaxLength: { } maxL } && minL > maxL)
            issues.Add(new DesignIssue(DesignSeverity.Warning,
                "Options", $"MinLength ({minL}) is greater than MaxLength ({maxL})."));

        // Numeric: Min > Max.
        if (field.Number is { Min: { } nMin, Max: { } nMax } && nMin > nMax)
            issues.Add(new DesignIssue(DesignSeverity.Warning,
                "Options", $"Min ({nMin}) is greater than Max ({nMax})."));

        // Text Pattern must be a compilable regex.
        if (!string.IsNullOrWhiteSpace(field.Text?.Pattern) && !TryCompileRegex(field.Text!.Pattern!, out var regexErr))
            issues.Add(new DesignIssue(DesignSeverity.Warning,
                "Options", $"Pattern is not a valid regex: {regexErr}"));

        // Same check for any user-authored PatternRule.
        for (int i = 0; i < field.Validation.Count; i++)
        {
            if (field.Validation[i] is PatternRule pr
                && !TryCompileRegex(pr.Regex, out var ruleRegexErr))
            {
                issues.Add(new DesignIssue(DesignSeverity.Warning,
                    $"Rule {i + 1}", $"Pattern is not a valid regex: {ruleRegexErr}"));
            }
        }

        // Calculated + Default: default is dead code.
        if (!string.IsNullOrWhiteSpace(field.CalculatedExpression) && field.DefaultValue is not null)
            issues.Add(new DesignIssue(DesignSeverity.Warning,
                "Default", "Default is ignored on calculated fields — the expression always wins."));

        // Calculated + Required: user can never satisfy "required" by editing.
        if (!string.IsNullOrWhiteSpace(field.CalculatedExpression) && field.Required)
            issues.Add(new DesignIssue(DesignSeverity.Warning,
                "Options", "Required is meaningless on a calculated field — the value is auto-computed."));
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static bool TryRegexMatch(string pattern, string input)
    {
        try { return Regex.IsMatch(input, pattern); }
        catch { return false; }  // invalid regex → surfaced separately
    }

    private static bool TryCompileRegex(string pattern, out string error)
    {
        try { _ = new Regex(pattern); error = ""; return true; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    private static bool TryDecimal(object value, out decimal result)
    {
        switch (value)
        {
            case decimal m: result = m; return true;
            case double d:  result = (decimal)d; return true;
            case long l:    result = l; return true;
            case int i:     result = i; return true;
            case string s when decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                                  System.Globalization.CultureInfo.InvariantCulture, out var parsed):
                result = parsed; return true;
            default: result = 0; return false;
        }
    }

    private static string Truncate(string s) => s.Length <= 40 ? s : s[..37] + "…";
}

public enum DesignSeverity { Info, Warning, Error }

/// <summary>One issue detected in a field's design-time configuration. <see cref="Path"/> is a short label pointing at the offending area ("Default", "Options", "Rule 2").</summary>
public sealed record DesignIssue(DesignSeverity Severity, string Path, string Message)
{
    public override string ToString() => $"[{Severity}] {Path}: {Message}";
}
