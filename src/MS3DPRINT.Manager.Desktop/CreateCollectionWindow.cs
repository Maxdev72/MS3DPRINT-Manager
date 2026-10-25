using Avalonia.Controls;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreateCollectionWindow : DialogWindow
{
    private readonly ComboBox _category = new() { ItemsSource = new[] { "Modèle 3D", "Produit", "Fournisseur" }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly TextBox _name = new() { PlaceholderText = "ex. Pots décoratifs" };
    private readonly TextBlock _error = FormKit.ErrorText();
    public string NameValue => _name.Text?.Trim() ?? string.Empty;
    public string Category => _category.SelectedIndex switch { 1 => "03_PRODUITS_MS3DPRINT", 2 => "06_FOURNISSEURS", _ => "02_MODELES_3D" };

    public CreateCollectionWindow()
    {
        Title = "Créer une collection"; Width = 460; Height = 380; MinWidth = 400;
        var create = new Button { Content = "Créer" };
        create.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(NameValue)) { FormKit.ShowError(_error, _name, "Donnez un nom à la collection."); return; }
            Close(true);
        };
        Layout("Nouvelle collection", "Un dossier est créé dans la catégorie choisie.",
            [FormKit.Field("Type", _category), FormKit.Field("Nom", _name), _error], create);
        Opened += (_, _) => _name.Focus();
    }
}
