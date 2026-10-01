using System.Windows;
using System.Windows.Controls;
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
        DataContext = _viewModel;
        Loaded += async (_, _) => await RefreshSafelyAsync();
    }

    public event EventHandler? CreateRequested;
    public event Action<ClientSummary>? ClientSelected;

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshSafelyAsync();
    private void Create_Click(object sender, RoutedEventArgs e) => CreateRequested?.Invoke(this, EventArgs.Empty);

    private void ClientsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClientsList.SelectedItem is not ClientSummary client) return;
        ClientsList.SelectedItem = null;
        ClientSelected?.Invoke(client);
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
