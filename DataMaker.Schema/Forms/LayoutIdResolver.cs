using DataMaker.Schema.Layout;

namespace DataMaker.Schema.Forms;

/// <summary>
/// Lenient parent-id resolution for the AI agent's layout tools.
///
/// The agent emits step/section/row mutations as a single batch and cannot
/// observe the GUIDs minted by earlier calls in that same batch, so it guesses
/// sequential ids (<c>step_1</c>, <c>section_1</c>, <c>row_1</c>). These helpers
/// honor an exact id when it exists and otherwise fall back to the most-recently
/// added container — correct because the batch builds strictly front-to-back.
/// </summary>
public static class LayoutIdResolver
{
    /// <summary>Exact section id if present, else the newest section, else null (no sections).</summary>
    public static string? ResolveSectionId(Form form, string requested)
    {
        var sections = form.Steps.SelectMany(s => s.Sections).ToList();
        if (sections.Count == 0) return null;
        return sections.Any(s => s.Id == requested) ? requested : sections[^1].Id;
    }

    /// <summary>
    /// Exact row id if present (including rows nested in groups), else the newest
    /// row in document order, else null (no rows).
    /// </summary>
    public static string? ResolveRowId(Form form, string requested)
    {
        var ids = AllRowIds(form);
        if (ids.Count == 0) return null;
        return ids.Contains(requested) ? requested : ids[^1];
    }

    /// <summary>
    /// The real (GUID) id of the field the agent is pointing at. Matches on
    /// <c>Id</c> first, then <c>Name</c> — the agent created the field with
    /// <c>AddField</c> (which mints a GUID) but references it by the snake_case
    /// <c>name</c> it chose. Falls back to the newest field, then null (no fields).
    /// </summary>
    public static string? ResolveFieldId(Form form, string requested)
    {
        if (form.Fields.Count == 0) return null;
        var byId = form.Fields.FirstOrDefault(f => f.Id == requested);
        if (byId is not null) return byId.Id;
        var byName = form.Fields.FirstOrDefault(f => f.Name == requested);
        if (byName is not null) return byName.Id;
        return form.Fields[^1].Id;
    }

    /// <summary>All row ids in document order, descending into <see cref="GroupColumn"/> rows.</summary>
    public static List<string> AllRowIds(Form form)
    {
        var ids = new List<string>();
        void Walk(IEnumerable<Row> rows)
        {
            foreach (var r in rows)
            {
                ids.Add(r.Id);
                foreach (var c in r.Columns)
                    if (c is GroupColumn g) Walk(g.Rows);
            }
        }
        foreach (var step in form.Steps)
            foreach (var sec in step.Sections)
                Walk(sec.Rows);
        return ids;
    }
}
