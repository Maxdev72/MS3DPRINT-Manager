using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MS3DPRINT.Manager.App.Controls;
using MS3DPRINT.Manager.App.Views;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Templates;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.App;

public partial class MainWindow
{
    private bool _managementNavigation;
    private EntityManagementService Management => new(_viewModel.StorageRoot);
    private void Filaments_Click(object sender, RoutedEventArgs e) => Run(() => TryShowPage(new FilamentsView(_viewModel.StorageRoot)));
    private void Trash_Click(object sender, RoutedEventArgs e) => Run(() => TryShowPage(new TrashView(_viewModel.StorageRoot)));
    private void Manage(Action action)
    {
        if (!CanLeaveCurrentPage()) return;
        _managementNavigation = true;
        try { Run(action); } finally { _managementNavigation = false; }
    }
    private string? PromptName(string title, string message, string initial)
    { var dialog = new TextPromptWindow(title, message, initial) { Owner = this }; return dialog.ShowDialog() == true ? dialog.Value : null; }
    private string? PickFolder(string initial)
    { var dialog = new OpenFolderDialog { Title = "Choisissez une destination dans la catégorie de cet élément", InitialDirectory = initial }; return dialog.ShowDialog(this) == true ? dialog.FolderName : null; }
    private bool ConfirmTrash(string label, string detail)
        => MessageBox.Show(this, $"Envoyer « {label} » dans la corbeille interne ?\n\n{detail}\nLes documents et les fiches seront conservés pour une restauration.", "Supprimer", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    private static EntityActions Actions(UserControl page) => (EntityActions)page.FindName("ManagementActions");

    private void CompleteClient(ClientSummary client, Action back) => Manage(() =>
    {
        var dialog = new CreateClientWindow(_viewModel.StorageRoot, _folders, _codes, _clientProfiles, client) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var completed = _clientCatalog.Load(_viewModel.StorageRoot).FirstOrDefault(candidate => string.Equals(candidate.ClientPath, client.ClientPath, StringComparison.OrdinalIgnoreCase) && candidate.Profile is not null)
            ?? throw new InvalidOperationException("La fiche client n’a pas été enregistrée. Les fichiers restent accessibles.");
        ShowClientDetailCore(completed, back);
        _viewModel.Status = "Fiche client complétée : " + completed.DisplayName;
        _ = RefreshDashboardSafelyAsync();
    });

    private void CompleteProject(ProjectSummary project, Action back) => Manage(() =>
    {
        var coordinator = new LegacyProjectCompletionCoordinator(_viewModel.StorageRoot, _clientCatalog, _projectCatalog);
        var completed = coordinator.Complete(project,
            client =>
            {
                var dialog = new CreateClientWindow(_viewModel.StorageRoot, _folders, _codes, _clientProfiles, client) { Owner = this };
                ((TextBlock)dialog.FindName("IntroText")).Text = "Complétez d’abord la fiche de ce client, puis celle du projet. Son dossier et ses fichiers existants seront conservés.";
                return dialog.ShowDialog() == true;
            },
            (existing, client) => new CreateTrackedProjectWindow(_viewModel.StorageRoot, _folders, _clientCatalog, _projectProfiles, existing, client) { Owner = this }.ShowDialog() == true);
        if (completed is null) return;
        ShowProjectDetail(completed, back);
        _viewModel.Status = "Fiche projet complétée : " + completed.Reference;
        _ = RefreshDashboardSafelyAsync();
    });

    private void RenameClientFolder(ClientSummary client, Action? back = null) => Manage(() =>
    {
        var name = PromptName("Renommer le dossier client", "Le code client et les références des projets resteront inchangés.", client.FolderName);
        if (name is null) return; Management.RenameClient(client, name); (back ?? ShowClients)();
    });
    private void MoveClientFolder(ClientSummary client, Action? back = null) => Manage(() =>
    {
        var folder = PickFolder(Path.Combine(_viewModel.StorageRoot, "01_CLIENTS"));
        if (folder is null) return; Management.MoveClient(client, folder); (back ?? ShowClients)();
    });
    private void TrashClientFolder(ClientSummary client, Action? back = null) => Manage(() =>
    {
        if (!ConfirmTrash(client.DisplayName, "Tous les projets et fichiers de ce client seront également placés dans la corbeille.")) return;
        Management.TrashClient(client); (back ?? ShowClients)();
    });
    private void RenameProjectFolder(ProjectSummary project, Action? back = null) => Manage(() =>
    {
        var name = PromptName("Renommer le projet", "La référence du projet restera inchangée.", project.Profile?.ProjectName ?? project.ProjectName);
        if (name is null) return; Management.RenameProject(project, name); (back ?? ShowProjects)();
    });
    private void MoveProjectFolder(ProjectSummary project, Action? back = null) => Manage(() =>
    {
        var dialog = new ClientPickerWindow(_clientCatalog.Load(_viewModel.StorageRoot)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedClient is null) return;
        Management.MoveProject(project, dialog.SelectedClient); (back ?? ShowProjects)();
    });
    private void TrashProjectFolder(ProjectSummary project, Action? back = null) => Manage(() =>
    {
        if (!ConfirmTrash(project.Reference, "Tous les fichiers et sous-dossiers du projet sont concernés.")) return;
        Management.TrashProject(project); (back ?? ShowProjects)();
    });
    private void EditCollection(CollectionItemSummary item, Action back) => Manage(() =>
    {
        var category = Path.GetRelativePath(_viewModel.StorageRoot, item.Path).Split(Path.DirectorySeparatorChar)[0];
        var editor = new CollectionEditorWindow(_viewModel.StorageRoot, item, category) { Owner = this };
        if (editor.ShowDialog() == true && editor.SavedProfile is { } profile)
            ShowCollectionDetail(new(profile.Name, item.Path, DateTimeOffset.UtcNow, profile), back);
    });
    private void RenameCollectionFolder(CollectionItemSummary item, string category, Action back) => Manage(() =>
    {
        var name = PromptName("Renommer", "Le dossier et sa fiche seront mis à jour ensemble.", Path.GetFileName(item.Path));
        if (name is null) return; Management.RenameCollection(item, category, name); back();
    });
    private void MoveCollectionFolder(CollectionItemSummary item, string category, Action back) => Manage(() =>
    {
        var folder = PickFolder(Path.Combine(_viewModel.StorageRoot, category));
        if (folder is null) return; Management.MoveCollection(item, category, folder); back();
    });
    private void TrashCollectionFolder(CollectionItemSummary item, Action back) => Manage(() =>
    {
        if (!ConfirmTrash(item.Name, "Tous les fichiers et sous-dossiers de cet élément sont concernés.")) return;
        Management.TrashCollection(item); back();
    });

    private void WireClientActions(ClientDetailView page, ClientSummary client)
    {
        var actions = Actions(page);
        actions.CanEdit = true;
        actions.EditRequested += (_, _) => page.ShowInformation();
        actions.RenameRequested += (_, _) => RenameClientFolder(client);
        actions.MoveRequested += (_, _) => MoveClientFolder(client);
        actions.TrashRequested += (_, _) => TrashClientFolder(client);
        ((ContentControl)page.FindName("ClientFilesHost")).Content = new WorkspaceFilesView(_viewModel.StorageRoot, client.ClientPath);
    }

    private void WireProjectActions(ProjectDetailView page, ProjectSummary project)
    {
        var actions = Actions(page);
        actions.CanEdit = true;
        actions.EditRequested += (_, _) => page.ShowInformation();
        actions.RenameRequested += (_, _) => RenameProjectFolder(project);
        actions.MoveRequested += (_, _) => MoveProjectFolder(project);
        actions.TrashRequested += (_, _) => TrashProjectFolder(project);
        page.ConfigureFileManagement(_viewModel.StorageRoot, this);
    }

    private void WireCollectionActions(CollectionDetailView page, CollectionItemSummary item, Action back)
    {
        var category = Path.GetRelativePath(_viewModel.StorageRoot, item.Path).Split(Path.DirectorySeparatorChar)[0];
        var actions = Actions(page); actions.CanEdit = true;
        if (item.Profile is null) ((Button)actions.FindName("EditButton")).Content = "Compléter la fiche…";
        actions.EditRequested += (_, _) => EditCollection(item, back);
        actions.RenameRequested += (_, _) => RenameCollectionFolder(item, category, back);
        actions.MoveRequested += (_, _) => MoveCollectionFolder(item, category, back);
        actions.TrashRequested += (_, _) => TrashCollectionFolder(item, back);
        page.ConfigureFileManagement(_viewModel.StorageRoot, this);
    }

    private void CreateCollection(string title, string category, IReadOnlyList<string> template, Action? back = null)
    {
        if (!CanLeaveCurrentPage()) return;
        var previousNavigation = _managementNavigation;
        _managementNavigation = true;
        try
        {
            var create = new CreateNamedItemWindow(title, Path.Combine(_viewModel.StorageRoot, category), template, _folders) { Owner = this };
            if (create.ShowDialog() != true || create.CreatedPath is null) return;
            var item = new CollectionItemSummary(Path.GetFileName(create.CreatedPath), create.CreatedPath, DateTimeOffset.UtcNow);
            var editor = new CollectionEditorWindow(_viewModel.StorageRoot, item, category) { Owner = this };
            if (editor.ShowDialog() == true && editor.SavedProfile is { } profile) item = item with { Name = profile.Name, Profile = profile };
            _viewModel.Status = "Élément créé : " + item.Name;
            ShowCollectionDetail(item, back ?? (() => ShowCategory(category)));
            _ = RefreshDashboardSafelyAsync();
        }
        finally { _managementNavigation = previousNavigation; }
    }
    private void ShowCategory(string category)
    {
        switch (category)
        {
            case "02_MODELES_3D": ShowCollection("Modèles 3D", "Retrouver vos modèles et leurs fichiers de conception.", category, FolderTemplates.Model, "Nouveau modèle 3D"); break;
            case "03_PRODUITS_MS3DPRINT": ShowCollection("Produits", "Suivre les produits et leur documentation de fabrication.", category, FolderTemplates.Product, "Nouveau produit MS3DPRINT"); break;
            case "06_FOURNISSEURS": ShowCollection("Fournisseurs", "Centraliser les dossiers fournisseurs, tarifs et commandes.", category, FolderTemplates.Supplier, "Nouveau fournisseur"); break;
        }
    }
}
