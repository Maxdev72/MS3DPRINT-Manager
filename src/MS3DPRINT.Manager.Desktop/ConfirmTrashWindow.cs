using Avalonia.Controls;
using Avalonia.Layout;

namespace MS3DPRINT.Manager.Desktop;

public sealed class ConfirmTrashWindow : Window
{
    public ConfirmTrashWindow(string label, string detail)
    {
        Title = "Envoyer à la corbeille"; Width = 460; Height = 270; MinWidth = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var cancel = new Button { Content = "Annuler" };
        cancel.Click += (_, _) => Close(false);
        var confirm = new Button { Content = "Envoyer à la corbeille" };
        confirm.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24), Spacing = 14,
            Children =
            {
                new TextBlock { Text = "Envoyer « " + label + " » à la corbeille ?", FontSize = 19, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { Text = detail + " Les fichiers et fiches pourront être restaurés depuis la corbeille.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Children = { cancel, confirm } }
            }
        };
    }
}
