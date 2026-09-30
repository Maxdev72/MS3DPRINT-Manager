using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.App.Views;

public partial class ClientsView : UserControl
{
    private readonly ClientsViewModel _viewModel;

    public ClientsView(ClientsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        Loaded += (_, _) => _viewModel.Refresh();
    }

    public event EventHandler? CreateRequested;

    private void Refresh_Click(object sender, RoutedEventArgs e) => _viewModel.Refresh();
    private void Create_Click(object sender, RoutedEventArgs e) => CreateRequested?.Invoke(this, EventArgs.Empty);

    private void KindFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SelectedKind = KindFilterBox.SelectedIndex switch
        {
            1 => ClientKind.Professional,
            2 => ClientKind.Individual,
            _ => null
        };
    }
}
