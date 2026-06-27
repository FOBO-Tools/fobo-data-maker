namespace DataMaker.Schema.Import;

/// <summary>
/// Compares the user-editable surface of two <see cref="ImportMapping"/>s,
/// backing the dialog's dirty-state gate (validation standard: Save stays
/// disabled until something actually changes). Ignores the volatile
/// <see cref="ImportMapping.Updated"/> stamp; mapping order is irrelevant.
/// </summary>
public static class ImportMappingComparer
{
    public static bool EditableEquivalent(ImportMapping a, ImportMapping b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        if (!string.Equals(a.FormId, b.FormId, StringComparison.Ordinal)) return false;
        if (a.SourceKind != b.SourceKind) return false;
        if (!string.Equals(a.SourceFingerprint, b.SourceFingerprint, StringComparison.Ordinal)) return false;
        return MappingsEqual(a.Mappings, b.Mappings);
    }

    private static bool MappingsEqual(
        IReadOnlyList<ImportFieldMapping> a,
        IReadOnlyList<ImportFieldMapping> b)
    {
        if (a.Count != b.Count) return false;
        var sa = Sort(a);
        var sb = Sort(b);
        for (var i = 0; i < sa.Count; i++)
        {
            if (!string.Equals(sa[i].SourceFieldName, sb[i].SourceFieldName, StringComparison.Ordinal)) return false;
            if (!string.Equals(sa[i].FormFieldName,   sb[i].FormFieldName,   StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static List<ImportFieldMapping> Sort(IReadOnlyList<ImportFieldMapping> m) =>
        m.OrderBy(x => x.SourceFieldName, StringComparer.Ordinal)
         .ThenBy(x => x.FormFieldName, StringComparer.Ordinal)
         .ToList();
}
