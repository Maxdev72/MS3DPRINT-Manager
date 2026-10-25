using Avalonia.Controls;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Read-only history of a corrected line: original values, then each correction with its reason.</summary>
public sealed class ConfirmHistoryWindow : DialogWindow
{
    public ConfirmHistoryWindow(string heading, string history)
    {
        Title = "Historique de la ligne"; Width = 600; Height = 560; MinWidth = 460;
        var close = new Button { Content = "Fermer" };
        close.Click += (_, _) => Close(true);
        Layout(heading, "Chaque correction est conservée avec son motif ; la ligne d’origine n’est jamais effacée.",
            [new SelectableTextBlock { Text = history, TextWrapping = Avalonia.Media.TextWrapping.Wrap, LineHeight = 22 }], close);
    }
}
