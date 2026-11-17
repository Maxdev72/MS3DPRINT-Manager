using Avalonia.Controls;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Asks whether to install a new version now (the application restarts) or later.</summary>
public sealed class ConfirmUpdateWindow : DialogWindow
{
    public ConfirmUpdateWindow(string version)
    {
        Title = "Mise à jour disponible"; Width = 500; Height = 260; MinWidth = 400;
        var confirm = new Button { Content = "Mettre à jour et redémarrer" };
        confirm.Click += (_, _) => Close(true);
        var later = new Button { Content = "Plus tard" };
        later.Click += (_, _) => Close(false);
        Layout($"Installer la version {version} ?", "Le logiciel se fermera puis se relancera. Vos données et vos documents sont conservés. Sans réponse maintenant, la question sera reposée au prochain démarrage.",
            [], confirm, later);
    }
}
