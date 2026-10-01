using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.App.Views;

public partial class ClientDetailView : UserControl
{
    private readonly ClientDetailViewModel _viewModel;
    private int _projectsRefreshVersion;
    public ClientDetailView(ClientDetailViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += async (_, _) => await RefreshProjectsSafelyAsync();
    }

    public event EventHandler? BackRequested;
    public event EventHandler? CreateProjectRequested;
    public event Action<ProjectSummary>? ProjectSelected;
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void CreateProject_Click(object sender, RoutedEventArgs e) => CreateProjectRequested?.Invoke(this, EventArgs.Empty);
    private void Project_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not ProjectSummary project) return;
        ProjectsList.SelectedItem = null;
        ProjectSelected?.Invoke(project);
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { _viewModel.Save(); MessageText.Text = "Fiche enregistrée."; }
        catch (Exception exception) { MessageText.Text = UiErrorMessages.For(exception); }
    }

    public Task RefreshAsync() => RefreshProjectsSafelyAsync();

    private async Task RefreshProjectsSafelyAsync()
    {
        var refreshVersion = ++_projectsRefreshVersion;
        ProjectsLoadingText.Visibility = Visibility.Visible;
        ProjectsErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var projects = await Task.Run(_viewModel.LoadProjects);
            if (refreshVersion != _projectsRefreshVersion) return;
            _viewModel.ApplyProjects(projects);
        }
        catch (Exception exception)
        {
            if (refreshVersion != _projectsRefreshVersion) return;
            ProjectsErrorText.Text = UiErrorMessages.For(exception);
            ProjectsErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            if (refreshVersion == _projectsRefreshVersion) ProjectsLoadingText.Visibility = Visibility.Collapsed;
        }
    }
}
