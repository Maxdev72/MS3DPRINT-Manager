using Avalonia.Controls;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreateProjectWindow : DialogWindow
{
    private readonly AutoCompleteBox _clients;
    private readonly TextBox _name;
    private readonly ProjectPriorityPicker _priority = new();
    public MS3DPRINT.Manager.Core.Projects.ProjectPriority Priority => _priority.Value;
    private readonly TextBlock _error = FormKit.ErrorText();
    public ClientSummary? Client => FormKit.SelectedClient(_clients);
    public string ProjectName => _name.Text?.Trim() ?? string.Empty;
    private readonly CalendarDatePicker _due = new() { FirstDayOfWeek = DayOfWeek.Monday, PlaceholderText = "Aucune", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    public DateOnly? DueDate => _due.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null;

    public CreateProjectWindow(IReadOnlyList<ClientSummary> clients, ClientSummary? preselected = null, string? suggestedName = null)
    {
        Title = "Créer un projet"; Width = 520; Height = 640; MinWidth = 400;
        _clients = FormKit.ClientSearch(clients);
        string? hint = "Saisissez un nom ou un code, puis choisissez le client dans les résultats.";
        if (preselected is not null)
        {
            // Opened from a client sheet: the client is already known and cannot be changed here.
            _clients.SelectedItem = clients.FirstOrDefault(client => string.Equals(client.ClientPath, preselected.ClientPath, StringComparison.OrdinalIgnoreCase));
            _clients.IsEnabled = false;
            hint = "Le projet est créé pour ce client.";
        }
        _name = new TextBox { PlaceholderText = "ex. Support de casque", Text = suggestedName };
        var create = new Button { Content = "Créer le projet" };
        create.Click += (_, _) =>
        {
            if (Client is null) { FormKit.ShowError(_error, _clients, "Choisissez un client."); return; }
            if (string.IsNullOrWhiteSpace(ProjectName)) { FormKit.ShowError(_error, _name, "Donnez un nom au projet."); return; }
            Close(true);
        };
        Layout("Nouveau projet", "La référence du projet est attribuée automatiquement.",
            [FormKit.Field("Client", _clients, hint: hint), FormKit.Field("Nom du projet", _name), FormKit.Field("Priorité", _priority),
             FormKit.Field("Échéance de traitement", _due, hint: "Facultatif. Le projet apparaît au calendrier et dans les alertes."), _error], create);
        Opened += (_, _) => (preselected is null ? (Control)_clients : _name).Focus();
    }
}
