using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Filaments;

namespace MS3DPRINT.Manager.App.Views;

public partial class FilamentsView : UserControl
{
    private readonly FilamentStore _store;
    private readonly FilamentsViewModel _model = new();
    private bool _loading;
    public FilamentsView(string workspaceRoot)
    {
        InitializeComponent();
        // Theme resources are optional when a view is constructed outside the application.
        var materialItemStyle = TryFindResource("MaterialDesignListViewItem") as Style
            ?? TryFindResource(typeof(ListViewItem)) as Style;
        if (materialItemStyle is not null && materialItemStyle.TargetType.IsAssignableFrom(typeof(ListViewItem))
            && !FilamentsList.ItemContainerStyle.IsSealed)
            FilamentsList.ItemContainerStyle.BasedOn = materialItemStyle;
        _store = new FilamentStore(workspaceRoot);
        DataContext = _model;
        _model.BrandFilter = FilamentsViewModel.AllBrands;
        _model.MaterialFilter = FilamentsViewModel.AllMaterials;
        AbrasiveFilterBox.ItemsSource = new[] { "Tous", "Oui", "Non" };
        AbrasiveFilterBox.SelectedIndex = 0;
        Loaded += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_loading) return;
        _loading = true;
        ErrorText.Text = "";
        IsEnabled = false;
        try { _model.ApplyProfiles(await Task.Run(_store.LoadAll)); }
        catch (Exception exception) when (IsDataException(exception)) { ErrorText.Text = "Chargement impossible : " + exception.Message; }
        finally { IsEnabled = true; _loading = false; }
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var editor = new FilamentEditorWindow(save: _store.Create) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true) await RefreshAsync();
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedFilament is not { } selected) return;
        var editor = new FilamentEditorWindow(selected, _store.Update) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true) await RefreshAsync();
    }

    private async void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedFilament is not { } selected) return;
        try
        {
            var duplicate = _store.Duplicate(selected.Id);
            await RefreshAsync();
            _model.SelectedFilament = _model.VisibleFilaments.FirstOrDefault(p => p.Id == duplicate.Id);
        }
        catch (Exception exception) when (IsDataException(exception)) { ErrorText.Text = "Duplication impossible : " + exception.Message; }
    }

    private async void Trash_Click(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedFilament is not { } selected) return;
        if (MessageBox.Show(Window.GetWindow(this), $"Envoyer le filament « {selected.Brand} {selected.Name} » à la corbeille ?\nVous pourrez restaurer sa fiche depuis la corbeille.", "Supprimer le filament", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { _store.Trash(selected.Id); await RefreshAsync(); }
        catch (Exception exception) when (IsDataException(exception)) { ErrorText.Text = "Suppression impossible : " + exception.Message; }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void AbrasiveFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _model.AbrasiveFilter = AbrasiveFilterBox.SelectedIndex switch { 1 => true, 2 => false, _ => null };
    private static bool IsDataException(Exception exception) => exception is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException;
}
