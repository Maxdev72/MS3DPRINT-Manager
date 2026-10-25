using Avalonia.Controls;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Explicit confirmation for removing an entry from a form or view.</summary>
public sealed class ConfirmRemovalWindow : DialogWindow
{
    public ConfirmRemovalWindow(string label, string detail)
    {
        Title = "Confirmer le retrait"; Width = 500; Height = 320; MinWidth = 440;
        var confirm = new Button { Content = "Confirmer le retrait" };
        confirm.Click += (_, _) => Close(true);
        Layout("Retirer « " + label + " » ?", detail, [], confirm, destructive: true);
    }

    public static async Task<bool> AskAsync(Control source, string label, string detail)
        => TopLevel.GetTopLevel(source) is Window owner && await new ConfirmRemovalWindow(label, detail).ShowDialog<bool>(owner);
}
