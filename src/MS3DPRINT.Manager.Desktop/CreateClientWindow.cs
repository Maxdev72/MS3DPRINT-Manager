using Avalonia.Controls;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreateClientWindow : Window
{
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

    public CreateClientWindow()
    {
        Title = "Créer un client"; Width = 420; Height = 370;
        var create = new Button { Content = "Créer", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        create.Click += (_, _) => Close((IsIndividual ? !string.IsNullOrWhiteSpace(LastName) : !string.IsNullOrWhiteSpace(ClientName)) && !string.IsNullOrWhiteSpace(Code));
        _individual.IsCheckedChanged += (_, _) =>
        {
            _name.IsVisible = !IsIndividual;
            _firstName.IsVisible = IsIndividual;
            _lastName.IsVisible = IsIndividual;
        };
        _firstName.IsVisible = _lastName.IsVisible = false;
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14, Children = { new TextBlock { Text = "Nouveau client", FontSize = 20 }, _individual, _name, _firstName, _lastName, _code, create } };
    }
}
