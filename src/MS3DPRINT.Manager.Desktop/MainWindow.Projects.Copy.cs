using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private Button[] CreateProjectCopyActions(ProjectSummary project)
    {
        return
        [
            CopyAction("Copier le nom du projet", project.Title(), "Nom du projet copié."),
            CopyAction("Copier la référence", project.Reference, "Référence du projet copiée."),
            CopyAction("Copier le nom et la référence", project.Title() + " — " + project.Reference, "Nom et référence copiés."),
            CopyAction("Copier le chemin du dossier", project.ProjectPath, "Chemin du dossier copié.")
        ];

        Button CopyAction(string label, string value, string feedback)
        {
            var action = new Button { Content = Icons.Label("IconCopy", label) };
            AutomationProperties.SetName(action, label);
            action.Click += async (_, _) => await CopyProjectTextAsync(value, feedback);
            return action;
        }
    }

    private async Task CopyProjectTextAsync(string value, string feedback)
    {
        if (Clipboard is not { } clipboard)
        {
            Notify("Le presse-papiers est indisponible. Réessayez la copie.", error: true);
            return;
        }
        try
        {
            await clipboard.SetTextAsync(value);
            Notify(feedback);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
            or NotSupportedException or System.Runtime.InteropServices.ExternalException)
        {
            Notify("Impossible de copier dans le presse-papiers. Réessayez. " + e.Message, error: true);
        }
    }
}
