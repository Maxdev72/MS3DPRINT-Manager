using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private async Task RunFileBatchAsync(FileBrowserView browser, string root, string directory, IReadOnlyList<string> paths,
        FileBatchOperation action, Func<Task> reload)
    {
        if (paths.Count == 0 || !ReferenceEquals(_activeFileBrowser, browser) || !string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return;
        using var pending = TryBeginOperation(root + "|files-batch", $"{(action == FileBatchOperation.Move ? "Déplacement" : "Mise à la corbeille")} de {paths.Count} fichier(s)…");
        if (pending is null) return;
        var opener = FocusManager?.GetFocusedElement() as Control;
        browser.SetBatchBusy(true);
        try
        {
            string? destination = null;
            if (action == FileBatchOperation.Trash)
            {
                if (!await new ConfirmTrashWindow(paths).ShowDialog<bool>(this)) return;
            }
            else
            {
                destination = await PickWorkspaceFolderAsync(directory, $"Déplacer {paths.Count} fichier(s)");
                if (destination is null) return;
            }
            // The root and file list are frozen before the asynchronous work. Never operate in a newly selected workspace.
            if (!string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return;
            var result = await Task.Run(() => new FileBatchService().Execute(root, directory, paths, action, destination));
            if (!string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return;
            if (ReferenceEquals(_activeFileBrowser, browser)) await reload();
            var verb = action == FileBatchOperation.Move ? "déplacé(s)" : "envoyé(s) à la corbeille";
            Notify($"{result.Completed.Count} fichier(s) {verb} · {result.Failures.Count} échec(s).", error: result.Failures.Count > 0);
            if (result.Failures.Count > 0) await new FileBatchResultWindow(result).ShowDialog<bool>(this);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify("Le traitement du lot n’a pas pu être terminé : " + exception.Message, error: true); }
        finally
        {
            browser.SetBatchBusy(false);
            if (ReferenceEquals(_activeFileBrowser, browser) && _panel is null && opener is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true }
                && opener.GetVisualAncestors().Contains(browser)) opener.Focus();
        }
    }
}

public sealed class FileBatchResultWindow : DialogWindow
{
    public FileBatchResultWindow(FileBatchResult result)
    {
        Title = "Bilan des fichiers"; Width = 620; Height = 500; MinWidth = 440;
        var close = new Button { Content = "Fermer" }; close.Click += (_, _) => Close(true);
        Layout("Certains fichiers n’ont pas été traités",
            $"{result.Completed.Count} fichier(s) traité(s), {result.Failures.Count} échec(s). Le lot ne s’arrête pas au premier échec ; aucun fichier existant n’est écrasé.",
            [new SelectableTextBlock { Text = string.Join(Environment.NewLine + Environment.NewLine, result.Failures.Select(f => Path.GetFileName(f.Path) + Environment.NewLine + f.Message)), TextWrapping = TextWrapping.Wrap }], close,
            new Button { Content = "Annuler", IsVisible = false });
    }
}
