using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MS3DPRINT.Manager.App.Controls;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.App.Views;

public partial class LegacyEntityDetailView : UserControl
{
    private readonly string _workspaceRoot;
    private readonly string _path;
    private readonly WorkspaceFilesView _files;
    private readonly ListView? _projects;
    private int _refreshVersion;
    public LegacyEntityDetailView(string workspaceRoot, string path, string title, bool client = false)
    {
        InitializeComponent();
        _workspaceRoot = workspaceRoot; _path = path;
        TitleText.Text = title; FolderPathText.Text = path;
        _files = new WorkspaceFilesView(workspaceRoot, path); FilesHost.Content = _files;
        if (client)
        {
            _projects = new ListView();
            var table = new GridView();
            table.Columns.Add(new() { Header = "Référence", Width = 170, DisplayMemberBinding = new Binding("Reference") });
            table.Columns.Add(new() { Header = "Projet", Width = 260, DisplayMemberBinding = new Binding("ProjectName") });
            table.Columns.Add(new() { Header = "Statut", Width = 150, DisplayMemberBinding = new Binding("StatusLabel") });
            _projects.View = table; CompactTable.Configure(_projects); CompactTable.BindOpen(_projects, OpenProject);
            var panel = new DockPanel { Margin = new Thickness(12) };
            var open = new Button { Content = "Ouvrir le projet", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
            open.SetResourceReference(StyleProperty, "SecondaryButton"); open.Click += (_, _) => OpenProject();
            DockPanel.SetDock(open, Dock.Top); panel.Children.Add(open); panel.Children.Add(_projects);
            DetailTabs.Items.Add(new TabItem { Header = "Projets", Content = panel });
            Loaded += async (_, _) => await RefreshAsync();
        }
    }
    public event EventHandler? BackRequested;
    public event EventHandler? CompleteRequested;
    public event EventHandler? TrashRequested;
    public event Action<ProjectSummary>? ProjectSelected;
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void Complete_Click(object sender, RoutedEventArgs e) => CompleteRequested?.Invoke(this, EventArgs.Empty);
    private void Trash_Click(object sender, RoutedEventArgs e) => TrashRequested?.Invoke(this, EventArgs.Empty);
    private void OpenProject() { if (_projects?.SelectedItem is ProjectSummary project) ProjectSelected?.Invoke(project); }
    public async Task RefreshAsync()
    {
        var version = ++_refreshVersion;
        await _files.RefreshAsync();
        if (_projects is null) return;
        try
        {
            var items = await Task.Run(() => new ProjectCatalog(new ProjectProfileStore(new WorkspaceMetadataPaths(_workspaceRoot))).Load(_workspaceRoot).Where(project => string.Equals(project.ClientPath, _path, StringComparison.OrdinalIgnoreCase)).ToArray());
            if (version == _refreshVersion) _projects.ItemsSource = items;
        }
        catch (Exception exception) { if (version == _refreshVersion) FolderPathText.Text = _path + " · " + UiErrorMessages.For(exception); }
    }
}
