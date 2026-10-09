using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.Desktop;

public sealed class ClientPickerWindow : Window
{
    private readonly ComboBox _clients;
    public ClientSummary? SelectedClient => _clients.SelectedItem as ClientSummary;

    public ClientPickerWindow(IReadOnlyList<ClientSummary> clients)
    {
        Title = "Déplacer le projet"; Width = 480; Height = 270;
        _clients = new ComboBox { ItemsSource = clients.Where(client => client.Profile is not null).ToArray(), PlaceholderText = "Choisir le client destinataire" };
        _clients.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ClientSummary>((client, _) => new TextBlock { Text = $"{client.DisplayName} · {client.ClientCode}" });
        var cancel = new Button { Content = "Annuler" }; cancel.Click += (_, _) => Close(false);
        var move = new Button { Content = "Déplacer" }; move.Click += (_, _) => Close(SelectedClient is not null);
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14, Children = { new TextBlock { Text = "Client destinataire", FontSize = 19, FontWeight = Avalonia.Media.FontWeight.SemiBold }, new TextBlock { Text = "Les documents et la fiche projet suivront le dossier.", Classes = { "muted" } }, _clients, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Children = { cancel, move } } } };
    }
}
