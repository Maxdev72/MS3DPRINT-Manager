using Avalonia.Controls;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.Desktop;

public sealed class ClientPickerWindow : DialogWindow
{
    private readonly ComboBox _clients;
    private readonly TextBlock _error = FormKit.ErrorText();
    public ClientSummary? SelectedClient => _clients.SelectedItem as ClientSummary;

    public ClientPickerWindow(IReadOnlyList<ClientSummary> clients)
    {
        Title = "Déplacer le projet"; Width = 500; Height = 320; MinWidth = 400;
        _clients = new ComboBox
        {
            ItemsSource = clients.Where(client => client.Profile is not null).ToArray(),
            PlaceholderText = "Choisir le client destinataire",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        _clients.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ClientSummary>((client, _) => new TextBlock
        {
            Text = client is null ? string.Empty : $"{client.DisplayName} · {client.ClientCode}"
        });
        var move = new Button { Content = "Déplacer" };
        move.Click += (_, _) =>
        {
            if (SelectedClient is null) { _error.Text = "Choisissez le client destinataire."; _error.IsVisible = true; return; }
            Close(true);
        };
        Layout("Déplacer le projet", "Les documents et la fiche projet suivent le dossier.", [FormKit.Field("Client destinataire", _clients), _error], move);
    }
}
