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
using MS3DPRINT.Manager.Core.Filaments;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Filaments page.</summary>
public partial class MainWindow
{
    private IReadOnlyList<FilamentProfile> _allFilaments = [];
    private IReadOnlyList<string> _filamentReadErrors = [];
    private string? _filamentCatalogRoot;
    private bool _filamentCatalogReady;

    private void InitializeFilamentFilters()
    {
        FilamentFamily.ItemsSource = FilamentCatalogSearch.Families;
        FilamentFamily.SelectedIndex = 0;
        FilamentSearch.TextChanged += (_, _) => RenderFilaments();
        FilamentFamily.SelectionChanged += (_, _) => RenderFilaments();
    }

    private void ClearFilamentFilters_Click(object? sender, RoutedEventArgs e)
    {
        FilamentSearch.Text = ""; FilamentFamily.SelectedIndex = 0; RenderFilaments();
    }

    private async void RefreshFilaments_Click(object? sender, RoutedEventArgs e) => await LoadFilamentsAsync();

    private async void CreateFilament_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new CreateFilamentWindow();
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            new WorkspaceFilamentEditor().Create(root, dialog.Brand, dialog.FilamentName, dialog.Material, dialog.Price, dialog.IsAbrasive, dialog.Color);
            Notify("Filament ajouté.");
            await LoadFilamentsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void EditFilament_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Filaments.FilamentProfile original }) return;
        var dialog = new CreateFilamentWindow(original);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            new WorkspaceFilamentEditor().Update(root, original, dialog.Brand, dialog.FilamentName, dialog.Material, dialog.Price, dialog.IsAbrasive, dialog.Color);
            Notify("Fiche filament enregistrée.");
            await LoadFilamentsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async Task LoadFilamentsAsync()
    {
        var workspaceRoot = WorkspaceRoot;
        var load = StartWorkspaceLoad("filaments", workspaceRoot);
        _filamentCatalogReady = false; FilamentFilters.IsEnabled = false;
        if (!string.Equals(_filamentCatalogRoot, workspaceRoot, StringComparison.OrdinalIgnoreCase))
        { FilamentSearch.Text = ""; FilamentFamily.SelectedIndex = 0; }
        FilamentList.Children.Clear();
        _filamentPageSummary.Text = "Chargement des matières…";
        try
        {
            var result = await Task.Run(() =>
            {
                var reader = new WorkspaceInventoryReader();
                return (Profiles: reader.LoadFilaments(workspaceRoot), Reader: reader);
            });
            if (!IsWorkspaceLoadCurrent(load)) return;
            _allFilaments = result.Profiles; _filamentReadErrors = result.Reader.ReadErrors; _filamentCatalogRoot = workspaceRoot;
            _filamentCatalogReady = true; FilamentFilters.IsEnabled = true;
            RenderFilaments();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!IsWorkspaceLoadCurrent(load)) return;
            _filamentPageSummary.Text = "Lecture des matières indisponible";
            FilamentList.Children.Clear();
            FilamentList.Children.Add(CreateReadErrorState("Matières indisponibles", "Vérifiez l’accès au dossier de l’atelier, puis réessayez.", exception, LoadFilamentsAsync));
        }
    }

    private void RenderFilaments()
    {
        if (!_filamentCatalogReady || !string.Equals(_filamentCatalogRoot, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return;
        FilamentList.Children.Clear();
        var filaments = FilamentCatalogSearch.Filter(_allFilaments, FilamentSearch.Text, FilamentFamily.SelectedItem as string);
        var errors = _filamentReadErrors;
        _filamentPageSummary.Text = filaments.Count == _allFilaments.Count ? $"{filaments.Count} référence{(filaments.Count > 1 ? "s" : "")} · tarifs au kilogramme" : $"{filaments.Count} sur {_allFilaments.Count} références · tarifs au kilogramme";
        if (errors.Count > 0) _filamentPageSummary.Text += $" · {errors.Count} fiche{(errors.Count > 1 ? "s" : "")} illisible{(errors.Count > 1 ? "s" : "")}";
        Control Warning() => CreateReadErrorState("Certaines fiches sont illisibles", "Les fichiers concernés sont conservés. Les matières lisibles restent disponibles. Après correction ou synchronisation, réessayez.", new IOException(string.Join(Environment.NewLine, errors)), LoadFilamentsAsync, compact: true);
        if (filaments.Count == 0)
        {
            if (errors.Count > 0) FilamentList.Children.Add(Warning());
            if (_allFilaments.Count > 0) FilamentList.Children.Add(CreateEmptyState("IconSearch", "Aucun résultat", "Modifiez la recherche ou réaffichez toutes les familles.", "Effacer les filtres", ClearFilamentFilters_Click));
            else if (errors.Count == 0) FilamentList.Children.Add(CreateEmptyState("IconFilaments", "Aucun filament enregistré", "Ajoutez vos bobines pour calculer le coût matière de vos estimations.", "Ajouter un filament", CreateFilament_Click));
            return;
        }
        PagedRows.Render(FilamentList, filaments, filament =>
        {
            var edit = new Button { Content = Icons.Label("IconEdit", "Modifier"), Tag = filament }; edit.Click += EditFilament_Click;
            var trash = new Button { Content = Icons.Label("IconTrash", "Corbeille"), Tag = filament, Classes = { "menu-destructive" } }; trash.Click += TrashFilament_Click;
            var color = string.IsNullOrWhiteSpace(filament.Color) ? string.Empty : $" · {filament.Color}";
            var material = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            material.Children.Add(CreateCompactTableCell($"{filament.Material}{color}"));
            var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (var variant in FilamentCatalogSearch.Variants(filament.Material)) badges.Children.Add(new Border { Classes = { "badge", "info" }, Child = new TextBlock { Text = variant } });
            if (filament.IsAbrasive) badges.Children.Add(new Border { Classes = { "badge", "warning" }, Child = new TextBlock { Text = "Abrasif" } });
            if (badges.Children.Count > 0) material.Children.Add(badges);
            return CreateActionRow(null, CreateOverflowActions(edit, trash), CreateCompactTableCell($"{filament.Brand} · {filament.Name}", prominent: true), material, CreateCompactTableCell($"{filament.PricePerKg:0.00} €/kg"));
        }, () => errors.Count == 0 ? CreateCompactTableHeader(ActionRowColumns, "FILAMENT", "MATIÈRE", "PRIX", "ACTIONS") : new StackPanel { Children = { Warning(), CreateCompactTableHeader(ActionRowColumns, "FILAMENT", "MATIÈRE", "PRIX", "ACTIONS") } }, (_filamentCatalogRoot, FilamentSearch.Text, FilamentFamily.SelectedItem));
    }

    private async void TrashFilament_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Filaments.FilamentProfile filament }) return;
        if (!await new ConfirmTrashWindow(filament.Brand + " " + filament.Name, "La fiche pourra être restaurée depuis la corbeille.").ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Filaments.FilamentStore(root).Trash(filament.Id));
            Notify("Filament envoyé dans la corbeille.");
            await LoadFilamentsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }
}
