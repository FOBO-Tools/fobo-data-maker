using DataMaker.Schema.Fields;

namespace DataMaker.Schema.Import;

/// <summary>
/// Auto-suggests which form field a source header/column maps to, by comparing
/// the header against each candidate field's NAME and LABEL with separators +
/// case stripped. An exact normalised match wins (header "First name" → field
/// name <c>first_name</c> or label "First name"); otherwise a containment match
/// (<c>txt_email</c> / "E-mail" → <c>email</c>). Returns the matched field's
/// storage name, or null when nothing matches.
/// </summary>
public static class ImportFieldMatcher
{
    public static string? Suggest(string header, IEnumerable<FieldDefinition> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var key = Normalize(header);
        if (key.Length == 0) return null;

        var list = candidates as IReadOnlyList<FieldDefinition> ?? candidates.ToList();

        // Exact match on name or label.
        foreach (var f in list)
            if (Normalize(f.Name) == key || Normalize(f.Label ?? "") == key)
                return f.Name;

        // Containment match on name or label.
        foreach (var f in list)
        {
            var n = Normalize(f.Name);
            var l = Normalize(f.Label ?? "");
            if (n.Length > 0 && (n.Contains(key) || key.Contains(n))) return f.Name;
            if (l.Length > 0 && (l.Contains(key) || key.Contains(l))) return f.Name;
        }
        return null;
    }

    private static string Normalize(string? s) =>
        s is null ? "" : new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
