using System.Windows;
using MS3DPRINT.Manager.Core.Clients;
namespace MS3DPRINT.Manager.App.Views;
public partial class ClientPickerWindow : Window
{
    public ClientPickerWindow(IReadOnlyList<ClientSummary> clients)
    { InitializeComponent(); ClientsBox.ItemsSource = clients.Where(client => client.Profile is not null).ToArray(); }
    public ClientSummary? SelectedClient => ClientsBox.SelectedItem as ClientSummary;
    private void Accept_Click(object sender, RoutedEventArgs e)
    { if (SelectedClient is null) { ErrorText.Text = "Choisissez un client dont la fiche est complète."; return; } DialogResult = true; }
}
