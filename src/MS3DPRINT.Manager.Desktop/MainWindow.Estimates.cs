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

/// <summary>Single-page estimates: live calculation, commercial adjustments and saved quotes.</summary>
public partial class MainWindow
{
    // The settings cache can change while editing another atelier in Settings.
    // Track the quote's origin separately so it cannot follow that cache into another workspace.
    private string? _estimateFormRoot;

    private async Task<bool> LoadEstimateFilamentsAsync()
    {
        var root = WorkspaceRoot;
        var load = StartWorkspaceLoad("estimate-inventory", root);
        var previousSuppression = _suppressEstimateChanges;
        var applying = false;
        var loaded = false;
        try
        {
            var (filaments, printers, projects) = await Task.Run(() =>
            {
                var inventory = new WorkspaceInventoryReader();
                return (inventory.LoadFilaments(root), inventory.LoadPrinters(root), new WorkspaceProjectReader().Load(root));
            });
            if (!IsWorkspaceLoadCurrent(load)) return false;
            var restoreDraft = _estimateFormRoot is null || !string.Equals(_estimateFormRoot, root, StringComparison.OrdinalIgnoreCase);
            if (restoreDraft)
            {
                if (!FlushEstimateDraft()) { Notify("L’estimateur conserve la saisie de l’ancien atelier : son brouillon n’a pas pu être sauvegardé.", error: true); return false; }
                BeginEstimateDraft();
            }
            applying = true;
            _suppressEstimateChanges = true;
            var workshopChanged = _estimateFormRoot is not null && !string.Equals(_estimateFormRoot, root, StringComparison.OrdinalIgnoreCase);
            LoadWorkshopDefaults(root);
            var untouched = _loadedEstimate is null && !_estimateDirty && _latestEstimateInputs is null;
            // The lists are reloaded on every visit: what is selected for the estimate in progress must survive it.
            var keptProject = (EstimateProject.SelectedItem as ProjectSummary)?.ProjectPath;
            var keptFilament = (EstimateFilament.SelectedItem as MS3DPRINT.Manager.Core.Filaments.FilamentProfile)?.Id;
            var editing = _loadedEstimate is not null;
            EstimateProject.ItemsSource = projects;
            EstimateProject.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ProjectSummary>((project, _) =>
            {
                var content = new StackPanel { Spacing = 3, Margin = new Avalonia.Thickness(0, 3), HorizontalAlignment = HorizontalAlignment.Stretch };
                if (project is null) return content;
                content.Children.Add(new TextBlock { Text = project.Title(), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
                content.Children.Add(new TextBlock { Text = $"{project.Reference} · {project.ClientTitle()}", FontSize = 13, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
                ToolTip.SetTip(content, $"{project.Title()}\n{project.Reference} · {project.ClientTitle()}");
                return content;
            });
            EstimateProject.SelectedItem = keptProject is null ? null : projects.FirstOrDefault(project => string.Equals(project.ProjectPath, keptProject, StringComparison.OrdinalIgnoreCase));
            EstimateFilament.ItemsSource = filaments;
            EstimateFilament.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<MS3DPRINT.Manager.Core.Filaments.FilamentProfile>((f, _) => FilamentChoiceVisual.Create(f));
            var keptFilamentItem = keptFilament is null ? null : filaments.FirstOrDefault(filament => filament.Id == keptFilament);
            // an estimate being edited never falls back to the first filament: its own choice stays, or none
            EstimateFilament.SelectedItem = keptFilamentItem ?? (editing ? null : filaments.FirstOrDefault());
            _fleetHourlyRate = PrinterCostCalculator.FleetHourlyRate(printers);
            _fleetMaintenance = PrinterCostCalculator.FleetMaintenanceHourlyRate(printers);
            _fleetPowerWatts = PrinterCostCalculator.FleetAveragePowerWatts(printers);
            PopulateEstimatePrinters(printers);
            if (workshopChanged)
            {
                ResetEstimate_Click(this, new RoutedEventArgs());
                _suppressEstimateChanges = true;
                Notify("Espace de travail changé : l’estimateur utilise les réglages du nouvel atelier. Les devis enregistrés restent dans leur espace d’origine.");
            }
            if (untouched)
            {
                ApplyWorkshopDefaults();
                ApplyEstimatePrinter();
                _estimateFilamentPrice.Text = Exact((EstimateFilament.SelectedItem as MS3DPRINT.Manager.Core.Filaments.FilamentProfile)?.PricePerKg ?? 0);
                RecalculateElectricity();
            }
            EstimateDetail.Text = filaments.Count == 0 ? "Aucun filament : ajoutez-en un avec le bouton +." : "Le prix s’actualise pendant votre saisie.";
            _estimateFormRoot = root;
            if (restoreDraft && new EstimateDraftStore(root).LoadReadable().FirstOrDefault() is { } draft) RestoreEstimateDraft(draft);
            loaded = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        {
            if (!IsWorkspaceLoadCurrent(load)) return false;
            EstimateFilament.ItemsSource = null;
            EstimateDetail.Text = "Les filaments ne peuvent pas être lus : " + exception.Message;
        }
        finally
        {
            if (applying)
            {
                _suppressEstimateChanges = previousSuppression;
                if (_loadedEstimate is null && _latestEstimateResult is null && !_estimateAwaitingNewInput) EstimateChanged(trackDraft: false);
            }
        }
        if (loaded && IsWorkspaceLoadCurrent(load)) await LoadSavedEstimatesAsync(root);
        return loaded && IsWorkspaceLoadCurrent(load);
    }

    private async void EstimateSearchFilament_Click(object? sender, RoutedEventArgs e)
    {
        var root = WorkspaceRoot; var navigation = CurrentNavigationStop?.Key;
        var picker = new FilamentPickerWindow((EstimateFilament.ItemsSource as IEnumerable<MS3DPRINT.Manager.Core.Filaments.FilamentProfile>)?.ToArray() ?? [], EstimateFilament.SelectedItem as MS3DPRINT.Manager.Core.Filaments.FilamentProfile);
        if (!await picker.ShowDialog<bool>(this) || picker.SelectedFilament is not { } selected || !MatchesNavigationContext(root, navigation)) return;
        EstimateFilament.SelectedItem = (EstimateFilament.ItemsSource as IEnumerable<MS3DPRINT.Manager.Core.Filaments.FilamentProfile>)?.FirstOrDefault(f => f.Id == selected.Id) ?? EstimateFilament.SelectedItem;
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
        if (EstimateElectricity is null || EstimateHours is null || EstimateKwhPrice is null || EstimateElectricityLabel is null || _updatingElectricity) return;
        _updatingElectricity = true;
        try
        {
            var multi = IsEstimateV2 && _estimateMachineEditors.Count > 0;
            int? power = _fleetPowerWatts;
            if (IsEstimateV2 && !string.IsNullOrWhiteSpace(_estimatePower.Text))
                power = int.TryParse(_estimatePower.Text, out var parsed) && parsed >= 0 ? parsed : null;
            else if (IsEstimateV2) power = null;
            var automatic = !multi && power is > 0;
            EstimateElectricity.IsReadOnly = automatic;
            EstimateElectricityLabel.Text = multi ? "Électricité manuelle des machines sans puissance (par passage ; 0 si toutes connues)" : automatic
                ? "Électricité par pièce / ensemble (calcul automatique)" : "Électricité par pièce / ensemble (puissance non renseignée)";
            if (!automatic || !TryEstimateValue(EstimateHours.Text, out var hours) || !TryEstimateValue(EstimateKwhPrice.Text, out var pricePerKwh) || hours < 0 || pricePerKwh < 0) return;
            EstimateElectricity.Text = Exact(hours * power!.Value / 1000m * pricePerKwh);
        }
        finally { _updatingElectricity = false; }
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
            if (value != 0 && _estimateOptionalCosts is not null) _estimateOptionalCosts.IsExpanded = true;
            UpdateEstimateQuickContext();
        }
    }

    private void EstimateScope_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var index)) return;
        EstimateScopeIndex = index;
        EstimateChanged();
        if (index == 1)
            EstimateDetail.Text = "Renseignez le coût de modélisation : le prix se met à jour automatiquement.";
    }

    private readonly TextBox _estimateHistorySearch = new() { PlaceholderText = "Rechercher un devis : nom, client ou projet…" };
    private readonly TextBlock _estimateHistoryCount = new() { Classes = { "muted" }, FontSize = 13 };
    private EstimateReadResult _estimateHistoryData = new([], []);
    private Control[] _estimateHistoryDraftRows = [];
    private string? _estimateHistoryRoot;
    private int _estimateHistoryLimit = 20;

    private async Task LoadSavedEstimatesAsync(string root)
    {
        if (!string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return;
        var load = StartWorkspaceLoad("estimate-history", root, trackNavigation: false);
        SavedEstimateList.Children.Clear();
        _estimateHistoryRoot = null;
        _estimateHistoryData = new([], []);
        _estimateHistoryCount.Text = "Chargement des devis…";
        try
        {
            AddEstimateDraftRows(root);
            var draftRows = SavedEstimateList.Children.ToArray();
            var read = await Task.Run(() => new EstimateStore(root).LoadWithDiagnostics());
            if (!IsWorkspaceLoadCurrent(load)) return;
            _estimateHistoryRoot = root;
            _estimateHistoryData = read;
            _estimateHistoryDraftRows = draftRows;
            _estimateHistoryLimit = 20;
            RenderEstimateHistory();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        {
            if (!IsWorkspaceLoadCurrent(load)) return;
            _estimateHistoryCount.Text = "Historique indisponible";
            SavedEstimateList.Children.Add(new TextBlock { Text = "Impossible de lire les estimations : " + exception.Message, Classes = { "muted" } });
        }
    }

    private void RenderEstimateHistory()
    {
        if (!string.Equals(_estimateHistoryRoot, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return;
        SavedEstimateList.Children.Clear();
        foreach (var row in _estimateHistoryDraftRows) SavedEstimateList.Children.Add(row);
        var query = _estimateHistorySearch.Text?.Trim() ?? string.Empty;
        var estimates = _estimateHistoryData.Estimates.Where(estimate => query.Length == 0
            || MatchesSettingText(estimate.Name, query) || MatchesSettingText(estimate.ClientName, query)
            || MatchesSettingText(estimate.ProjectReference, query)).ToArray();
        _estimateHistoryCount.Text = query.Length == 0
            ? $"{Math.Min(_estimateHistoryLimit, estimates.Length)} sur {estimates.Length} devis · du plus récent au plus ancien"
            : $"{estimates.Length} résultat(s) sur {_estimateHistoryData.Estimates.Count} devis";
        if (_estimateHistoryData.UnreadableFiles.Count > 0)
            {
                var warning = EstimateHint($"{_estimateHistoryData.UnreadableFiles.Count} estimation(s) illisible(s) : fichiers conservés, les autres devis restent disponibles.");
                ToolTip.SetTip(warning, string.Join(Environment.NewLine, _estimateHistoryData.UnreadableFiles));
                SavedEstimateList.Children.Add(warning);
            }
            if (estimates.Length == 0)
            {
                SavedEstimateList.Children.Add(new TextBlock { Text = query.Length == 0 ? "Aucune estimation enregistrée." : "Aucun devis ne correspond à cette recherche.", Classes = { "muted" } });
                return;
            }
            foreach (var estimate in estimates.Take(_estimateHistoryLimit))
            {
                var text = new Grid { ColumnDefinitions = new ColumnDefinitions("*,94,110,Auto"), ColumnSpacing = 16 };
                text.Children.Add(new TextBlock { Text = estimate.Name, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxLines = 2, VerticalAlignment = VerticalAlignment.Center });
                var date = new TextBlock { Text = estimate.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy", EstimateCulture), FontSize = 13, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
                var price = new TextBlock { Text = EstimateMoney(estimate.Result.FinalPrice), FontSize = 17, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                var resume = Icons.Label("IconArrowRight", "Reprendre", 16); resume.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(date, 1); Grid.SetColumn(price, 2); Grid.SetColumn(resume, 3);
                text.Children.Add(date); text.Children.Add(price); text.Children.Add(resume);
                var button = new Button { Content = text, Tag = estimate, Classes = { "estimate-history-row" }, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 56, Padding = new Avalonia.Thickness(0, 10) };
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
                var duplicate = new Button { Content = Icons.Glyph("IconCopy"), Tag = estimate, Classes = { "icon-action" } };
                ToolTip.SetTip(duplicate, "Dupliquer ce devis");
                Avalonia.Automation.AutomationProperties.SetName(duplicate, "Dupliquer " + estimate.Name);
                duplicate.Click += DuplicateEstimate_Click;
                row.ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"); Grid.SetColumn(duplicate, 1); Grid.SetColumn(trash, 2);
                row.Children.Add(duplicate);
                SavedEstimateList.Children.Add(row);
            }
        if (estimates.Length > _estimateHistoryLimit)
        {
            var more = new Button { Content = "Afficher plus de devis", HorizontalAlignment = HorizontalAlignment.Stretch };
            more.Click += (_, _) => { _estimateHistoryLimit += 20; RenderEstimateHistory(); };
            SavedEstimateList.Children.Add(more);
        }
    }

    private async void TrashEstimate_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EstimateProfile estimate }) return;
        if (!await new ConfirmTrashWindow(estimate.Name, "Cette estimation pourra être restaurée depuis la corbeille.").ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            await Task.Run(() => new EstimateStore(root).Trash(estimate.Id));
            if (!_estimateIsDuplicate && _loadedEstimate?.Id == estimate.Id) ResetEstimate_Click(sender, e);
            await LoadSavedEstimatesAsync(root);
            EstimateDetail.Text = "Estimation envoyée dans la corbeille.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { EstimateDetail.Text = exception.Message; }
    }

    private void LoadSavedEstimate_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EstimateProfile estimate }) return;
        TryLoadEstimate(estimate);
    }

    private bool TryLoadEstimate(EstimateProfile estimate)
    {
        if (!FlushEstimateDraft()) return false;
        BeginEstimateDraft(); _estimateIsDuplicate = false;
        _estimateTimer?.Stop();
        _suppressEstimateChanges = true;
        var inputs = estimate.Inputs;
        _loadedEstimate = estimate;
        EstimateName.Text = estimate.Name;
        EstimateScopeIndex = EstimateScopeRules.IsModellingOnly(estimate) ? 1 : 0;
        EstimateProject.SelectedItem = (EstimateProject.ItemsSource as IEnumerable<ProjectSummary>)?.FirstOrDefault(project => project.Reference == estimate.ProjectReference && project.ClientFolderName == estimate.ClientName);
        EstimateFilament.SelectedItem = (EstimateFilament.ItemsSource as IEnumerable<MS3DPRINT.Manager.Core.Filaments.FilamentProfile>)?.FirstOrDefault(filament => $"{filament.Brand} {filament.Name}" == estimate.FilamentName);
        EstimateWeight.Text = Exact(estimate.WeightGrams);
        EstimateHours.Text = Exact(estimate.MachineHours);
        EstimateMachineRate.Text = Exact(estimate.MachineHourlyRate);
        EstimateKwhPrice.Text = Exact(inputs.V2?.Production?.PricePerKwh ?? .25m);
        EstimateQuantity.Text = inputs.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        EstimateMinimum.Text = Exact(inputs.MinimumPerPiece);
        EstimatePrintDataMode.SelectedIndex = inputs.PrintDataForWholeOrder ? 1 : 0;
        EstimateElectricity.Text = (inputs.PrintDataForWholeOrder ? inputs.ElectricityCost : inputs.ElectricityCost / Math.Max(1, inputs.Quantity)).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateDesign.Text = Exact(inputs.DesignCost);
        EstimateLabor.Text = Exact(inputs.LaborCost);
        EstimatePostProcessing.Text = Exact(inputs.PostProcessingCost);
        EstimateConsumables.Text = Exact(inputs.ConsumablesCost);
        EstimatePackaging.Text = Exact(inputs.PackagingShippingCost);
        EstimateMisc.Text = Exact(inputs.MiscCost);
        EstimateMargin.Text = Exact(inputs.MarginPercent);
        EstimateFailure.Text = Exact(inputs.FailureRatePercent);
        EstimateDiscount.Text = Exact(inputs.DiscountPercent);
        RestoreEstimateV2(inputs);
        if (inputs.V2?.Production is { } production)
        {
            EstimateWeight.Text = Exact(production.WeightGrams);
            EstimateHours.Text = Exact(production.HoursPerRun);
            EstimateMachineRate.Text = Exact(production.MachineHourlyRate);
            EstimateElectricity.Text = Exact(production.ManualElectricityCost);
        }
        _latestEstimateInputs = inputs;
        _latestEstimateResult = estimate.Result;
        EstimateFinal.Text = EstimateMoney(estimate.Result.FinalPrice);
        UpdateEstimatePriceSummary(inputs, estimate.Result);
        EstimateDetail.Text = (inputs.EngineVersion == 1 ? "Moteur historique conservé. " : "Moteur V2. ") + "Devis chargé. Ajustez les valeurs puis enregistrez la mise à jour.";
        RecalculateElectricity();
        _estimateDirty = false;
        _suppressEstimateChanges = false;
        ArmEstimateSave(trackDraft: false);
        BuildEstimateRecap();
        EstimateFormScroll.Offset = default;
        return true;
    }

    private bool ResetEstimate_Click(object? sender, RoutedEventArgs e)
    {
        if (!FlushEstimateDraft()) return false;
        BeginEstimateDraft();
        _estimateIsDuplicate = false;
        _estimateAwaitingNewInput = false;
        _estimateTimer?.Stop();
        _suppressEstimateChanges = true;
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
        UpdateEstimatePriceSummary();
        EstimateDetail.Text = "Nouvelle estimation prête.";
        ResetEstimateV2();
        _estimateOptionalCosts.IsExpanded = false;
        _estimateAdjustments.IsExpanded = false;
        _estimateProductionOptions.IsExpanded = false;
        _estimateInputMessage.IsVisible = false;
        RecalculateElectricity();
        _estimateDirty = false;
        _suppressEstimateChanges = false;
        EstimateFormScroll.Offset = default;
        EstimateChanged(trackDraft: false);
        return true;
    }

    private bool _estimateSaving, _estimateAwaitingNewInput, _estimateIsDuplicate;

    private void ArmEstimateSave(bool trackDraft = true)
    {
        if (_suppressEstimateChanges) return;
        _estimateAwaitingNewInput = false;
        EstimateSaveButton.IsEnabled = !_estimateSaving;
        if (trackDraft) ScheduleEstimateDraft();
    }

    private async void SaveEstimate_Click(object? sender, RoutedEventArgs e)
    {
        if (_estimateSaving || _estimateAwaitingNewInput) return;
        if (_estimateFormRoot is not null && !string.Equals(_estimateFormRoot, WorkspaceRoot, StringComparison.OrdinalIgnoreCase))
        { Notify("Rechargez l’estimateur dans le bon espace de travail avant d’enregistrer.", error: true); return; }
        _estimateTimer?.Stop();
        if ((_estimateDirty || _latestEstimateResult is null) && !CalculateEstimate())
        {
            EstimateFormScroll.Offset = default;
            if (_estimateDurationInvalid) _estimateDurationHours.Focus();
            else if (!int.TryParse(EstimateQuantity.Text, out var quantity) || quantity < 1) EstimateQuantity.Focus();
            else if (EstimateFilament.SelectedItem is null && EstimateScopeIndex != 1 && !IsEstimateOutsourced) EstimateFilament.Focus();
            return;
        }
        if (_latestEstimateInputs is null || _latestEstimateResult is null)
        { EstimateDetail.Text = "Calculez l’estimation avant de l’enregistrer."; return; }
        _estimateSaving = true;
        EstimateSaveButton.IsEnabled = false;
        try
        {
            var root = WorkspaceRoot;
            TryEstimateValue(EstimateWeight.Text, out var savedWeight);
            TryEstimateValue(EstimateHours.Text, out var savedHours);
            TryEstimateValue(EstimateMachineRate.Text, out var savedRate);
            var project = EstimateProject.SelectedItem as ProjectSummary;
            var filament = EstimateFilament.SelectedItem as MS3DPRINT.Manager.Core.Filaments.FilamentProfile;
            var stored = EstimateScopeRules.ForSave(EstimateScopeIndex == 1, filament is null ? null : $"{filament.Brand} {filament.Name}", (IsEstimateV2 ? (_latestEstimateInputs.V2?.Production?.Allocations is { Count: > 0 } allocation ? string.Join(", ", allocation.Select(a => a.Name)) : _latestEstimateInputs.V2?.Production?.PrinterName ?? EstimateScopeRules.FleetLabel) : _loadedEstimate?.PrinterName ?? EstimateScopeRules.FleetLabel),
                savedWeight, savedHours, savedRate, _loadedEstimate);
            if (IsEstimateOutsourced) stored = ("Fabrication sous-traitée", string.IsNullOrWhiteSpace(_estimateSupplier.Text) ? "Fournisseur externe" : _estimateSupplier.Text.Trim(), 0m, 0m, 0m);
            var profile = new EstimateProfile(_estimateIsDuplicate ? Guid.NewGuid() : _loadedEstimate?.Id ?? Guid.NewGuid(), SuggestedEstimateName(), stored.Filament, stored.Printer, _latestEstimateInputs, _latestEstimateResult, _estimateIsDuplicate ? DateTimeOffset.UtcNow : _loadedEstimate?.CreatedAt ?? DateTimeOffset.UtcNow, stored.Grams, stored.Hours, stored.Rate,
                project?.Reference ?? _loadedEstimate?.ProjectReference, project?.ClientFolderName ?? _loadedEstimate?.ClientName);
            var store = new EstimateStore(root);
            if (_loadedEstimate is null || _estimateIsDuplicate) store.Create(profile);
            else profile = store.Update(profile with { Revision = _loadedEstimate.Revision });
            _loadedEstimate = profile;
            _estimateIsDuplicate = false;
            FlushEstimateDraft();
            try { ConsumeEstimateDraft(); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { Notify("Devis enregistré ; le brouillon n’a pas pu être retiré : " + cleanup.Message, error: true); }
            // the calculator starts over once saved: a saved estimate is reopened from the list, never edited by accident
            ResetEstimate_Click(sender, e);
            _estimateAwaitingNewInput = true;
            EstimateSaveButton.IsEnabled = false;
            EstimateDetail.Text = "Estimation enregistrée. Nouvelle estimation prête.";
            Notify("Estimation enregistrée. Vous pouvez préparer la suivante.");
            await LoadSavedEstimatesAsync(root);
        }
        catch (MS3DPRINT.Manager.Core.Workspace.ProfileConflictException exception)
        {
            FlushEstimateDraft();
            EstimateDetail.Text = exception.Message;
            Notify(exception.Message, error: true);
            await LoadSavedEstimatesAsync(WorkspaceRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { EstimateDetail.Text = exception.Message; Notify(exception.Message, error: true); }
        finally
        {
            _estimateSaving = false;
            EstimateSaveButton.IsEnabled = !_estimateAwaitingNewInput;
        }
    }

    private void BuildEstimateRecap()
    {
        EstimateRecap.Children.Clear();
        if (_latestEstimateInputs is not { } inputs || _latestEstimateResult is not { } result) return;
        if (result.V2 is not null) { BuildEstimateProfitability(inputs, result); return; }
        EstimateRecap.Children.Add(EstimateHint("Moteur historique : aléas sur tous les coûts, majoration sur coût, forfait commercial."));
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
        _estimateTimer?.Stop();
        EstimateRecap.Children.Clear();
        _latestEstimateInputs = null;
        _latestEstimateResult = null;
        EstimateFinal.Text = "—";
        UpdateEstimatePriceSummary();
        var modellingOnly = EstimateScopeIndex == 1;
        var internalPrinting = !modellingOnly && !IsEstimateOutsourced;
        _estimateInputMessage.IsVisible = false;
        if (internalPrinting && _estimateDurationInvalid)
            return RejectEstimateInput("Corrigez la durée : heures entières et minutes inférieures à 60.");
        var filament = EstimateFilament.SelectedItem as MS3DPRINT.Manager.Core.Filaments.FilamentProfile;
        if (internalPrinting && filament is null && !(IsEstimateV2 && _loadedEstimate?.Inputs.V2?.Production is not null))
        {
            return RejectEstimateInput("Choisissez un filament, ou ajoutez-en un avec « Nouveau filament ».");
        }

        if (!int.TryParse(modellingOnly ? "1" : EstimateQuantity.Text?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var quantity) || quantity < 1)
        {
            return RejectEstimateInput("La quantité de pièces doit être un nombre entier d’au moins 1.");
        }
        if (!TryEstimateValue(EstimateMinimum.Text, out var minimumPerPiece) || minimumPerPiece < 0)
        {
            return RejectEstimateInput("Le forfait minimum par pièce doit être un montant positif.");
        }
        if (!TryEstimateValue(internalPrinting ? EstimateWeight.Text : "0", out var grams) || !TryEstimateValue(internalPrinting ? EstimateHours.Text : "0", out var hours) ||
            !TryEstimateValue(IsEstimateOutsourced ? "0" : EstimateMachineRate.Text, out var rate) || !TryEstimateValue(IsEstimateOutsourced ? "0" : EstimateKwhPrice.Text, out var pricePerKwh) || !TryEstimateValue(IsEstimateOutsourced ? "0" : EstimateElectricity.Text, out var electricity) ||
            !TryEstimateValue(EstimateDesign.Text, out var design) || !TryEstimateValue(EstimateLabor.Text, out var labor) ||
            !TryEstimateValue(EstimatePostProcessing.Text, out var postProcessing) || !TryEstimateValue(EstimateConsumables.Text, out var consumables) ||
            !TryEstimateValue(EstimatePackaging.Text, out var packaging) || !TryEstimateValue(EstimateMisc.Text, out var misc) ||
            !TryEstimateValue(EstimateMargin.Text, out var margin) || !TryEstimateValue(EstimateFailure.Text, out var failure) || !TryEstimateValue(EstimateDiscount.Text, out var discount) ||
            grams < 0 || hours < 0 || rate < 0 || pricePerKwh < 0 || electricity < 0 || design < 0 || labor < 0 || postProcessing < 0 || consumables < 0 || packaging < 0 || misc < 0)
        {
            return RejectEstimateInput("Saisissez des montants positifs, avec une virgule ou un point.");
        }

        try
        {
            var printFactor = EstimatePrintDataMode.SelectedIndex == 1 ? 1 : quantity; // weight and time already cover every piece when entered for the whole order
            var machineCost = modellingOnly ? 0m : hours * rate * printFactor;
            var automaticElectricity = _fleetPowerWatts is > 0;
            // the field keeps what the user typed (per piece or for the whole order, like weight and time); the stored cost is always the total
            var electricityCost = modellingOnly ? 0m : automaticElectricity ? PrinterCostCalculator.ElectricityCost(_fleetPowerWatts, hours * printFactor, pricePerKwh) : electricity * printFactor;

            var inputs = new EstimateInputs(
                MaterialCost: modellingOnly || IsEstimateV2 ? 0m : filament!.PricePerKg * grams * printFactor / 1000m, MachineCost: IsEstimateV2 ? 0 : machineCost, ElectricityCost: IsEstimateV2 ? 0 : electricityCost,
                DesignCost: design, LaborCost: labor, PostProcessingCost: postProcessing, ConsumablesCost: consumables,
                PackagingShippingCost: packaging, MiscCost: misc, MarginPercent: margin, FailureRatePercent: failure, DiscountPercent: discount,
                Quantity: modellingOnly ? 1 : quantity, MinimumPerPiece: modellingOnly ? 0m : minimumPerPiece, PrintDataForWholeOrder: internalPrinting && EstimatePrintDataMode.SelectedIndex == 1,
                EngineVersion: IsEstimateV2 ? 2 : 1, V2: IsEstimateV2 ? ReadEstimateV2(modellingOnly, grams, hours, rate, pricePerKwh, electricity) : null);
            var result = EstimateCalculator.Calculate(inputs);
            _latestEstimateInputs = inputs;
            _latestEstimateResult = result;
            _estimateDirty = false;
            EstimateFinal.Text = EstimateMoney(result.FinalPrice);
            UpdateEstimatePriceSummary(inputs, result);
            EstimateDetail.Text = $"Coût brut : {result.RawCost:0.00} € · Avec aléas : {result.ExpectedCost:0.00} € · Avant remise : {result.SalePrice:0.00} €.";
            if (result.V2 is { } profitability)
                EstimateDetail.Text = $"Charges sur le prix final : {profitability.ChargeRatePercent:0.##} %.\nRésultat après les coûts et charges retenus, avant impôt personnel.";
            BuildEstimateRecap();
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            return RejectEstimateInput(exception is OverflowException ? "Les valeurs saisies sont trop élevées pour calculer ce devis." : exception.Message);
        }
    }

    private static bool TryEstimateValue(string? value, out decimal result) =>
        decimal.TryParse(value?.Replace(',', '.'), System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowLeadingWhite | System.Globalization.NumberStyles.AllowTrailingWhite, System.Globalization.CultureInfo.InvariantCulture, out result);
}
