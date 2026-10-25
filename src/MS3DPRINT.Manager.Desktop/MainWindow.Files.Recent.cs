using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Templates;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private async Task RememberOpenedFileAsync(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        if (Path.IsPathRooted(relative) || !FolderTemplates.Main.Contains(relative.Split(Path.DirectorySeparatorChar)[0], StringComparer.OrdinalIgnoreCase)) return;
        try
        {
            await Task.Run(() => new RecentFileStore(root).Remember(path, DateTimeOffset.Now));
            if (string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) await LoadRecentFilesAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        { Notify("Fichier ouvert, mais son raccourci récent n’a pas pu être enregistré : " + exception.Message, error: true); }
    }

    private async Task LoadRecentFilesAsync()
    {
        var root = WorkspaceRoot;
        var load = StartWorkspaceLoad("recent-files", root, trackNavigation: false);
        RecentFilesList.Children.Clear();
        try
        {
            var entries = await Task.Run(() =>
            {
                var store = new RecentFileStore(root);
                return store.Load().Select(entry =>
                {
                    try { var path = store.Resolve(entry); return (Entry: entry, Path: path, Available: File.Exists(path), Safe: true); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                    { return (Entry: entry, Path: Path.Combine(root, entry.RelativePath), Available: false, Safe: false); }
                }).ToArray();
            });
            if (!IsWorkspaceLoadCurrent(load)) return;
            if (entries.Length == 0)
            {
                RecentFilesList.Children.Add(new TextBlock { Text = "Vos fichiers ouverts apparaîtront ici pour les retrouver rapidement.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
                return;
            }
            var shown = 6;
            void Render()
            {
                RecentFilesList.Children.Clear();
                foreach (var item in entries.Take(shown))
                {
                    var labels = new StackPanel { Spacing = 3, Children =
                    {
                        new TextBlock { Text = System.IO.Path.GetFileName(item.Path), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = item.Available ? item.Entry.OpenedAt.ToLocalTime().ToString("dd/MM à HH:mm") : "Indisponible · déplacé ou supprimé", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap }
                    } };
                    var open = new Button { Tag = item.Path, Content = labels, Classes = { "workspace-card" }, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Avalonia.Thickness(10, 8), IsEnabled = item.Safe };
                    ToolTip.SetTip(open, item.Entry.RelativePath);
                    Avalonia.Automation.AutomationProperties.SetName(open, "Réouvrir " + System.IO.Path.GetFileName(item.Path));
                    open.Click += async (_, _) =>
                    {
                        if (!string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return;
                        try { await OpenFileAsync(new RecentFileStore(root).Resolve(item.Entry)); }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { Notify("Ce fichier n’est plus accessible : " + exception.Message, error: true); }
                    };
                    var folder = new Button { Content = Icons.Glyph("IconProjects", 16), Classes = { "tool" }, VerticalAlignment = VerticalAlignment.Center, IsEnabled = item.Safe };
                    ToolTip.SetTip(folder, "Voir le dossier du fichier"); Avalonia.Automation.AutomationProperties.SetName(folder, "Voir le dossier de " + System.IO.Path.GetFileName(item.Path));
                    folder.Click += (_, _) =>
                    {
                        if (!string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return;
                        try
                        {
                            var path = new RecentFileStore(root).Resolve(item.Entry);
                            var parent = System.IO.Path.GetDirectoryName(path)!;
                            if (Directory.Exists(parent)) OpenFolder_Click(new Button { Tag = parent }, new Avalonia.Interactivity.RoutedEventArgs());
                            else Notify("Le dossier de ce fichier est indisponible.", error: true);
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { Notify(exception.Message, error: true); }
                    };
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 4 };
                    Grid.SetColumn(folder, 1); row.Children.Add(open); row.Children.Add(folder); RecentFilesList.Children.Add(row);
                }
                if (entries.Length > shown)
                {
                    var more = new Button { Content = "Afficher les " + entries.Length + " fichiers récents", Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
                    more.Click += (_, _) => { shown = entries.Length; Render(); }; RecentFilesList.Children.Add(more);
                }
            }
            Render();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            if (!IsWorkspaceLoadCurrent(load)) return;
            RecentFilesList.Children.Add(CreateReadErrorState("Récents indisponibles", "Les fichiers restent accessibles depuis vos projets et dossiers.", exception, LoadRecentFilesAsync, compact: true));
        }
    }
}
