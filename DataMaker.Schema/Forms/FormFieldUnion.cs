using DataMaker.Schema.Fields;

namespace DataMaker.Schema.Forms;

/// <summary>
/// Denormalised "all fields ever seen on this form across all versions"
/// projection. Lives as a cached JSON blob on the form row
/// (<c>forms.field_union_json</c>) so the records grid can render its
/// column header set in O(1) without per-row schema lookups — see
/// docs/PLAN-STORAGE-V2.md #18b phase 2.
///
/// <para>
/// Each entry tracks the field's last-seen metadata (label / kind / name
/// might evolve across versions — we keep the latest for display) plus
/// the version range where it appeared (drives "deprecated since v3"
/// chips in the grid header).
/// </para>
/// </summary>
public sealed record FormFieldUnion(IReadOnlyList<FormFieldUnionEntry> Fields)
{
    /// <summary>Input for <see cref="Compute"/> — kept primitive so this type stays in DataMaker.Schema (no Storage.Abstractions dep).</summary>
    public readonly record struct VersionInput(int Version, IReadOnlyList<FieldDefinition> Fields);

    public static FormFieldUnion Compute(IEnumerable<VersionInput> versions)
    {
        var sortedVersions = versions.OrderBy(v => v.Version).ToList();
        if (sortedVersions.Count == 0) return new FormFieldUnion(Array.Empty<FormFieldUnionEntry>());

        // Build by field-id so renames don't fragment the union — id is
        // the stable handle. Last-write-wins on label / name / kind so
        // the grid shows the most recent naming the designer picked.
        var byId = new Dictionary<string, FormFieldUnionEntry>(StringComparer.Ordinal);

        foreach (var v in sortedVersions)
        {
            foreach (var f in v.Fields)
            {
                if (byId.TryGetValue(f.Id, out var existing))
                {
                    byId[f.Id] = existing with
                    {
                        Name           = f.Name,
                        Label          = f.Label,
                        Kind           = f.Kind,
                        LastSeenVersion = v.Version,
                    };
                }
                else
                {
                    byId[f.Id] = new FormFieldUnionEntry(
                        Id:               f.Id,
                        Name:             f.Name,
                        Label:            f.Label,
                        Kind:             f.Kind,
                        FirstSeenVersion: v.Version,
                        LastSeenVersion:  v.Version);
                }
            }
        }

        // Stable ordering for tests + diff-friendly persistence: order by
        // first-seen-version (so "older" fields appear first, mirroring
        // designer's authoring chronology) then by id for ties.
        var ordered = byId.Values
            .OrderBy(e => e.FirstSeenVersion)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
        return new FormFieldUnion(ordered);
    }

    /// <summary>
    /// True when <paramref name="fieldId"/> existed at <paramref name="version"/>
    /// — i.e. the version sits inside the field's lifespan
    /// (<c>FirstSeenVersion &lt;= version &lt;= LastSeenVersion</c>). Fields
    /// added in a later version OR removed in an earlier one return false.
    /// Used by the records grid to gate inline-cell editing against the
    /// archived schema a row was saved under.
    /// </summary>
    public bool IsActiveAtVersion(string fieldId, int version) =>
        Fields.FirstOrDefault(f => f.Id == fieldId) is { } e
            && e.FirstSeenVersion <= version
            && e.LastSeenVersion  >= version;
}

public sealed record FormFieldUnionEntry(
    string Id,
    string Name,
    string Label,
    string Kind,
    int    FirstSeenVersion,
    int    LastSeenVersion);
