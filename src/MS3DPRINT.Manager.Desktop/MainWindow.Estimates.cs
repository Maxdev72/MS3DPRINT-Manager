using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Printers;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Desktop.Dashboard;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Estimates page: wizard steps, calculation, recap and saved estimates.</summary>
public partial class MainWindow
{
    private async Task LoadEstimateFilamentsAsync()
    {
        try
        {
            var root = WorkspaceRoot;
            var (filaments, printers, projects) = await Task.Run(() =>
            {
                var inventory = new WorkspaceInventoryReader();
                return (inventory.LoadFilaments(root), inventory.LoadPrinters(root), new WorkspaceProjectReader().Load(root));
            });
            // The lists are reloaded on every visit: what is selected for the estimate in progress must survive it.
            var keptProject = (EstimateProject.SelectedItem as ProjectSummary)?.ProjectPath;
            var keptFilament = (EstimateFilament.SelectedItem as MS3DPRINT.Manager.Core.Filaments.FilamentProfile)?.Id;
            var editing = _loadedEstimate is not null;
            EstimateProject.ItemsSource = projects;
            EstimateProject.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ProjectSummary>((project, _) => new TextBlock { Text = project is null ? string.Empty : $"{project.Reference} · {project.ClientTitle()}" });
            EstimateProject.SelectedItem = keptProject is null ? null : projects.FirstOrDefault(project => string.Equals(project.ProjectPath, keptProject, StringComparison.OrdinalIgnoreCase));
            EstimateFilament.ItemsSource = filaments;
            EstimateFilament.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<MS3DPRINT.Manager.Core.Filaments.FilamentProfile>((f, _) => new TextBlock { Text = f is null ? string.Empty : $"{f?.Brand} · {f?.Name} ({f?.PricePerKg:0.00} €/kg)" });
            var keptFilamentItem = keptFilament is null ? null : filaments.FirstOrDefault(filament => filament.Id == keptFilament);
            // an estimate being edited never falls back to the first filament: its own choice stays, or none
            EstimateFilament.SelectedItem = keptFilamentItem ?? (editing ? null : filaments.FirstOrDefault());
            // the machine cost is the amortisation of the whole fleet: every active printer is added, none is chosen
            _fleetHourlyRate = PrinterCostCalculator.FleetHourlyRate(printers);
            _fleetPowerWatts = PrinterCostCalculator.FleetAveragePowerWatts(printers);
            if (!editing && _fleetHourlyRate > 0) EstimateMachineRate.Text = _fleetHourlyRate.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
            RecalculateElectricity();
            EstimateDetail.Text = filaments.Count == 0 ? "Aucun filament : ajoutez-en un à l’étape Impression." : "Renseignez vos paramètres puis calculez.";
            await LoadSavedEstimatesAsync(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        {
            EstimateFilament.ItemsSource = null;
            EstimateDetail.Text = "Les filaments ne peuvent pas être lus : " + exception.Message;
        }
    }

    private async void EstimateAddFilament_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new CreateFilamentWindow();
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            new WorkspaceFilamentEditor().Create(root, dialog.Brand, dialog.FilamentName, dialog.Material, dialog.Price, dialog.IsAbrasive, dialog.Color);
            var filaments = new WorkspaceInventoryReader().LoadFilaments(root);
            EstimateFilament.ItemsSource = filaments;
            EstimateFilament.SelectedItem = filaments.FirstOrDefault(f => f.Brand == dialog.Brand && f.Name == dialog.FilamentName) ?? filaments.FirstOrDefault();
            Notify("Filament ajouté et sélectionné.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void RefreshEstimates_Click(object? sender, RoutedEventArgs e) => await LoadEstimateFilamentsAsync();

    private decimal _fleetHourlyRate;
    private int? _fleetPowerWatts;

    private void EstimateElectricityInputs_Changed(object? sender, TextChangedEventArgs e) => RecalculateElectricity();

    /// <summary>Keeps the electricity cost in sync with machine time, printer power and kWh price.</summary>
    private void RecalculateElectricity()
    {
        // Raised while the XAML is still loading, before the named controls exist.
        if (EstimateElectricity is null || EstimateHours is null || EstimateKwhPrice is null || EstimateElectricityLabel is null) return;
        var automatic = _fleetPowerWatts is > 0;
        EstimateElectricity.IsReadOnly = automatic;
        EstimateElectricityLabel.Text = automatic ? "Électricité estimée (calculée automatiquement)" : "Électricité estimée (saisie manuelle : puissance des imprimantes non renseignée)";
        if (!automatic || !TryEstimateValue(EstimateHours.Text, out var hours) || !TryEstimateValue(EstimateKwhPrice.Text, out var pricePerKwh) || hours < 0 || pricePerKwh < 0) return;
        EstimateElectricity.Text = PrinterCostCalculator.ElectricityCost(_fleetPowerWatts, hours, pricePerKwh).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
    }

    private int _estimateScopeIndex;

    /// <summary>0 = impression, 1 = modélisation seule, 2 = impression + modélisation.</summary>
    private int EstimateScopeIndex
    {
        get => _estimateScopeIndex;
        set
        {
            _estimateScopeIndex = value;
            foreach (var choice in EstimateScope.Children.OfType<Button>())
            {
                if (choice.Tag as string == value.ToString()) choice.Classes.Add("selected"); else choice.Classes.Remove("selected");
            }
            EstimatePrintInputs.IsVisible = value != 1;
            UpdateEstimateStepper();
        }
    }

    private void EstimateScope_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var index)) return;
        EstimateScopeIndex = index;
        if (index == 1)
            EstimateDetail.Text = "Renseignez le coût de modélisation puis calculez.";
    }

    private async Task LoadSavedEstimatesAsync(string root)
    {
        SavedEstimateList.Children.Clear();
        try
        {
            var estimates = await Task.Run(() => new EstimateStore(root).LoadReadable());
            if (estimates.Count == 0)
            {
                SavedEstimateList.Children.Add(new TextBlock { Text = "Aucune estimation enregistrée.", Classes = { "muted" } });
                return;
            }
            foreach (var estimate in estimates.Take(5))
            {
                var text = new StackPanel { Spacing = 1 };
                text.Children.Add(new TextBlock { Text = estimate.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                text.Children.Add(new TextBlock { Text = $"{estimate.Result.FinalPrice:0.00} €", Classes = { "caption" } });
                var button = new Button { Content = text, Tag = estimate, Classes = { "workspace-card" }, MinHeight = 52, Padding = new Avalonia.Thickness(12, 8) };
                button.Click += LoadSavedEstimate_Click;
                var trash = new Button { Content = Icons.Glyph("IconTrash"), Tag = estimate, Classes = { "icon-action" } };
                ToolTip.SetTip(trash, "Envoyer à la corbeille");
                Avalonia.Automation.AutomationProperties.SetName(trash, $"Envoyer l’estimation {estimate.Name} à la corbeille");
                Avalonia.Automation.AutomationProperties.SetName(button, $"{estimate.Name}, {estimate.Result.FinalPrice:0.00} €");
                trash.Click += TrashEstimate_Click;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
                Grid.SetColumn(trash, 1);
                row.Children.Add(button);
                row.Children.Add(trash);
                SavedEstimateList.Children.Add(row);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { SavedEstimateList.Children.Add(new TextBlock { Text = "Impossible de lire les estimations : " + exception.Message, Classes = { "muted" } }); }
    }

    private async void TrashEstimate_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EstimateProfile estimate }) return;
        if (!await new ConfirmTrashWindow(estimate.Name, "Cette estimation pourra être restaurée depuis la corbeille.").ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            await Task.Run(() => new EstimateStore(root).Trash(estimate.Id));
            if (_loadedEstimate?.Id == estimate.Id) ResetEstimate_Click(sender, e);
            await LoadSavedEstimatesAsync(root);
            EstimateDetail.Text = "Estimation envoyée dans la corbeille.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { EstimateDetail.Text = exception.Message; }
    }

    private void LoadSavedEstimate_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EstimateProfile estimate }) return;
        var inputs = estimate.Inputs;
        _loadedEstimate = estimate;
        EstimateName.Text = estimate.Name;
        EstimateScopeIndex = EstimateScopeRules.IsModellingOnly(estimate) ? 1 : 0;
        EstimateProject.SelectedItem = (EstimateProject.ItemsSource as IEnumerable<ProjectSummary>)?.FirstOrDefault(project => project.Reference == estimate.ProjectReference && project.ClientFolderName == estimate.ClientName);
        EstimateFilament.SelectedItem = (EstimateFilament.ItemsSource as IEnumerable<MS3DPRINT.Manager.Core.Filaments.FilamentProfile>)?.FirstOrDefault(filament => $"{filament.Brand} {filament.Name}" == estimate.FilamentName);
        EstimateWeight.Text = estimate.WeightGrams.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateHours.Text = estimate.MachineHours.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateMachineRate.Text = estimate.MachineHourlyRate.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateKwhPrice.Text = "0,25";
        EstimateQuantity.Text = inputs.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        EstimateMinimum.Text = inputs.MinimumPerPiece.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimatePrintDataMode.SelectedIndex = inputs.PrintDataForWholeOrder ? 1 : 0;
        EstimateElectricity.Text = (inputs.PrintDataForWholeOrder ? inputs.ElectricityCost : inputs.ElectricityCost / Math.Max(1, inputs.Quantity)).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateDesign.Text = inputs.DesignCost.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateLabor.Text = inputs.LaborCost.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimatePostProcessing.Text = inputs.PostProcessingCost.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateConsumables.Text = inputs.ConsumablesCost.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimatePackaging.Text = inputs.PackagingShippingCost.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateMisc.Text = inputs.MiscCost.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateMargin.Text = inputs.MarginPercent.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateFailure.Text = inputs.FailureRatePercent.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateDiscount.Text = inputs.DiscountPercent.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        _latestEstimateInputs = inputs;
        _latestEstimateResult = estimate.Result;
        EstimateFinal.Text = estimate.Result.FinalPrice.ToString("0.00") + " €";
        EstimateDetail.Text = "Estimation chargée. Revenez sur une étape pour ajuster les valeurs, puis enregistrez la mise à jour.";
        ShowEstimateStep(3);
    }

    private void ResetEstimate_Click(object? sender, RoutedEventArgs e)
    {
        EstimateName.Text = "Nouvelle estimation";
        EstimateScopeIndex = 0;
        EstimateProject.SelectedItem = null;
        EstimateFilament.SelectedItem = (EstimateFilament.ItemsSource as IEnumerable<MS3DPRINT.Manager.Core.Filaments.FilamentProfile>)?.FirstOrDefault();
        EstimateWeight.Text = "100";
        EstimateHours.Text = "2";
        EstimateMachineRate.Text = "2";
        EstimateKwhPrice.Text = "0,25";
        EstimateQuantity.Text = "1";
        EstimateMinimum.Text = "5";
        EstimatePrintDataMode.SelectedIndex = 0;
        EstimateElectricity.Text = EstimateDesign.Text = EstimateLabor.Text = EstimatePostProcessing.Text = EstimateConsumables.Text = EstimatePackaging.Text = EstimateMisc.Text = EstimateDiscount.Text = "0";
        EstimateMargin.Text = "30";
        EstimateFailure.Text = "5";
        _latestEstimateInputs = null;
        _latestEstimateResult = null;
        _loadedEstimate = null;
        EstimateFinal.Text = "—";
        EstimateDetail.Text = "Nouvelle estimation prête.";
        // the fleet amortisation drives the hourly rate and the electricity cost, not the generic defaults above
        if (_fleetHourlyRate > 0)
            EstimateMachineRate.Text = _fleetHourlyRate.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        RecalculateElectricity();
        GoToEstimateStep(0);
    }

    private async void SaveEstimate_Click(object? sender, RoutedEventArgs e)
    {
        if (_latestEstimateInputs is null || _latestEstimateResult is null)
        { EstimateDetail.Text = "Calculez l’estimation avant de l’enregistrer."; return; }
        try
        {
            var root = WorkspaceRoot;
            TryEstimateValue(EstimateWeight.Text, out var savedWeight);
            TryEstimateValue(EstimateHours.Text, out var savedHours);
            TryEstimateValue(EstimateMachineRate.Text, out var savedRate);
            var project = EstimateProject.SelectedItem as ProjectSummary;
            var filament = EstimateFilament.SelectedItem as MS3DPRINT.Manager.Core.Filaments.FilamentProfile;
            var stored = EstimateScopeRules.ForSave(EstimateScopeIndex == 1, filament is null ? null : $"{filament.Brand} {filament.Name}", EstimateScopeRules.FleetLabel,
                savedWeight, savedHours, savedRate, _loadedEstimate);
            var profile = new EstimateProfile(_loadedEstimate?.Id ?? Guid.NewGuid(), string.IsNullOrWhiteSpace(EstimateName.Text) ? "Nouvelle estimation" : EstimateName.Text.Trim(), stored.Filament, stored.Printer, _latestEstimateInputs, _latestEstimateResult, _loadedEstimate?.CreatedAt ?? DateTimeOffset.UtcNow, stored.Grams, stored.Hours, stored.Rate,
                project?.Reference ?? _loadedEstimate?.ProjectReference, project?.ClientFolderName ?? _loadedEstimate?.ClientName);
            var store = new EstimateStore(root);
            if (_loadedEstimate is null) store.Create(profile);
            else store.Update(profile);
            // the calculator starts over once saved: a saved estimate is reopened from the list, never edited by accident
            ResetEstimate_Click(sender, e);
            EstimateDetail.Text = "Estimation enregistrée. Nouvelle estimation prête.";
            await LoadSavedEstimatesAsync(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { EstimateDetail.Text = exception.Message; }
    }

    private int _estimateStep;

    private void EstimateStep_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var index)) GoToEstimateStep(index);
    }

    private void EstimateBack_Click(object? sender, RoutedEventArgs e) =>
        GoToEstimateStep(_estimateStep == 2 && EstimateScopeIndex == 1 ? 0 : _estimateStep - 1);

    private void EstimateNext_Click(object? sender, RoutedEventArgs e) =>
        GoToEstimateStep(_estimateStep == 0 && EstimateScopeIndex == 1 ? 2 : _estimateStep + 1);

    /// <summary>Shows one wizard step; the recap step is only reachable once the inputs compute a valid price.</summary>
    private void GoToEstimateStep(int step)
    {
        step = Math.Clamp(step, 0, 3);
        if (step == 1 && EstimateScopeIndex == 1) step = _estimateStep > 1 ? 0 : 2;
        if (step > 1 && _estimateStep == 1 && EstimateScopeIndex != 1 && EstimateFilament.SelectedItem is null)
        {
            Notify("Choisissez un filament, ou ajoutez-en un avec « Nouveau filament ».", error: true);
            return;
        }
        if (step == 3 && !CalculateEstimate())
        {
            Notify(EstimateDetail.Text ?? "Vérifiez les valeurs saisies.", error: true);
            return;
        }
        ShowEstimateStep(step);
    }

    private void ShowEstimateStep(int step)
    {
        _estimateStep = step;
        EstimateStep0.IsVisible = step == 0;
        EstimateStep1.IsVisible = step == 1;
        EstimateStep2.IsVisible = step == 2;
        EstimateStep3.IsVisible = step == 3;
        EstimateBackButton.IsVisible = step > 0;
        EstimateNextButton.IsVisible = step < 3;
        EstimateSaveButton.IsVisible = step == 3;
        if (step == 3) BuildEstimateRecap();
        UpdateEstimateStepper();
    }

    private void UpdateEstimateStepper()
    {
        foreach (var chip in EstimateStepper.Children.OfType<Button>())
        {
            var index = int.Parse((string)chip.Tag!, System.Globalization.CultureInfo.InvariantCulture);
            chip.Classes.Set("active", index == _estimateStep);
            chip.Classes.Set("done", index < _estimateStep && !(index == 1 && EstimateScopeIndex == 1));
            chip.IsEnabled = !(index == 1 && EstimateScopeIndex == 1);
            if (chip.Content is StackPanel { Children: [Border dot, ..] })
            {
                if (dot.Child is TextBlock firstNumber)
                {
                    dot.Child = null; // detach first: a control cannot have two parents
                    dot.Child = new Panel { Children = { firstNumber, Icons.Glyph("IconCheck", 14) } };
                }
                if (dot.Child is Panel { Children: [TextBlock number, Control check] })
                {
                    var finished = index < _estimateStep && chip.IsEnabled;
                    number.Text = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    number.IsVisible = !finished;
                    check.IsVisible = finished;
                }
            }
        }
    }

    private void BuildEstimateRecap()
    {
        EstimateRecap.Children.Clear();
        if (_latestEstimateInputs is not { } inputs || _latestEstimateResult is not { } result) return;
        void Row(string label, string value, bool strong = false, bool negative = false)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var name = new TextBlock { Text = label };
            if (!strong) name.Classes.Add("muted");
            var amount = new TextBlock { Text = value, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal };
            if (strong) amount.FontSize = 18;
            Grid.SetColumn(amount, 1);
            row.Children.Add(name);
            row.Children.Add(amount);
            EstimateRecap.Children.Add(row);
        }
        void Cost(string label, decimal value) { if (value != 0) Row(label, value.ToString("0.00") + " €"); }
        void Separator() => EstimateRecap.Children.Add(new Border { Height = 1, Margin = new Avalonia.Thickness(0, 6), Background = (IBrush?)Avalonia.Application.Current?.FindResource("BorderBrush") });

        EstimateRecap.Children.Add(new TextBlock { Text = EstimateName.Text, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (inputs.Quantity > 1) Row("Quantité", inputs.Quantity + " pièces");
        Cost("Matière", inputs.MaterialCost);
        Cost("Machine", inputs.MachineCost);
        Cost("Électricité", inputs.ElectricityCost);
        Cost("Conception", inputs.DesignCost);
        Cost("Main-d’œuvre", inputs.LaborCost);
        Cost("Post-traitement", inputs.PostProcessingCost);
        Cost("Consommables", inputs.ConsumablesCost);
        Cost("Emballage / expédition", inputs.PackagingShippingCost);
        Cost("Autres coûts", inputs.MiscCost);
        Separator();
        Row("Coût brut", result.RawCost.ToString("0.00") + " €");
        if (result.ExpectedCost != result.RawCost) Row($"Aléas d’impression ({inputs.FailureRatePercent:0.##} %)", "+ " + (result.ExpectedCost - result.RawCost).ToString("0.00") + " €");
        var margin = result.SalePrice - result.FlatFee - result.ExpectedCost; // SalePrice already includes the flat fee
        if (margin != 0) Row($"Marge ({inputs.MarginPercent:0.##} %)", "+ " + margin.ToString("0.00") + " €");
        if (result.FlatFee > 0)
            Row($"Forfait par pièce ({inputs.Quantity} × {inputs.MinimumPerPiece:0.##} €)", "+ " + result.FlatFee.ToString("0.00") + " €");
        if (result.FinalPrice != result.SalePrice) Row($"Remise ({inputs.DiscountPercent:0.##} %)", "− " + (result.SalePrice - result.FinalPrice).ToString("0.00") + " €");
        Separator();
        Row("Prix de vente", result.FinalPrice.ToString("0.00") + " €", strong: true);
    }

    private void CalculateEstimate_Click(object? sender, RoutedEventArgs e) => CalculateEstimate();

    private bool CalculateEstimate()
    {
        var modellingOnly = EstimateScopeIndex == 1;
        var filament = EstimateFilament.SelectedItem as MS3DPRINT.Manager.Core.Filaments.FilamentProfile;
        if (!modellingOnly && filament is null)
        {
            EstimateDetail.Text = "Choisissez un filament, ou ajoutez-en un avec « Nouveau filament ».";
            return false;
        }

        if (!int.TryParse(EstimateQuantity.Text?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var quantity) || quantity < 1)
        {
            EstimateDetail.Text = "La quantité de pièces doit être un nombre entier d’au moins 1.";
            return false;
        }
        if (!TryEstimateValue(EstimateMinimum.Text, out var minimumPerPiece) || minimumPerPiece < 0)
        {
            EstimateDetail.Text = "Le forfait minimum par pièce doit être un montant positif.";
            return false;
        }
        if (!TryEstimateValue(EstimateWeight.Text, out var grams) || !TryEstimateValue(EstimateHours.Text, out var hours) ||
            !TryEstimateValue(EstimateMachineRate.Text, out var rate) || !TryEstimateValue(EstimateKwhPrice.Text, out var pricePerKwh) || !TryEstimateValue(EstimateElectricity.Text, out var electricity) ||
            !TryEstimateValue(EstimateDesign.Text, out var design) || !TryEstimateValue(EstimateLabor.Text, out var labor) ||
            !TryEstimateValue(EstimatePostProcessing.Text, out var postProcessing) || !TryEstimateValue(EstimateConsumables.Text, out var consumables) ||
            !TryEstimateValue(EstimatePackaging.Text, out var packaging) || !TryEstimateValue(EstimateMisc.Text, out var misc) ||
            !TryEstimateValue(EstimateMargin.Text, out var margin) || !TryEstimateValue(EstimateFailure.Text, out var failure) || !TryEstimateValue(EstimateDiscount.Text, out var discount) ||
            grams < 0 || hours < 0 || rate < 0 || pricePerKwh < 0 || electricity < 0 || design < 0 || labor < 0 || postProcessing < 0 || consumables < 0 || packaging < 0 || misc < 0)
        {
            EstimateDetail.Text = "Saisissez des montants positifs, avec une virgule ou un point.";
            return false;
        }

        try
        {
            var printFactor = EstimatePrintDataMode.SelectedIndex == 1 ? 1 : quantity; // weight and time already cover every piece when entered for the whole order
            var machineCost = modellingOnly ? 0m : hours * rate * printFactor;
            var automaticElectricity = _fleetPowerWatts is > 0;
            // the field keeps what the user typed (per piece or for the whole order, like weight and time); the stored cost is always the total
            var electricityCost = modellingOnly ? 0m : automaticElectricity ? PrinterCostCalculator.ElectricityCost(_fleetPowerWatts, hours * printFactor, pricePerKwh) : electricity * printFactor;
            if (automaticElectricity) EstimateElectricity.Text = electricityCost.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
            var inputs = new EstimateInputs(
                MaterialCost: modellingOnly ? 0m : filament!.PricePerKg * grams * printFactor / 1000m, MachineCost: machineCost, ElectricityCost: electricityCost,
                DesignCost: design, LaborCost: labor, PostProcessingCost: postProcessing, ConsumablesCost: consumables,
                PackagingShippingCost: packaging, MiscCost: misc, MarginPercent: margin, FailureRatePercent: failure, DiscountPercent: discount,
                Quantity: modellingOnly ? 1 : quantity, MinimumPerPiece: modellingOnly ? 0m : minimumPerPiece, PrintDataForWholeOrder: !modellingOnly && EstimatePrintDataMode.SelectedIndex == 1);
            var result = EstimateCalculator.Calculate(inputs);
            _latestEstimateInputs = inputs;
            _latestEstimateResult = result;
            EstimateFinal.Text = result.FinalPrice.ToString("0.00") + " €";
            EstimateDetail.Text = $"Coût brut : {result.RawCost:0.00} € · Avec aléas : {result.ExpectedCost:0.00} € · Avant remise : {result.SalePrice:0.00} €.";
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            EstimateDetail.Text = "Le taux d’échec doit être entre 0 et 100 % (100 exclu), la remise entre 0 et 100 % et la marge positive.";
            return false;
        }
    }

    private static bool TryEstimateValue(string? value, out decimal result) =>
        decimal.TryParse(value?.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out result);
}
