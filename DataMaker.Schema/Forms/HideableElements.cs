using DataMaker.Schema.Fields;
using DataMaker.Schema.Layout;
using DataMaker.Schema.Validation;

namespace DataMaker.Schema.Forms;

/// <summary>
/// One toggleable element in a form — a field or a decorative layout column —
/// surfaced for the hosted-page "hide elements" checklist. Mirrors the WP
/// plugin's per-form visibility list (<c>FormSettingsPage::enumerate</c>) so the
/// two surfaces stay conceptually identical: the publisher unchecks pieces they
/// don't want on the public page, and the serve layer strips them before the
/// renderer ever sees the form.
/// </summary>
/// <remarks>
/// <see cref="Id"/> is the hide key — a field's <see cref="FieldDefinition.Id"/>
/// (FieldColumns referencing it drop too) or a decorative column's own
/// <c>Id</c>. <see cref="Required"/> elements can't be hidden (the checklist
/// disables them and <see cref="HideableElements.StripHidden"/> refuses to drop
/// them, defence-in-depth).
/// </remarks>
public sealed record HideableElement(
    string Id,
    string Label,
    string Kind,
    string Where,
    bool   Required);

/// <summary>
/// Enumerate + strip the hide-able elements of a <see cref="Form"/>. Pure, no
/// renderer dependency: the desktop builds the checklist from
/// <see cref="Enumerate"/>; the hosted Lambda calls <see cref="StripHidden"/> on
/// the deserialized form BEFORE <c>WebRenderer.Render</c> (additive serve-layer
/// filter — the default render path is untouched, exactly like the WordPress
/// plugin's <c>BundleBuilder::strip_hidden</c>).
/// </summary>
public static class HideableElements
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    /// <summary>
    /// Flat, author-ordered list of every uniquely-id'd field + decorative
    /// column, walking steps → sections → rows → columns (recursing groups).
    /// FieldColumns surface under their target field's id/label so hiding a
    /// field hides every column that references it.
    /// </summary>
    public static IReadOnlyList<HideableElement> Enumerate(Form form)
    {
        var fieldsById = new Dictionary<string, FieldDefinition>(Ord);
        foreach (var f in form.Fields)
            fieldsById[f.Id] = f;

        var seen = new HashSet<string>(Ord);
        var outList = new List<HideableElement>();

        foreach (var step in form.Steps)
        {
            var stepLabel = string.IsNullOrWhiteSpace(step.Title) ? "Step" : step.Title;
            foreach (var sec in step.Sections)
            {
                var secLabel = string.IsNullOrWhiteSpace(sec.Title) ? null : sec.Title;
                var where    = secLabel is null ? stepLabel : $"{stepLabel} → {secLabel}";
                foreach (var row in sec.Rows)
                    foreach (var col in row.Columns)
                        Collect(col, fieldsById, where, seen, outList);
            }
        }
        return outList;
    }

    private static void Collect(
        Column col, IReadOnlyDictionary<string, FieldDefinition> fieldsById,
        string where, HashSet<string> seen, List<HideableElement> outList)
    {
        switch (col)
        {
            case FieldColumn fc:
                if (fieldsById.TryGetValue(fc.FieldId, out var f) && seen.Add(f.Id))
                    outList.Add(new HideableElement(
                        f.Id,
                        string.IsNullOrWhiteSpace(f.Label) ? f.Name : f.Label,
                        $"field — {f.Kind}",
                        where,
                        IsFieldRequired(f)));
                break;

            case GroupColumn gc:
                var groupLabel = string.IsNullOrWhiteSpace(gc.Title) ? "(group)" : gc.Title!;
                if (seen.Add(gc.Id))
                    outList.Add(new HideableElement(gc.Id, groupLabel, "group", where, false));
                var inner = $"{where} → {groupLabel}";
                foreach (var r in gc.Rows)
                    foreach (var c in r.Columns)
                        Collect(c, fieldsById, inner, seen, outList);
                break;

            case HeadingColumn hc when seen.Add(hc.Id):
                outList.Add(new HideableElement(
                    hc.Id, string.IsNullOrWhiteSpace(hc.Text) ? "(heading)" : hc.Text, "heading", where, false));
                break;

            case RichTextColumn rc when seen.Add(rc.Id):
                outList.Add(new HideableElement(rc.Id, Preview(rc.Markdown, "(rich text)"), "rich text", where, false));
                break;

            case ImageColumn ic when seen.Add(ic.Id):
                var imgLabel = !string.IsNullOrWhiteSpace(ic.AltText) ? ic.AltText!
                             : !string.IsNullOrWhiteSpace(ic.Source)  ? Preview(ic.Source, "(image)")
                             : "(image)";
                outList.Add(new HideableElement(ic.Id, imgLabel, "image", where, false));
                break;

            case DividerColumn dc when seen.Add(dc.Id):
                outList.Add(new HideableElement(dc.Id, "(divider)", "divider", where, false));
                break;

            case SpacerColumn sc when seen.Add(sc.Id):
                outList.Add(new HideableElement(sc.Id, "(spacer)", "spacer", where, false));
                break;

            case ButtonColumn bc when seen.Add(bc.Id):
                var btnLabel = !string.IsNullOrWhiteSpace(bc.Label) ? bc.Label
                             : !string.IsNullOrWhiteSpace(bc.Name)  ? bc.Name
                             : "(button)";
                outList.Add(new HideableElement(bc.Id, btnLabel, $"button — {bc.Action}", where, false));
                break;
        }
    }

    /// <summary>
    /// Return a copy of <paramref name="form"/> with every element in
    /// <paramref name="hiddenIds"/> removed: standalone fields by id (so orphan
    /// FieldColumns drop too), FieldColumns by their target field id, and
    /// decorative columns by their own id (recursing into groups; a hidden
    /// group id drops the whole group). Required fields are never dropped even
    /// if listed — the checklist disables them, this is the second gate.
    /// Empty input (or all-required after the protected-id scrub) returns the
    /// form unchanged.
    /// </summary>
    public static Form StripHidden(Form form, IReadOnlyCollection<string> hiddenIds)
    {
        if (hiddenIds is null || hiddenIds.Count == 0) return form;

        var protectedIds = form.Fields.Where(IsFieldRequired).Select(f => f.Id).ToHashSet(Ord);
        var effective    = hiddenIds.Where(id => !protectedIds.Contains(id)).ToHashSet(Ord);
        if (effective.Count == 0) return form;

        var fields = form.Fields.Where(f => !effective.Contains(f.Id)).ToList();
        var steps  = form.Steps.Select(s => s with { Sections = StripSections(s.Sections, effective) }).ToList();
        return form with { Fields = fields, Steps = steps };
    }

    private static List<Section> StripSections(List<Section> sections, HashSet<string> hidden) =>
        sections.Select(s => s with { Rows = StripRows(s.Rows, hidden) }).ToList();

    private static List<Row> StripRows(List<Row> rows, HashSet<string> hidden) =>
        rows.Select(r => r with { Columns = FilterColumns(r.Columns, hidden) }).ToList();

    private static List<Column> FilterColumns(List<Column> columns, HashSet<string> hidden)
    {
        var outList = new List<Column>(columns.Count);
        foreach (var col in columns)
        {
            switch (col)
            {
                case FieldColumn fc:
                    if (!hidden.Contains(fc.FieldId)) outList.Add(fc);
                    break;
                case GroupColumn gc:
                    if (hidden.Contains(gc.Id)) break;             // whole group hidden
                    outList.Add(gc with { Rows = StripRows(gc.Rows, hidden) });
                    break;
                case HeadingColumn hc when hidden.Contains(hc.Id): break;
                case RichTextColumn rc when hidden.Contains(rc.Id): break;
                case ImageColumn ic when hidden.Contains(ic.Id): break;
                case DividerColumn dc when hidden.Contains(dc.Id): break;
                case SpacerColumn sc when hidden.Contains(sc.Id): break;
                case ButtonColumn bc when hidden.Contains(bc.Id): break;
                default:
                    outList.Add(col);
                    break;
            }
        }
        return outList;
    }

    /// <summary>
    /// A field is treated as required — and therefore NOT hideable — when its
    /// <see cref="FieldDefinition.Required"/> flag is set OR it carries ANY
    /// <see cref="RequiredRule"/>, conditional (<c>When</c> gate) or not.
    /// <para>
    /// Conditional required rules count too: a requirement-expression field is
    /// still author-declared mandatory data, just situational. Hiding it would
    /// silently drop data the publisher marked mandatory under some condition —
    /// and if the condition fired on a value the submitter CAN set, the form
    /// could never satisfy a requirement for a field that isn't even rendered.
    /// So any required rule locks the element. (Stricter than the original WP
    /// gate, which only locked unconditional required; the WP plugin's
    /// <c>is_field_required</c> is being brought in line.)
    /// </para>
    /// </summary>
    public static bool IsFieldRequired(FieldDefinition field) =>
        field.Required ||
        field.Validation.OfType<RequiredRule>().Any();

    private static string Preview(string? s, string fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        var t = s.Trim();
        return t.Length > 60 ? t[..57] + "…" : t;
    }
}
