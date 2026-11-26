using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Every_calendar_marks_today_even_a_plain_date_picker() => Run(async () =>
    {
        // A plain picker, like the project forms use: today must stand out from the other days.
        var picker = new CalendarDatePicker { SelectedDate = DateTime.Today.AddDays(1) };
        var window = new Window { Content = picker, Width = 500, Height = 500 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            picker.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs(); await Task.Delay(100); Dispatcher.UIThread.RunJobs();
            var calendar = picker.GetVisualDescendants().OfType<Calendar>().FirstOrDefault()
                ?? TopLevel.GetTopLevel(picker)!.GetVisualDescendants().OfType<Calendar>().FirstOrDefault()
                ?? picker.GetLogicalDescendants().OfType<Calendar>().First();
            Assert.True(calendar.IsTodayHighlighted);
            var days = calendar.GetVisualDescendants().OfType<CalendarDayButton>().ToList();
            Assert.Contains(days, day => day.Classes.Contains(":today") && day.DataContext is DateTime date && date.Date == DateTime.Today);
        }
        finally { window.Close(); }
    });
}
