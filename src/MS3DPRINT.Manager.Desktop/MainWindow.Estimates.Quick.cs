using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private readonly TextBox _estimateDurationHours = Number("2"), _estimateDurationMinutes = Number("0");
    private readonly TextBlock _estimateDurationError = new() { Text = "Saisissez des heures entières et des minutes inférieures à 60.", IsVisible = false, TextWrapping = TextWrapping.Wrap, Classes = { "estimate-input-error" } };
    private readonly TextBlock _estimateInputMessage = new() { IsVisible = false, TextWrapping = TextWrapping.Wrap, Classes = { "estimate-input-error" } };
    private readonly TextBlock _estimateBasisHint = EstimateHint("");
    private Expander _estimateAdjustments = null!;
    private Expander _estimateProductionOptions = null!;
    private ScrollViewer? _estimateHistoryScroll;
    private Control _estimateProductionHeading = null!;
    private TextBlock? _estimateWeightLabel, _estimateDurationLabel;
    private bool _syncingEstimateDuration, _estimateDurationInvalid, _estimateQuickReady;

    private void InitializeEstimateQuickForm()
    {
        // Existing named controls remain the single source of inputs and saved snapshots.
        EstimateStep1.Children.RemoveAt(0);
        EstimateStep0.Children.Remove(EstimateScope);
        foreach (var choice in EstimateScope.Children.OfType<Button>())
        {
            var caption = new TextBlock { Text = choice.Tag as string == "1" ? "Modélisation seule" : choice.Tag as string == "2" ? "Impression + modélisation" : "Impression 3D", FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
            row.Children.Add(Icons.Glyph(choice.Tag as string == "1" ? "IconEdit" : "IconCube", 18));
            Grid.SetColumn(caption, 1); row.Children.Add(caption); choice.Content = row;
            choice.Padding = new Thickness(10); choice.MinHeight = 50;
        }
        EstimateAddFilamentButton.Content = Icons.Glyph("IconPlus", 18);
        EstimateAddFilamentButton.Classes.Add("tool");
        Avalonia.Automation.AutomationProperties.SetName(EstimateAddFilamentButton, "Nouveau filament");
        ToolTip.SetTip(EstimateAddFilamentButton, "Ajouter un filament à l’atelier");

        var identity = new StackPanel { Spacing = 14 };
        foreach (var child in EstimateStep0.Children.ToArray())
        {
            EstimateStep0.Children.Remove(child);
            if (child is StackPanel) identity.Children.Add(child);
        }

        static Grid Pair(Control left, Control right, string columns = "*,*")
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 12 };
            Grid.SetColumn(left, 0); Grid.SetColumn(right, 1);
            grid.Children.Add(left); grid.Children.Add(right); return grid;
        }
        if (identity.Children.Count >= 2)
        {
            var fields = identity.Children.TakeLast(2).ToArray();
            foreach (var field in fields) identity.Children.Remove(field);
            identity.Children.Add(Pair(fields[0], fields[1]));
        }
        var filament = (Control)EstimateFilament.Parent!.Parent!;
        var quantity = (Control)EstimateQuantity.Parent!.Parent!;
        var basis = (Control)EstimatePrintDataMode.Parent!;
        foreach (var control in new[] { filament, EstimateV2MachineHost, quantity, basis }) EstimatePrintInputs.Children.Remove(control);
        EstimatePrintInputs.Children.RemoveAt(0); // introductory hint is replaced by the contextual basis below
        EstimatePrintInputs.Children.Insert(0, Pair(filament, EstimateV2MachineHost));
        EstimatePrintInputs.Children.Remove(EstimateV2BatchHost);
        var quantityRow = Pair(quantity, basis, "1*,1.5*,Auto");
        EstimateV2BatchHost.Width = 140; Grid.SetColumn(EstimateV2BatchHost, 2);
        quantityRow.Children.Add(EstimateV2BatchHost);
        foreach (var field in quantityRow.Children) field.VerticalAlignment = VerticalAlignment.Bottom;
        EstimatePrintInputs.Children.Insert(1, quantityRow);
        EstimatePrintInputs.Children.Insert(2, _estimateBasisHint);
        if (EstimatePrintDataMode.Items[0] is ComboBoxItem piece) piece.Content = "Pour une pièce";
        if (EstimatePrintDataMode.Items[1] is ComboBoxItem plate) plate.Content = "Pour un plateau";

        var durationField = (StackPanel)EstimateHours.Parent!.Parent!;
        durationField.Children.Clear();
        _estimateDurationLabel = new TextBlock { Text = "Durée d’impression", Classes = { "estimate-label" } };
        durationField.Children.Add(_estimateDurationLabel);
        var duration = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*,Auto"), ColumnSpacing = 6 };
        _estimateDurationHours.Classes.Add("estimate-number"); _estimateDurationMinutes.Classes.Add("estimate-number");
        Avalonia.Automation.AutomationProperties.SetName(_estimateDurationHours, "Durée d’impression en heures");
        Avalonia.Automation.AutomationProperties.SetName(_estimateDurationMinutes, "Minutes complémentaires");
        EstimateFieldHelp.Attach(_estimateDurationHours, "EstimateHours"); EstimateFieldHelp.Attach(_estimateDurationMinutes, "EstimateHours");
        duration.Children.Add(_estimateDurationHours);
        var hoursUnit = new TextBlock { Text = "h", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
        var minutesUnit = new TextBlock { Text = "min", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
        Grid.SetColumn(hoursUnit, 1); Grid.SetColumn(_estimateDurationMinutes, 2); Grid.SetColumn(minutesUnit, 3);
        duration.Children.Add(hoursUnit); duration.Children.Add(_estimateDurationMinutes); duration.Children.Add(minutesUnit);
        durationField.Children.Add(duration); durationField.Children.Add(_estimateDurationError);
        var weightAndDuration = (Control)durationField.Parent!;
        if (weightAndDuration is Grid weightRow && weightRow.Children[0] is StackPanel weightField)
            _estimateWeightLabel = weightField.Children.OfType<TextBlock>().FirstOrDefault();
        EstimatePrintInputs.Children.Remove(weightAndDuration);
        EstimatePrintInputs.Children.Insert(2, weightAndDuration);

        var technical = EstimatePrintInputs.Children.OfType<Expander>().Single();
        EstimatePrintInputs.Children.Remove(technical);
        EstimatePrintInputs.Children.Remove(EstimateV2ProductionHost);
        var fabrication = new StackPanel { Spacing = 22 };
        foreach (var oldSection in EstimateV2ProductionHost.Children.OfType<Expander>().ToArray())
        {
            var header = (Control)oldSection.Header!; var content = (Control)oldSection.Content!;
            oldSection.Header = null; oldSection.Content = null;
            EstimateV2ProductionHost.Children.Remove(oldSection);
            EstimateV2ProductionHost.Children.Add(new StackPanel { Spacing = 12, Children = { header, content } });
        }
        var technicalHeader = (Control)technical.Header!; var technicalContent = (Control)technical.Content!;
        technical.Header = null; technical.Content = null;
        fabrication.Children.Add(EstimateV2ProductionHost);
        fabrication.Children.Add(new StackPanel { Spacing = 12, Children = { technicalHeader, technicalContent } });
        _estimateProductionOptions = (Expander)EstimateSection("Options de fabrication", "Supports, plusieurs machines et détail énergétique", fabrication);

        EstimateStep2.Children.Remove(_estimateOptionalCosts); EstimateStep2.Children.Remove(_estimateOptionalCostSummary);
        EstimateStep2.Children.Remove(EstimateV2PricingHost);
        var adjustments = new StackPanel { Spacing = 16 };
        foreach (var child in EstimateStep2.Children.ToArray()) { EstimateStep2.Children.Remove(child); adjustments.Children.Add(child); }
        adjustments.Children.Add(EstimateV2PricingHost);
        _estimateAdjustments = (Expander)EstimateSection("Réglages du devis", "Cotisations et prestations particulières", adjustments);
        _estimateProductionHeading = EstimateZoneHeading("IconPrinters", "Données d’impression");
        EstimateStep1.Children.Clear();
        EstimateStep1.Spacing = 12;
        foreach (var control in new Control[] { _estimateInputMessage, identity, EstimateScope,
            _estimateProductionHeading, EstimatePrintInputs,
            _estimateOptionalCosts, _estimateOptionalCostSummary, _estimateProductionOptions, _estimateAdjustments }) EstimateStep1.Children.Add(control);
        DecorateEstimateSection(_estimateOptionalCosts, "IconEdit");
        DecorateEstimateSection(_estimateProductionOptions, "IconPrinters");
        DecorateEstimateSection(_estimateAdjustments, "IconProjects");
        EstimateSaveButton.Content = Icons.Label("IconCheck", "Enregistrer le devis");
        var priceDetails = (Expander)EstimateSection("Calcul et rentabilité détaillés", "Production, coûts et charges retenues", EstimateRecap);
        DecorateEstimateSection(priceDetails, "IconEstimates");
        EstimateStep1.Children.Add(priceDetails);
        // Commercial adjustments belong beside the amount they change.
        var discount = (Control)EstimateDiscount.Parent!.Parent!;
        adjustments.Children.Remove(discount);
        EstimateV2CommercialHost.Children.Insert(1, discount);
        if (EstimateV2CommercialHost.Children.LastOrDefault() is TextBlock commercialHint)
            commercialHint.Text = "Laissez le prix vide pour utiliser le conseillé. La remise s’applique ensuite au total.";
        if (_estimateOptionalCosts.Header is Grid costHeader && costHeader.Children[1] is StackPanel costLabels && costLabels.Children[0] is TextBlock costTitle)
            costTitle.Text = "Ajouter des frais";
        ToolTip.SetTip(EstimateSaveButton, "Enregistrer · Ctrl+Entrée");
        if (SavedEstimateList.Parent is StackPanel sidebar)
        {
            var index = sidebar.Children.IndexOf(SavedEstimateList);
            if (index > 0) sidebar.Children.RemoveAt(index - 1);
            sidebar.Children.Remove(SavedEstimateList);
            if (sidebar.Children.LastOrDefault() is Border separator && separator.Height == 1) sidebar.Children.Remove(separator);
            _estimateHistoryScroll = new ScrollViewer { Content = SavedEstimateList, MaxHeight = 220, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            Avalonia.Automation.AutomationProperties.SetName(_estimateHistorySearch, "Rechercher dans tous les devis enregistrés");
            _estimateHistorySearch.TextChanged += (_, _) => { _estimateHistoryLimit = 20; RenderEstimateHistory(); if (_estimateHistoryScroll is not null) _estimateHistoryScroll.Offset = default; };
            var historyBody = new StackPanel { Spacing = 12, Children = { _estimateHistorySearch, _estimateHistoryCount, _estimateHistoryScroll } };
            var history = (Expander)EstimateSection("Historique des estimations", "Retrouvez vos devis par nom, client ou projet", historyBody);
            history.Classes.Add("estimate-history");
            if (history.Header is StackPanel historyHeader && historyHeader.Children[0] is TextBlock title) title.FontSize = 18;
            var historySurface = new Border { Classes = { "estimate-history-surface" }, Child = history };
            EstimateLayout.RowDefinitions = new RowDefinitions("*,Auto");
            EstimateLayout.RowSpacing = 16;
            Grid.SetRow(historySurface, 1); Grid.SetColumnSpan(historySurface, 2);
            EstimateLayout.Children.Add(historySurface);
        }

        EstimateHours.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) ReadEstimateDuration(); };
        foreach (var field in new[] { _estimateDurationHours, _estimateDurationMinutes })
            field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) WriteEstimateDuration(); };
        EstimateProject.SelectionChanged += (_, _) => { ArmEstimateSave(); UpdateEstimateQuickContext(); };
        EstimateName.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) { ArmEstimateSave(); UpdateEstimateQuickContext(); } };
        EstimatesWorkspacePanel.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            SaveEstimate_Click(this, new RoutedEventArgs());
            e.Handled = true;
        };
        _estimateQuickReady = true;
        UpdateEstimateLayout();
        ReadEstimateDuration(); UpdateEstimateQuickContext();
    }

    private static Control EstimateZoneHeading(string icon, string title)
        => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 8, 0, 0), Classes = { "estimate-zone-title" },
            Children = { Icons.Glyph(icon, 18), new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center } } };

    private void DecorateEstimateSection(Expander section, string icon)
    {
        var heading = (Control)section.Header!; section.Header = null;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        row.Children.Add(Icons.Glyph(icon, 18)); Grid.SetColumn(heading, 1); row.Children.Add(heading); section.Header = row;
        section.PropertyChanged += (_, e) =>
        {
            if (e.Property != Expander.IsExpandedProperty || !section.IsExpanded || section.Content is not Control content) return;
            Dispatcher.UIThread.Post(() => { if (section.IsExpanded) BrandMotion.Enter(content, MotionReduced, 6); }, DispatcherPriority.Render);
        };
    }

    private void ReadEstimateDuration()
    {
        if (_syncingEstimateDuration || !TryEstimateValue(EstimateHours.Text, out var total) || total < 0) return;
        _syncingEstimateDuration = true;
        try
        {
            _estimateDurationHours.Text = Exact(decimal.Truncate(total));
            _estimateDurationMinutes.Text = Exact((total - decimal.Truncate(total)) * 60);
            _estimateDurationInvalid = false; _estimateDurationError.IsVisible = false;
        }
        finally { _syncingEstimateDuration = false; }
    }

    private void WriteEstimateDuration()
    {
        if (_syncingEstimateDuration) return;
        _syncingEstimateDuration = true;
        try
        {
            _estimateDurationInvalid = !TryEstimateValue(_estimateDurationHours.Text, out var hours) || hours < 0 || hours != decimal.Truncate(hours)
                || !TryEstimateValue(_estimateDurationMinutes.Text, out var minutes) || minutes < 0 || minutes >= 60;
            if (_estimateDurationInvalid) EstimateHours.Text = "—";
            else
            {
                TryEstimateValue(_estimateDurationHours.Text, out hours); TryEstimateValue(_estimateDurationMinutes.Text, out var validMinutes);
                try { EstimateHours.Text = Exact(hours + validMinutes / 60m); }
                catch (OverflowException) { _estimateDurationInvalid = true; EstimateHours.Text = "—"; }
            }
            _estimateDurationError.IsVisible = _estimateDurationInvalid;
        }
        finally { _syncingEstimateDuration = false; }
    }

    private void UpdateEstimateQuickContext()
    {
        if (!_estimateQuickReady) return;
        _estimateBasisHint.Text = EstimatePrintDataMode.SelectedIndex != 1 ? "Poids et durée pour une seule pièce : la quantité sera appliquée automatiquement."
            : int.TryParse(_estimateCapacity.Text, out var capacity) && capacity > 0 ? $"Poids et durée pour un plateau de {capacity} pièces : les plateaux nécessaires seront calculés."
            : "Poids et durée pour toute la commande : la quantité ne sera pas appliquée une deuxième fois.";
        var perPiece = EstimatePrintDataMode.SelectedIndex != 1;
        var wholeOrder = !perPiece && _estimateCapacity.Text?.Trim() == "0";
        if (_estimateWeightLabel is not null) _estimateWeightLabel.Text = perPiece ? "Poids d’une pièce" : wholeOrder ? "Poids de la commande" : "Poids du plateau";
        if (_estimateDurationLabel is not null) _estimateDurationLabel.Text = perPiece ? "Durée pour une pièce" : wholeOrder ? "Durée de la commande" : "Durée du plateau";
        if (EstimateScopeIndex == 1) _estimateOptionalCosts.IsExpanded = true;
        _estimateOutsource.IsVisible = IsEstimateV2 && EstimateScopeIndex != 1;
        _estimateSupplierInputs.IsVisible = IsEstimateOutsourced;
        EstimatePrintInputs.IsVisible = _estimateProductionHeading.IsVisible = _estimateProductionOptions.IsVisible = EstimateScopeIndex != 1 && !IsEstimateOutsourced;
        UpdateEstimateDesignContext();
    }

    private string SuggestedEstimateName()
    {
        if (_loadedEstimate is not null) return string.IsNullOrWhiteSpace(EstimateName.Text) ? _loadedEstimate.Name : EstimateName.Text.Trim();
        if (!string.IsNullOrWhiteSpace(EstimateName.Text) && EstimateName.Text != "Nouvelle estimation") return EstimateName.Text.Trim();
        if (EstimateScopeIndex == 1) return "Modélisation";
        if (IsEstimateOutsourced) return "Fabrication sous-traitée · " + EstimateQuantity.Text + " pièces";
        return (EstimateScopeIndex == 1 ? "Modélisation" : EstimateScopeIndex == 2 ? "Impression et modélisation" : "Impression 3D") + " · " + EstimateQuantity.Text + (EstimateQuantity.Text?.Trim() == "1" ? " pièce" : " pièces");
    }

    private bool RejectEstimateInput(string message)
    {
        EstimateDetail.Text = _estimateInputMessage.Text = message;
        _estimateInputMessage.IsVisible = true;
        return false;
    }
}
