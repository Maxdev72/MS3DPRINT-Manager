using Avalonia.Controls;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreateClientWindow : Window
{
    private readonly TextBox _name = new() { PlaceholderText = "Nom de l’entreprise" };
    private readonly TextBox _code = new() { PlaceholderText = "Code client" };
    public string ClientName => _name.Text?.Trim() ?? string.Empty;
    public string Code => _code.Text?.Trim().ToUpperInvariant() ?? string.Empty;

    public CreateClientWindow()
    {
        Title = "Créer un client"; Width = 420; Height = 240;
        var create = new Button { Content = "Créer", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        create.Click += (_, _) => Close(!string.IsNullOrWhiteSpace(ClientName) && !string.IsNullOrWhiteSpace(Code));
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14, Children = { new TextBlock { Text = "Nouveau client", FontSize = 20 }, _name, _code, create } };
    }
}
