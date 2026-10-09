using Avalonia.Controls;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreateProjectWindow : Window
{
    private readonly ComboBox _clients;
    private readonly TextBox _name;
    public ClientSummary? Client => _clients.SelectedItem as ClientSummary;
    public string ProjectName => _name.Text?.Trim() ?? string.Empty;

    public CreateProjectWindow(IReadOnlyList<ClientSummary> clients)
    {
        Title = "Créer un projet"; Width = 440; Height = 260;
        _clients = new ComboBox { ItemsSource = clients, PlaceholderText = "Choisir un client" };
        _name = new TextBox { PlaceholderText = "Nom du projet" };
        var create = new Button { Content = "Créer", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        create.Click += (_, _) => Close(Client is not null && !string.IsNullOrWhiteSpace(ProjectName));
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14, Children = { new TextBlock { Text = "Nouveau projet", FontSize = 20 }, _clients, _name, create } };
    }
}
