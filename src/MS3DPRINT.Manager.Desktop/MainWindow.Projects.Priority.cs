using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private sealed record PriorityFilterChoice(ProjectPriority? Value);
    private ProjectPriority? _projectPriorityFilter;
    private readonly ComboBox _projectPriorityFilterBox = new() { Width = 230 };
    private readonly Button _projectPrioritySortButton = new() { Classes = { "ghost" } };

    private void InitializeProjectPriorities()
    {
        _projectPriorityFilterBox.ItemsSource = new PriorityFilterChoice[] { new(null), new(ProjectPriority.High), new(ProjectPriority.Medium), new(ProjectPriority.Low), new(ProjectPriority.None) };
        _projectPriorityFilterBox.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<PriorityFilterChoice>((choice, _) => choice?.Value is { } priority ? ProjectPriorityVisual.Label(priority) : new TextBlock { Text = "Toutes les priorités" });
        _projectPriorityFilterBox.SelectedIndex = 0;
        Avalonia.Automation.AutomationProperties.SetName(_projectPriorityFilterBox, "Filtrer les projets par priorité");
        _projectPriorityFilterBox.SelectionChanged += (_, _) => { _projectPriorityFilter = (_projectPriorityFilterBox.SelectedItem as PriorityFilterChoice)?.Value; RenderProjects(); };
        _projectPrioritySortButton.Click += (_, _) =>
        {
            _projectSortAscending = _projectSortColumn != 4 || !_projectSortAscending;
            _projectSortColumn = 4; RenderProjects();
        };
        var filters = new WrapPanel { ItemSpacing = 12, LineSpacing = 8 };
        filters.Children.Add(_projectPriorityFilterBox); filters.Children.Add(_projectPrioritySortButton);
        ProjectFilterBar.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(filters, 2); Grid.SetColumnSpan(filters, 3); ProjectFilterBar.Children.Add(filters);
        UpdatePrioritySortButton();
    }

    private void UpdatePrioritySortButton()
    {
        _projectPrioritySortButton.Content = Icons.Label("IconFlag", _projectSortColumn == 4 ? _projectSortAscending ? "Priorité : haute d’abord" : "Priorité : basse d’abord" : "Trier par priorité");
        _projectPrioritySortButton.Classes.Set("selected", _projectSortColumn == 4);
    }

    private async Task<MS3DPRINT.Manager.Core.Clients.ClientSummary?> EnsurePriorityClientAsync(MS3DPRINT.Manager.Core.Clients.ClientSummary client, ProjectPriority priority)
    {
        if (client.Profile is not null || priority == ProjectPriority.None) return client;
        var profile = await CompleteClientProfileAsync(client);
        return profile is null ? null : client with { Profile = profile };
    }

    private Button CreateProjectPriorityButton(ProjectSummary project, bool label = false)
    {
        var options = ProjectPriorityVisual.Choices.Select(priority =>
        {
            var option = new Button { Content = ProjectPriorityVisual.Label(priority) };
            option.Click += async (_, _) => await SetProjectPriorityAsync(project, priority);
            return option;
        }).ToArray();
        var button = CreateOverflowActions(options);
        button.Classes.Add("project-priority");
        button.Classes.Add("interactive-cell");
        button.Content = label ? ProjectPriorityVisual.Label(project.Priority) : ProjectPriorityVisual.Flag(project.Priority);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTip.SetTip(button, ProjectPriorityVisual.Text(project.Priority) + " · Modifier");
        Avalonia.Automation.AutomationProperties.SetName(button, ProjectPriorityVisual.Text(project.Priority) + " · " + project.Title());
        return button;
    }

    private async Task SetProjectPriorityAsync(ProjectSummary project, ProjectPriority priority)
    {
        if (project.Priority == priority) return;
        var root = WorkspaceRoot;
        try
        {
            var profile = project.Profile;
            if (profile is null)
            {
                var client = (await Task.Run(() => new WorkspaceClientReader().Load(root))).FirstOrDefault(candidate => string.Equals(candidate.ClientPath, project.ClientPath, StringComparison.OrdinalIgnoreCase));
                if (client is null) { Notify("Client introuvable pour ce projet.", error: true); return; }
                var clientProfile = client.Profile ?? await CompleteClientProfileAsync(client);
                if (clientProfile is null) return;
                profile = await Task.Run(() => new WorkspaceProjectCreator().CompleteExisting(root, project, clientProfile, DisplayNames.Format(project.ProjectName)));
            }
            var updated = await Task.Run(() => new ProjectProfileStore(new WorkspaceMetadataPaths(root)).Update(profile with { Priority = priority }, profile.UpdatedAt));
            if (!string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return;
            Notify(ProjectPriorityVisual.Text(priority) + " enregistrée.");
            if (_projectListMode) await LoadProjectsAsync();
            else await LoadProjectDirectoryAsync(project with { Profile = updated }, project.ProjectPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }
}
