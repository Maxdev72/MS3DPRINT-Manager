using Avalonia.Controls;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Naming;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreateCollectionWindow : DialogWindow
{
    private readonly ComboBox _category = new() { ItemsSource = new[] { "Modèle 3D", "Produit", "Fournisseur" }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly TextBox _name = new() { PlaceholderText = "ex. Pots décoratifs" };
    private readonly TextBlock _error = FormKit.ErrorText();
    private readonly TextBlock _folderPreview = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    public string NameValue => _name.Text?.Trim() ?? string.Empty;
    public string Category => _category.SelectedIndex switch { 1 => "03_PRODUITS_MS3DPRINT", 2 => "06_FOURNISSEURS", _ => "02_MODELES_3D" };

    public CreateCollectionWindow()
    {
        Title = "Créer une collection"; Width = 460; Height = 500; MinWidth = 400;
        void UpdatePreview()
        {
            var folder = NameNormalizer.Normalize(NameValue);
            _folderPreview.Text = folder.Length == 0 ? "Dossier : saisissez un nom contenant une lettre ou un chiffre."
                : "Dossier : " + folder;
        }
        _name.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();
        var create = new Button { Content = "Créer" };
        create.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(NameValue)) { FormKit.ShowError(_error, _name, "Donnez un nom à la collection."); return; }
            if (NameNormalizer.Normalize(NameValue).Length == 0) { FormKit.ShowError(_error, _name, "Le nom doit contenir au moins une lettre ou un chiffre."); return; }
            Close(true);
        };
        Layout("Nouvelle collection", "Le nom saisi reste affiché dans Collections. Le nom du dossier est normalisé ci-dessous.",
            [FormKit.Field("Type", _category), FormKit.Field("Nom", _name), _folderPreview, _error], create);
        Opened += (_, _) => _name.Focus();
    }
}
