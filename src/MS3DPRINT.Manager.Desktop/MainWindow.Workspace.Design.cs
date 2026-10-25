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

    private static TextBlock WorkspaceSummary() => new()
    {
        TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20, Classes = { "muted" }
    };

    private void InitializeWorkspaceDesign()
    {
        Classes.Add("workbench-ui");
        InitializeRevenueDrop();
        DecorateInventoryPage(PrintersWorkspacePanel, "Votre parc d’imprimantes", _printerPageSummary, "Nouvelle imprimante");
        DecorateInventoryPage(RevenuesWorkspacePanel, "Livre des recettes", _revenuePageSummary, "Enregistrer un encaissement");
        if (RevenuesWorkspacePanel.Child is Grid { Children: [Grid revenueToolbar, ..] } && revenueToolbar.Children[0] is StackPanel revenueHeading) revenueHeading.Children.Add(_revenueThresholdNote);
        DecorateInventoryPage(PurchasesWorkspacePanel, "Registre des achats", _purchasePageSummary, "Enregistrer un achat");
        DecorateInventoryPage(FilamentsWorkspacePanel, "Matières de l’atelier", _filamentPageSummary, "Nouveau filament");
        DecorateInventoryPage(TrashWorkspacePanel, "Éléments supprimés", _trashPageSummary, null);
        foreach (var panel in new[] { ProjectsWorkspacePanel, ClientsWorkspacePanel, CollectionsWorkspacePanel, FilamentsWorkspacePanel, PrintersWorkspacePanel, RevenuesWorkspacePanel, PurchasesWorkspacePanel, TrashWorkspacePanel, FolderWorkspacePanel })
            panel.Classes.Add("workspace-sheet");
    }

    private static void DecorateInventoryPage(Border page, string title, TextBlock summary, string? actionLabel)
    {
        if (page.Child is not Grid { Children: [Grid toolbar, ..] } || toolbar.Children[0] is not StackPanel heading) return;
        heading.Spacing = 5;
        heading.Children.Add(new TextBlock { Text = title, FontSize = 22, Classes = { "brand-heading" }, TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(summary);
        if (actionLabel is not null && toolbar.Children.OfType<StackPanel>().LastOrDefault()?.Children.OfType<Button>().LastOrDefault() is { } action)
        {
            action.Content = Icons.Label("IconPlus", actionLabel, 16);
            Avalonia.Automation.AutomationProperties.SetName(action, actionLabel);
        }
    }

    private static string ReadableColumnHeading(string value)
        => string.IsNullOrWhiteSpace(value) ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLower(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
}
