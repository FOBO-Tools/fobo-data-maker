using System.Text.Json.Serialization;

namespace DataMaker.Schema.Validation;

/// <summary>
/// Polymorphic validation rule. STJ discriminator = <c>$kind</c>.
/// Evaluated client-side for immediate feedback, and server-side on save
/// as the source of truth.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(RequiredRule),  "required")]
[JsonDerivedType(typeof(MinLengthRule), "minLength")]
[JsonDerivedType(typeof(MaxLengthRule), "maxLength")]
[JsonDerivedType(typeof(PatternRule),   "pattern")]
[JsonDerivedType(typeof(MinValueRule),  "min")]
[JsonDerivedType(typeof(MaxValueRule),  "max")]
[JsonDerivedType(typeof(ExpressionRule),"expression")]
public abstract record ValidationRule
{
    /// <summary>Custom error message shown when this rule fails. Null = engine's default message.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// Optional boolean expression (DataMaker.Expressions) — the rule only applies when
    /// this evaluates to true. Null = rule always applies. Enables conditional validation
    /// like "percentage required only when needs_vat is true":
    /// <c>new RequiredRule { When = "needs_vat == true" }</c>.
    /// </summary>
    public string? When { get; init; }
}

public sealed record RequiredRule : ValidationRule;

public sealed record MinLengthRule(int Length) : ValidationRule;
public sealed record MaxLengthRule(int Length) : ValidationRule;

public sealed record PatternRule(string Regex) : ValidationRule;

public sealed record MinValueRule(decimal Value) : ValidationRule;
public sealed record MaxValueRule(decimal Value) : ValidationRule;

/// <summary>Custom validation: a DataMaker.Expressions boolean expression. True = valid.</summary>
public sealed record ExpressionRule(string Expression) : ValidationRule;
