using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.Controls;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.App.Views;

public partial class ProjectsView : UserControl
{
    private readonly ProjectsViewModel _viewModel;
    private int _refreshVersion;
    public ProjectsView(ProjectsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        CompactTable.Configure(ProjectsList);
        CompactTable.BindOpen(ProjectsList, OpenSelection);
        UpdateActions();
        DataContext = _viewModel;
        Loaded += async (_, _) => await RefreshSafelyAsync();
    }
    public event EventHandler? CreateRequested;
    public event Action<ProjectSummary>? ProjectSelected;
    public event Action<ProjectSummary>? EditRequested;
    public event Action<ProjectSummary>? RenameRequested;
    public event Action<ProjectSummary>? MoveRequested;
    public event Action<ProjectSummary>? TrashRequested;
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshSafelyAsync();
    private void Create_Click(object sender, RoutedEventArgs e) => CreateRequested?.Invoke(this, EventArgs.Empty);
    private void Status_SelectionChanged(object sender, SelectionChangedEventArgs e) => _viewModel.SelectedStatus = StatusFilterBox.SelectedIndex switch { 1 => ProjectStatus.Quote, 2 => ProjectStatus.InProgress, 3 => ProjectStatus.Completed, _ => null };
    private void Client_SelectionChanged(object sender, SelectionChangedEventArgs e) => _viewModel.SelectedClient = ClientFilterBox.SelectedIndex > 0 ? ClientFilterBox.SelectedItem as string : null;
    private void Year_SelectionChanged(object sender, SelectionChangedEventArgs e) => _viewModel.SelectedYear = YearFilterBox.SelectedIndex > 0 && YearFilterBox.SelectedItem is int year ? year : null;
    private void Project_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActions();
    private void OpenSelection() { if (ProjectsList.SelectedItem is ProjectSummary row) ProjectSelected?.Invoke(row); }
    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelection();
    private void Edit_Click(object sender, RoutedEventArgs e) { if (ProjectsList.SelectedItem is ProjectSummary row) EditRequested?.Invoke(row); }
    private void Rename_Click(object sender, RoutedEventArgs e) { if (ProjectsList.SelectedItem is ProjectSummary row && row.Profile is not null) RenameRequested?.Invoke(row); }
    private void Move_Click(object sender, RoutedEventArgs e) { if (ProjectsList.SelectedItem is ProjectSummary row && row.Profile is not null) MoveRequested?.Invoke(row); }
    private void Trash_Click(object sender, RoutedEventArgs e) { if (ProjectsList.SelectedItem is ProjectSummary row) TrashRequested?.Invoke(row); }
    private void UpdateActions()
    {
        if (OpenButton is null) return;
        var row = ProjectsList.SelectedItem as ProjectSummary;
        OpenButton.IsEnabled = EditButton.IsEnabled = TrashButton.IsEnabled = row is not null;
        EditButton.Content = row is null ? "Modifier / Compléter" : row.Profile is null ? "Compléter" : "Modifier";
        RenameButton.IsEnabled = MoveButton.IsEnabled = row?.Profile is not null;
        var explanation = row is null ? "Sélectionnez une ligne." : row.Profile is null ? "Complétez la fiche pour préserver son identité lors du renommage ou du déplacement." : null;
        RenameButton.ToolTip = MoveButton.ToolTip = explanation;
    }

    private void PopulateFilters()
    {
        var client = _viewModel.SelectedClient;
        var year = _viewModel.SelectedYear;
        ClientFilterBox.ItemsSource = new[] { "Tous les clients" }.Concat(_viewModel.AvailableClients).ToArray();
        ClientFilterBox.SelectedItem = client is not null && _viewModel.AvailableClients.Contains(client) ? client : "Tous les clients";
        YearFilterBox.ItemsSource = new object[] { "Toutes années" }.Concat(_viewModel.AvailableYears.Cast<object>()).ToArray();
        YearFilterBox.SelectedItem = year is not null && _viewModel.AvailableYears.Contains(year.Value) ? (object)year.Value : "Toutes années";
    }

    public Task RefreshAsync() => RefreshSafelyAsync();

    private async Task RefreshSafelyAsync()
    {
        var refreshVersion = ++_refreshVersion;
        LoadingText.Visibility = Visibility.Visible;
        LoadErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var projects = await Task.Run(_viewModel.LoadCatalog);
            if (refreshVersion != _refreshVersion) return;
            _viewModel.ApplyCatalog(projects);
            PopulateFilters();
            LoadErrorText.Text = string.Empty;
            LoadErrorText.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            if (refreshVersion != _refreshVersion) return;
            LoadErrorText.Text = UiErrorMessages.For(exception);
            LoadErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            if (refreshVersion == _refreshVersion) LoadingText.Visibility = Visibility.Collapsed;
        }
    }
}
