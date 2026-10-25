using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Printers;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private int _estimateEngineVersion = 2;
    private readonly Button _estimateConvertV2 = new() { Content = "Convertir ce devis en V2" };
    private EstimatePricingMode _estimatePricingMode = EstimatePricingMode.TargetMargin;
    private PieceFeeKind _estimateFeeKind = PieceFeeKind.CommercialSupplement;
    private readonly ComboBox _estimatePrinter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _estimateCapacity = Number("0"), _estimateExtraGrams = Number("0"), _estimateFilamentPrice = Number("0");
    private readonly TextBox _estimateMaintenance = Number("0"), _estimatePower = Number("");
    private readonly CheckBox _estimateWeightIncludesSupports = new() { Content = "Le poids du slicer inclut déjà les supports et purges", IsChecked = true };
    private EstimateCharges _estimateQuoteCharges = new();
    private decimal _estimateGeneralFixedCost;
    private string? _estimateChargePresetId;
    private readonly TextBox _estimateSalesShare = Number("100"), _estimateBicShare = Number("0"), _estimateCommercial = Number("");
    private readonly ComboBox _estimateChargeCategory = Choice("Vente d’objets / marchandises", "Prestation artisanale ou commerciale (BIC)", "Prestation libérale (BNC)", "Mixte : répartir le prix du devis");
    private readonly StackPanel _estimateSplitFields = new() { Spacing = 10 };
    private readonly TextBlock _estimateChargeSummary = EstimateHint("");
    private readonly TextBlock _estimateWorkshopSummary = EstimateHint("");
    private Control? _estimateCapacityField;
    private CostFrequency[] _estimateFrequencies = [CostFrequency.PerOrder, CostFrequency.PerAttempt, CostFrequency.PerGoodPiece, CostFrequency.PerAttempt, CostFrequency.PerOrder, CostFrequency.PerOrder];
    private readonly StackPanel _estimateAllocations = new() { Spacing = 12 }, _estimateExtraCosts = new() { Spacing = 10 };
    private readonly List<MachineAllocationEditor> _estimateMachineEditors = [];
    private readonly List<ExtraCostEditor> _estimateCostEditors = [];
    private IReadOnlyList<PrinterProfile> _estimatePrinters = [];
    private bool _suppressEstimateChanges = true, _estimateDirty;
    private bool _updatingElectricity;
    private DispatcherTimer? _estimateTimer;
    private decimal _fleetMaintenance;
    private bool IsEstimateV2 => _estimateEngineVersion == 2;
    private static readonly System.Globalization.CultureInfo EstimateCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
    private static string Exact(decimal value) => value.ToString("0.############################", EstimateCulture);
    private static TextBox Number(string text) => new() { Text = text, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static ComboBox Choice(params string[] labels) => new()
    {
        ItemsSource = labels, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((label, _) => new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap })
    };
    private static TextBlock EstimateHint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Classes = { "muted", "estimate-help" } };
    private static Control EstimateField(string label, Control control, string? unit = null)
    {
        var field = FormKit.Field(label, control, unit);
        if (field.Children[0] is TextBlock caption) { caption.TextWrapping = TextWrapping.Wrap; caption.Classes.Add("estimate-label"); }
        field.Spacing = 6;
        return field;
    }
    private static Control EstimateSection(string label, string description, Control content)
    {
        var section = new Expander
        {
            Classes = { "estimate-section" },
            Header = new StackPanel { Spacing = 3, Children =
            {
                new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = description, FontSize = 13, LineHeight = 20, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } }
            } },
            Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        Avalonia.Automation.AutomationProperties.SetName(section, label);
        section.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find("ExpanderHeader") is Control header) Avalonia.Automation.AutomationProperties.SetName(header, label);
        };
        return section;
    }

    private void InitializeEstimateV2()
    {
        _estimateEngineVersion = 2;
        var compatibility = new StackPanel { Spacing = 12, Children =
        {
            EstimateHint("Ce devis a été enregistré avec le moteur historique. Son prix est conservé. La conversion en V2 recalculera le prix avant l’enregistrement."),
            _estimateConvertV2
        } };
        _estimateConvertV2.Click += (_, _) => { _estimateEngineVersion = 2; UpdateEstimateVersion(); EstimateChanged(); };
        EstimateV2VersionHost.Children.Add(compatibility);
        _estimatePrinter.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<PrinterChoice>((choice, _) => new TextBlock { Text = choice?.ToString(), TextWrapping = TextWrapping.Wrap });
        EstimateV2MachineHost.Children.Add(EstimateField("Imprimante utilisée", _estimatePrinter));
        var production = new StackPanel { Spacing = 10 };
        production.Children.Add(EstimateHint("Ensemble : 0 pièce par plateau signifie que les données couvrent toute la commande. Sinon, poids et durée décrivent un plateau complet, même le dernier partiellement rempli."));
        _estimateCapacityField = EstimateField("Pièces par plateau", _estimateCapacity, "pièces");
        _estimateCapacity.PlaceholderText = "0 = toute la commande";
        EstimateV2BatchHost.Children.Add(_estimateCapacityField);
        production.Children.Add(_estimateWeightIncludesSupports);
        production.Children.Add(EstimateField("Supports et purges supplémentaires par pièce / ensemble", _estimateExtraGrams, "g"));
        EstimateV2ProductionHost.Children.Add(EstimateSection("Production et consommations", "Ajouter des supports ou purges non compris dans le poids", production));
        var machines = new StackPanel { Spacing = 10 };
        machines.Children.Add(EstimateHint("Ajoutez chaque machine mobilisée. Les heures sont les totaux de la commande, avant échecs ; leur somme doit égaler durée saisie × passages. Ces affectations remplacent le taux de référence ci-dessous."));
        machines.Children.Add(_estimateAllocations);
        var addMachine = new Button { Content = "Ajouter une affectation machine" };
        addMachine.Click += (_, _) => { AddEstimateMachine(null); EstimateChanged(); };
        machines.Children.Add(addMachine);
        EstimateV2ProductionHost.Children.Add(EstimateSection("Répartition sur plusieurs machines", "Affecter les heures à chaque imprimante mobilisée", machines));

        EstimateV2PricingHost.Children.Add(_estimateWorkshopSummary);
        var workshop = new Button { Content = Icons.Label("IconSettings", "Réglages de l’atelier", 16), HorizontalAlignment = HorizontalAlignment.Left };
        workshop.Click += EstimateWorkshopSettings_Click;
        EstimateV2PricingHost.Children.Add(workshop);
        var costs = new StackPanel { Spacing = 10 };
        costs.Children.Add(_estimateExtraCosts);
        var addCost = new Button { Content = "Ajouter un poste (scan, préparation, contrôle…)" };
        addCost.Click += (_, _) => { AddEstimateCost(new("Prestation", 0, CostFrequency.PerOrder)); EstimateChanged(); };
        costs.Children.Add(addCost);
        EstimateV2CostsHost.Children.Add(EstimateSection("Prestations supplémentaires", "Scan, préparation, assemblage ou contrôle", costs));
        var charges = new StackPanel { Spacing = 10 };
        charges.Children.Add(EstimateField("Recettes concernées par ce devis", _estimateChargeCategory));
        _estimateSplitFields.Children.Add(EstimateField("Part du prix final en ventes", _estimateSalesShare, "%"));
        _estimateSplitFields.Children.Add(EstimateField("Part du prix final en prestations BIC", _estimateBicShare, "%"));
        _estimateSplitFields.Children.Add(EstimateHint("Le solde est affecté aux prestations BNC. Il s’agit du prix de CE devis, pas de la répartition annuelle de votre entreprise."));
        charges.Children.Add(_estimateSplitFields); charges.Children.Add(_estimateChargeSummary);
        EstimateV2CostsHost.Children.Insert(0, charges);
        EstimateV2CommercialHost.Children.Add(EstimateField("Votre prix avant remise", _estimateCommercial, "€"));
        _estimateCommercial.PlaceholderText = "Prix conseillé";
        EstimateV2CommercialHost.Children.Add(EstimateHint("La remise s’applique au prix commercial saisi ou au prix conseillé. La rentabilité est recalculée sur le montant final arrondi."));
        SetEstimateFrequencies(new());
        _estimateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _estimateTimer.Tick += (_, _) => { _estimateTimer.Stop(); if (!_suppressEstimateChanges) CalculateEstimate(); };
        Closed += (_, _) => _estimateTimer.Stop();
        foreach (var field in new[] { EstimateWeight, EstimateHours, EstimateMachineRate, EstimateKwhPrice, EstimateElectricity, EstimateQuantity, EstimateMinimum,
            EstimateDesign, EstimateLabor, EstimatePostProcessing, EstimateConsumables, EstimatePackaging, EstimateMisc, EstimateMargin, EstimateFailure, EstimateDiscount,
            _estimateCapacity, _estimateExtraGrams, _estimateFilamentPrice, _estimateMaintenance, _estimatePower,
            _estimateSalesShare, _estimateBicShare, _estimateCommercial }) field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) EstimateChanged(); };
        EstimatePrintDataMode.SelectionChanged += (_, _) => EstimateChanged();
        _estimateChargeCategory.SelectionChanged += (_, _) => ChangeEstimateChargeCategory();
        foreach (var check in new[] { _estimateWeightIncludesSupports }) check.IsCheckedChanged += (_, _) => EstimateChanged();
        _estimatePrinter.SelectionChanged += (_, _) => { if (!_suppressEstimateChanges) ApplyEstimatePrinter(); };
        EstimateFilament.SelectionChanged += (_, _) =>
        {
            if (!_suppressEstimateChanges && EstimateFilament.SelectedItem is FilamentProfile f) _estimateFilamentPrice.Text = Exact(f.PricePerKg);
            EstimateChanged();
        };
        InitializeEstimateHelp();
        InitializeEstimatePresentation();
        UpdateEstimateVersion();
        _suppressEstimateChanges = false;
    }

    private void UpdateEstimateVersion()
    {
        EstimateV2VersionHost.IsVisible = !IsEstimateV2 && _loadedEstimate is not null;
        UpdateEstimateOptionalCostSummary();
        UpdateEstimateQuickContext();
        EstimateV2MachineHost.IsVisible = EstimateV2PricingHost.IsVisible = EstimateV2ProductionHost.IsVisible = EstimateV2CostsHost.IsVisible = IsEstimateV2;
        EstimateV2BatchHost.IsVisible = IsEstimateV2 && EstimatePrintDataMode.SelectedIndex == 1;
        if (_estimateCommercial.Parent is Grid commercialGrid && commercialGrid.Parent is Control commercialField) commercialField.IsVisible = IsEstimateV2;
        EstimateMarginLabel.Text = IsEstimateV2 && _estimatePricingMode == EstimatePricingMode.TargetMargin ? "Marge cible sur CA" : "Majoration sur coût";
        _estimateCapacity.IsEnabled = EstimatePrintDataMode.SelectedIndex == 1;
        if (_estimateCapacityField is not null) _estimateCapacityField.IsVisible = EstimatePrintDataMode.SelectedIndex == 1;
        _estimateExtraGrams.IsEnabled = _estimateWeightIncludesSupports.IsChecked != true;
        var referenceRateUsed = !IsEstimateV2 || _estimateMachineEditors.Count == 0;
        EstimateMachineRate.IsEnabled = _estimatePrinter.IsEnabled = _estimateMaintenance.IsEnabled = _estimatePower.IsEnabled = referenceRateUsed;
        UpdateEstimateWorkshopSummary();
        UpdateEstimateCostLabels();
    }

    private void EstimateChanged(bool trackDraft = true)
    {
        if (_suppressEstimateChanges || _updatingElectricity || _estimateTimer is null) return;
        ArmEstimateSave(trackDraft);
        _estimateDirty = true;
        _latestEstimateInputs = null;
        _latestEstimateResult = null;
        MarkEstimatePricePending();
        EstimateRecap.Children.Clear();
        UpdateEstimateVersion();
        RecalculateElectricity();
        _estimateTimer.Stop(); _estimateTimer.Start();
    }

    private void PopulateEstimatePrinters(IReadOnlyList<PrinterProfile> printers)
    {
        _estimatePrinters = printers;
        var selected = (_estimatePrinter.SelectedItem as PrinterChoice)?.Printer?.Id;
        var kept = (_estimatePrinter.SelectedItem as PrinterChoice)?.Printer;
        var choices = new[] { new PrinterChoice(null) }.Concat(printers.Select(p => new PrinterChoice(p)))
            .Concat(kept is not null && !printers.Any(p => p.Id == kept.Id) ? new[] { new PrinterChoice(kept) } : []).ToArray();
        _estimatePrinter.ItemsSource = choices;
        _estimatePrinter.SelectedItem = choices.FirstOrDefault(p => p.Printer?.Id == selected) ?? choices[0];
    }

    private sealed record PrinterChoice(PrinterProfile? Printer)
    {
        public override string ToString() => Printer is null ? "Moyenne pondérée du parc" : Printer.Name + (Printer.IsActive ? "" : " (inactive)");
    }

    private void ApplyEstimatePrinter()
    {
        var printer = (_estimatePrinter.SelectedItem as PrinterChoice)?.Printer;
        var previous = _suppressEstimateChanges;
        _suppressEstimateChanges = true;
        EstimateMachineRate.Text = Exact(printer is null ? _fleetHourlyRate : PrinterCostCalculator.HourlyRate(printer));
        _estimateMaintenance.Text = Exact(printer?.MaintenanceHourlyRate ?? _fleetMaintenance);
        _estimatePower.Text = (printer is null ? _fleetPowerWatts : printer.PowerWatts)?.ToString() ?? "";
        _suppressEstimateChanges = previous;
        EstimateChanged();
    }

    private sealed record MachineAllocationEditor(StackPanel Panel, TextBox Name, TextBox Hours, TextBox Rate, TextBox Maintenance, TextBox Power, Guid? PrinterId);
    private void AddEstimateMachine(EstimateMachineAllocation? allocation)
    {
        var printer = (_estimatePrinter.SelectedItem as PrinterChoice)?.Printer;
        allocation ??= new(printer?.Name ?? "Moyenne du parc / taux libre", 0, printer is null ? _fleetHourlyRate : PrinterCostCalculator.HourlyRate(printer),
            printer?.MaintenanceHourlyRate ?? _fleetMaintenance, printer is null ? _fleetPowerWatts : printer.PowerWatts, printer?.Id);
        var panel = new StackPanel { Spacing = 6 };
        var editor = new MachineAllocationEditor(panel, Number(allocation.Name), Number(Exact(allocation.Hours)), Number(Exact(allocation.HourlyRate)), Number(Exact(allocation.MaintenanceHourlyRate)), Number(allocation.PowerWatts?.ToString() ?? ""), allocation.PrinterId);
        var select = new ComboBox { ItemsSource = _estimatePrinters.Select(p => new PrinterChoice(p)).ToArray(), PlaceholderText = "Reprendre les paramètres d’une imprimante", HorizontalAlignment = HorizontalAlignment.Stretch };
        select.SelectionChanged += (_, _) =>
        {
            if (select.SelectedItem is not PrinterChoice { Printer: { } p }) return;
            editor.Name.Text = p.Name; editor.Rate.Text = Exact(PrinterCostCalculator.HourlyRate(p)); editor.Maintenance.Text = Exact(p.MaintenanceHourlyRate); editor.Power.Text = p.PowerWatts?.ToString() ?? "";
            // Keep the identity paired with the newly selected snapshot.
            var index = _estimateMachineEditors.IndexOf(editor);
            if (index >= 0) { editor = editor with { PrinterId = p.Id }; _estimateMachineEditors[index] = editor; }
            EstimateChanged();
        };
        panel.Children.Add(select);
        panel.Children.Add(EstimateField("Machine / libellé", editor.Name));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
        grid.Children.Add(EstimateField("Total d’heures", editor.Hours, "h"));
        var rateField = EstimateField("Amortissement", editor.Rate, "€/h"); Grid.SetColumn(rateField, 1); grid.Children.Add(rateField); panel.Children.Add(grid);
        panel.Children.Add(EstimateField("Maintenance", editor.Maintenance, "€/h")); panel.Children.Add(EstimateField("Puissance (optionnelle)", editor.Power, "W"));
        var remove = new Button { Content = "Retirer cette affectation", Classes = { "ghost" } };
        remove.Click += async (_, _) =>
        {
            if (!await ConfirmRemovalWindow.AskAsync(this, string.IsNullOrWhiteSpace(editor.Name.Text) ? "Affectation machine" : editor.Name.Text, "Cette affectation sera retirée du devis. Le coût de fabrication sera recalculé. La fiche imprimante est conservée.")) return;
            _estimateMachineEditors.Remove(editor); _estimateAllocations.Children.Remove(panel); EstimateChanged();
        };
        panel.Children.Add(remove);
        foreach (var field in new[] { editor.Name, editor.Hours, editor.Rate, editor.Maintenance, editor.Power }) field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) EstimateChanged(); };
        _estimateMachineEditors.Add(editor); _estimateAllocations.Children.Add(panel);
    }

    private sealed record ExtraCostEditor(StackPanel Panel, TextBox Name, TextBox Amount, CheckBox RepeatOnFailure, CheckBox Enabled);
    private void AddEstimateCost(EstimateCostItem item)
    {
        var editor = new ExtraCostEditor(new() { Spacing = 6 }, Number(item.Name), Number(Exact(item.Amount)),
            new() { Content = "À refaire si l’impression échoue", IsChecked = item.Frequency == CostFrequency.PerAttempt }, new() { Content = "Inclure ce poste", IsChecked = item.Enabled });
        editor.Panel.Children.Add(EstimateField("Poste", editor.Name)); editor.Panel.Children.Add(EstimateField("Montant total de la commande", editor.Amount, "€")); editor.Panel.Children.Add(editor.RepeatOnFailure); editor.Panel.Children.Add(editor.Enabled);
        var remove = new Button { Content = "Retirer ce poste", Classes = { "ghost" } };
        remove.Click += async (_, _) =>
        {
            if (!await ConfirmRemovalWindow.AskAsync(this, string.IsNullOrWhiteSpace(editor.Name.Text) ? "Poste de coût" : editor.Name.Text, "Ce poste sera retiré du devis. Le coût de revient et le prix seront recalculés.")) return;
            _estimateCostEditors.Remove(editor); _estimateExtraCosts.Children.Remove(editor.Panel); EstimateChanged();
        };
        editor.Panel.Children.Add(remove); _estimateCostEditors.Add(editor); _estimateExtraCosts.Children.Add(editor.Panel);
        editor.Name.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) EstimateChanged(); }; editor.Amount.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) EstimateChanged(); }; editor.RepeatOnFailure.IsCheckedChanged += (_, _) => EstimateChanged(); editor.Enabled.IsCheckedChanged += (_, _) => EstimateChanged();
    }

    private static decimal EstimateNumber(TextBox box)
    {
        if (!TryEstimateValue(box.Text, out var value) || value < 0) throw new ArgumentException("Saisissez des montants, poids, durées et taux positifs avec une virgule ou un point.");
        return value;
    }
    private static int? EstimatePower(TextBox box)
    {
        if (string.IsNullOrWhiteSpace(box.Text)) return null;
        if (!int.TryParse(box.Text, out var power) || power < 0) throw new ArgumentException("La puissance doit être un nombre entier positif de watts, ou vide.");
        return power;
    }

    private EstimateV2Options ReadEstimateV2(bool modellingOnly, decimal grams, decimal hours, decimal rate, decimal pricePerKwh, decimal electricity)
    {
        var frequency = _estimateFrequencies;
        EstimateProduction? production = null;
        if (!modellingOnly && !IsEstimateOutsourced)
        {
            if (!int.TryParse(_estimateCapacity.Text, out var capacity) || capacity < 0) throw new ArgumentException("Le nombre de pièces par plateau doit être un entier positif ou 0 pour l’ensemble complet.");
            var allocations = _estimateMachineEditors.Select(a => new EstimateMachineAllocation(a.Name.Text ?? "Machine", EstimateNumber(a.Hours), EstimateNumber(a.Rate), EstimateNumber(a.Maintenance), EstimatePower(a.Power), a.PrinterId)).ToArray();
            var printer = (_estimatePrinter.SelectedItem as PrinterChoice)?.Printer;
            production = new(grams, EstimateNumber(_estimateExtraGrams), _estimateWeightIncludesSupports.IsChecked == true, hours, EstimateNumber(_estimateFilamentPrice), rate,
                EstimateNumber(_estimateMaintenance), EstimatePower(_estimatePower), pricePerKwh, electricity, capacity, allocations.Length == 0 ? null : allocations, printer?.Name, printer?.Id);
        }
        return new(production, ReadEstimateCharges(),
            _estimatePricingMode, _estimateFeeKind, string.IsNullOrWhiteSpace(_estimateCommercial.Text) ? null : EstimateNumber(_estimateCommercial), _estimateGeneralFixedCost,
            frequency[0], frequency[1], frequency[2], frequency[3], frequency[4], frequency[5],
            _estimateCostEditors.Select(c => new EstimateCostItem(c.Name.Text ?? "Poste", EstimateNumber(c.Amount), c.RepeatOnFailure.IsChecked == true ? CostFrequency.PerAttempt : CostFrequency.PerOrder, c.Enabled.IsChecked == true)).ToArray(), _estimateChargePresetId,
            ComplementaryCostsAreTotals: true, Subcontracting: ReadEstimateSubcontracting());
    }

    private void SetEstimateFrequencies(EstimateV2Options options)
    {
        CostFrequency[] values = [options.DesignFrequency, options.LaborFrequency, options.PostProcessingFrequency, options.ConsumablesFrequency, options.PackagingFrequency, options.MiscFrequency];
        _estimateFrequencies = values;
        UpdateEstimateCostLabels();
    }

    private void RestoreEstimateV2(EstimateInputs inputs)
    {
        var options = inputs.V2 ?? new(); var p = options.Production; var c = options.Charges ?? new();
        _estimateEngineVersion = inputs.EngineVersion == 2 ? 2 : 1;
        RestoreEstimateSubcontracting(options.Subcontracting);
        _estimatePricingMode = options.PricingMode; _estimateFeeKind = options.FeeKind;
        _estimateCapacity.Text = (p?.PiecesPerPlate ?? 0).ToString(); _estimateExtraGrams.Text = Exact(p?.ExtraWeightGrams ?? 0);
        _estimateFilamentPrice.Text = Exact(p?.MaterialPricePerKg ?? (EstimateFilament.SelectedItem as FilamentProfile)?.PricePerKg ?? 0);
        _estimateWeightIncludesSupports.IsChecked = p?.WeightIncludesSupports ?? true;
        _estimateMaintenance.Text = Exact(p?.MaintenanceHourlyRate ?? 0); _estimatePower.Text = p?.PowerWatts?.ToString() ?? "";
        _estimatePrinter.SelectedItem = (_estimatePrinter.ItemsSource as IEnumerable<PrinterChoice>)?.FirstOrDefault(choice => choice.Printer?.Id == p?.PrinterId);
        if (p?.PrinterId is { } missingId && _estimatePrinter.SelectedItem is null)
        {
            var snapshot = new PrinterProfile(missingId, "", "", p.PrinterName ?? "Machine enregistrée", p.MachineHourlyRate, "", false, DateTimeOffset.MinValue, DateTimeOffset.MinValue,
                PowerWatts: p.PowerWatts, UseManualHourlyRate: true, MaintenanceHourlyRate: p.MaintenanceHourlyRate);
            var choices = ((_estimatePrinter.ItemsSource as IEnumerable<PrinterChoice>) ?? []).Append(new(snapshot)).ToArray();
            _estimatePrinter.ItemsSource = choices; _estimatePrinter.SelectedItem = choices[^1];
        }
        SetEstimateCharges(c);
        _estimateGeneralFixedCost = options.GeneralFixedCost;
        _estimateChargePresetId = options.ChargePresetId;
        _estimateCommercial.Text = options.CommercialPrice is { } price ? Exact(price) : "";
        SetEstimateFrequencies(options);
        var runs = EstimateCalculatorV2.ProductionRuns(inputs.Quantity, inputs.PrintDataForWholeOrder, p?.PiecesPerPlate ?? 0);
        decimal Total(decimal amount, CostFrequency frequency) => inputs.EngineVersion != 2 || options.ComplementaryCostsAreTotals ? amount
            : amount * (frequency == CostFrequency.PerAttempt ? runs : frequency == CostFrequency.PerGoodPiece ? inputs.Quantity : 1);
        TextBox[] costFields = [EstimateDesign, EstimateLabor, EstimatePostProcessing, EstimateConsumables, EstimatePackaging, EstimateMisc];
        decimal[] amounts = [inputs.DesignCost, inputs.LaborCost, inputs.PostProcessingCost, inputs.ConsumablesCost, inputs.PackagingShippingCost, inputs.MiscCost];
        for (var i = 0; i < costFields.Length; i++) costFields[i].Text = Exact(Total(amounts[i], _estimateFrequencies[i]));
        _estimateAllocations.Children.Clear(); _estimateMachineEditors.Clear(); foreach (var a in p?.Allocations ?? []) AddEstimateMachine(a);
        _estimateExtraCosts.Children.Clear(); _estimateCostEditors.Clear(); foreach (var item in options.ExtraCosts ?? []) AddEstimateCost(item with { Amount = Total(item.Amount, item.Frequency) });
        UpdateEstimateVersion();
    }

    private void ResetEstimateV2()
    {
        RestoreEstimateV2(new(EngineVersion: 2));
        _estimateFilamentPrice.Text = Exact((EstimateFilament.SelectedItem as FilamentProfile)?.PricePerKg ?? 0);
        ApplyEstimatePrinter();
        ApplyWorkshopDefaults();
    }

    private void BuildEstimateProfitability(EstimateInputs input, EstimateResult result)
    {
        if (result.V2 is not { } p) return;
        EstimateRecap.Children.Clear();
        var details = new StackPanel { Spacing = 0 };
        void Row(string label, string value, bool strong = false)
            => details.Children.Add(EstimateResultRow(label, value, strong));
        void Group(string title) => details.Children.Add(new TextBlock { Text = title, Classes = { "estimate-group-title" } });
        void Divider() => details.Children.Add(new Border { Height = 1, Background = (IBrush?)Application.Current?.FindResource("BorderBrush"), Margin = new Thickness(0, 8) });
        void Money(string label, decimal amount, bool strong = false) => Row(label, EstimateMoney(amount), strong);
        Group("Production prévue");
        Row("Quantité conforme", input.Quantity + " pièces");
        if (input.V2?.Production is not null)
        {
            Row("Passages / plateaux", p.Runs.ToString()); Row("Matière totale avant échecs", p.TotalWeightGrams.ToString("0.##", EstimateCulture) + " g");
            Row("Occupation machine avant échecs", p.TotalMachineHours.ToString("0.##", EstimateCulture) + " h"); Row("Occupation attendue avec échecs", p.ExpectedMachineHours.ToString("0.##", EstimateCulture) + " h");
        }
        Group("Coût de revient");
        if (input.V2?.Subcontracting is { } subcontracting)
        {
            Money("Achat fournisseur", subcontracting.PurchaseCost); Money("Transport fournisseur", subcontracting.ShippingCost);
            Row("Fournisseur", string.IsNullOrWhiteSpace(subcontracting.Supplier) ? "Non renseigné" : subcontracting.Supplier);
        }
        if (input.V2?.Subcontracting is null)
        {
            Money("Coût matière", p.MaterialCost); Money("Coût machine (dont maintenance)", p.MachineCost); Money("Coût énergétique", p.ElectricityCost);
        }
        Money("Main-d’œuvre / conception / post-traitement", p.LaborCost); Money("Autres coûts", p.OtherCost);
        if (input.V2?.Subcontracting is null) Money("Provision pour échecs", p.FailureProvision);
        Divider();
        Money("Coût de revient total corrigé", result.ExpectedCost, true);
        if (result.FlatFee != 0) Money(input.V2?.FeeKind == PieceFeeKind.RealCost ? "Forfait réel déjà inclus dans les coûts" : "Supplément commercial inclus dans le conseillé", result.FlatFee);
        Group("Prix et rentabilité");
        details.Children.Add(EstimateHint(input.V2?.PricingMode == EstimatePricingMode.CostMarkup ? "V2 · Majoration sur coût" : "V2 · Marge cible sur le chiffre d’affaires"));
        Money("Prix plancher", p.FloorPrice, true); Money("Prix conseillé", p.RecommendedPrice, true); Money("Prix commercial final", result.FinalPrice, true);
        Divider();
        Money($"Charges proportionnelles ({p.ChargeRatePercent:0.##} %)", p.ProportionalCharges);
        Money("Résultat estimé après charges retenues", p.EstimatedResult, true); Row("Marge réelle sur chiffre d’affaires", p.ActualMarginPercent.ToString("0.00", EstimateCulture) + " %");
        Money("Prix de vente unitaire", p.UnitPrice); Money("Résultat estimé par pièce", p.UnitResult);
        if (p.BelowBreakEven)
        {
            var warning = EstimateHint("Prix inférieur au seuil de rentabilité : cette commande est potentiellement déficitaire.");
            warning.Foreground = (IBrush?)Application.Current?.FindResource("WarningBrush"); details.Children.Add(warning);
        }
        var note = EstimateHint("Coûts de production affichés avant provision pour échecs. Le résultat tient compte des charges activées ; il ne constitue pas un bénéfice net fiscal.");
        note.Margin = new Thickness(0, 18, 0, 0); details.Children.Add(note);
        if (input.DiscountPercent > 0)
        {
            var before = input.V2?.CommercialPrice ?? p.RecommendedPrice;
            Money("Prix avant remise", before);
            Row($"Remise ({input.DiscountPercent:0.##} %)", "− " + EstimateMoney(before - result.FinalPrice));
        }
        EstimateRecap.Children.Add(details);
    }
}
