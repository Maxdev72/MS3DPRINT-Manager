using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.Controls;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.App.Views;

public partial class ClientsView : UserControl
{
    private readonly ClientsViewModel _viewModel;
    private int _refreshVersion;

    public ClientsView(ClientsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        CompactTable.Configure(ClientsList);
        CompactTable.BindOpen(ClientsList, OpenSelection);
        UpdateActions();
        DataContext = _viewModel;
        Loaded += async (_, _) => await RefreshSafelyAsync();
    }

    public event EventHandler? CreateRequested;
    public event Action<ClientSummary>? ClientSelected;
    public event Action<ClientSummary>? EditRequested;
    public event Action<ClientSummary>? RenameRequested;
    public event Action<ClientSummary>? MoveRequested;
    public event Action<ClientSummary>? TrashRequested;

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshSafelyAsync();
    private void Create_Click(object sender, RoutedEventArgs e) => CreateRequested?.Invoke(this, EventArgs.Empty);

    private void ClientsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActions();
    private void OpenSelection() { if (ClientsList.SelectedItem is ClientSummary row) ClientSelected?.Invoke(row); }
    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelection();
    private void Edit_Click(object sender, RoutedEventArgs e) { if (ClientsList.SelectedItem is ClientSummary row) EditRequested?.Invoke(row); }
    private void Rename_Click(object sender, RoutedEventArgs e) { if (ClientsList.SelectedItem is ClientSummary row && row.Profile is not null) RenameRequested?.Invoke(row); }
    private void Move_Click(object sender, RoutedEventArgs e) { if (ClientsList.SelectedItem is ClientSummary row && row.Profile is not null) MoveRequested?.Invoke(row); }
    private void Trash_Click(object sender, RoutedEventArgs e) { if (ClientsList.SelectedItem is ClientSummary row) TrashRequested?.Invoke(row); }
    private void UpdateActions()
    {
        if (OpenButton is null) return;
        var row = ClientsList.SelectedItem as ClientSummary;
        OpenButton.IsEnabled = EditButton.IsEnabled = TrashButton.IsEnabled = row is not null;
        EditButton.Content = row is null ? "Modifier / Compléter" : row.Profile is null ? "Compléter" : "Modifier";
        RenameButton.IsEnabled = MoveButton.IsEnabled = row?.Profile is not null;
        var explanation = row is null ? "Sélectionnez une ligne." : row.Profile is null ? "Complétez la fiche pour préserver son identité lors du renommage ou du déplacement." : null;
        RenameButton.ToolTip = MoveButton.ToolTip = explanation;
    }

    private void KindFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SelectedKind = KindFilterBox.SelectedIndex switch
        {
            1 => ClientKind.Professional,
            2 => ClientKind.Individual,
            _ => null
        };
    }

    public Task RefreshAsync() => RefreshSafelyAsync();

    private async Task RefreshSafelyAsync()
    {
        var refreshVersion = ++_refreshVersion;
        LoadingText.Visibility = Visibility.Visible;
        LoadErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var clients = await Task.Run(_viewModel.LoadCatalog);
            if (refreshVersion != _refreshVersion) return;
            _viewModel.ApplyCatalog(clients);
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
