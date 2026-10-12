using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Printers;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreatePrinterWindow : Window
{
    private readonly TextBox _brand = new() { PlaceholderText = "Marque (ex. Bambu Lab)" };
    private readonly TextBox _model = new() { PlaceholderText = "Modèle (ex. P1S)" };
    private readonly TextBox _name = new() { PlaceholderText = "Nom dans l’atelier" };
    private readonly TextBox _hourlyRate = new() { PlaceholderText = "Coût horaire (ex. 2,50)" };
    private readonly TextBox _volume = new() { PlaceholderText = "Volume (ex. 256 × 256 × 256 mm)" };
    private readonly CheckBox _active = new() { Content = "Imprimante active", IsChecked = true };
    private readonly TextBlock _error = new() { Foreground = Avalonia.Media.Brushes.IndianRed, IsVisible = false };
    public PrinterProfile? Original { get; }
    public string Brand => _brand.Text?.Trim() ?? string.Empty;
    public string Model => _model.Text?.Trim() ?? string.Empty;
    public string PrinterName => _name.Text?.Trim() ?? string.Empty;
    public string BuildVolume => _volume.Text?.Trim() ?? string.Empty;
    public decimal HourlyRate { get; private set; }
    public bool PrinterIsActive => _active.IsChecked == true;

    public CreatePrinterWindow(PrinterProfile? original = null)
    {
        Original = original;
        Title = original is null ? "Ajouter une imprimante" : "Modifier l’imprimante"; Width = 440; Height = 440;
        _brand.Text = original?.Brand ?? string.Empty; _model.Text = original?.Model ?? string.Empty; _name.Text = original?.Name ?? string.Empty;
        _hourlyRate.Text = original?.HourlyRate.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) ?? string.Empty;
        _volume.Text = original?.BuildVolume ?? string.Empty; _active.IsChecked = original?.IsActive ?? true;
        var save = new Button { Content = original is null ? "Ajouter" : "Enregistrer", HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += Save_Click;
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12, Children = { new TextBlock { Text = "Nouvelle imprimante", FontSize = 20 }, _brand, _model, _name, _hourlyRate, _volume, _active, _error, save } };
    }

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Brand) || string.IsNullOrWhiteSpace(Model) || string.IsNullOrWhiteSpace(PrinterName) || !FilamentPrice.TryParse(_hourlyRate.Text, out var rate))
        { _error.Text = "Renseignez la marque, le modèle, le nom et un coût horaire valide."; _error.IsVisible = true; return; }
        HourlyRate = rate; Close(true);
    }
}

