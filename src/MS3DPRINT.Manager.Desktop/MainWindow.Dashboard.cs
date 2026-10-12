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
    private void BuildRecentProjects(IReadOnlyList<ProjectSummary> projects)
    {
        RecentProjectsList.Children.Clear();
        if (projects.Count == 0)
        {
            RecentProjectsList.Children.Add(CreateEmptyState("IconProjects", "Aucun projet pour l’instant",
                "Créez votre premier projet pour le retrouver ici.", "Nouveau projet", CreateProject_Click));
            return;
        }
        foreach (var project in projects)
        {
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = $"{project.Reference} · {project.Title()}", FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = project.ClientTitle(), Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis });
            var badge = CreateStatusBadge(project.StatusLabel);
            Grid.SetColumn(badge, 1);
            content.Children.Add(text);
            content.Children.Add(badge);
            var row = new Button { Classes = { "workspace-card" }, Tag = project, Content = content };
            row.Click += RecentProject_Click;
            RecentProjectsList.Children.Add(row);
        }
    }

    private void BuildAttention(WorkspaceDashboard dashboard)
    {
        AttentionList.Children.Clear();
        var today = DateOnly.FromDateTime(DateTime.Today);

        foreach (var project in dashboard.Deadlines)
        {
            var due = project.Profile!.DueDate!.Value;
            var days = due.DayNumber - today.DayNumber;
            var (text, variant) = days switch
            {
                < 0 => ($"En retard de {-days} j", "danger"),
                0 => ("Aujourd’hui", "warning"),
                1 => ("Demain", "warning"),
                <= 7 => ($"Dans {days} j", "warning"),
                _ => ($"{due:dd/MM}", "neutral")
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
            var labels = new StackPanel { Spacing = 1 };
            labels.Children.Add(new TextBlock { Text = project.Title(), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            labels.Children.Add(new TextBlock { Text = "Échéance · " + project.Reference, Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis });
            var badge = new Border { Classes = { "badge", variant }, Child = new TextBlock { Text = text } };
            Grid.SetColumn(badge, 1);
            row.Children.Add(labels);
            row.Children.Add(badge);
            var open = new Button { Classes = { "workspace-card" }, MinHeight = 52, Padding = new Avalonia.Thickness(12, 8), Tag = project, Content = row };
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
    }
}
