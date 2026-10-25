using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Desktop;

public sealed class EditProjectWindow : DialogWindow
{
    private readonly TextBox _name;
    private readonly string _suggestedName;
    private readonly ComboBox _status;
    private readonly ProjectPriorityPicker _priority;
    public ProjectPriority Priority => _priority.Value;
    private readonly TextBox _dueDate;
    private readonly TextBox _description;
    private readonly TextBox _notes;
    private readonly TextBlock _error = new() { Foreground = (Avalonia.Media.IBrush?)Avalonia.Application.Current?.FindResource("DangerBrush") ?? Avalonia.Media.Brushes.IndianRed, IsVisible = false, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

    public ProjectStatus Status => _status.SelectedItem is ProjectStatus value ? value : ProjectStatus.Quote;
    public DateOnly? DueDate { get; private set; }
    /// <summary>The corrected name, or null when it was left as proposed (nothing to store).</summary>
    public string? ProjectName => string.IsNullOrWhiteSpace(_name.Text) || _name.Text.Trim() == _suggestedName ? null : _name.Text.Trim();
    public string? Description => Normalize(_description.Text);
    public string? Notes => Normalize(_notes.Text);

    public EditProjectWindow(ProjectProfile profile)
    {
        Title = "Fiche projet";
        Width = 560;
        Height = 740;
        MinWidth = 440;
        // names made from folder names come back in capitals with underscores and no accents: propose them readable
        _suggestedName = DisplayNames.Format(profile.ProjectName);
        _priority = new ProjectPriorityPicker(profile.Priority);
        _name = new TextBox { Text = _suggestedName };
        _status = new ComboBox { ItemsSource = Enum.GetValues<ProjectStatus>(), SelectedItem = profile.Status, ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ProjectStatus>((value, _) => new TextBlock { Text = StatusText(value) }) };
        _dueDate = new TextBox { Text = profile.DueDate?.ToString("yyyy-MM-dd") ?? string.Empty, PlaceholderText = "ex. 2026-11-30" };
        _description = new TextBox { Text = profile.Description ?? string.Empty, PlaceholderText = "Description", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 90 };
        _notes = new TextBox { Text = profile.Notes ?? string.Empty, PlaceholderText = "Notes internes", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 90 };
        var save = new Button { Content = "Enregistrer" };
        save.Click += Save_Click;
        Layout(profile.Reference, _suggestedName,
            [FormKit.Field("Nom du projet", _name, hint: "Nom affiché, avec ses accents. Le dossier sur le disque garde son nom."), FormKit.Field("Statut", _status), FormKit.Field("Échéance", _dueDate, hint: "Format AAAA-MM-JJ, ou vide pour aucune échéance."),
             FormKit.Field("Priorité", _priority), FormKit.Field("Description", _description), FormKit.Field("Notes internes", _notes), _error], save);
    }

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var rawDate = _dueDate.Text?.Trim();
        DateOnly? date = null;
        if (!string.IsNullOrEmpty(rawDate))
        {
            if (!DateOnly.TryParse(rawDate, out var parsedDate))
            {
                FormKit.ShowError(_error, _dueDate, "Saisissez une date au format AAAA-MM-JJ, ou laissez le champ vide.");
                return;
            }
            date = parsedDate;
        }
        DueDate = date;
        Close(true);
    }

    private static string StatusText(ProjectStatus status) => status switch { ProjectStatus.InProgress => "En cours", ProjectStatus.Completed => "Terminé", _ => "À faire" };

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
