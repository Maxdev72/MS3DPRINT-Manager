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

/// <summary>Integrated 3D viewer page and the hand-over to a new estimate.</summary>
public partial class MainWindow
{
    private void OpenThreeDViewer_Click(object? sender, RoutedEventArgs e) => ShowModelViewer(null);

    private ModelViewerView? _modelViewer;

    private List<Control> _hiddenByViewer = [];

    private string? _viewerReturnTitle;

    private string? _viewerReturnSubtitle;

    /// <summary>Shows the 3D viewer inside the main window, over the current page, and loads a model when a path is given.</summary>
    private async void ShowModelViewer(string? path)
    {
        if (!ViewerWorkspacePanel.IsVisible)
        {
            _hiddenByViewer = WorkspacePanels().Append(FolderWorkspacePanel).Where(panel => panel.IsVisible).ToList();
            foreach (var panel in _hiddenByViewer) panel.IsVisible = false;
            _viewerReturnTitle = PageTitle.Text;
            _viewerReturnSubtitle = PageSubtitle.Text;
            ViewerWorkspacePanel.IsVisible = true;
            _selectedNavigationButton?.Classes.Remove("selected");
            ViewerNavButton.Classes.Add("selected");
            RefreshCentralWidth();
        }
        if (_modelViewer is null)
        {
            _modelViewer = new ModelViewerView(CloseModelViewer, () =>
            {
                var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
                return new WorkspaceInventoryReader().LoadFilaments(root);
            }, () =>
            {
                var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
                return new WorkspaceInventoryReader().LoadPrinters(root).Where(printer => printer.IsActive).Select(printer => printer.DisplayName).ToList();
            });
            _modelViewer.ModelLoaded += name => PageSubtitle.Text = name;
            _modelViewer.EstimateRequested += StartEstimateFromModel;
            ViewerWorkspacePanel.Child = _modelViewer;
        }
        _ = _modelViewer.ReloadFilamentsAsync();
        PageTitle.Text = "Visualiseur 3D";
        PageSubtitle.Text = "Déposez un STL ou un OBJ, ou ouvrez un fichier.";
        if (path is null) return;
        try { await _modelViewer.LoadModelAsync(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { Notify("Impossible d’ouvrir ce modèle : " + exception.Message, error: true); }
    }

    /// <summary>Opens a new estimate prefilled from the model shown in the viewer (name, weight, filament, project).</summary>
    private async void StartEstimateFromModel(ModelEstimateSeed seed)
    {
        CloseModelViewer();
        ShowWorkspace("Estimations");
        PageTitle.Text = "Estimations";
        PageSubtitle.Text = PageSubtitles.GetValueOrDefault("Estimations", string.Empty);
        await LoadEstimateFilamentsAsync();
        ResetEstimate_Click(this, new RoutedEventArgs());
        EstimateName.Text = seed.Name;
        EstimateScopeIndex = 0;
        if (seed.WeightGrams is { } grams)
            EstimateWeight.Text = Math.Max(0.1, grams).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        if (seed.FilamentKey is { } key && EstimateFilament.ItemsSource is IEnumerable<MS3DPRINT.Manager.Core.Filaments.FilamentProfile> filaments)
            EstimateFilament.SelectedItem = filaments.FirstOrDefault(filament => $"{filament.Brand} {filament.Name}" == key) ?? EstimateFilament.SelectedItem;
        if (seed.PrinterModel is { } model && EstimatePrinter.ItemsSource is IEnumerable<PrinterProfile> printers)
            EstimatePrinter.SelectedItem = printers.FirstOrDefault(printer => string.Equals(printer.DisplayName, model, StringComparison.OrdinalIgnoreCase)) ?? EstimatePrinter.SelectedItem;
        if (seed.DurationHours is { } hours)
        {
            EstimateHours.Text = Math.Round(hours, 2).ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
            RecalculateElectricity();
        }
        if (EstimateProject.ItemsSource is IEnumerable<ProjectSummary> projects)
            EstimateProject.SelectedItem = projects.FirstOrDefault(project => seed.ModelPath.StartsWith(project.ProjectPath, StringComparison.OrdinalIgnoreCase));
        GoToEstimateStep(1);
        Notify(seed.DurationHours is null ? "Estimation préparée à partir du modèle : vérifiez la durée machine." : "Estimation préparée : poids et durée viennent du découpeur.");
    }

    private void CloseModelViewer()
    {
        _modelViewer?.Release();
        ViewerWorkspacePanel.IsVisible = false;
        ViewerNavButton.Classes.Remove("selected");
        _selectedNavigationButton?.Classes.Add("selected");
        RefreshCentralWidth();
        foreach (var panel in _hiddenByViewer) panel.IsVisible = true;
        _hiddenByViewer = [];
        PageTitle.Text = _viewerReturnTitle ?? "Vue d’ensemble";
        PageSubtitle.Text = _viewerReturnSubtitle ?? string.Empty;
    }
}
