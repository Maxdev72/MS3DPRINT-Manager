using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreateFilamentWindow : Window
{
    private readonly TextBox _brand = new() { PlaceholderText = "Marque" };
    private readonly TextBox _name = new() { PlaceholderText = "Nom du filament" };
    private readonly ComboBox _material = new() { ItemsSource = new[] { "PLA", "PETG", "ABS", "ASA", "TPU", "PA", "PC", "PVA" }, SelectedIndex = 0 };
    private readonly TextBox _price = new() { PlaceholderText = "Prix au kg (ex. 24,90)" };
    private readonly CheckBox _abrasive = new() { Content = "Matière abrasive" };
    private readonly TextBlock _error = new() { Foreground = Avalonia.Media.Brushes.IndianRed, IsVisible = false };
    public string Brand => _brand.Text?.Trim() ?? string.Empty;
    public string FilamentName => _name.Text?.Trim() ?? string.Empty;
    public string Material => _material.SelectedItem?.ToString() ?? "PLA";
    public decimal Price { get; private set; }
    public bool IsAbrasive => _abrasive.IsChecked == true;

    public CreateFilamentWindow()
    {
        Title = "Ajouter un filament"; Width = 440; Height = 410;
        var save = new Button { Content = "Ajouter", HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += Save_Click;
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12, Children = { new TextBlock { Text = "Nouveau filament", FontSize = 20 }, _brand, _name, _material, _price, _abrasive, _error, save } };
    }
    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Brand) || string.IsNullOrWhiteSpace(FilamentName) || !decimal.TryParse(_price.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("fr-FR"), out var price) || price < 0)
        { _error.Text = "Renseignez la marque, le nom et un prix valide."; _error.IsVisible = true; return; }
        Price = price; Close(true);
    }
}
