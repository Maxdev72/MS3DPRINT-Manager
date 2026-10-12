using Avalonia.Controls;
using Avalonia.Layout;

namespace MS3DPRINT.Manager.Desktop;

public sealed class ConfirmMoveWindow : Window
{
    public ConfirmMoveWindow(string source, string destination)
    {
        Title = "Confirmer le classement"; Width = 520; Height = 300; MinWidth = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var cancel = new Button { Content = "Annuler" }; cancel.Click += (_, _) => Close(false);
        var confirm = new Button { Content = "Déplacer le fichier" }; confirm.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24), Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Classer ce fichier dans le projet ?", FontSize = 19, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                new TextBlock { Text = "Source : " + source, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } },
                new TextBlock { Text = "Destination : " + destination, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { Text = "Le fichier sera déplacé, sans écraser de document existant.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Children = { cancel, confirm } }
            }
        };
    }
}
