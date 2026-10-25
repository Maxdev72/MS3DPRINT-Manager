using Avalonia.Controls;
using Avalonia.Layout;

namespace MS3DPRINT.Manager.Desktop;

public sealed class WorkspaceUnavailableWindow : DialogWindow
{
    public string SelectedRoot { get; private set; }

    public WorkspaceUnavailableWindow(string root, Func<Task<string?>>? choose)
    {
        SelectedRoot = root;
        Title = "Espace de travail indisponible"; Width = 600; Height = 430; MinWidth = 440;
        var error = FormKit.ErrorText();
        var retry = new Button { Content = "Réessayer" };
        retry.Click += (_, _) =>
        {
            if (Directory.Exists(SelectedRoot)) Close(true);
            else { error.Text = "Le dossier reste inaccessible. Vérifiez votre disque, votre réseau ou la synchronisation, puis réessayez."; error.IsVisible = true; }
        };
        var other = new Button { Content = "Choisir un autre dossier", IsVisible = choose is not null };
        other.Click += async (_, _) =>
        {
            if (choose is null) return;
            try
            {
                var chosen = await choose();
                if (chosen is null) return;
                if (!Directory.Exists(chosen)) { error.Text = "Ce dossier n’est pas accessible."; error.IsVisible = true; return; }
                SelectedRoot = chosen; Close(true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { error.Text = exception.Message; error.IsVisible = true; }
        };
        var close = new Button { Content = "Fermer le logiciel" }; close.Click += (_, _) => Close(false);
        Layout("Retrouver votre espace de travail",
            "Votre dossier habituel est indisponible. Aucun autre atelier n’a été sélectionné et vos documents sont conservés.",
            [new SelectableTextBlock { Text = root, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
             new TextBlock { Text = "Reconnectez le disque ou attendez la synchronisation, puis réessayez.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } }, error],
            retry, close, extraAction: other);
        Opened += (_, _) => retry.Focus();
    }
}
