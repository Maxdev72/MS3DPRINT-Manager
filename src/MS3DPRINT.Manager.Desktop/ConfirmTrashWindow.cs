using Avalonia.Controls;

namespace MS3DPRINT.Manager.Desktop;

public sealed class ConfirmTrashWindow : DialogWindow
{
    public ConfirmTrashWindow(string label, string detail)
    {
        Title = "Envoyer à la corbeille"; Width = 480; Height = 270; MinWidth = 400;
        var confirm = new Button { Content = "Envoyer à la corbeille" };
        confirm.Click += (_, _) => Close(true);
        Layout("Envoyer « " + label + " » à la corbeille ?",
            detail + " Les fichiers et fiches pourront être restaurés depuis la corbeille.",
            [], confirm, destructive: true);
    }
}
