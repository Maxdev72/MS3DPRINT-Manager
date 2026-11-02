using Avalonia.Controls;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.Desktop;

public sealed class ClientPickerWindow : DialogWindow
{
    private readonly AutoCompleteBox _clients;
    private readonly TextBlock _error = FormKit.ErrorText();
    public ClientSummary? SelectedClient => FormKit.SelectedClient(_clients);

    public ClientPickerWindow(IReadOnlyList<ClientSummary> clients)
    {
        Title = "Déplacer le projet"; Width = 500; Height = 320; MinWidth = 400;
        _clients = FormKit.ClientSearch(clients.Where(client => client.Profile is not null));
        var move = new Button { Content = "Déplacer" };
        move.Click += (_, _) =>
        {
            if (SelectedClient is null) { FormKit.ShowError(_error, _clients, "Choisissez le client destinataire."); return; }
            Close(true);
        };
        Layout("Déplacer le projet", "Les documents et la fiche projet suivent le dossier.", [FormKit.Field("Client destinataire", _clients, hint: "Saisissez un nom ou un code, puis choisissez le client dans les résultats."), _error], move);
        Opened += (_, _) => _clients.Focus();
    }
}
