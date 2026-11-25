using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>
/// Surveillance in its own window, to leave on a screen of the workshop while the software is used on another: the live
/// cards move into it (they keep their connections and cameras) and come back to the page when it closes.
/// </summary>
public partial class MainWindow
{
    private Window? _monitoringWindow;

    private readonly WindowPlacementStore _monitoringPlacement = new(Environment.GetEnvironmentVariable("MS3DPRINT_WINDOW_FILE") is { } file
        ? Path.Combine(Path.GetDirectoryName(file)!, "surveillance-window.json")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "MS3DPRINT Manager", "surveillance-window.json"));

    /// <summary>Cameras run while someone can see them: on the page or in the separate window.</summary>
    private bool CamerasWanted => MonitoringWorkspacePanel.IsVisible || _monitoringWindow is not null;

    private void DetachMonitoring_Click(object? sender, RoutedEventArgs e) => OpenMonitoringWindow();

    private void ReattachMonitoring_Click(object? sender, RoutedEventArgs e) => _monitoringWindow?.Close();

    private async void OpenMonitoringWindow()
    {
        if (_monitoringWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        // From the notification area the page may never have been opened: load the printers first.
        if (_liveCards.Count == 0) await LoadMonitoringAsync();
        if (_liveCards.Count == 0 || _monitoringWindow is not null)
        {
            if (_liveCards.Count == 0) Notify("Aucune imprimante surveillée : renseignez l’adresse IP, le numéro de série et le code d’accès d’une Bambu Lab dans Imprimantes.", error: true);
            return;
        }
        var page = (StackPanel)PrinterLivePanel.Parent!;
        var summaryIndex = page.Children.IndexOf(MonitoringSummary);
        page.Children.Remove(MonitoringSummary);
        page.Children.Remove(PrinterLivePanel);
        var saved = _monitoringPlacement.Load();
        var window = new Window
        {
            Title = "Surveillance · MS3DPRINT Manager", Icon = Icon, Width = saved?.Width ?? 1280, Height = saved?.Height ?? 800, MinWidth = 480, MinHeight = 360,
            Background = (Avalonia.Media.IBrush?)this.FindResource("CanvasBrush"),
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = new StackPanel { Margin = new Thickness(Ui.Space.S5), Children = { MonitoringSummary, PrinterLivePanel } }
            }
        };
        if (saved is { X: { } x, Y: { } y } && Screens.All.Any(screen => WindowPlacementStore.IsReachable(new PixelRect(x, y, (int)window.Width, (int)window.Height), [screen.WorkingArea])))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(x, y);
        }
        // F11 fills the screen (a TV or monitor of the workshop), Échap leaves full screen.
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.F11) window.WindowState = window.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
            else if (e.Key == Key.Escape && window.WindowState == WindowState.FullScreen) window.WindowState = WindowState.Normal;
            else return;
            e.Handled = true;
        };
        window.Opened += (_, _) => { if (saved?.Maximized == true) window.WindowState = WindowState.Maximized; };
        window.Closing += (_, _) =>
        {
            var normal = window.WindowState == WindowState.Normal;
            _monitoringPlacement.Save(new WindowPlacement(normal ? window.Width : saved?.Width ?? window.Width, normal ? window.Height : saved?.Height ?? window.Height,
                window.WindowState is WindowState.Maximized or WindowState.FullScreen, window.Position.X, window.Position.Y));
        };
        window.Closed += (_, _) =>
        {
            ((StackPanel)MonitoringSummary.Parent!).Children.Clear();
            page.Children.Insert(summaryIndex, MonitoringSummary);
            page.Children.Insert(summaryIndex + 1, PrinterLivePanel);
            _monitoringWindow = null;
            RefreshMonitoringDetached();
        };
        _monitoringWindow = window;
        RefreshMonitoringDetached();
        window.Show();
    }

    /// <summary>Page state while the window is open: a note with « Rappeler ici » instead of the cards; cameras follow.</summary>
    private void RefreshMonitoringDetached()
    {
        var detached = _monitoringWindow is not null;
        MonitoringDetachedNote.IsVisible = detached;
        MonitoringDetachButton.IsVisible = !detached && _liveCards.Count > 0;
        foreach (var live in _liveCards.Values) live.SetCameraActive(CamerasWanted);
    }
}
