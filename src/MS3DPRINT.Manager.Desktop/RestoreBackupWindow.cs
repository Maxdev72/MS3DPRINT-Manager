using Avalonia.Controls;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Desktop;

public sealed class RestoreBackupWindow : DialogWindow
{
    public RestoreBackupWindow(WorkspaceBackupPreview preview, string destination)
    {
        Title = "Restaurer une sauvegarde"; Width = 560; Height = 610; MinWidth = 440;
        var confirm = new Button { Content = "Restaurer la copie" };
        confirm.Click += (_, _) => Close(true);
        TextBlock Detail(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, LineHeight = 22 };
        Layout("Vérifier la sauvegarde",
            "Une copie sera restaurée dans un nouveau dossier. L’espace de travail actuel restera actif et intact.",
            [
                FormKit.Field("Sauvegarde du", Detail(preview.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy à HH:mm"))),
                FormKit.Field("Contenu vérifié", Detail($"{preview.FileCount:N0} fichiers · {MainWindow.BackupSize(preview.TotalBytes)} · intégrité SHA256 vérifiée")),
                Detail(preview.IncludesDocuments ? "Données métier, réglages atelier, documents des dossiers gérés et corbeille." : "Données métier et réglages atelier uniquement. Documents et corbeille exclus : les dossiers de documents seront vides."),
                FormKit.Field("Nouveau dossier de restauration", new SelectableTextBlock { Text = destination, TextWrapping = TextWrapping.Wrap }),
                Detail("Après vérification de cette copie, vous pourrez l’utiliser avec « Changer d’espace de travail » dans les paramètres.")
            ], confirm, destructive: true);
        // Explicit confirmation and Cancel focus, without styling a non-destructive copy as a deletion.
        confirm.Classes.Remove("danger"); confirm.Classes.Add("primary-action");
    }
}
