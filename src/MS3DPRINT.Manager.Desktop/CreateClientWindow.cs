using Avalonia.Controls;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreateClientWindow : Window
{
    private readonly ClientSummary? _existingClient;
    private readonly TextBox _name = new() { PlaceholderText = "Nom de l’entreprise" };
    private readonly TextBox _firstName = new() { PlaceholderText = "Prénom" };
    private readonly TextBox _lastName = new() { PlaceholderText = "Nom" };
    private readonly TextBox _code = new() { PlaceholderText = "Code client" };
    private readonly CheckBox _individual = new() { Content = "Client particulier" };
    public string ClientName => _name.Text?.Trim() ?? string.Empty;
    public string FirstName => _firstName.Text?.Trim() ?? string.Empty;
    public string LastName => _lastName.Text?.Trim() ?? string.Empty;
    public string Code => _code.Text?.Trim().ToUpperInvariant() ?? string.Empty;
    public bool IsIndividual => _individual.IsChecked == true;

    public CreateClientWindow(ClientSummary? existingClient = null)
    {
        _existingClient = existingClient;
        Title = existingClient is null ? "Créer un client" : "Compléter la fiche client"; Width = 420; Height = 370;
        if (existingClient is not null) { _name.Text = existingClient.DisplayName; _code.Text = existingClient.ClientCode; }
        var create = new Button { Content = existingClient is null ? "Créer" : "Enregistrer la fiche", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        create.Click += (_, _) => Close((IsIndividual ? !string.IsNullOrWhiteSpace(LastName) : !string.IsNullOrWhiteSpace(ClientName)) && !string.IsNullOrWhiteSpace(Code));
        _individual.IsCheckedChanged += (_, _) =>
        {
            _name.IsVisible = !IsIndividual;
            _firstName.IsVisible = IsIndividual;
            _lastName.IsVisible = IsIndividual;
        };
        _firstName.IsVisible = _lastName.IsVisible = false;
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14, Children = { new TextBlock { Text = existingClient is null ? "Nouveau client" : "Compléter la fiche client", FontSize = 20 }, new TextBlock { Text = existingClient is null ? string.Empty : "Le dossier et les fichiers existants sont conservés.", Classes = { "muted" } }, _individual, _name, _firstName, _lastName, _code, create } };
    }
}
