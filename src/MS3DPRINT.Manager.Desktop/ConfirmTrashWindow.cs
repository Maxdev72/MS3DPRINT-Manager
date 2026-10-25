using Avalonia.Controls;

namespace MS3DPRINT.Manager.Desktop;

public sealed class ConfirmTrashWindow : DialogWindow
{
    public ConfirmTrashWindow(IReadOnlyList<string> paths)
    {
        Title = "Envoyer à la corbeille"; Width = 560; Height = 490; MinWidth = 420;
        var confirm = new Button { Content = "Envoyer à la corbeille" };
        confirm.Click += (_, _) => Close(true);
        Layout($"Envoyer {paths.Count} fichier(s) à la corbeille ?",
            "Seuls les fichiers listés ci-dessous seront traités. Ils pourront être restaurés depuis la corbeille.",
            [new SelectableTextBlock { Text = Path.GetDirectoryName(paths.FirstOrDefault()), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } },
             new SelectableTextBlock { Text = string.Join(Environment.NewLine, paths.Select(Path.GetFileName)), TextWrapping = Avalonia.Media.TextWrapping.Wrap }],
            confirm, destructive: true);
    }

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
