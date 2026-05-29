using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Modal date+time picker for <see cref="DateTimeBinding"/>. Two
/// fields side-by-side — <see cref="DateField"/> for the calendar
/// portion + <see cref="TimeField"/> for hours/minutes. OK commits a
/// composite <see cref="DateTime"/>; Cancel leaves <see cref="Committed"/>
/// null. Both widgets auto-correct unparseable keystrokes, so the
/// returned value is always real.
/// </summary>
internal sealed class DateTimePickerDialog : Dialog
{
    private readonly DateField _date;
    private readonly TimeField _time;

    public DateTime? Committed { get; private set; }

    public DateTimePickerDialog(DateTime seed) : base("Pick date + time", 56, 11)
    {
        var dateLabel = new Label("Date:") { X = 1, Y = 1 };
        _date = new DateField(seed.Date)
        {
            X = Pos.Right(dateLabel) + 1, Y = 1, Width = 16,
        };

        var timeLabel = new Label("Time:") { X = 1, Y = 3 };
        _time = new TimeField(seed.TimeOfDay)
        {
            X = Pos.Right(timeLabel) + 1, Y = 3, Width = 10,
            IsShortFormat = true,   // HH:mm (24h) — matches storage shape
        };

        Add(dateLabel, _date, timeLabel, _time);

        var ok = new Button("OK", is_default: true);
        ok.Clicked += () =>
        {
            Committed = _date.Date.Date + _time.Time;
            Application.RequestStop();
        };
        var cancel = new Button("Cancel");
        cancel.Clicked += () => { Committed = null; Application.RequestStop(); };
        AddButton(ok);
        AddButton(cancel);
    }
}
