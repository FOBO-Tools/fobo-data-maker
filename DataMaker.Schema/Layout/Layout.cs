using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DataMaker.Schema.Styling;

namespace DataMaker.Schema.Layout;

/// <summary>
/// Responsive grid layout. A form is a list of sections → rows → columns.
/// A column is either a <see cref="FieldColumn"/> (bound to a single field)
/// or a <see cref="GroupColumn"/> (a sub-grid of nested rows). Each column
/// takes 1..ColumnsPerRow slots on desktop; narrower viewports cause the
/// whole row to stack.
/// </summary>
public sealed record Section
{
    public required string Id { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public List<Row> Rows { get; init; } = new();

    /// <summary>Section-level style overrides. Null properties inherit from <see cref="Forms.Form.Style"/>.</summary>
    public Style? Style { get; init; }
}

public sealed record Row
{
    /// <summary>
    /// Stable id. Auto-generated for fresh rows. Older JSON without an id
    /// gets a fresh GUID on load (System.Text.Json fills the default).
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Number of equal-width slots in this row (1–12, Bootstrap-style).</summary>
    public int ColumnsPerRow { get; init; } = 12;

    public List<Column> Columns { get; init; } = new();
}

/// <summary>
/// Base type for row children. Serialized with a <c>Kind</c> discriminator
/// (<c>"field"</c> or <c>"group"</c>). JSON written before the group feature
/// landed has no discriminator — the converter infers <c>FieldColumn</c>
/// from presence of <c>FieldId</c> so existing forms load unchanged.
///
/// <para>
/// <b>Depth is unbounded at the schema level.</b> A <see cref="GroupColumn"/>
/// may contain rows whose columns include further groups. The designer UI
/// caps at depth 2 (a group cannot contain another group) — that's a UX
/// constraint, not a data-model one, so we can raise the cap without a
/// schema migration.
/// </para>
/// </summary>
[JsonConverter(typeof(ColumnJsonConverter))]
public abstract record Column
{
    /// <summary>Span in grid units (1..ColumnsPerRow). Default 12 = full row.</summary>
    public int Span { get; init; } = 12;

    /// <summary>Below this viewport width (px) the row stacks. Null = always respect Span.</summary>
    public int? StackBelowPx { get; init; } = 640;
}

public sealed record FieldColumn : Column
{
    /// <summary>Reference to a <see cref="Fields.FieldDefinition.Id"/>.</summary>
    public required string FieldId { get; init; }
}

public sealed record GroupColumn : Column
{
    /// <summary>
    /// Stable id (GUID/string). Mirrors <see cref="Section.Id"/> — used by
    /// renderers as the keying surface for per-group CSS and ancestor-
    /// visibility lookups. Auto-generated on construction so existing
    /// designer paths don't have to set it explicitly; legacy JSON without
    /// an <c>id</c> property gets a fresh GUID on load (stable within that
    /// Form instance, which is all the renderer pipeline needs).
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Optional header rendered above the group's rows at runtime. Null = no header.</summary>
    public string? Title { get; init; }

    /// <summary>Optional expression gating visibility. Hidden groups skip all descendant field validation.</summary>
    public string? VisibleWhen { get; init; }

    /// <summary>If true, the group renders a chevron and users can toggle its content at runtime.</summary>
    public bool IsCollapsible { get; init; }

    /// <summary>Initial collapsed state when <see cref="IsCollapsible"/> is true.</summary>
    public bool DefaultCollapsed { get; init; }

    /// <summary>Nested rows. Each row's columns may themselves be field- or group-columns.</summary>
    public List<Row> Rows { get; init; } = new();

    /// <summary>Group-level style overrides. Null properties inherit from the containing <see cref="Section"/> / <see cref="Forms.Form"/>.</summary>
    public Style? Style { get; init; }
}

/// <summary>
/// Decorative rich-text block in the layout. NOT a field — collects no
/// data, holds no field id. Used for headers, instructions, paragraphs,
/// disclaimers, anything the publisher wants the form filler to <i>read</i>
/// rather than fill in.
///
/// <para>
/// Body is authored as <b>Markdown</b> (a small subset: <c>#</c> headings,
/// <c>**bold**</c>, <c>*italic*</c>, <c>-</c> / <c>1.</c> lists, blank-line
/// paragraph breaks, backtick inline code). Each renderer converts
/// Markdown to its native form: HTML for the web target, plain runs for
/// Uno desktop, plain paragraphs for the terminal client. We picked
/// Markdown over raw HTML because the designer's edit surface is a plain
/// TextBox (Uno's contenteditable / RichEditBox paths don't deliver
/// printable characters reliably on Skia desktop) — Markdown stays
/// readable in raw text form, HTML doesn't.
/// </para>
///
/// <para>
/// Visibility gating works the same way as on <see cref="GroupColumn"/>:
/// a non-null <see cref="VisibleWhen"/> expression hides the block until
/// it evaluates true. Useful for context-sensitive help text that only
/// appears once a related field is filled.
/// </para>
/// </summary>
public sealed record RichTextColumn : Column
{
    /// <summary>Stable id, mirrors <see cref="GroupColumn.Id"/>. Auto-default GUID — see GroupColumn docs.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Markdown body of the block. Empty string is allowed.</summary>
    public required string Markdown { get; init; }

    /// <summary>Optional gating expression. Null = always visible.</summary>
    public string? VisibleWhen { get; init; }

    /// <summary>Block-level style overrides (background, padding, text colour). Null = inherit.</summary>
    public Style? Style { get; init; }
}

/// <summary>
/// Decorative image in the layout. NOT a field — the form filler doesn't
/// upload anything; this is publisher-supplied imagery (logo, banner,
/// diagram). <see cref="Source"/> is either a remote URL or a
/// <c>data:</c> URI for fully-self-contained forms.
///
/// <para>
/// <see cref="AltText"/> is mandatory in spirit (renderers should warn
/// when missing) so the rendering for assistive tech and the terminal
/// fallback both have something to say. The renderer is free to scale
/// to fit the column's grid span; <see cref="MaxHeight"/> caps that
/// when the publisher wants a banner instead of "as tall as needed".
/// </para>
/// </summary>
public sealed record ImageColumn : Column
{
    /// <summary>Stable id, mirrors <see cref="GroupColumn.Id"/>.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>HTTP(S) URL or <c>data:</c> URI. Empty string is invalid.</summary>
    public required string Source { get; init; }

    /// <summary>Description for assistive tech + non-graphical renderers (terminal). Strongly recommended.</summary>
    public string? AltText { get; init; }

    /// <summary>Optional gating expression. Null = always visible.</summary>
    public string? VisibleWhen { get; init; }

    /// <summary>Cap on rendered height in pixels. Null = render at natural height up to the column's width.</summary>
    public int? MaxHeight { get; init; }

    /// <summary>Block-level style overrides. Null = inherit.</summary>
    public Style? Style { get; init; }
}

/// <summary>
/// Decorative horizontal-rule / divider in the layout. NOT a field —
/// holds no data. Used for visual separation between groups of fields, or
/// to break a long form into airier sections without forcing a new
/// <see cref="Section"/>.
///
/// <para>
/// Renders as a horizontal line spanning the column. Thickness and
/// optional explicit colour are own-level; missing values inherit the
/// surrounding divider colour from the cascade.
/// </para>
/// </summary>
public sealed record DividerColumn : Column
{
    /// <summary>Stable id, mirrors <see cref="GroupColumn.Id"/>.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Line thickness in CSS pixels. Defaults to 1.</summary>
    public double Thickness { get; init; } = 1;

    /// <summary>Optional explicit colour (<c>#RRGGBB</c> / <c>#AARRGGBB</c>). Null = inherit divider colour from cascade.</summary>
    public string? Color { get; init; }

    /// <summary>Optional gating expression. Null = always visible.</summary>
    public string? VisibleWhen { get; init; }

    /// <summary>Block-level style overrides (margin, padding, opacity). Null = inherit.</summary>
    public Style? Style { get; init; }
}

/// <summary>
/// Decorative heading in the layout. NOT a field — collects no data, holds
/// no field id. Four levels (1–4) parallel to HTML H1–H4. Exists so the
/// designer can place visible titles wherever they want, instead of relying
/// on hard-rendered <c>Form.Name</c> / <c>FormStep.Title</c> headers (which
/// live outside the themed cascade).
///
/// <para>
/// Per-level theme defaults live on <see cref="FormStyle.Heading1Style"/>
/// through <see cref="FormStyle.Heading4Style"/>. The per-instance
/// <see cref="Style"/> override merges field-by-field on top of the theme
/// default — null props fall through to the theme value, then through the
/// usual ancestor cascade.
/// </para>
/// </summary>
public sealed record HeadingColumn : Column
{
    /// <summary>Stable id, mirrors <see cref="GroupColumn.Id"/>.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Heading text. Empty string is allowed (renders as a blank gap).</summary>
    public required string Text { get; init; }

    /// <summary>Hierarchy level: 1 (largest) … 4 (smallest). Out-of-range values are clamped at render time.</summary>
    public int Level { get; init; } = 1;

    /// <summary>Optional gating expression. Null = always visible.</summary>
    public string? VisibleWhen { get; init; }

    /// <summary>Per-instance overrides on top of the theme's per-level default. Null = use theme default as-is.</summary>
    public Style? Style { get; init; }
}

/// <summary>
/// Empty layout block. Holds no data — its only job is to reserve visual
/// space. Width comes from <see cref="Column.Span"/> like every other
/// column kind (so it pushes its row-mates aside horizontally); a non-null
/// <see cref="Height"/> reserves vertical space, which lets the designer
/// drop a Spacer into its own full-width row to create a deliberate
/// vertical gap between content blocks.
/// </summary>
public sealed record SpacerColumn : Column
{
    /// <summary>Stable id, mirrors <see cref="GroupColumn.Id"/>.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Vertical height in CSS pixels. Null = no minimum height (column shrinks to row's min height).</summary>
    public double? Height { get; init; }

    /// <summary>Optional gating expression. Null = always visible.</summary>
    public string? VisibleWhen { get; init; }
}

/// <summary>
/// Polymorphic JSON converter for <see cref="Column"/>. Writes a <c>Kind</c>
/// discriminator on output; tolerates missing <c>Kind</c> on input by inferring
/// from the shape (presence of <c>FieldId</c> → field; presence of <c>Rows</c>
/// → group). <c>CanConvert</c> returns true only for the abstract base so the
/// default converter handles the concrete subtypes (no recursion).
/// </summary>
public sealed class ColumnJsonConverter : JsonConverter<Column>
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(Column);

    public override Column? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var kind = TryGetString(root, "Kind") ?? TryGetString(root, "kind");
        if (kind is null)
        {
            // Structural fallback for forms persisted before the kind
            // discriminator existed, or hand-edited JSON. Order is
            // deliberate: most-specific signal first.
            if      (TryGetProperty(root, "FieldId")   is not null) kind = "field";
            else if (TryGetProperty(root, "Rows")      is not null) kind = "group";
            // Either "Markdown" (current) or "Html" (legacy, when the body
            // was authored as raw HTML) identifies a richtext column —
            // legacy data still loads, just gets re-saved as Markdown.
            else if (TryGetProperty(root, "Markdown")  is not null) kind = "richtext";
            else if (TryGetProperty(root, "Html")      is not null) kind = "richtext";
            else if (TryGetProperty(root, "Source")    is not null) kind = "image";
            else if (TryGetProperty(root, "Thickness") is not null) kind = "divider";
            // Heading: identified by Level + Text combo (Text alone could be
            // ambiguous if a future column type also carries a "Text" prop).
            else if (TryGetProperty(root, "Level")     is not null
                  && TryGetProperty(root, "Text")      is not null) kind = "heading";
            // Button: identified by the combination of Name + Label + Variant.
            // Name+Label alone could collide with future column types; Variant
            // is button-specific.
            else if (TryGetProperty(root, "Variant")   is not null
                  && TryGetProperty(root, "Label")     is not null) kind = "button";
            else kind = "field";
        }

        var raw = root.GetRawText();
        // Trim-safe path: pull the typeinfo for the concrete column kind out
        // of the options' resolver (set up by SchemaJsonContext registrations
        // on the caller's options). Generic Deserialize<T>(string, options)
        // would be marked RequiresUnreferencedCode and break under trimming.
        return kind switch
        {
            "field"     => (FieldColumn?)    JsonSerializer.Deserialize(raw, options.GetTypeInfo(typeof(FieldColumn))),
            "group"     => (GroupColumn?)    JsonSerializer.Deserialize(raw, options.GetTypeInfo(typeof(GroupColumn))),
            "richtext"  => (RichTextColumn?) JsonSerializer.Deserialize(raw, options.GetTypeInfo(typeof(RichTextColumn))),
            "image"     => (ImageColumn?)    JsonSerializer.Deserialize(raw, options.GetTypeInfo(typeof(ImageColumn))),
            "divider"   => (DividerColumn?)  JsonSerializer.Deserialize(raw, options.GetTypeInfo(typeof(DividerColumn))),
            "heading"   => (HeadingColumn?)  JsonSerializer.Deserialize(raw, options.GetTypeInfo(typeof(HeadingColumn))),
            "button"    => (ButtonColumn?)   JsonSerializer.Deserialize(raw, options.GetTypeInfo(typeof(ButtonColumn))),
            "spacer"    => (SpacerColumn?)   JsonSerializer.Deserialize(raw, options.GetTypeInfo(typeof(SpacerColumn))),
            _            => throw new JsonException($"Unknown Column kind '{kind}'."),
        };
    }

    public override void Write(Utf8JsonWriter writer, Column value, JsonSerializerOptions options)
    {
        var kind = value switch
        {
            FieldColumn    => "field",
            GroupColumn    => "group",
            RichTextColumn => "richtext",
            ImageColumn    => "image",
            DividerColumn  => "divider",
            HeadingColumn  => "heading",
            ButtonColumn   => "button",
            SpacerColumn   => "spacer",
            _              => throw new JsonException($"Unknown Column type '{value.GetType().Name}'."),
        };

        // Same story on the write side: resolve concrete typeinfo via options.
        using var doc = JsonSerializer.SerializeToDocument(value, options.GetTypeInfo(value.GetType()));
        writer.WriteStartObject();
        writer.WriteString("Kind", kind);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name.Equals("Kind", StringComparison.OrdinalIgnoreCase)) continue;
            prop.WriteTo(writer);
        }
        writer.WriteEndObject();
    }

    private static string? TryGetString(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static JsonElement? TryGetProperty(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        foreach (var prop in el.EnumerateObject())
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return prop.Value;
        return null;
    }
}
