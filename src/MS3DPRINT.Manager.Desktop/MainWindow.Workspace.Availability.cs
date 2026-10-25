using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private bool _recoveringWorkspace;

    private async Task<bool> EnsureWorkspaceAvailableAsync()
    {
        var root = WorkspaceRoot;
        if (Directory.Exists(root)) return true;
        if (_recoveringWorkspace || _panel is not null) return false;
        _recoveringWorkspace = true;
        try
        {
            async Task<string?> Choose()
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Retrouver l’espace de travail", AllowMultiple = false });
                return folders.FirstOrDefault()?.TryGetLocalPath();
            }
            var dialog = new WorkspaceUnavailableWindow(root, WorkspaceLocation.IsFixedByEnvironment ? null : Choose);
            if (!await dialog.ShowDialog<bool>(this)) { Close(); return false; }
            if (!Directory.Exists(dialog.SelectedRoot)) return false;
            if (!string.Equals(root, dialog.SelectedRoot, StringComparison.OrdinalIgnoreCase))
            {
                if (!FlushEstimateDraft())
                {
                    Notify("Vos saisies sont conservées. Rétablissez l’accès à l’ancien atelier pour sauvegarder le brouillon avant de changer de dossier.", error: true);
                    return false;
                }
                _workspaceLocation.Save(dialog.SelectedRoot);
            }
            _preloaded = false;
            await PreloadAsync(force: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { Notify("Impossible de retrouver l’espace de travail : " + exception.Message, error: true); return false; }
        finally { _recoveringWorkspace = false; }
    }
}
