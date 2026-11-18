using Avalonia.Controls;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Asks what the close button should do: quit the software or only minimize it.</summary>
public sealed class ConfirmCloseWindow : DialogWindow
{
    /// <summary>True when the user chose to quit; false for minimize.</summary>
    public bool Quit { get; private set; }

    public ConfirmCloseWindow()
    {
        Title = "Fermer le logiciel"; Width = 560; Height = 330; MinWidth = 440;
        var minimize = new Button { Content = "Réduire près de l’horloge" };
        minimize.Click += (_, _) => Close(true);
        var quit = new Button { Content = "Quitter" };
        quit.Click += (_, _) => { Quit = true; Close(true); };
        Layout("Fermer MS3DPRINT Manager ?", "Quitter arrête le logiciel et la synchronisation. Réduire le garde ouvert près de l’horloge (zone de notification). Ce choix est mémorisé ; vous pourrez le changer dans Paramètres → Application.", [], minimize, extraAction: quit);
    }

    /// <summary>Returns true to quit, false to minimize, null when cancelled.</summary>
    public static async Task<bool?> AskAsync(Window owner)
    {
        var dialog = new ConfirmCloseWindow();
        // bool? keeps it a real window (a bool result would open it in the tall side panel).
        return await dialog.ShowDialog<bool?>(owner) == true ? dialog.Quit : null;
    }
}
