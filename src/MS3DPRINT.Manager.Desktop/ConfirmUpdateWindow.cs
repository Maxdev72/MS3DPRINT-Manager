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
        Layout($"Installer la version {version} ?", "La mise à jour est déjà téléchargée : le logiciel se ferme puis se relance en quelques secondes. Vos données et vos documents sont conservés. Avec « Plus tard », elle reste disponible dans la barre de gauche.",
            [], confirm, later);
    }
}
