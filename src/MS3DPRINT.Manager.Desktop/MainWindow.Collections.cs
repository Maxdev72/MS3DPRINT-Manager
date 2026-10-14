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

/// <summary>Collections page.</summary>
public partial class MainWindow
{
    private async void RefreshCollections_Click(object? sender, RoutedEventArgs e) => await LoadCollectionsAsync();

    private async void CreateCollection_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new CreateCollectionWindow();
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            var (newCategory, newName) = (dialog.Category, dialog.NameValue);
            var item = await Task.Run(() => new WorkspaceCollectionCreator().Create(root, newCategory, newName));
            await Task.Run(() => new WorkspaceCollectionEditor().Save(root, item, newCategory, item.Name, null, null));
            Notify("Collection créée.");
            await LoadCollectionsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async Task LoadCollectionsAsync()
    {
        var workspaceRoot = WorkspaceRoot;
        CollectionList.Children.Clear();
        try
        {
            var collections = await Task.Run(() => new WorkspaceInventoryReader().LoadAllCollections(workspaceRoot));
            if (collections.Count == 0) { CollectionList.Children.Add(CreateEmptyState("IconCollections", "Aucune collection", "Regroupez vos modèles 3D, vos produits et vos fournisseurs.", "Nouvelle collection", CreateCollection_Click)); return; }
            if (_collectionCategoryFilter.Length > 0)
                collections = collections.Where(item => CollectionCategory(workspaceRoot, item) == _collectionCategoryFilter).ToArray();
            if (collections.Count == 0) { CollectionList.Children.Add(CreateEmptyState("IconCollections", "Rien dans cette catégorie", "Choisissez « Tout » ou créez un nouvel élément.")); return; }
            CollectionList.Children.Add(CreateCompactTableHeader(ActionRowColumns, "COLLECTION", "TYPE", "MODIFIÉE LE", "ACTIONS"));
            foreach (var item in collections)
            {
                var category = CollectionCategory(workspaceRoot, item);
                var open = new Button { Tag = item.Path, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                open.Click += OpenFolder_Click;
                var openMenu = new Button { Content = Icons.Label("IconOpen", "Ouvrir"), Tag = item.Path }; openMenu.Click += OpenFolder_Click;
                var edit = new Button { Content = Icons.Label("IconEdit", "Modifier"), Tag = item }; edit.Click += EditCollection_Click;
                var rename = new Button { Content = Icons.Label("IconRename", "Renommer"), Tag = item }; rename.Click += RenameCollection_Click;
                var move = new Button { Content = Icons.Label("IconMove", "Déplacer"), Tag = item }; move.Click += MoveCollection_Click;
                var trash = new Button { Content = Icons.Label("IconTrash", "Corbeille"), Tag = item }; trash.Click += TrashCollection_Click;
                var title = new StackPanel { Spacing = 1 };
                title.Children.Add(CreateCompactTableCell(item.Name, prominent: true));
                if (!string.IsNullOrWhiteSpace(item.Profile?.Description))
                    title.Children.Add(new TextBlock { Text = item.Profile.Description, Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis });
                var type = new Border { Classes = { "badge", category == "06_FOURNISSEURS" ? "warning" : category == "03_PRODUITS_MS3DPRINT" ? "success" : "info" }, Child = new TextBlock { Text = CollectionCategoryLabel(category) } };
                CollectionList.Children.Add(CreateActionRow(open, CreateOverflowActions(openMenu, edit, rename, move, trash), title, type,
                    CreateCompactTableCell($"{item.LastWriteTime:dd/MM/yyyy}")));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { CollectionList.Children.Add(new TextBlock { Text = "Impossible de lire les collections.", Margin = new Avalonia.Thickness(8) }); }
    }

    private async void EditCollection_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Collections.CollectionItemSummary item }) return;
        var dialog = new EditCollectionWindow(item);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            var category = Path.GetRelativePath(root, item.Path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            var (name, description, notes, contact) = (dialog.NameValue, dialog.Description, dialog.Notes, dialog.Contact);
            var (address, phone, email, website) = (dialog.Address, dialog.Phone, dialog.Email, dialog.Website);
            await Task.Run(() => new WorkspaceCollectionEditor().Save(root, item, category, name, description, notes,
                contact, address, phone, email, website));
            Notify("Fiche collection enregistrée.");
            await LoadCollectionsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private static string CollectionCategoryLabel(string category) => category switch
    {
        "02_MODELES_3D" => "Modèle 3D",
        "03_PRODUITS_MS3DPRINT" => "Produit",
        "06_FOURNISSEURS" => "Fournisseur",
        _ => "Collection"
    };

    private async void CollectionFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string category }) return;
        _collectionCategoryFilter = category;
        foreach (var chip in CollectionFilters.Children.OfType<Button>())
        {
            if (chip.Tag as string == category) chip.Classes.Add("selected"); else chip.Classes.Remove("selected");
        }
        await LoadCollectionsAsync();
    }

    private static string CollectionCategory(string root, MS3DPRINT.Manager.Core.Collections.CollectionItemSummary item) =>
        Path.GetRelativePath(root, item.Path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

    private async void RenameCollection_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Collections.CollectionItemSummary item }) return;
        var dialog = new TextPromptWindow("Renommer la collection", "Le dossier et sa fiche seront mis à jour ensemble.", item.Name);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            var newName = dialog.Value;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).RenameCollection(item, CollectionCategory(root, item), newName));
            Notify("Collection renommée.");
            await LoadCollectionsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void MoveCollection_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Collections.CollectionItemSummary item }) return;
        var destination = await PickWorkspaceFolderAsync(Path.GetDirectoryName(item.Path) ?? WorkspaceRoot,
            "Déplacer « " + Path.GetFileName(item.Path) + " »", item.Path);
        if (string.IsNullOrWhiteSpace(destination)) return;
        try
        {
            var root = WorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).MoveCollection(item, CollectionCategory(root, item), destination));
            Notify("Collection déplacée.");
            await LoadCollectionsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void TrashCollection_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Collections.CollectionItemSummary item }) return;
        if (!await new ConfirmTrashWindow(item.Name, "La collection et son contenu pourront être restaurés depuis la corbeille.").ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).TrashCollection(item));
            Notify("Collection envoyée dans la corbeille.");
            await LoadCollectionsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }
}
