using System.Globalization;
using System.Text.RegularExpressions;

namespace DataMaker.Sdk;

/// <summary>
/// Canonical Data Maker field kinds and per-kind coercion. form.json persists
/// kinds in lowercase kebab-case ("long-text", "multi-choice"); older bundles
/// use PascalCase enum names or no-dash spellings, so <see cref="Normalize"/>
/// folds every variant onto the canonical id.
/// </summary>
public static partial class FieldKinds
{
    public const string Text = "text", LongText = "long-text", RichText = "rich-text",
        Number = "number", Decimal = "decimal", Money = "money",
        Date = "date", DateTime = "datetime", Boolean = "boolean",
        Choice = "choice", MultiChoice = "multi-choice", List = "list",
        Email = "email", Phone = "phone", Url = "url", Geo = "geo",
        Image = "image", Attachment = "attachment", Relation = "relation";

    private static readonly HashSet<string> All = new(StringComparer.Ordinal)
    {
        Text, LongText, RichText, Number, Decimal, Money, Date, DateTime, Boolean,
        Choice, MultiChoice, List, Email, Phone, Url, Geo, Image, Attachment, Relation,
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["longtext"] = LongText,
        ["richtext"] = RichText,
        ["multichoice"] = MultiChoice,
        ["datetimeoffset"] = DateTime,
    };

    /// <summary>Kinds in fields[] that never accept a submitted value.</summary>
    public static readonly HashSet<string> NonInput = new(StringComparer.Ordinal)
    {
        "calc", "calculated", "heading", "signature",
    };

    public static string Normalize(string? kind)
    {
        var k = (kind ?? "").Trim().ToLowerInvariant();
        if (All.Contains(k)) return k;
        if (Aliases.TryGetValue(k, out var a)) return a;
        var squashed = Squash(k);
        if (Aliases.TryGetValue(squashed, out var a2)) return a2;
        foreach (var c in All)
            if (Squash(c) == squashed) return c;
        return k;
    }

    public static bool IsInput(string? kind) => !NonInput.Contains(Normalize(kind));

    /// <summary>Coerce a raw value to the wire shape for <paramref name="kind"/>. Returns (value, error).</summary>
    public static (object? value, string? error) Coerce(string? kind, object? raw, FieldDescriptor field)
    {
        var k = Normalize(kind);
        switch (k)
        {
            case Number:
            case Decimal:
            case Money:
            {
                if (raw is double or long or int or float or decimal) return (raw, null);
                if (double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var n))
                {
                    // Emit integral values as long so JSON matches the JS SDK.
                    if (n == Math.Floor(n) && !double.IsInfinity(n)) return ((long)n, null);
                    return (n, null);
                }
                return (null, $"expected a number, got \"{raw}\"");
            }
            case Boolean:
            {
                if (raw is bool b) return (b, null);
                var s = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim().ToLowerInvariant() ?? "";
                if (s is "true" or "1" or "yes" or "y" or "on") return (true, null);
                if (s is "false" or "0" or "no" or "n" or "off") return (false, null);
                return (null, $"expected a boolean, got \"{raw}\"");
            }
            case MultiChoice:
            {
                var items = raw is System.Collections.IEnumerable en && raw is not string
                    ? en.Cast<object?>().Select(v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").ToList()
                    : new List<string> { Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "" };
                var allowed = ChoiceValues(field);
                if (allowed is not null && !field.AllowCustom)
                {
                    var bad = items.Where(v => !allowed.Contains(v)).ToList();
                    if (bad.Count > 0) return (null, "not in allowed choices: " + string.Join(", ", bad));
                }
                return (items, null);
            }
            case Choice:
            {
                var v = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "";
                var allowed = ChoiceValues(field);
                if (allowed is not null && !field.AllowCustom && !allowed.Contains(v))
                    return (null, $"\"{v}\" is not one of the allowed choices");
                return (v, null);
            }
            case Text:
            case LongText:
            case RichText:
            case Email:
            case Phone:
            case Url:
            case Date:
            case DateTime:
                return (Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "", null);
            default:
                // image/attachment/geo/relation/list and unknown: pass through.
                return (raw, null);
        }
    }

    private static HashSet<string>? ChoiceValues(FieldDescriptor field)
    {
        if (field.Choices is not { Count: > 0 }) return null;
        return field.Choices.Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
    }

    private static string Squash(string s) => NonAlnum().Replace(s, "");

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NonAlnum();
}
