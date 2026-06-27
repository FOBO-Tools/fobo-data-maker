using DataMaker.Schema.Fields;
using DataMaker.Schema.Forms;
using DataMaker.Schema.Layout;
using DataMaker.Schema.Styling;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms;

/// <summary>
/// Walks a <see cref="Form"/>'s step → section → row → column tree and emits
/// Terminal.Gui views. The schema's 12-col grid (<see cref="Row.ColumnsPerRow"/>
/// + <see cref="Column.Span"/>) is honoured horizontally: a row with two
/// span-6 columns puts its fields side by side, a row with three span-4
/// columns gives three columns, and so on. Each column's contents still
/// stack vertically (label + editor + hint), so a column behaves like a
/// mini-section. Row height = max of all column heights.
///
/// <para>
/// Style cascade threads through the walker root-to-leaf. Each level's
/// resolved <see cref="ColorScheme"/> applies to the views it introduces; only
/// TUI-honorable properties (fg, bg, bold-as-bright) pass through.
/// </para>
///
/// <para>
/// <see cref="FormRuntime"/> is optional. When supplied, every field and group
/// the walker emits is registered with the runtime so dynamic
/// <c>VisibleWhen</c> evaluation and calculated-field recompute work end-to-end.
/// Without a runtime the walker is purely a render pass — fine for tests.
/// </para>
/// </summary>
internal sealed class FormRenderer
{
    private readonly Form _form;
    private readonly FormState _state;
    private readonly List<FieldBinding> _bindings = new();
    private readonly Dictionary<string, FieldDefinition> _fieldsById;
    private readonly ColorScheme _fallback;
    private readonly FormRuntime? _runtime;
    private readonly bool _ignoreStyles;

    public FormRenderer(
        Form form,
        FormState state,
        ColorScheme fallback,
        FormRuntime? runtime = null,
        bool ignoreStyles = false)
    {
        _form         = form;
        _state        = state;
        _fallback     = fallback;
        _runtime      = runtime;
        _ignoreStyles = ignoreStyles;

        _fieldsById = form.Fields.ToDictionary(f => f.Id, StringComparer.Ordinal);

        foreach (var f in form.Fields)
        {
            if (f.DefaultValue is not null && _state.Get(f.Name) is null)
                _state.Set(f.Name, CoerceDefault(f.DefaultValue, f.Kind));
        }
    }

    /// <summary>
    /// Coerce a JSON-deserialised DefaultValue into the CLR type the
    /// validator + evaluator expect for the field's kind. Form JSON
    /// rides through STJ as <see cref="System.Text.Json.JsonElement"/>
    /// (FieldDefinition.DefaultValue is object?, see SchemaJsonContext).
    /// JsonElement isn't IConvertible, so a bare Convert.ToInt64 throws
    /// and a 5-default would surface as "Not a whole number." on submit.
    /// Unwrap JsonElement to its primitive shape FIRST, then coerce.
    /// </summary>
    private static object? CoerceDefault(object? value, string kind)
    {
        if (value is null) return null;
        value = UnwrapJsonElement(value);
        try
        {
            return kind switch
            {
                FieldTypes.Number  => value is long l ? l : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture),
                FieldTypes.Decimal or FieldTypes.Money
                                   => value is decimal d ? d : Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture),
                FieldTypes.Boolean => value is bool b ? b : Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture),
                _                  => value,
            };
        }
        catch
        {
            return value;
        }
    }

    /// <summary>
    /// Pull a primitive out of a JsonElement so the per-kind coercion
    /// above has an IConvertible to feed Convert.ToXxx. Anything that
    /// isn't a JsonElement passes through unchanged.
    /// </summary>
    private static object? UnwrapJsonElement(object value)
    {
        if (value is not System.Text.Json.JsonElement je) return value;
        return je.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => je.GetString(),
            System.Text.Json.JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
            System.Text.Json.JsonValueKind.True   => true,
            System.Text.Json.JsonValueKind.False  => false,
            System.Text.Json.JsonValueKind.Null   => null!,
            _                                     => je.GetRawText(),
        };
    }

    public IReadOnlyList<FieldBinding> Bindings => _bindings;

    /// <summary>
    /// Invoked when a <see cref="ButtonColumn"/> rendered inside the form is
    /// clicked. Hosts (FormWindow) wire this to their submit / save / reset
    /// handlers. Null = button is rendered but click is a no-op.
    /// </summary>
    public Action<ButtonColumn>? OnButtonAction { get; set; }

    public int Render(View host)
    {
        if (_ignoreStyles)
            host.ColorScheme = _fallback;
        else if (_form.Style is not null)
            host.ColorScheme = StyleToColorScheme.Build(_fallback, _form.Style);

        var y = 0;
        foreach (var step in _form.Steps)
            y = RenderStep(host, step, y, _ignoreStyles ? null : _form.Style);
        return y;
    }

    // Hierarchy rules — pre-sized long enough to fill any reasonable terminal
    // after Dim.Fill() clips the label to the viewport. Using the same Unicode
    // box-drawing characters the FrameView uses for groups so the whole screen
    // has one visual language.
    private static readonly string SingleRule = new('─', 200);

    private int RenderStep(View host, FormStep step, int y, Style? ancestor)
    {
        // step.Title + step.Description are metadata only — visible step
        // headings live on explicit HeadingColumn instances now (not yet
        // implemented in the terminal target).

        foreach (var section in step.Sections)
            y = RenderSection(host, section, y, ancestor);

        return y + 1;
    }

    private int RenderSection(View host, Section section, int y, Style? ancestor)
    {
        var sectionChain = Compose(ancestor, section.Style);

        // Section.Title + Description are designer-side metadata — visible
        // section headings come from HeadingColumn rows inside the section.
        foreach (var row in section.Rows)
            y = RenderRow(host, row, y, sectionChain);

        return y + 1;
    }

    /// <summary>
    /// Lay out a row's columns horizontally using the schema's grid units.
    /// Each column becomes a sub-view sized by <c>span / ColumnsPerRow</c>
    /// of the available width. Field contents stack vertically inside that
    /// sub-view exactly like the single-column case used to. Row height is
    /// the tallest column.
    ///
    /// <para>
    /// The row is wrapped in a <see cref="View"/> that uses
    /// <see cref="Dim.Fill"/> so column percents resolve against the row
    /// container's width (which always tracks the parent) rather than the
    /// ScrollView's virtual <c>ContentSize</c>. Without the wrapper, column
    /// percents resolve against a stale or zero-width ContentSize during
    /// the first layout pass and briefly collapse to a single stack.
    /// </para>
    /// </summary>
    private int RenderRow(View host, Row row, int y, Style? ancestor)
    {
        if (row.Columns.Count == 0) return y;

        var cpr = Math.Max(1, row.ColumnsPerRow);
        var cumulative = 0;
        var maxInnerY = 0;

        var rowContainer = new View
        {
            X      = 0,
            Y      = y,
            Width  = Dim.Fill(),
            Height = 1, // fixed up after children render
        };
        host.Add(rowContainer);

        // 2-char gutter shaved off EVERY column — was only non-last,
        // which made the last column (or a single-column row) 2 cells
        // wider than its siblings. With the shave applied uniformly,
        // a 1-col 50% row, a 2-col 50/50 row, and a 4-col 25% row all
        // render their columns at the same effective width.
        const int Gutter = 2;

        for (var i = 0; i < row.Columns.Count; i++)
        {
            var column = row.Columns[i];
            var span = Math.Clamp(column.Span, 1, cpr);
            var xPct = 100 * cumulative / cpr;
            var wPct = 100 * span       / cpr;

            Dim widthDim = Dim.Percent(wPct) - Gutter;

            var colView = new View
            {
                X      = Pos.Percent(xPct),
                Y      = 0,
                Width  = widthDim,
                Height = 1,
            };
            rowContainer.Add(colView);

            var innerY = RenderColumn(colView, column, 0, ancestor);
            colView.Height = Math.Max(1, innerY);

            if (innerY > maxInnerY) maxInnerY = innerY;
            cumulative += span;
        }

        rowContainer.Height = Math.Max(1, maxInnerY);
        return y + maxInnerY + 1; // +1 spacer between rows
    }

    private int RenderColumn(View host, Column column, int y, Style? ancestor) => column switch
    {
        FieldColumn    fc => RenderFieldColumn   (host, fc, y, ancestor),
        GroupColumn    gc => RenderGroupColumn   (host, gc, y, ancestor),
        RichTextColumn rt => RenderRichTextColumn(host, rt, y, ancestor),
        ImageColumn    ic => RenderImageColumn   (host, ic, y, ancestor),
        DividerColumn  dc => RenderDividerColumn (host, dc, y, ancestor),
        HeadingColumn  hc => RenderHeadingColumn (host, hc, y, ancestor),
        ButtonColumn   bc => RenderButtonColumn  (host, bc, y, ancestor),
        SpacerColumn   sp => y + (int)Math.Ceiling((sp.Height ?? 0) / 12.0),
        _                  => y,
    };

    private int RenderFieldColumn(View host, FieldColumn column, int y, Style? ancestor)
    {
        if (!_fieldsById.TryGetValue(column.FieldId, out var def))
            return y;

        var binding = FieldBindingFactory.Create(def, _state);
        _bindings.Add(binding);

        var fieldScheme = _ignoreStyles
            ? _fallback
            : StyleToColorScheme.Build(_fallback, ancestor, def.Style);
        var emitted = new List<View>(3);

        if (binding.Label is { } label)
        {
            label.X           = 0;
            label.Y           = y;
            label.Width       = Dim.Fill();
            label.ColorScheme = fieldScheme;
            host.Add(label);
            emitted.Add(label);

            // Red (*) for required fields — keeps the field's own background
            // so it reads on every template and every styled cascade.
            if (binding.RequiredAsterisk is { } asterisk)
                asterisk.ColorScheme = RequiredLabel.AccentScheme(fieldScheme, Color.BrightRed);

            y += binding.LabelHeight;
        }

        binding.Editor.X           = 0;
        binding.Editor.Y           = y;
        binding.Editor.ColorScheme = fieldScheme;
        host.Add(binding.Editor);
        emitted.Add(binding.Editor);

        // Overlay the error indicator on the last column of the editor's
        // top row. Starts hidden; FormRuntime toggles Visible after the
        // user tabs out of an invalid field.
        if (binding.ErrorIndicator is { } indicator)
        {
            indicator.X = Pos.Right(binding.Editor) - 1;
            indicator.Y = y;
            host.Add(indicator);
            // Not added to `emitted` — keeps the indicator visibility decoupled
            // from the field's VisibleWhen cascade (which toggles the whole
            // emitted list). FormRuntime owns this one.
        }

        y += binding.EditorHeight;

        // Field descriptions are intentionally not rendered — on par with the
        // web/WASM/WordPress + PDF renderers, which drop them too.

        _runtime?.RegisterField(def.Name, binding, emitted);

        return y + 1;
    }

    private int RenderGroupColumn(View host, GroupColumn column, int y, Style? ancestor)
    {
        var groupChain  = _ignoreStyles ? null : Compose(ancestor, column.Style);
        var groupScheme = _ignoreStyles
            ? _fallback
            : StyleToColorScheme.Build(_fallback, groupChain);

        var frame = new FrameView(column.Title ?? "")
        {
            X           = 0,
            Y           = y,
            Width       = Dim.Fill(),
            Height      = 3,
            ColorScheme = groupScheme,
        };
        host.Add(frame);
        _runtime?.RegisterGroup(column, frame);

        var innerY = 0;
        foreach (var row in column.Rows)
            foreach (var col in row.Columns)
                innerY = RenderColumn(frame, col, innerY, groupChain);

        var contentHeight = Math.Max(1, innerY);
        frame.Height = contentHeight + 2;

        return y + contentHeight + 2 + 1;
    }

    /// <summary>
    /// Render a <see cref="RichTextColumn"/> in the terminal — strip
    /// Markdown syntax, leave the prose intact. Authors use the same
    /// Markdown body across renderers; the terminal target drops the
    /// inline emphasis / list markers and renders one paragraph per
    /// blank-line-separated block.
    /// </summary>
    private int RenderRichTextColumn(View host, RichTextColumn column, int y, Style? ancestor)
    {
        var plain = DataMaker.Schema.Layout.Markdown.ToPlain(column.Markdown);
        if (string.IsNullOrWhiteSpace(plain)) return y;

        var scheme = _ignoreStyles
            ? _fallback
            : StyleToColorScheme.Build(_fallback, ancestor, column.Style);

        // Each "paragraph" (HTML block boundary) becomes its own line group;
        // keeps a divider between intro / body / disclaimer copy.
        foreach (var paragraph in plain.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var label = new Label(paragraph.Trim())
            {
                X           = 0,
                Y           = y,
                Width       = Dim.Fill(),
                Height      = Dim.Fill(),   // Terminal.Gui v1 wraps when Height is unbounded + AutoSize false
                AutoSize    = false,
                ColorScheme = scheme,
            };
            host.Add(label);
            // Heuristic line count: ~80-col wrap, +1 for spacing.
            var lines = Math.Max(1, (int)Math.Ceiling(paragraph.Length / 80.0));
            y += lines + 1;
        }
        return y;
    }

    /// <summary>
    /// Image column has no equivalent in a terminal — render a placeholder
    /// line carrying the alt text (or filename fallback) so the recipient
    /// at least knows an image was present in the printed/web version.
    /// </summary>
    private int RenderImageColumn(View host, ImageColumn column, int y, Style? ancestor)
    {
        var alt = !string.IsNullOrWhiteSpace(column.AltText)
            ? column.AltText
            : SourceFileName(column.Source);
        return AddLabel(host, $"[image: {alt}]", y, Compose(ancestor, column.Style));
    }

    /// <summary>
    /// Divider — a single line of horizontal box-drawing characters.
    /// Thickness collapses to 1 row in the terminal (multi-row dividers
    /// don't make visual sense in a TUI); the colour scheme can come from
    /// the cascade if the terminal supports it.
    /// </summary>
    private int RenderDividerColumn(View host, DividerColumn column, int y, Style? ancestor)
    {
        var label = new Label(SingleRule)   // capped by host width via Dim.Fill()
        {
            X           = 0,
            Y           = y,
            Width       = Dim.Fill(),
            Height      = 1,
            AutoSize    = false,
            ColorScheme = _ignoreStyles
                ? _fallback
                : StyleToColorScheme.Build(_fallback, ancestor, column.Style),
        };
        host.Add(label);
        return y + 2;   // +1 line + 1 spacer
    }


    /// <summary>
    /// Render a <see cref="HeadingColumn"/>. Terminal.Gui has no real
    /// font weight or size — synthesise hierarchy via box-drawing
    /// underlines (H1 = double rule, H2 = single rule, H3/H4 = plain
    /// label) so headings still feel distinct at a glance.
    /// </summary>
    private int RenderHeadingColumn(View host, HeadingColumn column, int y, Style? ancestor)
    {
        if (string.IsNullOrWhiteSpace(column.Text)) return y + 1;   // blank gap

        var level   = Math.Clamp(column.Level, 1, 4);
        var scheme  = _ignoreStyles
            ? _fallback
            : StyleToColorScheme.Build(_fallback, ancestor, column.Style);

        // H1 uppercase + double underline; H2 single underline; H3/H4 plain.
        var text = level == 1 ? column.Text.ToUpperInvariant() : column.Text;
        var headingLabel = new Label(text)
        {
            X           = 0,
            Y           = y,
            Width       = Dim.Fill(),
            Height      = 1,
            AutoSize    = false,
            ColorScheme = scheme,
        };
        host.Add(headingLabel);
        y += 1;

        if (level <= 2)
        {
            var ruleChar = level == 1 ? '═' : '─';
            var rule = new Label(new string(ruleChar, 200))
            {
                X           = 0,
                Y           = y,
                Width       = Dim.Fill(),
                Height      = 1,
                AutoSize    = false,
                ColorScheme = scheme,
            };
            host.Add(rule);
            y += 1;
        }
        return y + 1;   // trailing breathing room
    }

    /// <summary>
    /// Render a <see cref="ButtonColumn"/>. Maps the schema's
    /// <see cref="ButtonAction"/> onto the host's submit / save / reset
    /// flow via <see cref="OnButtonAction"/>. Buttons without an action
    /// (or with no host callback) just render — clicking them is a no-op.
    /// </summary>
    private int RenderButtonColumn(View host, ButtonColumn column, int y, Style? ancestor)
    {
        // Terminal.Gui Button content includes the label; an _underscore
        // before a char marks the Alt-hotkey. Skip the hotkey decoration
        // — labels are user-provided and may already contain underscores.
        var btn = new Button(column.Label ?? column.Name ?? "Button")
        {
            X = 0, Y = y,
            ColorScheme = _ignoreStyles
                ? _fallback
                : StyleToColorScheme.Build(_fallback, ancestor, column.Style),
        };
        btn.Clicked += () => OnButtonAction?.Invoke(column);
        host.Add(btn);
        return y + 2;   // 1 row for the button + 1 for breathing room
    }

    private static string SourceFileName(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "untitled";
        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return "embedded";
        var slash = source.LastIndexOf('/');
        return slash >= 0 && slash < source.Length - 1 ? source[(slash + 1)..] : source;
    }

    private int AddLabel(View host, string text, int y, Style? ancestor)
    {
        // AutoSize = false is required so Dim.Fill() clips the 200-char
        // rule strings at the viewport. Terminal.Gui v1's default (AutoSize
        // true) would let the text dictate width and blow out ContentSize.
        var label = new Label(text)
        {
            X           = 0,
            Y           = y,
            Width       = Dim.Fill(),
            Height      = 1,
            AutoSize    = false,
            ColorScheme = _ignoreStyles || ancestor is null
                ? host.ColorScheme ?? _fallback
                : StyleToColorScheme.Build(_fallback, ancestor),
        };
        host.Add(label);
        return y + 1;
    }

    private static Style? Compose(Style? parent, Style? child)
    {
        if (parent is null) return child;
        if (child  is null) return parent;
        return StyleResolver.Resolve(parent, child);
    }
}
