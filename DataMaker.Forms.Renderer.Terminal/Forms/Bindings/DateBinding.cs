using System.Globalization;
using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Binding for the <c>date</c> kind. Editor row shows the current date
/// (or "(none)") + a "Pick…" button that opens a modal popup with a
/// Terminal.Gui <see cref="DateField"/>. Storage stays ISO 8601
/// (yyyy-MM-dd) per the project convention.
///
/// <para>Picker-based to mirror the Uno desktop runtime's
/// CalendarDatePicker — both renderers now refuse to commit a value
/// that isn't a valid date. Free-text editing (and its swallow-garbage
/// failure mode) is gone.</para>
/// </summary>
internal sealed class DateBinding : FieldBinding
{
    private readonly View _label;
    private readonly Label? _asterisk;
    private readonly View _editor;
    private readonly Label _display;
    private readonly Label _errorIndicator = CreateErrorIndicator();

    public DateBinding(FieldDefinition definition, FormState state) : base(definition, state)
    {
        (_label, _asterisk) = RequiredLabel.Build(definition.Label, definition.Required);

        _editor = new View { Width = Dim.Fill(), Height = 1 };
        _display = new Label(RenderDisplay(state.Get(definition.Name)))
        {
            X = 0, Y = 0, Width = Dim.Fill() - 12, Height = 1,
        };
        var pick = new Button("Set")
        {
            X = Pos.Right(_display) + 1, Y = 0,
        };
        pick.Clicked += OpenPicker;
        _editor.Add(_display, pick);
    }

    private void OpenPicker()
    {
        var seed = ParseExisting(State.Get(Definition.Name)) ?? DateTime.Today;
        var dlg  = new DatePickerDialog(seed);
        Application.Run(dlg);
        if (dlg.Committed is { } picked)
        {
            State.Set(Definition.Name, picked.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            _display.Text = RenderDisplay(picked);
        }
    }

    private static string RenderDisplay(object? raw)
    {
        var parsed = ParseExisting(raw);
        return parsed?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "(none)";
    }

    private static DateTime? ParseExisting(object? value)
    {
        if (value is null) return null;
        if (value is DateTime dt) return dt;
        if (value is DateTimeOffset dto) return dto.Date;

        var s = value.ToString();
        if (string.IsNullOrWhiteSpace(s)) return null;

        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed : null;
    }

    public override View Label => _label;
    public override View Editor => _editor;
    public override int EditorHeight => 1;
    public override Label? RequiredAsterisk => _asterisk;
    public override Label? ErrorIndicator => _errorIndicator;
}

/// <summary>
/// Binding for the <c>datetime</c> kind. Same picker-popup pattern as
/// <see cref="DateBinding"/>, plus a <see cref="TimeField"/> for the
/// hour/minute. Storage round-trips ISO 8601 with offset
/// (<c>yyyy-MM-ddTHH:mm:sszzz</c>).
///
/// <para>The previous TextField-based editor silently null-stored
/// unparseable input, leaving the visible text out of sync with state
/// and bypassing validation. The picker can't emit garbage so that
/// failure mode is gone.</para>
/// </summary>
internal sealed class DateTimeBinding : FieldBinding
{
    private readonly View _label;
    private readonly Label? _asterisk;
    private readonly View _editor;
    private readonly Label _display;
    private readonly Label _errorIndicator = CreateErrorIndicator();

    public DateTimeBinding(FieldDefinition definition, FormState state) : base(definition, state)
    {
        (_label, _asterisk) = RequiredLabel.Build(definition.Label, definition.Required);

        _editor = new View { Width = Dim.Fill(), Height = 1 };
        _display = new Label(RenderDisplay(state.Get(definition.Name)))
        {
            X = 0, Y = 0, Width = Dim.Fill() - 12, Height = 1,
        };
        var pick = new Button("Set")
        {
            X = Pos.Right(_display) + 1, Y = 0,
        };
        pick.Clicked += OpenPicker;
        _editor.Add(_display, pick);
    }

    private void OpenPicker()
    {
        var seed = ParseExisting(State.Get(Definition.Name)) ?? DateTime.Now;
        var dlg  = new DateTimePickerDialog(seed);
        Application.Run(dlg);
        if (dlg.Committed is { } picked)
        {
            // ISO 8601 with offset — round-trip safe for the Uno + web
            // renderers' DateTime parsing.
            State.Set(Definition.Name, picked.ToString("o", CultureInfo.InvariantCulture));
            _display.Text = RenderDisplay(picked);
        }
    }

    private static string RenderDisplay(object? raw)
    {
        var parsed = ParseExisting(raw);
        return parsed?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "(none)";
    }

    private static DateTime? ParseExisting(object? value)
    {
        if (value is null) return null;
        if (value is DateTime dt) return dt;
        if (value is DateTimeOffset dto) return dto.LocalDateTime;

        var s = value.ToString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed : null;
    }

    public override View Label => _label;
    public override View Editor => _editor;
    public override int EditorHeight => 1;
    public override Label? RequiredAsterisk => _asterisk;
    public override Label? ErrorIndicator => _errorIndicator;
}
