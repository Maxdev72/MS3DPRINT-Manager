using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Desktop;

public sealed class EditProjectWindow : Window
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
        Height = 610;
        MinWidth = 440;
        _status = new ComboBox { ItemsSource = Enum.GetValues<ProjectStatus>(), SelectedItem = profile.Status };
        _dueDate = new TextBox { Text = profile.DueDate?.ToString("yyyy-MM-dd") ?? string.Empty, PlaceholderText = "Échéance (AAAA-MM-JJ)" };
        _description = new TextBox { Text = profile.Description ?? string.Empty, PlaceholderText = "Description", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 90 };
        _notes = new TextBox { Text = profile.Notes ?? string.Empty, PlaceholderText = "Notes internes", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 90 };
        var save = new Button { Content = "Enregistrer", HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += Save_Click;
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24), Spacing = 12,
                Children =
                {
                    new TextBlock { Text = profile.Reference, FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    new TextBlock { Text = profile.ProjectName, Classes = { "muted" } },
                    new TextBlock { Text = "Statut" }, _status,
                    new TextBlock { Text = "Échéance (AAAA-MM-JJ)" }, _dueDate,
                    new TextBlock { Text = "Description" }, _description,
                    new TextBlock { Text = "Notes" }, _notes, _error, save
                }
            }
        };
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
