using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.Controls;
using MS3DPRINT.Manager.Core.Workspace;
namespace MS3DPRINT.Manager.App.Views;
public partial class TrashView : UserControl
{
    private readonly ManagedFileService _files;
    private int _version;
    private bool _restoring;
    public TrashView(string root) { InitializeComponent(); CompactTable.Configure(TrashList); _files = new(root); Loaded += async (_, _) => await RefreshAsync(); }
    private void Trash_SelectionChanged(object sender, SelectionChangedEventArgs e) => RestoreButton.IsEnabled = TrashList.SelectedItem is TrashEntry;
    public async Task RefreshAsync()
    {
        var version = ++_version;
        try
        {
            var entries = await Task.Run(_files.ListTrash);
            if (version != _version) return;
            TrashList.ItemsSource = entries;
            MessageText.Text = entries.Count == 0 ? "La corbeille est vide." : $"{entries.Count} élément(s) restaurable(s).";
            if (_files.TrashReadErrors.Count > 0) MessageText.Text += "\n" + string.Join("\n", _files.TrashReadErrors);
        }
        catch (Exception exception) { if (version == _version) MessageText.Text = UiErrorMessages.For(exception); }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_restoring) return;
        if (TrashList.SelectedItem is not TrashEntry entry) { MessageText.Text = "Sélectionnez un élément à restaurer."; return; }
        _restoring = true;
        IsEnabled = false;
        ++_version;
        try { await Task.Run(() => _files.Restore(entry.Id)); await RefreshAsync(); MessageText.Text = "Élément restauré à son emplacement d’origine."; }
        catch (Exception exception) { MessageText.Text = UiErrorMessages.For(exception); }
        finally { _restoring = false; IsEnabled = true; }
    }
}
