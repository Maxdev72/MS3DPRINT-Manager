using Avalonia.Controls;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreateProjectWindow : DialogWindow
{
    private readonly ComboBox _clients;
    private readonly TextBox _name;
    private readonly TextBlock _error = FormKit.ErrorText();
    public ClientSummary? Client => _clients.SelectedItem as ClientSummary;
    public string ProjectName => _name.Text?.Trim() ?? string.Empty;

    public CreateProjectWindow(IReadOnlyList<ClientSummary> clients, ClientSummary? preselected = null)
    {
        Title = "Créer un projet"; Width = 480; Height = 400; MinWidth = 400;
        _clients = new ComboBox { ItemsSource = clients, PlaceholderText = "Choisir un client", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        _clients.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ClientSummary>((client, _) => new TextBlock
        {
            Text = client is null ? string.Empty : $"{client.DisplayName} · {client.ClientCode}"
        });
        string? hint = null;
        if (preselected is not null)
        {
            // Opened from a client sheet: the client is already known and cannot be changed here.
            _clients.SelectedItem = clients.FirstOrDefault(client => string.Equals(client.ClientPath, preselected.ClientPath, StringComparison.OrdinalIgnoreCase));
            _clients.IsEnabled = false;
            hint = "Le projet est créé pour ce client.";
        }
        _name = new TextBox { PlaceholderText = "ex. Support de casque" };
        var create = new Button { Content = "Créer le projet" };
        create.Click += (_, _) =>
        {
            if (Client is null) { _error.Text = "Choisissez un client."; _error.IsVisible = true; return; }
            if (string.IsNullOrWhiteSpace(ProjectName)) { _error.Text = "Donnez un nom au projet."; _error.IsVisible = true; return; }
            Close(true);
        };
        Layout("Nouveau projet", "La référence du projet est attribuée automatiquement.",
            [FormKit.Field("Client", _clients, hint: hint), FormKit.Field("Nom du projet", _name), _error], create);
        Opened += (_, _) => (preselected is null ? (Control)_clients : _name).Focus();
    }
}
