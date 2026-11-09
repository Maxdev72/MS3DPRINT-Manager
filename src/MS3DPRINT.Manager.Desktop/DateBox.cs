using Avalonia.Controls;
using Avalonia.Layout;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Date field of every form: a calendar opens from the button, and the date can still be typed as jj/mm/aaaa.</summary>
public sealed class DateBox : CalendarDatePicker
{
    protected override Type StyleKeyOverride => typeof(CalendarDatePicker);

    public DateBox(DateOnly? value = null, string placeholder = "jj/mm/aaaa")
    {
        SelectedDateFormat = CalendarDatePickerFormat.Custom;
        CustomDateFormatString = "dd/MM/yyyy";
        FirstDayOfWeek = DayOfWeek.Monday;
        IsTodayHighlighted = true;
        PlaceholderText = placeholder;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Date = value;
    }

    public DateOnly? Date
    {
        get => SelectedDate is { } date ? DateOnly.FromDateTime(date) : null;
        set => SelectedDate = value?.ToDateTime(TimeOnly.MinValue);
    }
}
