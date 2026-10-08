using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.App.Views;

namespace MS3DPRINT.Manager.App.Controls;

public sealed class WorkspaceFilesView : UserControl
{
    private readonly string _root;
    private string _current;
    private int _refreshVersion;
    private readonly ProjectFileBrowser _browser = new();
    private readonly ListView _list = new() { MinHeight = 180 };
    private readonly TextBlock _path = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _up = new() { Content = "Remonter", Margin = new Thickness(0, 0, 10, 8), IsEnabled = false };
    public WorkspaceFilesView(string workspaceRoot, string folder)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)); _current = _root;
        var panel = new DockPanel(); var toolbar = new WrapPanel();
        var up = _up;
        up.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryButton");
        up.Click += async (_, _) => { if (_current != _root) { _current = _browser.GetParentDirectory(_root, _current); await RefreshAsync(); } };
        toolbar.Children.Add(up);
        _list.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        _list.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        _list.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        _path.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _message.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        FileManagement.ConfigureFileTable(_list);
        DockPanel.SetDock(_path, Dock.Top); DockPanel.SetDock(toolbar, Dock.Top); DockPanel.SetDock(_message, Dock.Bottom);
        panel.Children.Add(_path); panel.Children.Add(toolbar); panel.Children.Add(_message); panel.Children.Add(_list); Content = panel;
        Loaded += async (_, _) =>
        {
            FileManagement.Attach(toolbar, _list, workspaceRoot, () => _current, RefreshAsync, Open, Window.GetWindow(this));
            await RefreshAsync();
        };
    }
    public async Task RefreshAsync()
    {
        var version = ++_refreshVersion;
        var directory = _current;
        try
        {
            var entries = await Task.Run(() => _browser.List(_root, directory));
            if (version != _refreshVersion) return;
            _list.ItemsSource = entries;
            _path.Text = directory;
            _up.IsEnabled = !string.Equals(_current, _root, StringComparison.OrdinalIgnoreCase);
            _message.Text = "Double-cliquez pour ouvrir un élément.";
        }
        catch (Exception exception) { if (version == _refreshVersion) _message.Text = UiErrorMessages.For(exception); }
    }
    private async void Open(ProjectFileEntry entry)
    {
        try
        {
            if (entry.IsDirectory) { _current = entry.FullPath; await RefreshAsync(); }
            else if (ThreeDFileSupport.IsPreviewable(entry.FullPath)) ModelPreviewLauncher.Show(entry.FullPath, Window.GetWindow(this));
            else if (PreviewFileSupport.GetKind(entry.FullPath) != PreviewFileKind.None) new DocumentPreviewWindow(entry.FullPath) { Owner = Window.GetWindow(this) }.ShowDialog();
            else ExplorerService.Open(entry.FullPath);
        }
        catch (Exception exception) { _message.Text = UiErrorMessages.For(exception); }
    }
}
