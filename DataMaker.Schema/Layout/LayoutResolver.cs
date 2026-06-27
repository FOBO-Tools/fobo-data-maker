using DataMaker.Schema.Forms;

namespace DataMaker.Schema.Layout;

/// <summary>
/// Produces the list of <see cref="FormStep"/>s to render for a form.
///
/// <para>
/// When the form has explicit <see cref="Form.Steps"/> authored by the designer,
/// those are returned after filtering out columns that reference fields no
/// longer present (stale references from a deleted field). When <see cref="Form.Steps"/>
/// is empty, a default layout is synthesized: one step, one section, one row
/// per field, full-width span. This is the runtime's escape hatch for forms
/// authored before the layout editor lands — they still render sensibly.
/// </para>
///
/// <para>
/// This is pure schema logic; the UI layer consumes the result without
/// platform dependencies. A future Postgres-side renderer would call the
/// exact same method and get the same default shape.
/// </para>
/// </summary>
public static class LayoutResolver
{
    public static IReadOnlyList<FormStep> Resolve(Form form)
    {
        if (form.Steps.Count == 0)
            return new[] { SynthesizeSingleStep(form) };

        var knownFieldIds = form.Fields.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        return form.Steps
            .Select(step => step with { Sections = FilterSections(step.Sections, knownFieldIds) })
            .ToList();
    }

    private static FormStep SynthesizeSingleStep(Form form)
    {
        return new FormStep
        {
            Id    = $"{form.Id}_step",
            Title = form.Name,
            Sections =
            {
                new Section
                {
                    Id = $"{form.Id}_section",
                    Rows = form.Fields.Select(f => new Row
                    {
                        ColumnsPerRow = 12,
                        Columns =
                        {
                            new FieldColumn { FieldId = f.Id, Span = 12 },
                        },
                    }).ToList(),
                },
            },
        };
    }

    private static List<Section> FilterSections(List<Section> sections, HashSet<string> knownFieldIds)
    {
        return sections
            .Select(s => s with { Rows = FilterRows(s.Rows, knownFieldIds) })
            .Where(s => s.Rows.Count > 0)
            .ToList();
    }

    private static List<Row> FilterRows(List<Row> rows, HashSet<string> knownFieldIds) =>
        rows
            .Select(r => r with { Columns = FilterColumns(r.Columns, knownFieldIds) })
            .Where(r => r.Columns.Count > 0)
            .ToList();

    /// <summary>
    /// Drops <see cref="FieldColumn"/>s pointing at deleted fields and
    /// empties out <see cref="GroupColumn"/>s whose descendants all point at
    /// deleted fields. Decorative columns (rich-text, image, divider,
    /// heading) carry no field reference so they pass through untouched.
    /// Recursive so depth is unbounded.
    /// </summary>
    private static List<Column> FilterColumns(List<Column> columns, HashSet<string> knownFieldIds)
    {
        var result = new List<Column>();
        foreach (var c in columns)
        {
            switch (c)
            {
                case FieldColumn fc when knownFieldIds.Contains(fc.FieldId):
                    result.Add(fc);
                    break;
                case GroupColumn gc:
                    var filtered = FilterRows(gc.Rows, knownFieldIds);
                    if (filtered.Count > 0)
                        result.Add(gc with { Rows = filtered });
                    break;
                case RichTextColumn or ImageColumn or DividerColumn or HeadingColumn or ButtonColumn or SpacerColumn:
                    result.Add(c);
                    break;
            }
        }
        return result;
    }
}
