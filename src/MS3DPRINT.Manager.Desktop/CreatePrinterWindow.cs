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
    private readonly TextBox _powerWatts = new() { PlaceholderText = "ex. 150" };
    private readonly TextBox _years = new() { Text = "0" };
    private readonly TextBox _annualHours = new() { Text = "0" };
    private readonly TextBox _manualRate = new() { Text = "0" };
    private readonly decimal _initialManualRate;
    private readonly string _initialManualRateText;
    private readonly TextBox _maintenance = new() { Text = "0" };
    private readonly CheckBox _useManual = new() { Content = "Utiliser le taux horaire manuel" };
    private readonly TextBlock _calculatedRate = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } };
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
    public decimal AmortizationYears { get; private set; }
    public decimal ProductiveHoursPerYear { get; private set; }
    public bool UseManualHourlyRate => _useManual.IsChecked == true;
    public decimal MaintenanceHourlyRate { get; private set; }
    public bool PrinterIsActive => Original?.IsActive ?? true;

    public CreatePrinterWindow(PrinterProfile? original = null)
    {
        Original = original;
        Title = original is null ? "Ajouter une imprimante" : "Modifier l’imprimante"; Width = 480; Height = 700; MinWidth = 420;
        _brand.Text = original?.Brand ?? string.Empty;
        _brand.SelectedItem = KnownBrands.Contains(original?.Brand ?? string.Empty, StringComparer.OrdinalIgnoreCase) ? original?.Brand : null;
        _model.Text = original?.Model ?? string.Empty;
        _purchasePrice.Text = original?.PurchasePrice.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) ?? "0";
        _powerWatts.Text = original?.PowerWatts?.ToString() ?? string.Empty;
        // Keep old machine costs as explicit rates until the user supplies annual production.
        var years = original is { AmortizationYears: > 0 } ? original.AmortizationYears : 4m;
        var annual = original?.ProductiveHoursPerYear ?? 0m;
        _years.Text = years.ToString();
        _annualHours.Text = annual.ToString();
        _initialManualRate = original is null ? 0 : PrinterCostCalculator.HourlyRate(original);
        _initialManualRateText = _initialManualRate.ToString("0.######", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        _manualRate.Text = _initialManualRateText;
        _useManual.IsChecked = original?.UseManualHourlyRate == true ||
            (original is not null && (original.PurchasePrice == 0 || annual == 0));
        _maintenance.Text = original?.MaintenanceHourlyRate.ToString() ?? "0";
        var save = new Button { Content = original is null ? "Ajouter l’imprimante" : "Enregistrer" };
        save.Click += Save_Click;
        Layout(original is null ? "Nouvelle imprimante" : "Modifier l’imprimante",
            "Calculez l’amortissement avec vos heures de production, ou renseignez directement un taux horaire.",
            [CreateUnitField("Marque", _brand), CreateUnitField("Modèle", _model), CreateUnitField("Prix d’achat", _purchasePrice, "€"),
             _useManual,
             CreateUnitField("Durée d’amortissement", _years, "ans"), CreateUnitField("Heures productives annuelles", _annualHours, "h/an"),
             CreateUnitField("Taux manuel", _manualRate, "€/h"), CreateUnitField("Maintenance / usure distincte", _maintenance, "€/h"), _calculatedRate,
             CreateUnitField("Puissance (optionnelle)", _powerWatts, "W"),
             _error], save);
        foreach (var field in new[] { _purchasePrice, _years, _annualHours, _manualRate, _maintenance, _powerWatts, _model }) field.TextChanged += (_, _) => { FormKit.ClearError(_error); UpdateCalculatedRate(); };
        _useManual.IsCheckedChanged += (_, _) => { FormKit.ClearError(_error); UpdateCalculatedRate(); };
        UpdateCalculatedRate();
    }

    private void UpdateCalculatedRate()
    {
        _manualRate.IsVisible = UseManualHourlyRate;
        _years.IsVisible = _annualHours.IsVisible = !UseManualHourlyRate;
        if (!TryGetRate(out var rate, out var message)) { _calculatedRate.Text = message; return; }
        var label = UseManualHourlyRate ? "Coût horaire retenu" : "Amortissement";
        _calculatedRate.Text = $"{label} : {rate.ToString("0.######", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} €/h (hors maintenance et électricité).";
    }

    private bool TryGetRate(out decimal rate, out string message)
    {
        rate = 0;
        message = "Renseignez un taux horaire manuel positif ou nul.";
        if (UseManualHourlyRate)
        {
            // Formatting a saved rate must not round its underlying value when editing other fields.
            if (_manualRate.Text == _initialManualRateText) { rate = _initialManualRate; return true; }
            return FilamentPrice.TryParse(_manualRate.Text, out rate);
        }
        message = "Renseignez le prix, une durée en années et des heures productives annuelles supérieures à zéro.";
        if (!FilamentPrice.TryParse(_purchasePrice.Text, out var price) || !FilamentPrice.TryParse(_years.Text, out var years) || years <= 0
            || !FilamentPrice.TryParse(_annualHours.Text, out var annual) || annual <= 0) return false;
        try { var productiveLifetime = years * annual; if (productiveLifetime == 0) return false; rate = price / productiveLifetime; return true; }
        catch (OverflowException) { message = "Ces valeurs dépassent la capacité de calcul. Vérifiez les unités saisies."; return false; }
    }

    internal static StackPanel CreateUnitField(string label, Control input, string? unit = null) => FormKit.Field(label, input, unit);

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Brand) || string.IsNullOrWhiteSpace(Model))
        { FormKit.ShowError(_error, string.IsNullOrWhiteSpace(Brand) ? (Control)_brand : _model, "Renseignez la marque et le modèle de l’imprimante."); return; }
        if (!FilamentPrice.TryParse(_purchasePrice.Text, out var purchasePrice) ||
            !FilamentPrice.TryParse(_maintenance.Text, out var maintenance) ||
            (!string.IsNullOrWhiteSpace(_powerWatts.Text) && (!int.TryParse(_powerWatts.Text, out var watts) || watts < 0)))
        { FormKit.ShowError(_error, !FilamentPrice.TryParse(_purchasePrice.Text, out _) ? _purchasePrice : !FilamentPrice.TryParse(_maintenance.Text, out _) ? _maintenance : _powerWatts, "Vérifiez les valeurs : coûts positifs ou nuls et puissance entière en watts."); return; }
        if (!TryGetRate(out var rate, out var message))
        { FormKit.ShowError(_error, UseManualHourlyRate ? _manualRate : !FilamentPrice.TryParse(_years.Text, out var validYears) || validYears <= 0 ? _years : _annualHours, message); return; }
        PurchasePrice = purchasePrice;
        LifetimeHours = Original?.LifetimeHours ?? 0;
        FilamentPrice.TryParse(_years.Text, out var years);
        FilamentPrice.TryParse(_annualHours.Text, out var annual);
        AmortizationYears = UseManualHourlyRate ? Original?.AmortizationYears ?? 0 : years;
        ProductiveHoursPerYear = UseManualHourlyRate ? Original?.ProductiveHoursPerYear ?? 0 : annual;
        MaintenanceHourlyRate = maintenance;
        PowerWatts = string.IsNullOrWhiteSpace(_powerWatts.Text) ? null : int.Parse(_powerWatts.Text);
        HourlyRate = rate;
        Close(true);
    }

}

