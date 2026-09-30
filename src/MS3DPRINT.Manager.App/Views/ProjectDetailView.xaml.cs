using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.App.Views;

public partial class ProjectDetailView : UserControl
{
    private readonly ProjectDetailViewModel _viewModel;

    public ProjectDetailView(ProjectDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += (_, _) => StatusBox.SelectedIndex = (int)_viewModel.Status;
    }

    public event EventHandler? BackRequested;
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void Status_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatusBox.SelectedIndex >= 0) _viewModel.Status = (ProjectStatus)StatusBox.SelectedIndex;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { _viewModel.Save(); MessageText.Text = "Projet enregistré."; }
        catch (Exception exception) { MessageText.Text = UiErrorMessages.For(exception); }
    }
}
