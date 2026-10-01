using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Collections;

namespace MS3DPRINT.Manager.App.Views;

public partial class CollectionView : UserControl
{
    private readonly CollectionViewModel _viewModel;
    private int _refreshVersion;

    public CollectionView(CollectionViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += async (_, _) => await RefreshSafelyAsync();
    }

    public event EventHandler? BackRequested;
    public event EventHandler? CreateRequested;
    public event Action<CollectionItemSummary>? ItemSelected;

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshSafelyAsync();
    private void Create_Click(object sender, RoutedEventArgs e) => CreateRequested?.Invoke(this, EventArgs.Empty);
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void Item_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemsList.SelectedItem is not CollectionItemSummary item) return;
        ItemsList.SelectedItem = null;
        ItemSelected?.Invoke(item);
    }

    private async Task RefreshSafelyAsync()
    {
        var refreshVersion = ++_refreshVersion;
        LoadingText.Visibility = Visibility.Visible;
        LoadErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var items = await Task.Run(_viewModel.LoadCatalog);
            if (refreshVersion != _refreshVersion) return;
            _viewModel.ApplyCatalog(items);
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
