using Avalonia.Controls;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Confirms the switch of the workspace to a folder of a synchronised service.</summary>
public sealed class UseWorkspaceWindow : DialogWindow
{
    public UseWorkspaceWindow(string service, string root)
    {
        Title = "Changer d’espace de travail"; Width = 540; Height = 420; MinWidth = 420;
        var confirm = new Button { Content = "Utiliser ce dossier" };
        confirm.Click += (_, _) => Close(true);
        Layout("Utiliser " + service + " ?",
            "Le dossier ci-dessous sera créé s’il n’existe pas, avec l’arborescence de base. Vos clients et projets actuels restent dans l’ancien dossier : le logiciel n’y touche pas. Copiez-les dans le nouveau si vous voulez les retrouver.",
            [FormKit.Field("Nouvel espace de travail", new TextBlock { Text = root, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontWeight = Avalonia.Media.FontWeight.SemiBold })],
            confirm);
    }
}
