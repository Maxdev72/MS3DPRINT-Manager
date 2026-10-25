using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Estimates;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private WorkshopEstimateSettings? _estimateWorkshopDefaults;
    private string? _estimateWorkshopRoot;

    private Border BuildWorkshopSettingsCard(string root)
    {
        WorkshopEstimateSettings settings;
        try { settings = new WorkshopEstimateSettingsStore(root).Load() ?? new(new()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { settings = new(new()); Notify("Les réglages de l’atelier ne peuvent pas être lus : " + e.Message, error: true); }
        var edit = new Button { Content = Icons.Label("IconSettings", "Modifier les réglages", 16), Classes = { "primary-action" }, HorizontalAlignment = HorizontalAlignment.Left };
        edit.Click += EditWorkshopSettings_Click;
        var printers = new Button { Content = "Fiches imprimantes", Classes = { "ghost" } };
        var filaments = new Button { Content = "Tarifs des filaments", Classes = { "ghost" } };
        printers.Click += (_, _) => NavigationButton_Click(NavigationButtons().First(b => b.Tag as string == "Imprimantes"), new RoutedEventArgs());
        filaments.Click += (_, _) => NavigationButton_Click(NavigationButtons().First(b => b.Tag as string == "Filaments"), new RoutedEventArgs());
        printers.Content = SettingsLinkContent("Imprimantes", "Achat, amortissement, puissance et maintenance");
        filaments.Content = SettingsLinkContent("Filaments", "Matières disponibles et prix au kilogramme");
        foreach (var link in new[] { printers, filaments })
        { link.HorizontalAlignment = HorizontalAlignment.Stretch; link.HorizontalContentAlignment = HorizontalAlignment.Stretch; }
        var links = new StackPanel { Spacing = 8, Children = { printers, filaments } };
        var description = settings.PresetId is { } id ? EstimateChargeCatalog.Load().Presets.FirstOrDefault(p => p.Id == id)?.Name ?? "Taux personnalisés" : "Taux personnalisés / autre régime";
        static TextBlock Group(string title) => new() { Text = title, Classes = { "settings-group-title" } };
        var charges = settings.Charges;
        string Rate(decimal amount, bool enabled) => enabled ? Exact(amount) + " %" : "Désactivé";
        var values = new StackPanel { Spacing = 0, Children =
        {
            Group("Prix et rentabilité"),
            SettingsValueRow(settings.PricingMode == EstimatePricingMode.TargetMargin ? "Marge cible après charges" : "Majoration sur coût", Exact(settings.MarginPercent) + " %"),
            SettingsValueRow(settings.FeeKind == PieceFeeKind.RealCost ? "Coût réel par pièce" : "Supplément commercial par pièce", Exact(settings.PieceFee) + " €"),
            SettingsValueRow("Frais généraux par commande", EstimateMoney(settings.GeneralFixedCost)),
            Group("Production"),
            SettingsValueRow("Électricité", Exact(settings.ElectricityPricePerKwh) + " €/kWh"),
            SettingsValueRow("Provision pour échecs", Exact(settings.FailureRatePercent) + " %"),
            Group("Cotisations et frais commerciaux"),
            new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, Margin = new Avalonia.Thickness(0, 4, 0, 10) },
            SettingsValueRow("Cotisations · ventes", Rate(charges.SalesSocialPercent, charges.SocialEnabled)),
            SettingsValueRow("Cotisations · prestations BIC", Rate(charges.ServicesBicSocialPercent, charges.SocialEnabled)),
            SettingsValueRow("Cotisations · prestations BNC", Rate(charges.ServicesBncSocialPercent, charges.SocialEnabled)),
            SettingsValueRow("Contributions complémentaires", Rate(charges.ContributionPercent, charges.ContributionsEnabled)),
            SettingsValueRow("Commissions et frais commerciaux", Rate(charges.CommercialPercent, charges.CommercialEnabled))
        } };
        values.Children[0].Margin = default;
        var header = SettingsCardHeader("IconEstimates", "Tarification des devis", "Réglages par défaut pour les prochains devis. Chaque devis enregistré conserve ses propres paramètres.", edit);
        return new Border { Classes = { "card-raised" }, Child = new StackPanel
        {
            Spacing = 16, Children =
            {
                header, values,
                Group("Machines et matières"), links
            }
        } };
    }

    private static Control SettingsValueRow(string label, string value)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 20, MinHeight = 44 };
        row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var amount = new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 230, TextAlignment = TextAlignment.Right };
        Grid.SetColumn(amount, 1); row.Children.Add(amount);
        return new Border { BorderBrush = (IBrush?)Avalonia.Application.Current?.FindResource("SubtleBorderBrush"), BorderThickness = new Avalonia.Thickness(0, 0, 0, 1), Child = row };
    }

    private static Control SettingsLinkContent(string title, string detail)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        row.Children.Add(new StackPanel { Spacing = 4, Children =
        {
            new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = detail, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 21, FontWeight = FontWeight.Normal }
        } });
        var arrow = Icons.Glyph("IconArrowRight", 18); Grid.SetColumn(arrow, 1); row.Children.Add(arrow);
        return row;
    }

    private async void EditWorkshopSettings_Click(object? sender, RoutedEventArgs e)
    {
        var root = WorkspaceRoot;
        try
        {
            var store = new WorkshopEstimateSettingsStore(root);
            var original = await Task.Run(() => store.Load()) ?? new(new());
            var dialog = new WorkshopEstimateWindow(original);
            if (!await dialog.ShowDialog<bool>(this) || dialog.Result is not { } settings) return;
            await Task.Run(() => store.Save(settings));
            _estimateWorkshopDefaults = settings; _estimateWorkshopRoot = root;
            if (_loadedEstimate is null && !_estimateDirty && _latestEstimateInputs is null) ApplyWorkshopDefaults();
            Notify("Réglages de l’atelier enregistrés pour les prochains devis. Les devis existants sont conservés.");
            await LoadSettingsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { Notify(exception.Message, error: true); }
    }

    private void LoadWorkshopDefaults(string root)
    {
        _estimateWorkshopRoot = root;
        try { _estimateWorkshopDefaults = new WorkshopEstimateSettingsStore(root).Load(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { _estimateWorkshopDefaults = null; Notify("Réglages de l’atelier non chargés : " + e.Message, error: true); }
    }

    private void ApplyWorkshopDefaults()
    {
        if (!string.Equals(_estimateWorkshopRoot, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) LoadWorkshopDefaults(WorkspaceRoot);
        var settings = _estimateWorkshopDefaults ?? new WorkshopEstimateSettings(new());
        var suppressed = _suppressEstimateChanges; _suppressEstimateChanges = true;
        try
        {
            RestoreEstimateV2(new(EngineVersion: 2, V2: settings.Options));
            EstimateKwhPrice.Text = Exact(settings.ElectricityPricePerKwh);
            EstimateMargin.Text = Exact(settings.MarginPercent); EstimateFailure.Text = Exact(settings.FailureRatePercent); EstimateMinimum.Text = Exact(settings.PieceFee);
            ApplyEstimatePrinter(); RecalculateElectricity();
        }
        finally { _suppressEstimateChanges = suppressed; }
    }

    private async void EstimateWorkshopSettings_Click(object? sender, RoutedEventArgs e)
    {
        _settingsCategory = "atelier";
        SettingsSearchBox.Text = "";
        ShowWorkspace("Paramètres"); PageTitle.Text = "Paramètres"; PageSubtitle.Text = "Réglages de l’atelier et de l’espace de travail.";
        await LoadSettingsAsync();
    }

    private void SetEstimateCharges(EstimateCharges charges)
    {
        var suppressed = _suppressEstimateChanges; _suppressEstimateChanges = true;
        try
        {
            _estimateQuoteCharges = charges;
            _estimateSalesShare.Text = Exact(charges.SalesSharePercent); _estimateBicShare.Text = Exact(charges.ServicesBicSharePercent);
            _estimateChargeCategory.SelectedIndex = charges.SalesSharePercent == 100 ? 0 : charges.ServicesBicSharePercent == 100 ? 1
                : charges.SalesSharePercent == 0 && charges.ServicesBicSharePercent == 0 ? 2 : 3;
            UpdateEstimateWorkshopSummary();
        }
        finally { _suppressEstimateChanges = suppressed; }
    }

    private EstimateCharges ReadEstimateCharges() => _estimateQuoteCharges with
    {
        SalesSharePercent = EstimateNumber(_estimateSalesShare), ServicesBicSharePercent = EstimateNumber(_estimateBicShare)
    };

    private void ChangeEstimateChargeCategory()
    {
        if (_suppressEstimateChanges) return;
        var suppressed = _suppressEstimateChanges; _suppressEstimateChanges = true;
        switch (_estimateChargeCategory.SelectedIndex)
        {
            case 0: _estimateSalesShare.Text = "100"; _estimateBicShare.Text = "0"; break;
            case 1: _estimateSalesShare.Text = "0"; _estimateBicShare.Text = "100"; break;
            case 2: _estimateSalesShare.Text = "0"; _estimateBicShare.Text = "0"; break;
        }
        _suppressEstimateChanges = suppressed;
        EstimateChanged();
    }

    private void UpdateEstimateWorkshopSummary()
    {
        _estimateSplitFields.IsVisible = _estimateChargeCategory.SelectedIndex == 3;
        var method = _estimatePricingMode == EstimatePricingMode.TargetMargin ? "Marge cible" : "Majoration";
        _estimateWorkshopSummary.Text = $"{method} : {EstimateMargin.Text} % · Aléas : {EstimateFailure.Text} %\nForfait : {EstimateMinimum.Text} €/pièce · Électricité : {EstimateKwhPrice.Text} €/kWh";
        try
        {
            var c = ReadEstimateCharges(); var rate = EstimateCalculatorV2.ChargeRatePercent(c);
            var social = EstimateCalculatorV2.ChargeRatePercent(c with { ContributionsEnabled = false, CommercialEnabled = false });
            _estimateChargeSummary.Text = $"Charges retenues : {rate:0.###} % du prix final.\nCotisations : {social:0.###} % · Contributions : {(c.ContributionsEnabled ? c.ContributionPercent : 0):0.###} % · Commissions : {(c.CommercialEnabled ? c.CommercialPercent : 0):0.###} %."
                + (rate == 0 ? " Configurez les cotisations de votre atelier si elles s’appliquent." : "");
        }
        catch (ArgumentException) { _estimateChargeSummary.Text = "Vérifiez les parts de ventes et de prestations : leur somme ne doit pas dépasser 100 %."; }
    }

    private void UpdateEstimateCostLabels()
    {
        TextBox[] fields = [EstimateDesign, EstimateLabor, EstimatePostProcessing, EstimateConsumables, EstimatePackaging, EstimateMisc];
        string[] labels = ["Conception", "Main-d’œuvre", "Finitions", "Consommables", "Emballage / livraison", "Autres frais"];
        for (var i = 0; i < fields.Length; i++)
        {
            if (fields[i]?.Parent is Grid { Parent: StackPanel stack } && stack.Children[0] is TextBlock label)
            { label.Text = labels[i] + (!IsEstimateV2 ? " · total commande" : ""); label.TextWrapping = TextWrapping.Wrap; }
        }
    }
}
