using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.ViewModels;

namespace MS3DPRINT.Manager.App.Views;

public partial class ClientDetailView : UserControl
{
    private readonly ClientDetailViewModel _viewModel;
    public ClientDetailView(ClientDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    public event EventHandler? BackRequested;
    public event EventHandler? CreateProjectRequested;
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void CreateProject_Click(object sender, RoutedEventArgs e) => CreateProjectRequested?.Invoke(this, EventArgs.Empty);
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { _viewModel.Save(); MessageText.Text = "Fiche enregistrée."; }
        catch (Exception exception) { MessageText.Text = UiErrorMessages.For(exception); }
    }
}
