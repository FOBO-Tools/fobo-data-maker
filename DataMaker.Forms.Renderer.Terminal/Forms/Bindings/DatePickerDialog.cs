using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Modal date picker for <see cref="DateBinding"/>. One row —
/// Terminal.Gui <see cref="DateField"/> plus OK / Cancel. Picker is
/// inherently valid (it auto-corrects bad keystrokes), so any
/// committed value is a real <see cref="DateTime"/>.
/// </summary>
internal sealed class DatePickerDialog : Dialog
{
    private readonly DateField _field;

    public DateTime? Committed { get; private set; }

    public DatePickerDialog(DateTime seed) : base("Pick date", 50, 9)
    {
        _field = new DateField(seed) { X = 1, Y = 1, Width = Dim.Fill(2) };
        Add(_field);

        var ok = new Button("OK", is_default: true);
        ok.Clicked += () => { Committed = _field.Date; Application.RequestStop(); };
        var cancel = new Button("Cancel");
        cancel.Clicked += () => { Committed = null; Application.RequestStop(); };
        AddButton(ok);
        AddButton(cancel);
    }
}
