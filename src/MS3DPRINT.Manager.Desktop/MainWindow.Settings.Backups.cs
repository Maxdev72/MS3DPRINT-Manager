using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private bool _backupPickerActive;
    private string? _backupControlsRoot;
    private Control[] _backupControls = [];
    private void UpdateBackupControls()
    {
        var enabled = !_backupPickerActive && !_pendingOperations.Contains(_backupControlsRoot + "|backup");
        foreach (var control in _backupControls) control.IsEnabled = enabled;
    }
    private sealed record LastBackup(DateTimeOffset At, string ArchivePath, int FileCount, long TotalBytes, bool IncludesDocuments);
    private static string BackupHistoryPath(string root) => Path.Combine(root, ".ms3dprint-manager", "last-backup.json");

    internal static string BackupSize(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):N1} Gio" : $"{bytes / (1024d * 1024):N1} Mio";

    private static string LoadBackupStatus(string root)
    {
        try
        {
            var path = BackupHistoryPath(root); WorkspacePathSafety.EnsureNoLinks(path);
            if (!File.Exists(path)) return "Aucune sauvegarde enregistrée depuis ce logiciel.";
            var last = JsonSerializer.Deserialize<LastBackup>(File.ReadAllText(path)) ?? throw new JsonException();
            return $"Dernière sauvegarde réussie : {last.At.ToLocalTime():dd/MM/yyyy à HH:mm} · {last.FileCount:N0} fichiers · {BackupSize(last.TotalBytes)}\n" +
                (last.IncludesDocuments ? "Avec documents" : "Sans documents") + " · " + last.ArchivePath;
        }
        catch (Exception exception) when (BackupError(exception))
        { return "Historique de sauvegarde illisible. Vous pouvez créer une nouvelle sauvegarde."; }
    }

    private Border BuildBackupSettingsCard(string root, string status)
    {
        var documents = new CheckBox { Content = "Inclure les documents (STL, CAO, PDF, photos…)", IsChecked = true };
        var last = new TextBlock { Text = status, TextWrapping = TextWrapping.Wrap, Classes = { "caption" }, LineHeight = 21 };
        Avalonia.Automation.AutomationProperties.SetLiveSetting(last, Avalonia.Automation.AutomationLiveSetting.Polite);
        var save = new Button { Content = Icons.Label("IconSave", "Créer une sauvegarde…", 16), Classes = { "primary-action" } };
        var restore = new Button { Content = Icons.Label("IconHistory", "Restaurer une copie…", 16) };
        _backupControlsRoot = root; _backupControls = [documents, save, restore]; UpdateBackupControls();
        save.Click += async (_, _) =>
        {
            if (_backupPickerActive || _pendingOperations.Contains(root + "|backup")) return;
            _backupPickerActive = true;
            UpdateBackupControls();
            try
            {
                var chosen = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Enregistrer la sauvegarde hors de l’espace de travail", SuggestedFileName = $"MS3DPRINT-sauvegarde-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
                    DefaultExtension = "zip", FileTypeChoices = [new FilePickerFileType("Sauvegarde ZIP") { Patterns = ["*.zip"] }]
                });
                if (chosen?.TryGetLocalPath() is { } path && WorkspaceRoot == root)
                    await SaveWorkspaceBackupAsync(root, path, documents.IsChecked == true);
            }
            catch (Exception exception) when (BackupError(exception)) { Notify("Sauvegarde impossible : " + exception.Message, error: true); }
            finally { _backupPickerActive = false; UpdateBackupControls(); }
        };
        restore.Click += async (_, _) =>
        {
            if (_backupPickerActive || _pendingOperations.Contains(root + "|backup")) return;
            _backupPickerActive = true;
            UpdateBackupControls();
            try
            {
                var chosen = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Ouvrir une sauvegarde MS3DPRINT", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Sauvegarde ZIP") { Patterns = ["*.zip"] }] });
                if (chosen.FirstOrDefault()?.TryGetLocalPath() is not { } archive) return;
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choisir le dossier parent de la copie restaurée", AllowMultiple = false });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } parent || WorkspaceRoot != root) return;
                var destination = Path.Combine(parent, $"MS3DPRINT-restaure-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}");
                await RestoreWorkspaceBackupAsync(root, archive, destination);
            }
            catch (Exception exception) when (BackupError(exception)) { Notify("Restauration impossible : " + exception.Message, error: true); }
            finally { _backupPickerActive = false; UpdateBackupControls(); }
        };
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { save, restore }) { button.Margin = new Thickness(0, 0, 8, 8); actions.Children.Add(button); }
        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(SettingsCardHeader("IconSave", "Sauvegarde et restauration", "Conservez une copie indépendante de vos clients, projets, devis, matières, imprimantes et réglages atelier."));
        content.Children.Add(documents);
        content.Children.Add(new TextBlock { Text = "Avec documents : fichiers des dossiers gérés et corbeille inclus. Sans documents : données métier et arborescence uniquement, sans corbeille. Les réglages du poste (performances, fenêtre, emplacement actif) restent sur cet ordinateur.", TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, FontSize = 13, LineHeight = 21 });
        content.Children.Add(actions); content.Children.Add(last);
        content.Children.Add(new TextBlock { Text = "Enregistrez vos saisies avant de lancer la sauvegarde et évitez de modifier les fichiers pendant la copie. Choisissez un emplacement hors de l’espace de travail ; l’archive ZIP n’est pas chiffrée.", TextWrapping = TextWrapping.Wrap, Classes = { "caption" }, LineHeight = 21 });
        return new Border { Classes = { "card-raised" }, Padding = new Thickness(22, 20), Child = content };
    }

    private async Task<bool> SaveWorkspaceBackupAsync(string root, string archive, bool documents)
    {
        using var operation = TryBeginOperation(root + "|backup", "Sauvegarde de l’espace de travail…");
        if (operation is null) return false;
        UpdateBackupControls();
        try
        {
            if (!FlushEstimateDraft()) throw new IOException("Le brouillon de l’estimateur n’a pas pu être enregistré. Réessayez après avoir vérifié l’accès à vos données.");
            var result = await Task.Run(() => new WorkspaceBackupService().Create(root, archive, documents));
            try
            {
                await Task.Run(() =>
                {
                    var path = BackupHistoryPath(root); WorkspacePathSafety.EnsureNoLinks(path);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllText(temporary, JsonSerializer.Serialize(new LastBackup(result.CreatedAt, archive, result.FileCount, result.TotalBytes, documents)));
                        File.Move(temporary, path, overwrite: true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                });
                Notify($"Sauvegarde créée : {result.FileCount:N0} fichiers · {BackupSize(result.TotalBytes)}.");
            }
            catch (Exception exception) when (BackupError(exception)) { Notify("Archive créée, mais son historique n’a pas pu être enregistré : " + exception.Message, error: true); }
            if (string.Equals(WorkspaceRoot, root, StringComparison.OrdinalIgnoreCase) && SettingsWorkspacePanel.IsVisible) await LoadSettingsAsync();
            return true;
        }
        catch (Exception exception) when (BackupError(exception)) { Notify("Sauvegarde impossible : " + exception.Message, error: true); return false; }
        finally { operation.Dispose(); UpdateBackupControls(); }
    }

    private async Task<bool> RestoreWorkspaceBackupAsync(string root, string archive, string destination)
    {
        using var operation = TryBeginOperation(root + "|backup", "Vérification et restauration de la sauvegarde…");
        if (operation is null) return false;
        UpdateBackupControls();
        try
        {
            var fullDestination = Path.GetFullPath(destination);
            if (fullDestination.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Choisissez un dossier parent en dehors de l’espace de travail actif.");
            var preview = await Task.Run(() => new WorkspaceBackupService().Inspect(archive));
            if (WorkspaceRoot != root || !await new RestoreBackupWindow(preview, destination).ShowDialog<bool>(this)) return false;
            await Task.Run(() => new WorkspaceBackupService().Restore(archive, destination, preview.ArchiveSha256));
            Notify("Copie restaurée dans « " + destination + " ». L’espace de travail actif est conservé.");
            return true;
        }
        catch (Exception exception) when (BackupError(exception)) { Notify("Restauration impossible : " + exception.Message, error: true); return false; }
        finally { operation.Dispose(); UpdateBackupControls(); }
    }
    private static bool BackupError(Exception exception) => exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException or System.Security.SecurityException;
}
