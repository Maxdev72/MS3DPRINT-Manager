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
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += (_, _) =>
        {
            _viewModel.Refresh();
            PopulateFilters();
        };
    }
    public event EventHandler? CreateRequested;
    public event Action<ProjectSummary>? ProjectSelected;
    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Refresh();
        PopulateFilters();
    }
    private void Create_Click(object sender, RoutedEventArgs e) => CreateRequested?.Invoke(this, EventArgs.Empty);
    private void Status_SelectionChanged(object sender, SelectionChangedEventArgs e) => _viewModel.SelectedStatus = StatusFilterBox.SelectedIndex switch { 1 => ProjectStatus.Quote, 2 => ProjectStatus.InProgress, 3 => ProjectStatus.Completed, _ => null };
    private void Client_SelectionChanged(object sender, SelectionChangedEventArgs e) => _viewModel.SelectedClient = ClientFilterBox.SelectedIndex > 0 ? ClientFilterBox.SelectedItem as string : null;
    private void Year_SelectionChanged(object sender, SelectionChangedEventArgs e) => _viewModel.SelectedYear = YearFilterBox.SelectedIndex > 0 && YearFilterBox.SelectedItem is int year ? year : null;
    private void Project_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not ProjectSummary project) return;
        ProjectsList.SelectedItem = null;
        ProjectSelected?.Invoke(project);
    }

    private void PopulateFilters()
    {
        ClientFilterBox.ItemsSource = new[] { "Tous les clients" }.Concat(_viewModel.AvailableClients).ToArray();
        ClientFilterBox.SelectedIndex = 0;
        YearFilterBox.ItemsSource = new object[] { "Toutes années" }.Concat(_viewModel.AvailableYears.Cast<object>()).ToArray();
        YearFilterBox.SelectedIndex = 0;
    }
}
