using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private Guid _estimateDraftId = Guid.NewGuid();
    private bool _estimateDraftTouched, _restoringEstimateDraft;
    private long _estimateDraftRevision;
    private bool _estimateDraftAutoSaving, _estimateDraftClosed;
    private EstimateDraftWriter _estimateDraftWriter = new();
    private readonly DispatcherTimer _estimateDraftTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly TextBlock _estimateDraftStatus = EstimateHint("");

    private Dictionary<string, TextBox> DraftFields() => new()
    {
        ["Name"] = EstimateName, ["Quantity"] = EstimateQuantity, ["Weight"] = EstimateWeight, ["Hours"] = EstimateHours,
        ["DurationHours"] = _estimateDurationHours, ["Minutes"] = _estimateDurationMinutes,
        ["Rate"] = EstimateMachineRate, ["Kwh"] = EstimateKwhPrice, ["Electricity"] = EstimateElectricity,
        ["Design"] = EstimateDesign, ["Labor"] = EstimateLabor, ["Post"] = EstimatePostProcessing, ["Consumables"] = EstimateConsumables,
        ["Packaging"] = EstimatePackaging, ["Misc"] = EstimateMisc, ["Margin"] = EstimateMargin, ["Failure"] = EstimateFailure,
        ["Discount"] = EstimateDiscount, ["Fee"] = EstimateMinimum, ["Capacity"] = _estimateCapacity, ["ExtraGrams"] = _estimateExtraGrams,
        ["MaterialPrice"] = _estimateFilamentPrice, ["Maintenance"] = _estimateMaintenance, ["Power"] = _estimatePower,
        ["SalesShare"] = _estimateSalesShare, ["BicShare"] = _estimateBicShare, ["Commercial"] = _estimateCommercial,
        ["Supplier"] = _estimateSupplier, ["Purchase"] = _estimateSupplierCost, ["Transport"] = _estimateSupplierShipping
    };

    private void InitializeEstimateDrafts()
    {
        _estimateDraftStatus.FontSize = 13;
        _estimateDraftStatus.IsVisible = false;
        EstimateStep1.Children.Insert(0, _estimateDraftStatus);
        _estimateDraftTimer.Tick += async (_, _) => await AutoSaveEstimateDraftAsync();
        Closing += (_, e) =>
        {
            if (FlushEstimateDraft()) return;
            e.Cancel = true;
            Notify("Fermeture interrompue : le brouillon n’a pas pu être sauvegardé. Vos saisies restent dans l’estimateur.", error: true);
        };
        Closed += (_, _) => { _estimateDraftClosed = true; _estimateDraftTimer.Stop(); };
    }

    private void ScheduleEstimateDraft()
    {
        if (_suppressEstimateChanges || _restoringEstimateDraft || _estimateFormRoot is null) return;
        _estimateDraftTouched = true;
        _estimateDraftRevision++;
        _estimateDraftStatus.IsVisible = true;
        _estimateDraftStatus.Text = "Brouillon · sauvegarde en cours…";
        _estimateDraftTimer.Stop(); _estimateDraftTimer.Start();
    }

    private bool FlushEstimateDraft()
    {
        _estimateDraftTimer.Stop();
        if (!_estimateDraftTouched || _estimateFormRoot is null || _restoringEstimateDraft) return true;
        if (_estimateDraftWriter.IsSaved(_estimateFormRoot, _estimateDraftId, _estimateDraftRevision)) return true;
        try
        {
            _estimateDraftWriter.Save(_estimateFormRoot, CaptureEstimateDraft(), _estimateDraftRevision);
            _estimateDraftStatus.Text = "Brouillon sauvegardé automatiquement · " + DateTime.Now.ToString("HH:mm");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { ShowEstimateDraftError(e); return false; }
    }

    private EstimateDraft CaptureEstimateDraft()
    {
            var frequencies = _estimateFrequencies;
            var options = new EstimateV2Options(Charges: _estimateQuoteCharges, PricingMode: _estimatePricingMode, FeeKind: _estimateFeeKind,
                GeneralFixedCost: _estimateGeneralFixedCost, DesignFrequency: frequencies[0], LaborFrequency: frequencies[1],
                PostProcessingFrequency: frequencies[2], ConsumablesFrequency: frequencies[3], PackagingFrequency: frequencies[4], MiscFrequency: frequencies[5], ChargePresetId: _estimateChargePresetId, QuantityTiers: _estimateQuantityTiers);
            var project = EstimateProject.SelectedItem as ProjectSummary;
            return new EstimateDraft(_estimateDraftId, DateTimeOffset.UtcNow, DraftFields().ToDictionary(pair => pair.Key, pair => pair.Value.Text),
                _estimateEngineVersion, EstimateScopeIndex, EstimatePrintDataMode.SelectedIndex, EstimateFilament.SelectedItem as FilamentProfile,
                (_estimatePrinter.SelectedItem as PrinterChoice)?.Printer, project?.Reference, project?.ClientFolderName, options,
                _estimateWeightIncludesSupports.IsChecked == true, _estimateOutsource.IsChecked == true,
                _estimateMachineEditors.Select(m => new DraftMachine(m.Name.Text, m.Hours.Text, m.Rate.Text, m.Maintenance.Text, m.Power.Text, m.PrinterId)).ToArray(),
                _estimateCostEditors.Select(c => new DraftCost(c.Name.Text, c.Amount.Text, c.RepeatOnFailure.IsChecked == true, c.Enabled.IsChecked == true)).ToArray(),
                _loadedEstimate, _latestEstimateInputs, _latestEstimateResult, _estimateChargeCategory.SelectedIndex, _estimateIsDuplicate);
    }

    private void ShowEstimateDraftError(Exception error)
        => _estimateDraftStatus.Text = "Brouillon non sauvegardé : " + error.Message + " Vos saisies sont conservées dans cette fenêtre.";

    private async Task AutoSaveEstimateDraftAsync()
    {
        _estimateDraftTimer.Stop();
        if (_estimateDraftAutoSaving || !_estimateDraftTouched || _restoringEstimateDraft || _estimateFormRoot is not { } root) return;
        var id = _estimateDraftId;
        var revision = _estimateDraftRevision;
        if (_estimateDraftWriter.IsSaved(root, id, revision)) return;
        var draft = CaptureEstimateDraft(); // UI values are copied before entering the worker.
        _estimateDraftAutoSaving = true;
        try
        {
            await Task.Run(() => _estimateDraftWriter.Save(root, draft, revision));
            if (!_estimateDraftClosed && _estimateDraftId == id && _estimateFormRoot == root && _estimateDraftRevision == revision)
                _estimateDraftStatus.Text = "Brouillon sauvegardé automatiquement · " + DateTime.Now.ToString("HH:mm");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        {
            if (!_estimateDraftClosed && _estimateDraftId == id && _estimateFormRoot == root && _estimateDraftRevision == revision
                && !_estimateDraftWriter.IsSaved(root, id, revision)) ShowEstimateDraftError(error);
        }
        finally
        {
            _estimateDraftAutoSaving = false;
            if (!_estimateDraftClosed && _estimateDraftTouched && _estimateFormRoot is { } currentRoot
                && (_estimateDraftId != id || currentRoot != root || _estimateDraftRevision != revision)
                && !_estimateDraftWriter.IsSaved(currentRoot, _estimateDraftId, _estimateDraftRevision)) _estimateDraftTimer.Start();
        }
    }

    private void BeginEstimateDraft()
    {
        _estimateDraftTimer.Stop(); _estimateDraftId = Guid.NewGuid(); _estimateDraftTouched = false;
        _estimateDraftStatus.Text = "";
        _estimateDraftStatus.IsVisible = false;
    }

    private void ConsumeEstimateDraft()
    {
        _estimateDraftTimer.Stop();
        if (_estimateFormRoot is not null) _estimateDraftWriter.Remove(_estimateFormRoot, _estimateDraftId);
        BeginEstimateDraft();
    }

    private void RestoreEstimateDraft(EstimateDraft draft)
    {
        if (!FlushEstimateDraft()) return;
        var previousSuppression = _suppressEstimateChanges;
        _restoringEstimateDraft = _suppressEstimateChanges = true;
        try
        {
            _estimateTimer?.Stop();
            EstimateScopeIndex = draft.Scope;
            EstimateFilament.SelectedItem = (EstimateFilament.ItemsSource as IEnumerable<FilamentProfile>)?.FirstOrDefault(f => f.Id == draft.Filament?.Id);
            if (draft.Filament is { } filament && EstimateFilament.SelectedItem is null)
            { EstimateFilament.ItemsSource = ((EstimateFilament.ItemsSource as IEnumerable<FilamentProfile>) ?? []).Append(filament).ToArray(); EstimateFilament.SelectedItem = filament; }
            RestoreEstimateV2(new(EngineVersion: draft.EngineVersion, V2: draft.Options));
            _loadedEstimate = draft.Editing;
            _estimateIsDuplicate = draft.IsDuplicate || draft.Editing is { } editing && !new EstimateStore(_estimateFormRoot!).LoadReadable().Any(q => q.Id == editing.Id);
            _estimateChargeCategory.SelectedIndex = Math.Clamp(draft.ChargeCategory, 0, 3);
            EstimateProject.SelectedItem = (EstimateProject.ItemsSource as IEnumerable<ProjectSummary>)?.FirstOrDefault(p => p.Reference == draft.ProjectReference && p.ClientFolderName == draft.ClientName);
            EstimatePrintDataMode.SelectedIndex = draft.DataMode;
            var choices = (_estimatePrinter.ItemsSource as IEnumerable<PrinterChoice>)?.ToArray() ?? [];
            if (draft.Printer is { } printer && !choices.Any(c => c.Printer?.Id == printer.Id))
            { choices = [.. choices, new(printer)]; _estimatePrinter.ItemsSource = choices; }
            _estimatePrinter.SelectedItem = choices.FirstOrDefault(c => c.Printer?.Id == draft.Printer?.Id);
            _estimateWeightIncludesSupports.IsChecked = draft.IncludesSupports; _estimateOutsource.IsChecked = draft.Outsourced;
            _estimateMachineEditors.Clear(); _estimateAllocations.Children.Clear();
            foreach (var machine in draft.Machines)
            {
                AddEstimateMachine(new(machine.Name ?? "Machine", 0, 0, PrinterId: machine.PrinterId));
                var m = _estimateMachineEditors[^1];
                m.Name.Text = machine.Name; m.Hours.Text = machine.Hours; m.Rate.Text = machine.Rate; m.Maintenance.Text = machine.Maintenance; m.Power.Text = machine.Power;
            }
            _estimateCostEditors.Clear(); _estimateExtraCosts.Children.Clear();
            foreach (var cost in draft.Costs)
            {
                AddEstimateCost(new(cost.Name ?? "Poste", 0, CostFrequency.PerOrder)); var c = _estimateCostEditors[^1];
                c.Name.Text = cost.Name; c.Amount.Text = cost.Amount; c.RepeatOnFailure.IsChecked = cost.Repeated; c.Enabled.IsChecked = cost.Enabled;
            }
            var fields = DraftFields();
            foreach (var (key, value) in draft.Fields)
                if (key is not ("DurationHours" or "Minutes") && fields.TryGetValue(key, out var control)) control.Text = value;
            // Duration helpers deliberately retain text which cannot be parsed yet.
            _syncingEstimateDuration = true;
            try { _estimateDurationHours.Text = draft.Fields.GetValueOrDefault("DurationHours"); _estimateDurationMinutes.Text = draft.Fields.GetValueOrDefault("Minutes"); }
            finally { _syncingEstimateDuration = false; }
            WriteEstimateDuration();
            RecalculateElectricity();
            _estimateDraftId = draft.Id; _estimateDraftTouched = true;
            _estimateDraftRevision++;
            _latestEstimateInputs = draft.Inputs; _latestEstimateResult = draft.Result;
            _estimateDirty = draft.Result is null;
            if (draft.Result is { } result) { EstimateFinal.Text = EstimateMoney(result.FinalPrice); UpdateEstimatePriceSummary(draft.Inputs, result); BuildEstimateRecap(); }
            else { EstimateFinal.Text = "—"; UpdateEstimatePriceSummary(); EstimateRecap.Children.Clear(); }
            _estimateAwaitingNewInput = false; EstimateSaveButton.IsEnabled = true;
            UpdateEstimateVersion();
            _estimateDraftStatus.Text = "Brouillon récupéré · " + draft.UpdatedAt.ToLocalTime().ToString("dd/MM à HH:mm");
            _estimateDraftStatus.IsVisible = true;
        }
        finally { _suppressEstimateChanges = previousSuppression; _restoringEstimateDraft = false; }
        if (_estimateDirty) { _estimateTimer?.Stop(); _estimateTimer?.Start(); }
    }

    private async void NewEstimate_Click(object? sender, RoutedEventArgs e)
    {
        if (_estimateDraftTouched && !await ConfirmRemovalWindow.AskAsync(this, "Brouillon en cours", "Le brouillon sera abandonné. Les devis enregistrés sont conservés.")) return;
        try { ConsumeEstimateDraft(); ResetEstimate_Click(sender, e); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { Notify(error.Message, error: true); }
    }

    private void DuplicateEstimate_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EstimateProfile source }) return;
        if (!TryLoadEstimate(source)) return;
        _estimateIsDuplicate = true;
        EstimateName.Text = source.Name + " — copie";
        ScheduleEstimateDraft();
        Notify("Copie préparée. Ajustez-la puis enregistrez le nouveau devis.");
    }

    private void AddEstimateDraftRows(string root)
    {
        var drafts = new EstimateDraftStore(root).LoadReadable();
        if (drafts.Count == 0) return;
        SavedEstimateList.Children.Add(new TextBlock { Text = "Brouillons à reprendre", Classes = { "card-title" } });
        foreach (var draft in drafts)
        {
            var name = draft.Fields.GetValueOrDefault("Name") ?? "Brouillon sans nom";
            var label = new StackPanel { Spacing = 3, Children =
            {
                new TextBlock { Text = name, FontSize = 15, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxLines = 2, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis },
                new TextBlock { Text = "Brouillon · " + draft.UpdatedAt.ToLocalTime().ToString("dd/MM HH:mm"), FontSize = 13, Classes = { "muted" } }
            } };
            var open = new Button { Content = label, Classes = { "estimate-history-row" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Padding = new Avalonia.Thickness(0, 10) };
            Avalonia.Automation.AutomationProperties.SetName(open, "Reprendre le brouillon " + name);
            open.Click += (_, _) =>
            {
                if (_estimateDraftId != draft.Id || !_estimateDraftTouched)
                {
                    var fresh = new EstimateDraftStore(root).LoadReadable().FirstOrDefault(d => d.Id == draft.Id);
                    if (fresh is null) { Notify("Ce brouillon n’est plus disponible.", error: true); return; }
                    RestoreEstimateDraft(fresh);
                }
                EstimateFormScroll.Offset = default;
            };
            var remove = new Button { Content = Icons.Glyph("IconTrash"), Classes = { "icon-action" } };
            Avalonia.Automation.AutomationProperties.SetName(remove, "Supprimer le brouillon " + name); ToolTip.SetTip(remove, "Supprimer le brouillon");
            remove.Click += async (_, _) =>
            {
                if (!await ConfirmRemovalWindow.AskAsync(this, name, "Ce brouillon sera supprimé définitivement. Les devis enregistrés sont conservés.")) return;
                try
                {
                    _estimateDraftWriter.Remove(root, draft.Id);
                    if (_estimateDraftId == draft.Id) { BeginEstimateDraft(); ResetEstimate_Click(this, new RoutedEventArgs()); }
                    if (string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) await LoadSavedEstimatesAsync(root);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { Notify(e.Message, error: true); }
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            Grid.SetColumn(remove, 1); row.Children.Add(open); row.Children.Add(remove); SavedEstimateList.Children.Add(row);
        }
        SavedEstimateList.Children.Add(new TextBlock { Text = "Devis enregistrés", Classes = { "card-title" }, Margin = new Avalonia.Thickness(0, 16, 0, 0) });
    }
}
