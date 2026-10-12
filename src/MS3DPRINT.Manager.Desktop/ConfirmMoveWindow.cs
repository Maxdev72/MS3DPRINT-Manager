using Avalonia.Controls;

namespace MS3DPRINT.Manager.Desktop;

public sealed class ConfirmMoveWindow : DialogWindow
{
    public ConfirmMoveWindow(string source, string destination)
    {
        Title = "Confirmer le classement"; Width = 540; Height = 320; MinWidth = 420;
        var confirm = new Button { Content = "Déplacer le fichier" };
        confirm.Click += (_, _) => Close(true);
        Layout("Classer ce fichier dans le projet ?", "Le fichier sera déplacé, sans écraser de document existant.",
            [FormKit.Field("Source", new TextBlock { Text = source, TextWrapping = Avalonia.Media.TextWrapping.Wrap }),
             FormKit.Field("Destination", new TextBlock { Text = destination, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontWeight = Avalonia.Media.FontWeight.SemiBold })],
            confirm);
    }
}
