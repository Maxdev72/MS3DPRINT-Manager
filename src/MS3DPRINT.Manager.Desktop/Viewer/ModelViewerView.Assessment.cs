using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MS3DPRINT.Estimator;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>The quick weight and time estimate and the feasibility check of the displayed model, both computed by the estimator engine.</summary>
public sealed partial class ModelViewerView
{
    private readonly StackPanel _feasibilityRows = new() { Spacing = 8 };
    private readonly TextBlock _feasibilityVerdict = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Text = "Ouvrez un modèle pour vérifier sa faisabilité." };
    private readonly TextBlock _feasibilityFigures = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button _feasibilityChip = new() { Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
    private readonly CheckBox _supports = new() { Content = "Avec supports", IsChecked = true };
    private readonly ComboBox _speedClass = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly DispatcherTimer _assessDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? _assessCancel;
    private EstimateResult? _quick;
    private double _quickDensity = 1;
    private FeasibilityReport? _feasibility;
    private double _assessScale = 1;
    private FeasibilityIssue? _highlighted;

    private void InitializeAssessment()
    {
        _assessDelay.Tick += async (_, _) => await RunAssessmentAsync();
        _supports.IsCheckedChanged += (_, _) => { ScheduleAssessment(); ScheduleSlice(); };
        _speedClass.SelectionChanged += (_, _) => ScheduleAssessment();
        _feasibilityChip.Click += (_, _) => ShowTab(3);
    }

    /// <summary>The feasibility section of the analysis tab.</summary>
    private Control[] FeasibilityControls() =>
    [
        FormKit.SectionLabel("Faisabilité"),
        new TextBlock { Text = "Vérifie l’étanchéité du maillage, les parois trop fines, les surplombs et la base posée sur le plateau. Calculé par le logiciel, sans découpeur.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
        _feasibilityVerdict, _feasibilityFigures, _feasibilityRows,
        FormKit.SectionLabel("Parois : mesure détaillée")
    ];

    /// <summary>Recomputes the estimate and the feasibility a moment after the last change; a new change cancels the running one.</summary>
    private void ScheduleAssessment()
    {
        if (_mesh is null) return;
        _assessDelay.Stop();
        _assessDelay.Start();
    }

    /// <summary>The estimate settings and machine for what the panel shows now; no settings when a field is not valid.</summary>
    private (EstimateSettings? Settings, MachineProfile Machine) CurrentEstimateInputs()
    {
        var layer = ModelAssessment.LayerHeightFrom(_slicerQuality.SelectedItem as string);
        var material = _material.SelectedItem as MaterialChoice;
        EstimateSettings? settings = null;
        if (material is not null && TryAmount(_infill.Text, out var infill) && infill <= 100 && TryAmount(_walls.Text, out var wallMm) && wallMm > 0)
            settings = new EstimateSettings
            {
                LayerHeightMm = layer, InfillPercent = infill, WallCount = Math.Min(20, ModelAssessment.WallLoopsFrom(wallMm)),
                DensityGcm3 = material.Density, Supports = _supports.IsChecked == true ? SupportMode.Auto : SupportMode.None,
                MaterialMaxFlowMm3S = material.MaxFlowMm3S ?? MeshVolume.MaxFlowFor(material.MaterialName)
            };
        var machine = new MachineProfile { HeadSpeedFactor = ModelAssessment.SpeedClasses[Math.Max(0, _speedClass.SelectedIndex)].Factor };
        return (settings, machine);
    }

    private async Task RunAssessmentAsync()
    {
        _assessDelay.Stop();
        var mesh = _mesh;
        if (mesh is null) return;
        var version = _loadVersion;
        _assessCancel?.Cancel();
        var cancellation = _assessCancel = new CancellationTokenSource();
        var scale = TryNumber(_scale.Text, out var s) ? s : 1;
        var layer = ModelAssessment.LayerHeightFrom(_slicerQuality.SelectedItem as string);
        var material = _material.SelectedItem as MaterialChoice;
        var (settings, machine) = CurrentEstimateInputs();
        var plate = _plate.ToVector3();
        var token = cancellation.Token;
        try
        {
            var (quick, feasibility) = await Task.Run(() =>
            {
                var print = ModelAssessment.ToPrintMesh(mesh, scale);
                var estimate = settings is null ? null : PrintEstimator.Estimate(print, settings, machine, token);
                return (estimate, FeasibilityAnalyzer.Analyze(print, new FeasibilitySettings { LayerHeightMm = layer, BuildVolumeMm = plate }, null, token));
            }, token);
            if (version != _loadVersion || token.IsCancellationRequested) return;
            _quick = quick;
            _quickDensity = material?.Density ?? 1;
            _feasibility = feasibility;
            _assessScale = scale;
            ShowFeasibility();
            RefreshVolume();
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException exception)
        {
            if (version != _loadVersion || token.IsCancellationRequested) return;
            _quick = null;
            _feasibility = null;
            ShowFeasibility();
            _feasibilityVerdict.Text = "Analyse indisponible : " + exception.Message;
            RefreshVolume();
        }
    }

    /// <summary>Forgets the results of the previous model.</summary>
    private void ClearAssessment()
    {
        _assessCancel?.Cancel();
        _quick = null;
        _feasibility = null;
        _viewport.Markers = null;
        ShowFeasibility();
    }

    private void ShowFeasibility()
    {
        _feasibilityRows.Children.Clear();
        if (_highlighted is not null)
        {
            _highlighted = null;
            _viewport.Markers = null;
            _viewport.RequestRender(false);
        }
        if (_feasibility is not { } report)
        {
            _feasibilityVerdict.Text = "Ouvrez un modèle pour vérifier sa faisabilité.";
            _feasibilityVerdict.Foreground = null;
            _feasibilityFigures.Text = string.Empty;
            _feasibilityChip.IsVisible = false;
            return;
        }

        var verdictBrush = VerdictBrush(report.Verdict);
        _feasibilityVerdict.Text = ModelAssessment.Describe(report.Verdict);
        _feasibilityVerdict.Foreground = verdictBrush;
        _feasibilityChip.Content = ModelAssessment.Summarize(report);
        _feasibilityChip.Foreground = verdictBrush;
        _feasibilityChip.IsVisible = true;

        var french = CultureInfo.GetCultureInfo("fr-FR");
        var figures = new List<string>();
        if (report.MinWallThicknessMm is { } wall) figures.Add($"Paroi la plus fine : {wall.ToString("0.##", french)} mm");
        if (report.ShellCount > 1) figures.Add($"{report.ShellCount} pièces séparées");
        _feasibilityFigures.Text = string.Join(" · ", figures);

        if (report.Issues.Count == 0)
        {
            _feasibilityRows.Children.Add(new TextBlock { Text = "Aucun problème détecté. Ce contrôle est géométrique : il ne remplace pas un essai dans le découpeur.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (var issue in report.Issues.OrderByDescending(issue => issue.Severity)) _feasibilityRows.Children.Add(IssueRow(issue));
    }

    private Control IssueRow(FeasibilityIssue issue)
    {
        var (glyph, brush) = issue.Severity switch
        {
            FeasibilitySeverity.Error => ("✕", Resource("DangerBrush", Brushes.IndianRed)),
            FeasibilitySeverity.Warning => ("▲", Resource("WarningBrush", Brushes.Orange)),
            _ => ("●", Resource("MutedTextBrush", Brushes.Gray))
        };
        var message = new TextBlock { Text = issue.Message, TextWrapping = TextWrapping.Wrap };
        var body = new StackPanel { Spacing = 4, Children = { message } };
        if (issue.Points is { Count: > 0 } points)
        {
            var show = new Button { Content = "Voir sur le modèle", Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
            show.Click += (_, _) =>
            {
                var turnOff = ReferenceEquals(_highlighted, issue);
                _highlighted = turnOff ? null : issue;
                // the engine works in millimetres, the view in the units of the model
                _viewport.Markers = turnOff ? null : points.Select(point => point / (float)_assessScale).ToArray();
                _viewport.RequestRender(false);
                show.Content = turnOff ? "Voir sur le modèle" : "Masquer";
            };
            body.Children.Add(show);
        }
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        row.Children.Add(new TextBlock { Text = glyph, Foreground = brush, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Top, Width = 14 });
        Grid.SetColumn(body, 1);
        row.Children.Add(body);
        return row;
    }

    private static IBrush VerdictBrush(FeasibilityVerdict verdict) => verdict switch
    {
        FeasibilityVerdict.Printable => Resource("SuccessBrush", Brushes.MediumSeaGreen),
        FeasibilityVerdict.PrintableWithWarnings => Resource("WarningBrush", Brushes.Orange),
        _ => Resource("DangerBrush", Brushes.IndianRed)
    };

    private static IBrush Resource(string key, IBrush fallback) => (IBrush?)Application.Current?.FindResource(key) ?? fallback;

    private static bool TryAmount(string? text, out double value) =>
        double.TryParse(text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && value >= 0;
}
