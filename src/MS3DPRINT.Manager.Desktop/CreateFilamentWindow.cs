using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Filaments;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreateFilamentWindow : DialogWindow
{
    // Guide checked on 2026-10-24: https://ms3dprint.fr/matieres-impression-3d
    // Suggestions only: existing profiles and custom formulations remain editable.
    private static readonly string[] Materials = [
        "PLA", "PLA-CF", "PLA-GF", "PETG", "PETG-CF", "PETG-ESD", "ABS", "ASA",
        "PC", "PC-FR", "PC-ABS", "PC-PBT", "PET-CF", "ABS-GF", "ASA-CF", "PET-GF",
        "PA6-GF", "PA6-CF", "PA12-CF", "PA612-CF", "CoPA", "PA612-ESD", "PPS-CF", "PPS-GF",
        "TPU 90A", "TPU 95A"
    ];
    private static readonly string[] Colors = [
        "Noir", "Blanc", "Gris", "Argent", "Rouge", "Orange", "Jaune", "Vert", "Bleu", "Violet", "Rose", "Marron", "Transparent", "Multicolore", "Autre"
    ];
    private readonly TextBox _brand = new() { PlaceholderText = "Marque" };
    private readonly TextBox _name = new() { PlaceholderText = "Nom du filament" };
    private readonly ComboBox _material = new() { ItemsSource = Materials, SelectedIndex = 0, IsEditable = true };
    private readonly ComboBox _color = new() { ItemsSource = Colors, IsEditable = true, PlaceholderText = "Couleur (optionnelle)" };
    private readonly TextBox _price = new() { PlaceholderText = "ex. 24,90" };
    private readonly CheckBox _abrasive = new() { Content = "Matière abrasive" };
    private readonly TextBlock _error = FormKit.ErrorText();
    public FilamentProfile? Original { get; }
    public string Brand => _brand.Text?.Trim() ?? string.Empty;
    public string FilamentName => _name.Text?.Trim() ?? string.Empty;
    public string Material => (_material.Text ?? _material.SelectedItem?.ToString() ?? string.Empty).Trim();
    public string? Color => string.IsNullOrWhiteSpace(_color.Text) ? _color.SelectedItem?.ToString()?.Trim() : _color.Text.Trim();
    public decimal Price { get; private set; }
    public bool IsAbrasive => _abrasive.IsChecked == true;

    public CreateFilamentWindow(FilamentProfile? original = null)
    {
        Original = original;
        Title = original is null ? "Ajouter un filament" : "Modifier le filament"; Width = 480; Height = 640; MinWidth = 420;
        _brand.Text = original?.Brand ?? string.Empty;
        _name.Text = original?.Name ?? string.Empty;
        var material = original?.Material ?? "PLA";
        _material.SelectedItem = Materials.Contains(material, StringComparer.Ordinal) ? material : null;
        _material.Text = material;
        _color.Text = original?.Color ?? string.Empty;
        _color.SelectedItem = Colors.Contains(original?.Color ?? string.Empty, StringComparer.OrdinalIgnoreCase) ? original?.Color : null;
        _price.Text = original?.PricePerKg.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) ?? string.Empty;
        _abrasive.IsChecked = original?.IsAbrasive ?? false;
        var save = new Button { Content = original is null ? "Ajouter le filament" : "Enregistrer" };
        save.Click += Save_Click;
        Layout(original is null ? "Nouveau filament" : "Modifier le filament", "Le prix au kilo sert au coût matière de vos estimations.",
            [FormKit.Field("Marque", _brand), FormKit.Field("Nom du filament", _name), FormKit.Field("Matière", _material, hint: "Liste du guide MS3DPRINT ; vous pouvez aussi saisir une matière personnalisée."),
             FormKit.Field("Couleur (optionnelle)", _color), FormKit.Field("Prix au kilo", _price, "€/kg"), _abrasive, _error], save);
    }
    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Brand) || string.IsNullOrWhiteSpace(FilamentName) || !FilamentPrice.TryParse(_price.Text, out var price))
        { FormKit.ShowError(_error, string.IsNullOrWhiteSpace(Brand) ? _brand : string.IsNullOrWhiteSpace(FilamentName) ? _name : _price, "Renseignez la marque, le nom et un prix valide."); return; }
        if (string.IsNullOrWhiteSpace(Material)) { FormKit.ShowError(_error, _material, "Choisissez une matière ou saisissez son nom."); return; }
        Price = price; Close(true);
    }
}
