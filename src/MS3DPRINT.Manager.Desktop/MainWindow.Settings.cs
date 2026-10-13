using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Printers;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Desktop.Dashboard;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Settings page and changelog.</summary>
public partial class MainWindow
{
    private async void Changelog_Click(object? sender, RoutedEventArgs e)
        => await new ChangelogWindow().ShowDialog(this);

    private async void RefreshSettings_Click(object? sender, RoutedEventArgs e) => await LoadSettingsAsync();

    private async void ChangeWorkspace_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choisir l’espace de travail", AllowMultiple = false });
        var chosen = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(chosen)) return;
        try
        {
            _workspaceLocation.Save(chosen);
            Notify("Espace de travail changé. Utilisez « Vérifier l’arborescence » pour y créer les dossiers manquants.");
            await PreloadAsync(force: true);
            await LoadSettingsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { Notify(exception.Message, error: true); }
    }

    private async void VerifyWorkspace_Click(object? sender, RoutedEventArgs e)
    {
        var root = WorkspaceRoot;
        try
        {
            var result = await Task.Run(() => new MS3DPRINT.Manager.Core.Storage.FolderTreeService().EnsureMainStructure(root));
            Notify(result.CreatedFolders.Count == 0
                ? "Arborescence vérifiée : tous les dossiers existent."
                : $"Arborescence vérifiée : {result.CreatedFolders.Count} dossier(s) ajouté(s).");
            await LoadSettingsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { Notify(exception.Message, error: true); }
    }

    private async Task LoadSettingsAsync()
    {
        var workspaceRoot = WorkspaceRoot;
        StorageList.Children.Clear();
        StorageList.Spacing = 14;
        var providers = await Task.Run(() => new WorkspaceStorageReader().Load(workspaceRoot));

        Border Card(string title, string description, params Control[] content)
        {
            var stack = new StackPanel { Spacing = 12 };
            var heading = new StackPanel { Spacing = 2 };
            heading.Children.Add(new TextBlock { Text = title, Classes = { "card-title" } });
            heading.Children.Add(new TextBlock { Text = description, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(heading);
            foreach (var control in content) stack.Children.Add(control);
            return new Border { Classes = { "card-raised" }, Padding = new Avalonia.Thickness(20), Child = stack };
        }

        var open = new Button { Content = "Ouvrir l’espace de travail", Tag = workspaceRoot };
        open.Click += OpenFolder_Click;
        var verify = new Button { Content = "Vérifier l’arborescence" };
        verify.Click += VerifyWorkspace_Click;
        var change = new Button { Content = "Changer d’espace de travail…" };
        change.Click += ChangeWorkspace_Click;
        var fixedByEnvironment = MS3DPRINT.Manager.Core.Storage.WorkspaceLocation.IsFixedByEnvironment;
        change.IsEnabled = !fixedByEnvironment;
        if (fixedByEnvironment) ToolTip.SetTip(change, "L’espace de travail est imposé par la variable d’environnement MS3DPRINT_WORKSPACE.");
        StorageList.Children.Add(Card("Espace de travail", workspaceRoot,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, verify, change } }));

        var providerRows = new StackPanel { Spacing = 0 };
        foreach (var provider in providers)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 40 };
            row.Children.Add(new TextBlock { Text = provider.DisplayName, VerticalAlignment = VerticalAlignment.Center });
            var badge = new Border { Classes = { "badge", provider.IsAvailable ? "success" : "neutral" }, Child = new TextBlock { Text = provider.IsAvailable ? "Disponible" : "Non configuré" } };
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);
            providerRows.Children.Add(new Border { BorderBrush = (IBrush?)Avalonia.Application.Current?.FindResource("SubtleBorderBrush"), BorderThickness = new Avalonia.Thickness(0, 0, 0, 1), Child = row });
        }
        StorageList.Children.Add(Card("Stockage", "Emplacements où vos dossiers peuvent être synchronisés.", providerRows));

        var changelog = new Button { Content = "Journal des modifications", HorizontalAlignment = HorizontalAlignment.Left };
        changelog.Click += Changelog_Click;
        StorageList.Children.Add(Card("À propos", "MS3DPRINT Manager : gestion de vos clients, projets, collections, filaments, imprimantes et estimations.", changelog));
    }
}
