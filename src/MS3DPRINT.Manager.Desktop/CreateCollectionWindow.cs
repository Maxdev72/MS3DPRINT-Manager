using Avalonia.Controls;
using Avalonia.Layout;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreateCollectionWindow : Window
{
    private readonly ComboBox _category = new() { ItemsSource = new[] { "Modèle 3D", "Produit", "Fournisseur" }, SelectedIndex = 0 };
    private readonly TextBox _name = new() { PlaceholderText = "Nom" };
    public string NameValue => _name.Text?.Trim() ?? string.Empty;
    public string Category => _category.SelectedIndex switch { 1 => "03_PRODUITS_MS3DPRINT", 2 => "06_FOURNISSEURS", _ => "02_MODELES_3D" };
    public CreateCollectionWindow()
    {
        Title = "Créer une collection"; Width = 440; Height = 275;
        var create = new Button { Content = "Créer", HorizontalAlignment = HorizontalAlignment.Right };
        create.Click += (_, _) => Close(!string.IsNullOrWhiteSpace(NameValue));
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14, Children = { new TextBlock { Text = "Nouvel élément", FontSize = 20 }, _category, _name, create } };
    }
}
