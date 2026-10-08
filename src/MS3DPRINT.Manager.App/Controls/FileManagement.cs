using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Win32;
using MS3DPRINT.Manager.App.Views;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.App.Controls;

public static class FileManagement
{
    public static void ConfigureFileTable(ListView list)
    {
        list.ItemTemplate = null;
        list.DisplayMemberPath = string.Empty;
        var table = new GridView { AllowsColumnReorder = true };
        table.Columns.Add(new GridViewColumn { Header = "Nom", Width = 330, DisplayMemberBinding = new Binding(nameof(ProjectFileEntry.Name)) });
        table.Columns.Add(new GridViewColumn { Header = "Type", Width = 85, DisplayMemberBinding = new Binding(nameof(ProjectFileEntry.TypeLabel)) });
        table.Columns.Add(new GridViewColumn { Header = "Taille", Width = 95, DisplayMemberBinding = new Binding(nameof(ProjectFileEntry.Length)) { Converter = new FileSizeConverter() } });
        table.Columns.Add(new GridViewColumn { Header = "Modifié", Width = 150, DisplayMemberBinding = new Binding(nameof(ProjectFileEntry.LastWriteTime)) { StringFormat = "{0:dd/MM/yyyy HH:mm}" } });
        list.View = table;
        CompactTable.Configure(list);
    }

    private sealed class FileSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => new ProjectFileEntry("", "", false, value is long size ? size : null, default).SizeLabel;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
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
        ConfigureFileTable(list);
        var selectedButtons = new List<Button>();
        Button SelectionButton(string label)
        {
            var button = AddButton(label); button.IsEnabled = list.SelectedItem is ProjectFileEntry;
            selectedButtons.Add(button); return button;
        }
        var openButton = SelectionButton("Ouvrir");
        openButton.Click += (_, _) => { if (list.SelectedItem is ProjectFileEntry entry) open(entry); };
        var renameButton = SelectionButton("Renommer…");
        renameButton.Click += async (_, _) =>
        { if (list.SelectedItem is ProjectFileEntry entry) await Rename(entry.FullPath, entry.IsDirectory); };
        var moveButton = SelectionButton("Déplacer…");
        moveButton.Click += async (_, _) =>
        {
            if (list.SelectedItem is not ProjectFileEntry entry) return;
            var picker = new OpenFolderDialog { Title = "Destination dans l’espace MS3DPRINT", InitialDirectory = currentDirectory() };
            if (picker.ShowDialog(owner) == true) await Run(() => entities.MovePath(entry.FullPath, picker.FolderName));
        };
        SelectionButton("Supprimer…").Click += async (_, _) =>
        {
            if (list.SelectedItem is not ProjectFileEntry entry) return;
            if (MessageBox.Show($"Envoyer « {entry.Name} » et son contenu dans la corbeille interne ?", "Supprimer", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                await Run(() => entities.TrashPath(entry.FullPath));
        };
        void UpdateSelectionActions()
        {
            var entry = list.SelectedItem as ProjectFileEntry;
            foreach (var button in selectedButtons) button.IsEnabled = entry is not null;
            string? explanation = null;
            if (entry?.IsDirectory == true)
            {
                try
                {
                    var client = new ClientCatalog(new ClientProfileStore(new WorkspaceMetadataPaths(root))).Load(root)
                        .FirstOrDefault(row => string.Equals(row.ClientPath, entry.FullPath, StringComparison.OrdinalIgnoreCase));
                    var project = projects.Load(root).FirstOrDefault(row => string.Equals(row.ProjectPath, entry.FullPath, StringComparison.OrdinalIgnoreCase));
                    if ((client is not null && client.Profile is null) || (project is not null && project.Profile is null))
                        explanation = "Complétez la fiche depuis Clients ou Projets avant de renommer ou déplacer ce dossier. Ses fichiers restent accessibles.";
                }
                catch (Exception exception) { explanation = "Impossible de vérifier cette action : " + UiErrorMessages.For(exception); }
            }
            renameButton.IsEnabled = moveButton.IsEnabled = entry is not null && explanation is null;
            renameButton.ToolTip = moveButton.ToolTip = explanation;
            ToolTipService.SetShowOnDisabled(renameButton, true); ToolTipService.SetShowOnDisabled(moveButton, true);
        }
        list.SelectionChanged += (_, _) => UpdateSelectionActions();
        UpdateSelectionActions();
        CompactTable.BindOpen(list, () => { if (list.SelectedItem is ProjectFileEntry entry) open(entry); });
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
        AddButton("Importer et classer…").Click += async (_, _) =>
        {
            var picker = new OpenFileDialog { Multiselect = true, Title = "Choisir les fichiers à importer" };
            if (picker.ShowDialog(owner) != true) return;
            var destination = new OpenFolderDialog
            {
                Title = "Choisir le dossier de destination dans le projet",
                InitialDirectory = currentDirectory()
            };
            if (destination.ShowDialog(owner) != true) return;
            await Run(() =>
            {
                var failures = new List<string>();
                foreach (var path in picker.FileNames)
                    try { files.Import(path, destination.FolderName); }
                    catch (Exception exception) { failures.Add(Path.GetFileName(path) + " : " + UiErrorMessages.For(exception)); }
                if (failures.Count > 0) throw new IOException(string.Join(Environment.NewLine, failures));
            });
        };
        var help = new TextBlock { Text = "Double-clic ou Entrée : ouvrir. Sélectionnez une ligne pour la gérer.", Margin = new Thickness(0, 10, 0, 8), TextWrapping = TextWrapping.Wrap };
        help.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); toolbar.Children.Add(help);

        ProjectFileEntry? selected = null;
        var menu = new ContextMenu();
        MenuItem AddItem(string header)
        { var item = new MenuItem { Header = header }; menu.Items.Add(item); return item; }
        AddItem("Ouvrir").Click += (_, _) => { if (selected is not null) open(selected); };
        var renameMenu = AddItem("Renommer…");
        renameMenu.Click += async (_, _) =>
        {
            if (selected is null) return;
            var entry = selected;
            await Rename(entry.FullPath, entry.IsDirectory);
        };
        var moveMenu = AddItem("Déplacer…");
        moveMenu.Click += async (_, _) =>
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
            list.SelectedItem = selected;
            renameMenu.IsEnabled = renameButton.IsEnabled;
            moveMenu.IsEnabled = moveButton.IsEnabled;
            renameMenu.ToolTip = moveMenu.ToolTip = renameButton.ToolTip;
            args.Handled = true; menu.PlacementTarget = list; menu.IsOpen = true;
        };
        list.SetValue(AttachedProperty, true);
    }
}
