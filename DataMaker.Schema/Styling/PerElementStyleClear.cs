using DataMaker.Schema.Fields;
using DataMaker.Schema.Forms;
using DataMaker.Schema.Layout;

namespace DataMaker.Schema.Styling;

/// <summary>
/// "Theme click = fresh start" element-tree cleanup. Strips every per-element
/// <see cref="Style"/> override (sections, groups, headings, rich-text / image
/// / divider / button columns, field definitions) AND the per-field colour
/// overrides that live in the kind options (scale figure colours, choice radio
/// colour) so a custom colour the user set on a field doesn't quietly survive a
/// theme apply and contradict the new theme's accent / muted-ink / paper.
///
/// <para>Layout topology, validation, visibility expressions, field values, and
/// field content (scale Min/Max/labels/shape/spacing, choice choices/columns)
/// are preserved — theme apply is a recolour, not a content reset.</para>
///
/// <para>Pure record transform — lives in the schema library so the styling tab
/// (Uno) drives it on theme apply and the pure test harness can verify it
/// without mounting UI.</para>
/// </summary>
public static class PerElementStyleClear
{
    public static Form ClearAll(Form form) =>
        form with
        {
            Fields = form.Fields.Select(f => f with
            {
                Style  = null,
                Scale  = ClearScaleColors(f.Scale),
                Choice = ClearChoiceColors(f.Choice),
            }).ToList(),
            Steps = form.Steps.Select(step => step with
            {
                Sections = step.Sections.Select(section => section with
                {
                    Style = null,
                    Rows  = section.Rows.Select(ClearRow).ToList(),
                }).ToList(),
            }).ToList(),
        };

    // Strip the scale field's per-figure colour overrides so the figures fall
    // back to the new theme's accent / muted-ink / paper. Content (Min/Max,
    // anchor labels, shape, cumulative, alignment, spacing, margins) is kept.
    public static ScaleOptions? ClearScaleColors(ScaleOptions? s) =>
        s is null ? null : s with
        {
            HighlightColor     = null,
            HighlightTextColor = null,
            UnselectedColor    = null,
            LabelColor         = null,
            MinLabelColor      = null,
            MaxLabelColor      = null,
        };

    // Drop the choice field's radio-indicator colour override (falls back to
    // the form accent). OptionSize / Columns / Display / Choices are content.
    public static ChoiceOptions? ClearChoiceColors(ChoiceOptions? c) =>
        c is null ? null : c with { OptionColor = null };

    private static Row ClearRow(Row row) => row with
    {
        Columns = row.Columns.Select(ClearColumn).ToList(),
    };

    private static Column ClearColumn(Column col) => col switch
    {
        GroupColumn g => g with
        {
            Style = null,
            Rows  = g.Rows.Select(ClearRow).ToList(),
        },
        RichTextColumn rt => rt with { Style = null },
        ImageColumn   img => img with { Style = null },
        DividerColumn d   => d with { Style = null },
        HeadingColumn h   => h with { Style = null },
        ButtonColumn  bt  => bt with { Style = null, HoverStyle = null, PressedStyle = null },
        _                 => col, // FieldColumn has no Style.
    };
}
