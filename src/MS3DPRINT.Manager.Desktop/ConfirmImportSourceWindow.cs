using Avalonia.Controls;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop;

public sealed class ConfirmImportSourceWindow : DialogWindow
{
    public ConfirmImportSourceWindow(string source, string imported, Action? stopImport = null)
    {
        Title = "Fichier importé"; Width = 560; Height = 490; MinWidth = 440;
        var keep = new Button { Content = "Conserver la source" };
        keep.Click += (_, _) => Close(false);
        var delete = new Button { Content = "Supprimer la source" };
        delete.Click += (_, _) => Close(true);
        Button? stop = null;
        if (stopImport is not null)
        {
            stop = new Button { Content = "Conserver et arrêter l’import", Classes = { "ghost" } };
            stop.Click += (_, _) => { stopImport(); Close(false); };
        }
        static Control Location(string label, string path) => FormKit.Field(label,
            new SelectableTextBlock { Text = path, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
        var body = new List<Control> { Location("Original dans le dossier source", source), Location("Copie importée", imported) };
        if (stop is not null) body.Add(stop);
        Layout("Supprimer le fichier source ?",
            "L’import est terminé. Vous pouvez conserver l’original ou le supprimer définitivement de son dossier source. La copie importée sera conservée et vérifiée avant toute suppression.",
            body, delete, keep, destructive: true);
    }
}
