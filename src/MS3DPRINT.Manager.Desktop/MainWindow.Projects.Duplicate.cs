using Avalonia.Controls;
using Avalonia.Interactivity;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private bool _duplicatingProject;
    private async void DuplicateProject_Click(object? sender, RoutedEventArgs e)
    {
        if (_duplicatingProject || sender is not Button { Tag: ProjectSummary source }) return;
        var root = WorkspaceRoot;
        var navigation = CurrentNavigationStop?.Key;
        _duplicatingProject = true;
        try
        {
            var dialog = new DuplicateProjectWindow(source.Title());
            if (!await dialog.ShowDialog<bool>(this)) return;
            if (!MatchesNavigationContext(root, navigation)) return;
            using var operation = TryBeginOperation(root + "|duplicate|" + source.Reference, "Duplication du projet " + source.Reference + "…", sender as Button, "Duplication…");
            if (operation is null) return;
            var client = await Task.Run(() => new WorkspaceClientReader().Load(root).FirstOrDefault(c => string.Equals(c.ClientPath, source.ClientPath, StringComparison.OrdinalIgnoreCase)))
                ?? throw new InvalidOperationException("Le client de ce projet est introuvable.");
            if (!MatchesNavigationContext(root, navigation)) return;
            if (client.Profile is null)
            {
                var profile = await CompleteClientProfileAsync(client);
                if (profile is null) return;
                client = client with { Profile = profile };
            }
            if (!MatchesNavigationContext(root, navigation)) return;
            var name = dialog.ProjectName; var files = dialog.IncludeFiles;
            var copy = await Task.Run(() => new ProjectDuplicationService().Duplicate(root, source, client, name, files));
            Notify("Projet dupliqué · " + copy.Reference);
            if (!MatchesNavigationContext(root, navigation)) return;
            if (_projectListMode) await LoadProjectsAsync();
            else await LoadProjectDirectoryAsync(copy, copy.ProjectPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or AggregateException)
        { Notify(error.Message, error: true); }
        finally { _duplicatingProject = false; }
    }
}
