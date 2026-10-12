using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Desktop;

public sealed class EditProjectWindow : DialogWindow
{
    private readonly ComboBox _status;
    private readonly TextBox _dueDate;
    private readonly TextBox _description;
    private readonly TextBox _notes;
    private readonly TextBlock _error = new() { Foreground = Avalonia.Media.Brushes.IndianRed, IsVisible = false, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

    public ProjectStatus Status => _status.SelectedItem is ProjectStatus value ? value : ProjectStatus.Quote;
    public DateOnly? DueDate { get; private set; }
    public string? Description => Normalize(_description.Text);
    public string? Notes => Normalize(_notes.Text);

    public EditProjectWindow(ProjectProfile profile)
    {
        Title = "Fiche projet";
        Width = 560;
        Height = 660;
        MinWidth = 440;
        _status = new ComboBox { ItemsSource = Enum.GetValues<ProjectStatus>(), SelectedItem = profile.Status };
        _dueDate = new TextBox { Text = profile.DueDate?.ToString("yyyy-MM-dd") ?? string.Empty, PlaceholderText = "ex. 2026-11-30" };
        _description = new TextBox { Text = profile.Description ?? string.Empty, PlaceholderText = "Description", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 90 };
        _notes = new TextBox { Text = profile.Notes ?? string.Empty, PlaceholderText = "Notes internes", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 90 };
        var save = new Button { Content = "Enregistrer" };
        save.Click += Save_Click;
        Layout(profile.Reference, profile.ProjectName,
            [FormKit.Field("Statut", _status), FormKit.Field("Échéance", _dueDate, hint: "Format AAAA-MM-JJ, ou vide pour aucune échéance."),
             FormKit.Field("Description", _description), FormKit.Field("Notes internes", _notes), _error], save);
    }

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var rawDate = _dueDate.Text?.Trim();
        DateOnly? date = null;
        if (!string.IsNullOrEmpty(rawDate))
        {
            if (!DateOnly.TryParse(rawDate, out var parsedDate))
            {
                _error.Text = "Saisissez une date au format AAAA-MM-JJ, ou laissez le champ vide.";
                _error.IsVisible = true;
                return;
            }
            date = parsedDate;
        }
        DueDate = date;
        Close(true);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
