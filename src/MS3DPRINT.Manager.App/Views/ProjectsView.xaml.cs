using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.App.Views;

public partial class ProjectsView : UserControl
{
    private readonly ProjectsViewModel _viewModel;
    public ProjectsView(ProjectsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += (_, _) => _viewModel.Refresh();
    }
    public event EventHandler? CreateRequested;
    private void Refresh_Click(object sender, RoutedEventArgs e) => _viewModel.Refresh();
    private void Create_Click(object sender, RoutedEventArgs e) => CreateRequested?.Invoke(this, EventArgs.Empty);
    private void Status_SelectionChanged(object sender, SelectionChangedEventArgs e) => _viewModel.SelectedStatus = StatusFilterBox.SelectedIndex switch { 1 => ProjectStatus.Quote, 2 => ProjectStatus.InProgress, 3 => ProjectStatus.Completed, _ => null };
}
