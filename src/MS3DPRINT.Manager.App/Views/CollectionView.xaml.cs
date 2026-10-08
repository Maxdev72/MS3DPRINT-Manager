using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.Controls;
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
        CompactTable.Configure(ItemsList);
        CompactTable.BindOpen(ItemsList, OpenSelection);
        UpdateActions();
        DataContext = _viewModel;
        Loaded += async (_, _) => await RefreshSafelyAsync();
    }

    public event EventHandler? BackRequested;
    public event EventHandler? CreateRequested;
    public event Action<CollectionItemSummary>? ItemSelected;
    public event Action<CollectionItemSummary>? EditRequested;
    public event Action<CollectionItemSummary>? RenameRequested;
    public event Action<CollectionItemSummary>? MoveRequested;
    public event Action<CollectionItemSummary>? TrashRequested;

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshSafelyAsync();
    private void Create_Click(object sender, RoutedEventArgs e) => CreateRequested?.Invoke(this, EventArgs.Empty);
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void Item_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActions();
    private void OpenSelection() { if (ItemsList.SelectedItem is CollectionItemSummary row) ItemSelected?.Invoke(row); }
    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelection();
    private void Edit_Click(object sender, RoutedEventArgs e) { if (ItemsList.SelectedItem is CollectionItemSummary row) EditRequested?.Invoke(row); }
    private void Rename_Click(object sender, RoutedEventArgs e) { if (ItemsList.SelectedItem is CollectionItemSummary row) RenameRequested?.Invoke(row); }
    private void Move_Click(object sender, RoutedEventArgs e) { if (ItemsList.SelectedItem is CollectionItemSummary row) MoveRequested?.Invoke(row); }
    private void Trash_Click(object sender, RoutedEventArgs e) { if (ItemsList.SelectedItem is CollectionItemSummary row) TrashRequested?.Invoke(row); }
    private void UpdateActions()
    {
        if (OpenButton is null) return;
        var row = ItemsList.SelectedItem as CollectionItemSummary;
        OpenButton.IsEnabled = EditButton.IsEnabled = TrashButton.IsEnabled = row is not null;
        EditButton.Content = row is null ? "Modifier / Compléter" : row.Profile is null ? "Compléter" : "Modifier";
        RenameButton.IsEnabled = MoveButton.IsEnabled = row is not null;
        var explanation = row is null ? "Sélectionnez une ligne." : null;
        RenameButton.ToolTip = MoveButton.ToolTip = explanation;
    }

    public Task RefreshAsync() => RefreshSafelyAsync();

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
