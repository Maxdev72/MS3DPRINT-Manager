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

/// <summary>Home page: recent projects, attention list and quick actions.</summary>
public partial class MainWindow
{
    private static Control DashboardProjectTitle(ProjectSummary project, bool large = false)
    {
        var title = new TextBlock
        {
            Text = project.Title(), FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (large) { title.FontSize = 16; title.LineHeight = 22; }
        if (project.Priority == ProjectPriority.None) return title;
        var flag = ProjectPriorityVisual.Flag(project.Priority);
        flag.Classes.Add("dashboard-priority");
        flag.VerticalAlignment = VerticalAlignment.Top;
        flag.Margin = new Avalonia.Thickness(0, large ? 2 : 0, 0, 0);
        ToolTip.SetTip(flag, ProjectPriorityVisual.Text(project.Priority));
        Avalonia.Automation.AutomationProperties.SetName(flag, ProjectPriorityVisual.Text(project.Priority));
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        Grid.SetColumn(title, 1);
        line.Children.Add(flag); line.Children.Add(title);
        return line;
    }

    private static string DashboardProjectName(ProjectSummary project, string deadline)
        => $"Ouvrir {project.Title()} · {project.Reference}"
            + (project.Priority == ProjectPriority.None ? "" : " · " + ProjectPriorityVisual.Text(project.Priority))
            + " · " + deadline;

    private void BuildWorkQueue(WorkspaceDashboard dashboard)
    {
        var projects = dashboard.WorkQueue;
        RecentProjectsList.Children.Clear();
        if (projects.Count == 0)
        {
            RecentProjectsList.Children.Add(dashboard.ProjectCount == 0
                ? CreateEmptyState("IconProjects", "Votre atelier est prêt", "Créez un projet pour suivre ses fichiers, son estimation et sa fabrication.", "Nouveau projet", CreateProject_Click)
                : CreateEmptyState("IconCheck", "Aucun projet actif à reprendre", "Les projets terminés et archivés restent consultables dans la liste des projets.", "Consulter les archives", async (_, _) => await OpenFilteredAsync("Projets", "ARCHIVES")));
            return;
        }
        foreach (var project in projects)
        {
            var tasks = ProjectTasks.Items(project.Profile);
            var nextTask = ProjectTasks.Next(project.Profile);
            var actionText = nextTask is not null ? "Prochaine tâche : " + nextTask.Title
                : tasks.Count > 0 ? "Toutes les tâches sont terminées · vérifier le projet"
                : project.Profile is null ? "Compléter la fiche du projet"
                : project.Status == ProjectStatus.InProgress ? "Poursuivre la fabrication" : "Préparer le projet";
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 14 };
            var text = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(DashboardProjectTitle(project, large: true));
            text.Children.Add(new TextBlock { Text = project.Reference + " · " + project.ClientTitle(), Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis });
            var nextAction = new TextBlock { Text = actionText, Classes = { "queue-next-action" }, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(nextAction, actionText);
            text.Children.Add(nextAction);
            var state = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            var badge = CreateStatusBadge(project.StatusLabel); badge.HorizontalAlignment = HorizontalAlignment.Right;
            state.Children.Add(badge);
            if (tasks.Count > 0) state.Children.Add(new TextBlock { Text = $"{tasks.Count(t => t.IsCompleted)} / {tasks.Count} tâches", Classes = { "caption" } });
            var (deadline, variant) = ProjectDeadlineLabel(project);
            state.Children.Add(new TextBlock { Text = deadline, FontSize = 13, Foreground = (IBrush?)Avalonia.Application.Current?.FindResource(variant == "danger" || variant == "warning" ? "WarningBrush" : "MutedTextBrush") });
            var arrow = Icons.Glyph("IconArrowRight", 18); Grid.SetColumn(arrow, 2);
            Grid.SetColumn(state, 1);
            content.Children.Add(text);
            content.Children.Add(state); content.Children.Add(arrow);
            var row = new Button { Classes = { "workspace-card", "queue-row" }, Tag = project, Content = content };
            var rowName = DashboardProjectName(project, deadline) + " · " + actionText;
            Avalonia.Automation.AutomationProperties.SetName(row, rowName);
            ToolTip.SetTip(row, rowName);
            if (tasks.Count > 0)
                row.Click += async (_, _) => await OpenProjectTaskTrackingAsync(project);
            else row.Click += RecentProject_Click;
            RecentProjectsList.Children.Add(row);
        }
    }

    private static (string Text, string Variant) ProjectDeadlineLabel(ProjectSummary project)
    {
        if (project.Profile?.DueDate is not { } due) return ("Sans échéance", "neutral");
        var days = due.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber;
        return days switch
        {
            < 0 => ($"En retard de {-days} j", "danger"),
            0 => ("Aujourd’hui", "warning"),
            1 => ("Demain", "warning"),
            <= 7 => ($"Dans {days} j", "warning"),
            _ => ($"{due:dd/MM/yyyy}", "neutral")
        };
    }

    private void BuildAttention(WorkspaceDashboard dashboard)
    {
        AttentionList.Children.Clear();
        foreach (var project in dashboard.Deadlines)
        {
            var (text, variant) = ProjectDeadlineLabel(project);
            var row = new StackPanel { Spacing = 6 };
            var labels = new StackPanel { Spacing = 1 };
            labels.Children.Add(DashboardProjectTitle(project));
            labels.Children.Add(new TextBlock { Text = project.Reference, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            var badge = new Border { Classes = { "badge", variant }, HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = text } };
            row.Children.Add(labels);
            row.Children.Add(badge);
            var open = new Button { Classes = { "workspace-card" }, MinHeight = 52, Padding = new Avalonia.Thickness(12, 8), Tag = project, Content = row };
            Avalonia.Automation.AutomationProperties.SetName(open, DashboardProjectName(project, text));
            ToolTip.SetTip(open, DashboardProjectName(project, text));
            open.Click += RecentProject_Click;
            AttentionList.Children.Add(open);
        }

        void AddShortcut(string text, string destination, string filterTag)
        {
            var button = new Button { Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Content = text };
            button.Click += async (_, _) => await OpenFilteredAsync(destination, filterTag);
            AttentionList.Children.Add(button);
        }

        if (dashboard.IncompleteProjects > 0)
            AddShortcut(dashboard.IncompleteProjects == 1 ? "1 projet à compléter" : $"{dashboard.IncompleteProjects} projets à compléter", "Projets", "TODO");
        if (dashboard.IncompleteClients > 0)
            AddShortcut(dashboard.IncompleteClients == 1 ? "1 fiche client à compléter" : $"{dashboard.IncompleteClients} fiches client à compléter", "Clients", "Fiche à compléter");

        if (AttentionList.Children.Count == 0)
            AttentionList.Children.Add(new TextBlock { Text = "Rien d’urgent : toutes les fiches sont à jour et aucune échéance n’est enregistrée.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
    }

    private async Task OpenFilteredAsync(string destination, string filterTag)
    {
        if (NavigationButtons().FirstOrDefault(button => button.Tag as string == destination) is not { } target) return;
        if (destination == "Projets") { _projectStatusFilter = filterTag; SelectChip(ProjectStatusFilters, filterTag); }
        else { _clientKindFilter = filterTag; SelectChip(ClientStatusFilters, filterTag); }
        NavigationButton_Click(target, new RoutedEventArgs());
        await Task.CompletedTask;
    }

    private async void DashboardCreateClient_Click(object? sender, RoutedEventArgs e) => CreateClient_Click(sender, e);

    private void DashboardEstimate_Click(object? sender, RoutedEventArgs e)
    {
        if (NavigationButtons().FirstOrDefault(button => button.Tag as string == "Estimations") is { } target)
            NavigationButton_Click(target, e);
        ResetEstimate_Click(sender, e); // « Nouvelle estimation » always opens a blank calculator
    }
}
