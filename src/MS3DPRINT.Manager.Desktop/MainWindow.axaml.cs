using Avalonia;
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

public partial class MainWindow : Window
{
    private sealed record ProjectDirectoryContext(ProjectSummary? Project, string Directory, string Path);

    private readonly MS3DPRINT.Manager.Core.Storage.WorkspaceLocation _workspaceLocation = new();

    /// <summary>The workspace in use: the environment override, else the folder chosen in the settings, else the default.</summary>
    private string WorkspaceRoot => _workspaceLocation.Resolve();

    private CancellationTokenSource? _searchCancellation;

    private Button? _selectedNavigationButton;

    private EstimateInputs? _latestEstimateInputs;

    private EstimateResult? _latestEstimateResult;

    private EstimateProfile? _loadedEstimate;

    private string? _folderWorkspaceRoot;

    private string? _folderWorkspaceDirectory;

    private string? _folderWorkspaceReturnTitle;

    private string? _folderWorkspaceReturnSubtitle;

    private readonly WindowPlacementStore _placement = new(Environment.GetEnvironmentVariable("MS3DPRINT_WINDOW_FILE"));

    public MainWindow()
    {
        InitializeComponent();
        InitializeShellPresentation();
        InitializeFilamentFilters();
        InitializeEstimateV2();
        InitializeEstimateDrafts();
        InitializeNavigationHistory();
        MS3DPRINT.Estimator.Parallelism.Apply(new PerformanceSettings().Load());
        if (DesktopApplication.ReduceMotion) Classes.Add("reduce-motion");
        if (_placement.Load() is { } saved)
        {
            Width = Math.Max(MinWidth, saved.Width);
            Height = Math.Max(MinHeight, saved.Height);
            if (saved.X is { } x && saved.Y is { } y)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new PixelPoint(x, y);
                _normalPosition = Position;
                _positionRestored = true;
            }
            if (saved.Maximized) WindowState = WindowState.Maximized;
        }
        PositionChanged += (_, e) => { if (WindowState == WindowState.Normal) _normalPosition = e.Point; };
        Closing += (_, _) =>
            _placement.Save(new WindowPlacement(
                WindowState == WindowState.Normal ? Bounds.Width : Width,
                WindowState == WindowState.Normal ? Bounds.Height : Height,
                WindowState == WindowState.Maximized,
                (WindowState == WindowState.Normal ? Position : _normalPosition)?.X,
                (WindowState == WindowState.Normal ? Position : _normalPosition)?.Y));
        Closed += (_, _) => { CancelFileRead(); _searchCancellation?.Cancel(); _searchCancellation?.Dispose(); _searchCancellation = null; };
    }

    private PixelPoint? _normalPosition;
    private bool _positionRestored;

    /// <summary>Puts the window back on a visible monitor when its saved place is no longer reachable.</summary>
    private void EnsureOnScreen()
    {
        if (!_positionRestored || WindowState != WindowState.Normal || Screens is not { All.Count: > 0 } screens) return;
        var scale = RenderScaling;
        var size = new PixelSize((int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
        if (size.Width < 100) size = new PixelSize((int)(Width * scale), (int)(Height * scale));
        var areas = screens.All.Select(screen => screen.WorkingArea).ToList();
        if (WindowPlacementStore.IsReachable(new PixelRect(Position, size), areas)) return;
        Position = WindowPlacementStore.CentreIn((screens.Primary ?? screens.All[0]).WorkingArea, size);
    }

    private bool _preloaded;
    private string? _preloadedRoot;

    private string _collectionCategoryFilter = string.Empty;

    private async void Window_Opened(object? sender, EventArgs e)
    {
        EnsureOnScreen();
        UpdateCentralWorkspaceWidth(Bounds.Width);
        var shortcut = 1;
        // Ctrl+1 to Ctrl+9, then Ctrl+0 for the tenth page
        foreach (var navigation in NavigationButtons().Take(10))
            ToolTip.SetTip(navigation, $"{navigation.Tag} (Ctrl+{shortcut++ % 10})");
        if (_workspaceLocation.NeedsSetup) await RunWorkspaceSetupAsync();
        if (!await EnsureWorkspaceAvailableAsync()) return;
        await PreloadAsync();
        // MS3DPRINT_START_PAGE=Clients opens that page directly (screenshots, demos).
        if (Environment.GetEnvironmentVariable("MS3DPRINT_START_PAGE") is { Length: > 0 } page &&
            NavigationButtons().FirstOrDefault(button => button.Tag as string == page) is { } target)
            NavigationButton_Click(target, new RoutedEventArgs());
        // MS3DPRINT_START_PROJECT=<reference> opens that project (screenshots, demos).
        if (Environment.GetEnvironmentVariable("MS3DPRINT_START_PROJECT") is { Length: > 0 } reference)
        {
            var root = WorkspaceRoot;
            var projects = await Task.Run(() => new WorkspaceProjectReader().Load(root));
            if (projects.FirstOrDefault(project => project.Reference == reference) is { } found)
            {
                _projectOriginClient = null;
                ShowWorkspaceForDetail("Projets");
                PageTitle.Text = found.Title();
                PageSubtitle.Text = $"{found.Reference} · {found.ClientTitle()}";
                await LoadProjectDirectoryAsync(found, found.ProjectPath);
            }
        }
        // MS3DPRINT_START_CLIENT=<code> opens that client sheet (screenshots, demos).
        if (Environment.GetEnvironmentVariable("MS3DPRINT_START_CLIENT") is { Length: > 0 } clientCode)
        {
            var root = WorkspaceRoot;
            var clients = await Task.Run(() => new WorkspaceClientReader().Load(root));
            if (clients.FirstOrDefault(client => client.ClientCode == clientCode) is { } found)
            {
                ShowWorkspaceForDetail("Clients");
                PageTitle.Text = "Clients";
                await LoadClientDetailAsync(found);
            }
        }
        // MS3DPRINT_START_DIALOG=client|project|printer|filament|collection|template|setup|viewer|toast opens that dialog (screenshots, demos).
        switch (Environment.GetEnvironmentVariable("MS3DPRINT_START_DIALOG"))
        {
            case "client": CreateClient_Click(this, new RoutedEventArgs()); break;
            case "project": CreateProject_Click(this, new RoutedEventArgs()); break;
            case "printer": CreatePrinter_Click(this, new RoutedEventArgs()); break;
            case "filament": CreateFilament_Click(this, new RoutedEventArgs()); break;
            case "collection": CreateCollection_Click(this, new RoutedEventArgs()); break;
            case "template": EditProjectTemplate_Click(this, new RoutedEventArgs()); break;
            case "setup": _ = RunWorkspaceSetupAsync(); break;
            case "viewer": ShowModelViewer(Environment.GetEnvironmentVariable("MS3DPRINT_START_MODEL")); break;
#if DEBUG
            case "picker": _ = PickWorkspaceFolderAsync(WorkspaceRoot, "Déplacer « essai.stl »"); break;
#endif
            case "toast":
                Notify("Projet créé avec succès.");
                Notify("Impossible d’écrire dans ce dossier : accès refusé.", error: true);
                break;
        }
    }

    /// <summary>Reads the dashboard data once, so a splash screen can wait for it before showing the window.</summary>
    public async Task PreloadAsync(bool force = false)
    {
        var workspaceRoot = WorkspaceRoot;
        if (!Directory.Exists(workspaceRoot)) { _preloaded = false; return; }
        if (_preloaded && !force && _preloadedRoot == workspaceRoot) return;
        _preloaded = true;
        _preloadedRoot = workspaceRoot;
        var load = StartWorkspaceLoad("dashboard", workspaceRoot, trackNavigation: false);
        RefreshWorkspaceIdentity();

        try
        {
            var dashboard = await Task.Run(() => new WorkspaceDashboardReader().Load(workspaceRoot));
            if (!IsWorkspaceLoadCurrent(load)) return;
            ProjectCountText.Text = dashboard.ProjectCount.ToString();
            QuoteCountText.Text = dashboard.QuoteCount.ToString();
            InProgressCountText.Text = dashboard.InProgressCount.ToString();
            NavProjectsCount.Text = dashboard.ProjectCount.ToString();
            NavProjectsBadge.IsVisible = dashboard.ProjectCount > 0;
            NavClientsCount.Text = dashboard.ClientCount.ToString();
            NavClientsBadge.IsVisible = dashboard.ClientCount > 0;
            BuildWorkQueue(dashboard);
            BuildAttention(dashboard);
            await LoadRecentFilesAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!IsWorkspaceLoadCurrent(load)) return;
            Notify("Impossible de lire l’espace de travail.", error: true);
        }
    }

    private void Window_SizeChanged(object? sender, SizeChangedEventArgs e)
        => UpdateCentralWorkspaceWidth(e.NewSize.Width);

    private double _lastWindowWidth;

    /// <summary>The 3D viewer and the model search (a browser) use the whole width; every other page keeps its comfortable reading width.</summary>
    private void RefreshCentralWidth() => UpdateCentralWorkspaceWidth(_lastWindowWidth > 0 ? _lastWindowWidth : Width);

    private void UpdateCentralWorkspaceWidth(double windowWidth)
    {
        _lastWindowWidth = windowWidth;
        // Below 1100 px the sidebar shrinks to an icon rail so the content keeps its room.
        var compact = windowWidth < 1100;
        if (compact) Classes.Add("compact"); else Classes.Remove("compact");
        var sidebarWidth = compact ? 68d : 248d;
        RootGrid.ColumnDefinitions[0].Width = new GridLength(sidebarWidth);
        GlobalSearchContainer.Width = compact ? 260 : 300;
        // The model search is a browser: its page chrome is thinned so the web page gets the room.
        var browser = ModelSearchWorkspacePanel.IsVisible;
        CentralWorkspace.Margin = browser ? new Avalonia.Thickness(16, 12) : new Avalonia.Thickness(34, 28);
        CentralWorkspace.RowSpacing = browser ? 8 : 24;
        PageSubtitle.IsVisible = !browser;
        var centralMargins = browser ? 32d : 68d;
        var available = Math.Max(0, windowWidth - sidebarWidth - centralMargins);
        var wide = ViewerWorkspacePanel.IsVisible || browser;
        CentralWorkspace.MaxWidth = wide ? double.PositiveInfinity : 1400;
        CentralWorkspace.Width = wide ? available : Math.Min(1400, available);
        SearchResultsPanel.Width = Math.Min(compact ? 500 : 560, available);
        SearchResultsPanel.MaxHeight = Math.Max(180, Math.Min(520, (Bounds.Height > 0 ? Bounds.Height : Height) - 160));
        if (SearchResultsPanel.IsVisible) Dispatcher.UIThread.Post(PositionSearchResults, DispatcherPriority.Render);
    }

    /// <summary>Last line of defence: an unexpected error is shown and logged, and the work in progress stays open.</summary>
    public void ReportUnexpectedError(Exception exception)
    {
        ErrorLog.Write(exception);
        Notify("Une erreur inattendue s’est produite. Réessayez ; si elle persiste, le détail est dans erreurs.log. (" + exception.Message + ")", error: true);
        EndBusyAll();
    }

    private void EndBusyAll()
    {
        _busy = 0;
        BusyBar.IsVisible = false;
    }

    private int _busy;

    private void BeginBusy()
    {
        _busy++;
        BusyBar.IsVisible = true;
    }

    private void EndBusy()
    {
        if (--_busy <= 0) { _busy = 0; BusyBar.IsVisible = false; }
    }

    /// <summary>Short feedback bubble in the bottom-right corner; it fades away on its own.</summary>
    private void Notify(string message, bool error = false)
    {
        // an identical toast already on screen is not stacked a second time
        if (ToastHost.Children.OfType<Border>().Any(existing => existing.Tag as string == message)) return;
        var icon = new Avalonia.Controls.Shapes.Path { Classes = { "icon", error ? "icon-danger" : "icon-success" }, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new Avalonia.Thickness(0, 1, 0, 0) };
        if (Avalonia.Application.Current?.TryFindResource(error ? "IconAlert" : "IconCheck", out var geometry) == true && geometry is Geometry data) icon.Data = data;
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 };
        var close = new Button { Content = Icons.Glyph("IconClose", 14), Classes = { "icon-action" }, MinWidth = 32, MinHeight = 32, Padding = new Avalonia.Thickness(4, 0), VerticalAlignment = VerticalAlignment.Top };
        ToolTip.SetTip(close, "Fermer");
        Avalonia.Automation.AutomationProperties.SetName(close, "Fermer la notification");
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        Grid.SetColumn(text, 1); Grid.SetColumn(close, 2);
        layout.Children.Add(icon); layout.Children.Add(text); layout.Children.Add(close);
        var toast = new Border { Classes = { "toast", error ? "error" : "success" }, Child = layout, Tag = message };
        void Dismiss() => ToastHost.Children.Remove(toast);
        close.Click += (_, _) => Dismiss();
        ToastHost.Children.Add(toast);
        BrandMotion.Enter(toast, MotionReduced, 18);
        while (ToastHost.Children.Count > 4) ToastHost.Children.RemoveAt(0);
        DispatcherTimer.RunOnce(Dismiss, TimeSpan.FromSeconds(error ? 8 : 4.5));
    }

    private IEnumerable<Button> NavigationButtons()
        => NavigationItems.Children.OfType<Button>().Concat(FooterNavigation.Children.OfType<Button>()).Where(button => button.Tag is string);

    private static readonly Dictionary<string, string> PageSubtitles = new()
    {
        ["Accueil"] = "Bienvenue dans votre atelier. Voici ce qui mérite votre attention.",
        ["Projets"] = "Suivez vos devis, vos fabrications et vos livraisons.",
        ["Clients"] = "Particuliers et professionnels, avec leurs projets.",
        ["Collections"] = "Modèles 3D, produits et fournisseurs.",
        ["Filaments"] = "Votre stock de matières et leurs coûts.",
        ["Estimations"] = "Calculez un prix de vente à partir de vos coûts réels.",
        ["Imprimantes"] = "Votre parc machines et leurs coûts horaires.",
        ["Modèles 3D"] = "Cherchez un modèle sur plusieurs sites à la fois, dans des onglets.",
        ["Corbeille"] = "Retrouvez et restaurez ce que vous avez supprimé.",
        ["Paramètres"] = "Stockage, arborescence et journal des modifications."
    };
}
