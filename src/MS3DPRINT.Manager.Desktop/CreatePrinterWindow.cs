using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Printers;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreatePrinterWindow : DialogWindow
{
    public static IReadOnlyList<string> KnownBrands { get; } =
    [
        "AnkerMake", "Anycubic", "Artillery", "Bambu Lab", "BCN3D", "BigRep",
        "Creality", "Elegoo", "Flashforge", "FLSUN", "Formlabs", "Markforged",
        "Phrozen", "Prusa Research", "Qidi Tech", "Raise3D", "Rat Rig", "Snapmaker",
        "Sovol", "UltiMaker", "Uniformation", "Voron", "Zortrax"
    ];

    private readonly ComboBox _brand = new()
    {
        PlaceholderText = "Choisir ou saisir une marque",
        IsEditable = true,
        ItemsSource = KnownBrands
    };
    private readonly TextBox _model = new() { PlaceholderText = "Modèle (ex. P1S)" };
    private readonly TextBox _purchasePrice = new() { PlaceholderText = "Prix d’achat" };
    private readonly TextBox _lifetimeHours = new() { PlaceholderText = "ex. 8000" };
    private readonly TextBox _powerWatts = new() { PlaceholderText = "ex. 150" };
    private readonly TextBox _volume = new() { PlaceholderText = "Non utilisé pour l’instant", IsEnabled = false };
    private readonly TextBlock _error = FormKit.ErrorText();
    public PrinterProfile? Original { get; }
    public string Brand => _brand.Text?.Trim() ?? _brand.SelectedItem as string ?? string.Empty;
    public string Model => _model.Text?.Trim() ?? string.Empty;
    public string PrinterName => $"{Brand} {Model}".Trim();
    public string BuildVolume => Original?.BuildVolume ?? string.Empty;
    public decimal PurchasePrice { get; private set; }
    public int LifetimeHours { get; private set; }
    public int? PowerWatts { get; private set; }
    public decimal HourlyRate { get; private set; }
    public bool PrinterIsActive => Original?.IsActive ?? true;

    public CreatePrinterWindow(PrinterProfile? original = null)
    {
        Original = original;
        Title = original is null ? "Ajouter une imprimante" : "Modifier l’imprimante"; Width = 480; Height = 700; MinWidth = 420;
        _brand.Text = original?.Brand ?? string.Empty;
        _brand.SelectedItem = KnownBrands.Contains(original?.Brand ?? string.Empty, StringComparer.OrdinalIgnoreCase) ? original?.Brand : null;
        _model.Text = original?.Model ?? string.Empty;
        _purchasePrice.Text = original?.PurchasePrice.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) ?? "0";
        _lifetimeHours.Text = (original?.LifetimeHours is > 0 ? original.LifetimeHours : 8000).ToString();
        _powerWatts.Text = original?.PowerWatts?.ToString() ?? string.Empty;
        _volume.Text = original?.BuildVolume ?? string.Empty;
        var save = new Button { Content = original is null ? "Ajouter l’imprimante" : "Enregistrer" };
        save.Click += Save_Click;
        Layout(original is null ? "Nouvelle imprimante" : "Modifier l’imprimante",
            "Le coût horaire est calculé à partir du prix d’achat et de la durée de vie estimée.",
            [CreateUnitField("Marque", _brand), CreateUnitField("Modèle", _model), CreateUnitField("Prix d’achat", _purchasePrice, "€"),
             CreateUnitField("Durée de vie estimée", _lifetimeHours, "h"), CreateUnitField("Puissance (optionnelle)", _powerWatts, "W"),
             CreateUnitField("Volume d’impression — bientôt disponible", _volume, "mm"), _error], save);
    }

    internal static StackPanel CreateUnitField(string label, Control input, string? unit = null) => FormKit.Field(label, input, unit);

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Brand) || string.IsNullOrWhiteSpace(Model) || !FilamentPrice.TryParse(_purchasePrice.Text, out var purchasePrice) ||
            !int.TryParse(_lifetimeHours.Text, out var lifetimeHours) || lifetimeHours <= 0 ||
            (!string.IsNullOrWhiteSpace(_powerWatts.Text) && (!int.TryParse(_powerWatts.Text, out var watts) || watts < 0)))
        { _error.Text = "Renseignez la marque, le modèle, le prix d’achat et une durée de vie valide."; _error.IsVisible = true; return; }
        PurchasePrice = purchasePrice;
        LifetimeHours = lifetimeHours;
        PowerWatts = string.IsNullOrWhiteSpace(_powerWatts.Text) ? null : int.Parse(_powerWatts.Text);
        HourlyRate = PrinterCostCalculator.HourlyRate(new PrinterProfile(Guid.Empty, Brand, Model, PrinterName, 0m, BuildVolume, PrinterIsActive, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, PurchasePrice, LifetimeHours, PowerWatts));
        Close(true);
    }
}

