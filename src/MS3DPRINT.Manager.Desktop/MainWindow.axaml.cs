using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void NavigationButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string destination })
            return;

        PageTitle.Text = destination;
        PageSubtitle.Text = $"Espace {destination.ToLowerInvariant()} de l’atelier.";
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control || e.Key != Key.K)
            return;

        SearchBox.Focus();
        e.Handled = true;
    }
}
