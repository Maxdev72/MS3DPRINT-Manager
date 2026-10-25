using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Globalization;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private void InitializeShellPresentation()
    {
        AddHandler(PointerPressedEvent, Workspace_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        foreach (var panel in new[] { ProjectsWorkspacePanel, ClientsWorkspacePanel, CollectionsWorkspacePanel, FilamentsWorkspacePanel, PrintersWorkspacePanel, RevenuesWorkspacePanel, TrashWorkspacePanel, FolderWorkspacePanel })
            panel.Classes.Add("table-surface");
        foreach (var button in NavigationButtons())
            if (button.Tag is string destination) ToolTip.SetTip(button, destination);
        ToolTip.SetTip(ViewerNavButton, "Visualiseur 3D");
        RenamePrimaryAction(ProjectsPanelActions, "Nouveau projet");
        RenamePrimaryAction(ClientsPanelActions, "Nouveau client");
        RefreshWorkspaceIdentity();
        InitializeBrandPresentation();
        InitializeSettingsPresentation();
        InitializeWorkspaceDesign();
        InitializeProjectPriorities();
    }

    private static void RenamePrimaryAction(Panel actions, string label)
    {
        if (actions.Children.OfType<Button>().LastOrDefault()?.Content is StackPanel content && content.Children.LastOrDefault() is TextBlock text)
            text.Text = label;
    }

    private void RefreshWorkspaceIdentity()
    {
        var root = WorkspaceRoot;
        WorkspaceNameText.Text = new DirectoryInfo(root).Name;
        ToolTip.SetTip(WorkspaceNameText, root);
    }

    private string _settingsCategory = "atelier";

    private void SettingsCategory_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string category }) return;
        _settingsCategory = category;
        SettingsSearchBox.Text = "";
        UpdateSettingsCategory();
        SettingsScroll.Offset = default;
    }

    private void AddSettingsCard(string category, Border card)
    {
        card.Tag = category;
        card.Classes.Add("settings-section");
        card.IsVisible = category == _settingsCategory;
        StorageList.Children.Add(card);
    }

    private void UpdateSettingsCategory()
    {
        var query = SettingsSearchBox.Text?.Trim() ?? "";
        var searching = query.Length > 0;
        foreach (var tab in SettingsCategories.Children.OfType<Button>())
            tab.Classes.Set("selected", !searching && tab.Tag as string == _settingsCategory);
        foreach (var card in StorageList.Children.OfType<Border>())
            card.IsVisible = searching ? MatchesSettingText(SettingsSearchText(card), query)
                : card.Tag as string == _settingsCategory;
        ClearSettingsSearchButton.IsVisible = searching;
        SettingsEmptyState.IsVisible = searching && !StorageList.Children.Any(card => card.IsVisible);
        if (!searching) BrandMotion.Enter(StorageList, MotionReduced, 8);
        UpdateSettingsHeading();
        if (searching)
        {
            var count = StorageList.Children.Count(card => card.IsVisible);
            SettingsSectionTitle.Text = "Résultats de recherche";
            SettingsDescription.Text = count + " section" + (count > 1 ? "s" : "") + " pour « " + query + " » · toutes les rubriques";
        }
        QueueSettingsMatches(query);
    }
}
