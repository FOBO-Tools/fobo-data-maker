using System.Text.RegularExpressions;

namespace DataMaker.Schema.Validation;

/// <summary>
/// The single snake_case SQL-safe rule for identifiers that become SQLite
/// table / column names — <see cref="Forms.Form.Id"/> (table seed) and
/// <see cref="Fields.FieldDefinition.Name"/> (generated column). One source
/// so the FormEditor inspector, the new-form dialog's id box, and the
/// storage-layer backstop (<c>SqlIdentifier</c>) all agree and can't drift.
///
/// <para>
/// Field / record <b>ids</b> (GUIDs, may contain hyphens + uppercase) take a
/// separate, permissive path — this rule is only for storage-key <em>names</em>.
/// </para>
/// </summary>
public static class SqlSafeName
{
    /// <summary>Lowercase letter or underscore, then lowercase letters / digits / underscores, ≤63 chars.</summary>
    public const string Pattern = "^[a-z_][a-z0-9_]{0,62}$";

    private static readonly Regex Rx = new(Pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValid(string? name) => name is not null && Rx.IsMatch(name);

    private static readonly Regex NonSlugRx = new("[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Derive a snake_case, SQL-safe name suggestion from a human label
    /// (e.g. "First Name" → <c>first_name</c>). Lowercases, collapses runs of
    /// non-alphanumerics to a single underscore, prefixes "_" when it would
    /// start with a digit, and caps at 63 chars. Returns "" when nothing usable
    /// remains. The result always satisfies <see cref="IsValid"/> (or is empty).
    /// </summary>
    public static string Suggest(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "";
        var s = NonSlugRx.Replace(label.Trim().ToLowerInvariant(), "_").Trim('_');
        if (s.Length == 0) return "";
        if (char.IsDigit(s[0])) s = "_" + s;
        if (s.Length > 63) s = s[..63].TrimEnd('_');
        return s;
    }
}
