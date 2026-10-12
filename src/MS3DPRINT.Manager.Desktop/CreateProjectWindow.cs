using Avalonia.Controls;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreateProjectWindow : Window
{
    private readonly ComboBox _clients;
    private readonly TextBox _name;
    public ClientSummary? Client => _clients.SelectedItem as ClientSummary;
    public string ProjectName => _name.Text?.Trim() ?? string.Empty;

    public CreateProjectWindow(IReadOnlyList<ClientSummary> clients, ClientSummary? preselected = null)
    {
        Title = "Créer un projet"; Width = 440; Height = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _clients = new ComboBox { ItemsSource = clients, PlaceholderText = "Choisir un client" };
        _clients.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ClientSummary>((client, _) => new TextBlock
        {
            Text = client is null ? string.Empty : $"{client.DisplayName} · {client.ClientCode}"
        });
        if (preselected is not null)
        {
            // Opened from a client sheet: the client is already known and cannot be changed here.
            _clients.SelectedItem = clients.FirstOrDefault(client => string.Equals(client.ClientPath, preselected.ClientPath, StringComparison.OrdinalIgnoreCase));
            _clients.IsEnabled = false;
        }
        _name = new TextBox { PlaceholderText = "Nom du projet" };
        var create = new Button { Content = "Créer", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        create.Click += (_, _) => Close(Client is not null && !string.IsNullOrWhiteSpace(ProjectName));
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14, Children = { new TextBlock { Text = "Nouveau projet", FontSize = 20 }, _clients, _name, create } };
    }
}
