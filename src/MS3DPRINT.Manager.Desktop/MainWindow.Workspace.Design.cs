using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private readonly TextBlock _printerPageSummary = WorkspaceSummary();
    private readonly TextBlock _filamentPageSummary = WorkspaceSummary();
    private readonly TextBlock _trashPageSummary = WorkspaceSummary();
    private readonly TextBlock _collectionPageSummary = WorkspaceSummary();

    private static TextBlock WorkspaceSummary() => new()
    {
        TextWrapping = TextWrapping.Wrap, FontSize = Ui.Text.Caption, LineHeight = 20, Classes = { "muted" }
    };

    private void InitializeWorkspaceDesign()
    {
        Classes.Add("workbench-ui");
        InitializeRevenueDrop();
        StartMobilePublication();
        UseListToolbar(PrintersWorkspacePanel, [], _printerPageSummary, "Nouvelle imprimante");
        DecorateInventoryPage(RevenuesWorkspacePanel, "Livre des recettes", _revenuePageSummary, "Enregistrer un encaissement");
        if (RevenuesWorkspacePanel.Child is Grid { Children: [Grid revenueToolbar, ..] } && revenueToolbar.Children[0] is StackPanel revenueHeading) revenueHeading.Children.Add(_revenueThresholdNote);
        DecorateInventoryPage(PurchasesWorkspacePanel, "Registre des achats", _purchasePageSummary, "Enregistrer un achat");
        // The page title already says « Filaments »: the toolbar carries search, family, count and the creation button.
        UseListToolbar(FilamentsWorkspacePanel, [ListToolbar.Search(ListToolbar.Detach(FilamentSearch)), ListToolbar.Detach(FilamentFamily), ListToolbar.Detach(FilamentClearButton)], _filamentPageSummary, "Nouveau filament");
        // The emptied filter row goes away entirely, so the table sits right under the toolbar like on the other lists.
        if (FilamentsWorkspacePanel.Child is Grid filamentLayout && FilamentList.Parent is ScrollViewer filamentScroll)
        {
            filamentLayout.Children.Remove(FilamentFilters);
            filamentLayout.RowDefinitions.RemoveAt(1);
            Grid.SetRow(filamentScroll, 1);
        }
        FilamentFamily.Width = 220;
        UseListToolbar(TrashWorkspacePanel, [], _trashPageSummary, null);
        UseListToolbar(CollectionsWorkspacePanel, [ListToolbar.Detach(CollectionFilters)], _collectionPageSummary, "Nouvelle collection");
        foreach (var panel in new[] { ProjectsWorkspacePanel, ClientsWorkspacePanel, CollectionsWorkspacePanel, FilamentsWorkspacePanel, PrintersWorkspacePanel, RevenuesWorkspacePanel, PurchasesWorkspacePanel, TrashWorkspacePanel, FolderWorkspacePanel })
            panel.Classes.Add("workspace-sheet");
    }

    private static void DecorateInventoryPage(Border page, string title, TextBlock summary, string? actionLabel)
    {
        if (page.Child is not Grid { Children: [Grid toolbar, ..] } || toolbar.Children[0] is not StackPanel heading) return;
        heading.Spacing = 4;
        heading.Children.Add(new TextBlock { Text = title, FontSize = Ui.Text.Title, Classes = { "brand-heading" }, TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(summary);
        if (actionLabel is not null && toolbar.Children.OfType<StackPanel>().LastOrDefault()?.Children.OfType<Button>().LastOrDefault() is { } action)
        {
            action.Content = Icons.Label("IconPlus", actionLabel, 16);
            Avalonia.Automation.AutomationProperties.SetName(action, actionLabel);
        }
    }

    /// <summary>Replaces a page's first toolbar row with the shared <see cref="ListToolbar"/>, keeping its creation button.</summary>
    private static void UseListToolbar(Border page, IEnumerable<Control> left, TextBlock count, string? actionLabel)
    {
        if (page.Child is not Grid { Children: [Grid old, ..] } layout) return;
        var left0 = left.ToArray();
        var action = old.Children.OfType<StackPanel>().LastOrDefault()?.Children.OfType<Button>().LastOrDefault();
        if (actionLabel is not null && action is not null)
        {
            action.Content = Icons.Label("IconPlus", actionLabel, 16);
            Avalonia.Automation.AutomationProperties.SetName(action, actionLabel);
        }
        count.TextWrapping = TextWrapping.NoWrap;
        var toolbar = ListToolbar.Create(left0, actionLabel is not null && action is not null ? [count, ListToolbar.Detach(action)] : [count]);
        Grid.SetRow(toolbar, Grid.GetRow(old));
        layout.Children[layout.Children.IndexOf(old)] = toolbar;
    }

    private static string ReadableColumnHeading(string value)
        => string.IsNullOrWhiteSpace(value) ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLower(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
}
