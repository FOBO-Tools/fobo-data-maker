using DataMaker.Schema.Fields;
using DataMaker.Schema.Forms;
using DataMaker.Schema.Layout;

namespace DataMaker.Schema.Cards;

/// <summary>
/// Produces the <see cref="Section"/> list the card view renders for a form.
///
/// <para>
/// Mirrors <see cref="LayoutResolver"/>: when the form has an explicit
/// <see cref="Form.CardLayout"/> authored (by a future card designer), those
/// sections are returned with stale field references (columns pointing at
/// deleted fields) filtered out. When no card layout is authored, a default
/// is synthesized — a single section that packs fields two-per-row, with
/// wide kinds (long text, images, lists, …) taking the full width. That
/// default reproduces the FOBO record-entry rhythm without anyone having to
/// design a card.
/// </para>
///
/// <para>
/// Pure schema logic, no platform dependencies — the card view control
/// consumes the result, and it is unit-tested in isolation.
/// </para>
/// </summary>
public static class CardLayoutResolver
{
    public static IReadOnlyList<Section> Resolve(Form form)
    {
        var knownFieldIds = form.Fields.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);

        if (form.CardLayout is { Sections.Count: > 0 } layout)
            return layout.Sections
                .Select(s => s with { Rows = FilterRows(s.Rows, knownFieldIds) })
                .ToList();

        return new[] { SynthesizeDefault(form) };
    }

    /// <summary>
    /// Build the FOBO default card: one section, fields packed two-per-row in
    /// a 12-unit grid (each narrow field spans 6), with <see cref="IsWideKind"/>
    /// fields breaking to their own full-width row. A wide field that would
    /// land in the right half of a row bumps to a fresh row so the left slot
    /// isn't left dangling — same rule the record-entry dialog uses.
    /// </summary>
    private static Section SynthesizeDefault(Form form)
    {
        var rows = new List<Row>();
        Row? pending = null; // a started row holding a single narrow field, awaiting its pair

        foreach (var field in form.Fields)
        {
            if (IsWideKind(field.Kind))
            {
                if (pending is not null) { rows.Add(pending); pending = null; }
                rows.Add(new Row
                {
                    ColumnsPerRow = 12,
                    Columns = { new FieldColumn { FieldId = field.Id, Span = 12 } },
                });
                continue;
            }

            var col = new FieldColumn { FieldId = field.Id, Span = 6 };
            if (pending is null)
            {
                pending = new Row { ColumnsPerRow = 12, Columns = { col } };
            }
            else
            {
                pending.Columns.Add(col);
                rows.Add(pending);
                pending = null;
            }
        }
        if (pending is not null) rows.Add(pending);

        return new Section
        {
            Id   = $"{form.Id}_card",
            Rows = rows,
        };
    }

    /// <summary>
    /// Composite / long-form field kinds that need the full card width to stay
    /// legible. Everything else (single-line text, numbers, dates, choices,
    /// booleans) shares the two-column rhythm. Kept in sync with the
    /// record-entry dialog's wide-kind rule.
    /// </summary>
    public static bool IsWideKind(string kind) => kind switch
    {
        FieldTypes.LongText
            or FieldTypes.RichText
            or FieldTypes.Image
            or FieldTypes.Attachment
            or FieldTypes.MultiChoice
            or FieldTypes.List
            or FieldTypes.Geo
            => true,
        _   => false,
    };

    /// <summary>
    /// Drop columns that reference fields no longer on the form (e.g. a field
    /// deleted after the card was designed). Group columns are recursed into;
    /// a group left with no columns is kept (it may carry its own title /
    /// decoration). Mirrors <see cref="LayoutResolver"/>'s stale-ref scrub.
    /// </summary>
    private static List<Row> FilterRows(List<Row> rows, HashSet<string> knownFieldIds)
    {
        var result = new List<Row>(rows.Count);
        foreach (var row in rows)
        {
            var columns = new List<Column>(row.Columns.Count);
            foreach (var col in row.Columns)
            {
                switch (col)
                {
                    case FieldColumn fc when !knownFieldIds.Contains(fc.FieldId):
                        continue; // stale — drop
                    case GroupColumn gc:
                        columns.Add(gc with { Rows = FilterRows(gc.Rows, knownFieldIds) });
                        break;
                    default:
                        columns.Add(col);
                        break;
                }
            }
            result.Add(row with { Columns = columns });
        }
        return result;
    }
}
