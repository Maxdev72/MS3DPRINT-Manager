using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MS3DPRINT.Manager.Desktop;

public sealed class ProjectDeadlineWindow : DialogWindow
{
    public ProjectDeadlineWindow(string project, DateOnly date, Func<DateOnly, Task<string?>> save)
    {
        Title = "Échéance du projet"; Width = 500; Height = 600; MinWidth = 440;
        var calendar = new Calendar { SelectedDate = date.ToDateTime(TimeOnly.MinValue), DisplayDate = date.ToDateTime(TimeOnly.MinValue), FirstDayOfWeek = DayOfWeek.Monday, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "project-date-picker" } };
        AutomationProperties.SetName(calendar, "Date d’échéance du projet");
        var error = FormKit.ErrorText();
        var saveButton = new Button { Content = "Enregistrer l’échéance" };
        saveButton.Click += async (_, _) =>
        {
            if (calendar.SelectedDate is not { } selected) { error.Text = "Choisissez une date dans le calendrier."; error.IsVisible = true; return; }
            saveButton.IsEnabled = false;
            try { error.Text = await save(DateOnly.FromDateTime(selected)); error.IsVisible = error.Text is not null; if (error.Text is null) Close(true); }
            finally { saveButton.IsEnabled = true; }
        };
        Layout("Échéance du projet", project, [calendar, new TextBlock { Text = "Cette date est partagée avec la fiche projet et le tableau de bord.", Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, error], saveButton);
    }
}
