using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using MS3DPRINT.Manager.App.Views;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.App.Controls;

public static class FileManagement
{
    public static void StretchFileRows(ListView list)
    {
        var material = list.TryFindResource("MaterialDesignListViewItem") as Style
            ?? list.TryFindResource(typeof(ListViewItem)) as Style;
        var style = new Style(typeof(ListViewItem));
        if (material is not null && material.TargetType.IsAssignableFrom(typeof(ListViewItem))) style.BasedOn = material;
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        list.ItemContainerStyle = style;
    }

    private static readonly DependencyProperty AttachedProperty = DependencyProperty.RegisterAttached(
        "ManagementAttached", typeof(bool), typeof(FileManagement), new PropertyMetadata(false));

    public static void Attach(Panel toolbar, ListView list, string root, Func<string> currentDirectory, Func<Task> refresh, Action<ProjectFileEntry> open, Window? owner)
    {
        if ((bool)list.GetValue(AttachedProperty)) return;
        var files = new ManagedFileService(root);
        var entities = new EntityManagementService(root);
        var projects = new ProjectCatalog(new ProjectProfileStore(new WorkspaceMetadataPaths(root)));
        async Task Run(Action action)
        {
            var errors = new List<string>();
            try { action(); }
            catch (Exception exception) { errors.Add(UiErrorMessages.For(exception)); }
            // A batch may have imported some files before reporting failures.
            try { await refresh(); }
            catch (Exception exception) { errors.Add(UiErrorMessages.For(exception)); }
            if (errors.Count > 0) MessageBox.Show(string.Join(Environment.NewLine, errors), "MS3DPRINT Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Button AddButton(string label)
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 10, 8) };
            button.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryButton"); toolbar.Children.Add(button); return button;
        }
        async Task Rename(string path, bool isDirectory)
        {
            await Run(() =>
            {
                var project = isDirectory ? projects.Load(root).FirstOrDefault(p =>
                    string.Equals(p.ProjectPath, path, StringComparison.OrdinalIgnoreCase)) : null;
                var extension = Path.GetExtension(path);
                var description = project is not null
                    ? "Modifiez le nom du projet. Sa référence restera inchangée."
                    : isDirectory ? "Le nom du dossier sera modifié."
                    : "Saisissez le nouveau nom du fichier" + (extension.Length == 0 ? "." : $" en conservant son extension {extension}.");
                var prompt = new TextPromptWindow("Renommer", description,
                    project?.ProjectName ?? Path.GetFileName(path)) { Owner = owner };
                if (prompt.ShowDialog() == true) entities.RenamePath(path, prompt.Value);
            });
        }
        AddButton("Nouveau dossier…").Click += async (_, _) =>
        {
            var prompt = new TextPromptWindow("Nouveau dossier", "Nom du sous-dossier à créer") { Owner = owner };
            if (prompt.ShowDialog() == true) await Run(() => files.CreateFolder(currentDirectory(), prompt.Value));
        };
        AddButton("Importer des fichiers…").Click += async (_, _) =>
        {
            var picker = new OpenFileDialog { Multiselect = true, Title = "Importer dans le dossier courant" };
            if (picker.ShowDialog(owner) == true) await Run(() =>
            {
                var failures = new List<string>();
                foreach (var path in picker.FileNames)
                    try { files.Import(path, currentDirectory()); }
                    catch (Exception exception) { failures.Add(Path.GetFileName(path) + " : " + UiErrorMessages.For(exception)); }
                if (failures.Count > 0) throw new IOException(string.Join(Environment.NewLine, failures));
            });
        };
        AddButton("Renommer un fichier…").Click += async (_, _) =>
        {
            var picker = new OpenFileDialog
            {
                Title = "Choisissez le fichier à renommer", InitialDirectory = currentDirectory(),
                CheckFileExists = true, Multiselect = false
            };
            if (picker.ShowDialog(owner) == true) await Rename(picker.FileName, isDirectory: false);
        };
        var help = new TextBlock { Text = "Clic droit : ouvrir, renommer, déplacer, supprimer.", Margin = new Thickness(0, 10, 0, 8), TextWrapping = TextWrapping.Wrap };
        help.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); toolbar.Children.Add(help);

        ProjectFileEntry? selected = null;
        var menu = new ContextMenu();
        MenuItem AddItem(string header)
        { var item = new MenuItem { Header = header }; menu.Items.Add(item); return item; }
        AddItem("Ouvrir").Click += (_, _) => { if (selected is not null) open(selected); };
        AddItem("Renommer…").Click += async (_, _) =>
        {
            if (selected is null) return;
            var entry = selected;
            await Rename(entry.FullPath, entry.IsDirectory);
        };
        AddItem("Déplacer…").Click += async (_, _) =>
        {
            if (selected is null) return;
            var picker = new OpenFolderDialog { Title = "Destination dans l’espace MS3DPRINT", InitialDirectory = currentDirectory() };
            if (picker.ShowDialog(owner) == true) await Run(() => entities.MovePath(selected.FullPath, picker.FolderName));
        };
        AddItem("Supprimer vers la corbeille…").Click += async (_, _) =>
        {
            if (selected is null) return;
            if (MessageBox.Show($"Envoyer « {selected.Name} » et son contenu dans la corbeille interne ?", "Supprimer", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                await Run(() => entities.TrashPath(selected.FullPath));
        };
        list.PreviewMouseRightButtonDown += (_, args) =>
        {
            var node = args.OriginalSource as DependencyObject;
            while (node is not null && node is not ListViewItem)
                node = node is Visual ? VisualTreeHelper.GetParent(node) : (node as FrameworkContentElement)?.Parent;
            selected = (node as ListViewItem)?.DataContext as ProjectFileEntry;
            if (selected is null) return;
            args.Handled = true; menu.PlacementTarget = list; menu.IsOpen = true;
        };
        list.SetValue(AttachedProperty, true);
    }
}
