using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private bool _projectCalendarView;
    private bool _projectCalendarAgenda;
    private bool _projectCalendarFiltersVisible;
    private bool _projectDeadlineBusy;
    private DateOnly _projectCalendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateOnly _projectCalendarDay = DateOnly.FromDateTime(DateTime.Today);
    private static readonly CultureInfo CalendarCulture = CultureInfo.GetCultureInfo("fr-FR");

    private void RenderProjectCalendar(ProjectSummary[] projects)
    {
        var root = WorkspaceRoot;
        var body = new StackPanel { Spacing = 14 };
        var toolbar = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        Button Action(string label, string name, Action action)
        {
            var button = new Button { Content = label, Classes = { "ghost" } };
            AutomationProperties.SetName(button, name); button.Click += (_, _) => action(); toolbar.Children.Add(button); return button;
        }
        var month = Action("Mois", "Vue mensuelle des échéances", () => { _projectCalendarAgenda = false; RenderProjects(); });
        var agenda = Action("À venir", "Vue à venir des échéances", () => { _projectCalendarAgenda = true; RenderProjects(); });
        month.Classes.Set("selected", !_projectCalendarAgenda); agenda.Classes.Set("selected", _projectCalendarAgenda);
        var filtersActive = _projectStatusFilter.Length > 0 || _projectPriorityFilter is not null;
        var filters = Action(filtersActive ? "Filtres actifs" : "Filtres", "Afficher les filtres du calendrier", () => { _projectCalendarFiltersVisible = !_projectCalendarFiltersVisible; RenderProjects(); });
        filters.Classes.Set("selected", filtersActive || _projectCalendarFiltersVisible);
        Action("Aujourd’hui", "Revenir à aujourd’hui", () => { _projectCalendarDay = DateOnly.FromDateTime(DateTime.Today); _projectCalendarMonth = new(_projectCalendarDay.Year, _projectCalendarDay.Month, 1); RenderProjects(); });
        body.Children.Add(toolbar);
        if (_projectCalendarAgenda)
        {
            var dates = ProjectCalendar.Agenda(projects);
            body.Children.Add(new TextBlock { Text = "Échéances · en retard et à venir", FontSize = 20, FontWeight = FontWeight.SemiBold });
            if (dates.Count == 0) body.Children.Add(CalendarHint("Aucune échéance pour les projets affichés."));
            // Use the common pager to keep long histories bounded.
            var agendaList = new StackPanel { Spacing = 4 };
            PagedRows.Render(agendaList, dates, p => BuildCalendarProject(p, root), () => CalendarHint($"{dates.Count} projet(s) avec échéance"),
                (root, ProjectFilterBox.Text, _projectStatusFilter, _projectPriorityFilter));
            body.Children.Add(agendaList);
        }
        else
        {
            var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
            var previous = CalendarNavigation("IconArrowLeft", "Mois précédent", -1);
            var next = CalendarNavigation("IconArrowRight", "Mois suivant", 1);
            previous.IsEnabled = _projectCalendarMonth.Year > 1 || _projectCalendarMonth.Month > 1;
            next.IsEnabled = _projectCalendarMonth.Year < 9999 || _projectCalendarMonth.Month < 12;
            var caption = new TextBlock { Text = CalendarCulture.TextInfo.ToTitleCase(_projectCalendarMonth.ToString("MMMM yyyy", CalendarCulture)), FontSize = 20, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(caption, 1); Grid.SetColumn(next, 2); heading.Children.Add(previous); heading.Children.Add(caption); heading.Children.Add(next); body.Children.Add(heading);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto"), ColumnSpacing = 3, RowSpacing = 3 };
            var weekdays = new[] { "Lun.", "Mar.", "Mer.", "Jeu.", "Ven.", "Sam.", "Dim." };
            for (var i = 0; i < 7; i++)
            {
                var label = CalendarHint(weekdays[i]); label.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(label, i); grid.Children.Add(label);
            }
            var days = ProjectCalendar.Month(_projectCalendarMonth, projects);
            for (var i = 0; i < days.Count; i++)
            {
                var day = days[i];
                var button = new Button { Classes = { "project-calendar-day" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(8, 5), MinHeight = 52, IsEnabled = day.Date is not null };
                if (day.Date is { } date)
                {
                    button.Tag = date;
                    button.Classes.Set("selected", date == _projectCalendarDay);
                    button.Classes.Set("outside-month", date.Month != _projectCalendarMonth.Month);
                    var content = new StackPanel { Spacing = 4 };
                    content.Children.Add(new TextBlock { Text = date.Day.ToString(), FontWeight = date == DateOnly.FromDateTime(DateTime.Today) ? FontWeight.Bold : FontWeight.Normal });
                    if (day.Projects.Count > 0)
                    {
                        content.Children.Add(new TextBlock { Text = $"{day.Projects.Count} échéance{(day.Projects.Count == 1 ? "" : "s")}", FontSize = 12, Foreground = (IBrush?)Application.Current?.FindResource("CyanBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
                    }
                    button.Content = content;
                    var name = date.ToString("dddd d MMMM yyyy", CalendarCulture) + (day.Projects.Count == 0 ? " · aucune échéance" : " · " + string.Join(", ", day.Projects.Select(p => p.Title())));
                    AutomationProperties.SetName(button, name); ToolTip.SetTip(button, name);
                    button.Click += (_, _) => { _projectCalendarDay = date; _projectCalendarMonth = new(date.Year, date.Month, 1); RenderProjects(); };
                }
                Grid.SetColumn(button, i % 7); Grid.SetRow(button, i / 7 + 1); grid.Children.Add(button);
            }
            body.Children.Add(grid);
            body.Children.Add(new TextBlock { Text = _projectCalendarDay.ToString("dddd d MMMM yyyy", CalendarCulture), FontSize = 18, FontWeight = FontWeight.SemiBold });
            var selected = ProjectCalendar.Agenda(projects).Where(p => p.Profile!.DueDate == _projectCalendarDay).ToArray();
            if (selected.Length == 0) body.Children.Add(CalendarHint("Aucune échéance ce jour. Planifiez un projet depuis « À planifier » ci-dessous."));
            foreach (var project in selected) body.Children.Add(BuildCalendarProject(project, root));
        }
        var unplanned = projects.Where(p => p.Profile?.DueDate is null).ToArray();
        var unplannedBody = new StackPanel { Spacing = 6 };
        PagedRows.Render(unplannedBody, unplanned, p => BuildCalendarProject(p, root), () => CalendarHint("Choisissez une date pour ces projets. Aucun fichier ne sera déplacé."),
            (root, ProjectFilterBox.Text, _projectStatusFilter, _projectPriorityFilter));
        body.Children.Add(new Expander { Header = $"À planifier · {unplanned.Length} projet{(unplanned.Length == 1 ? "" : "s")}", Content = unplannedBody, IsExpanded = unplanned.Length > 0 && projects.All(p => p.Profile?.DueDate is null), Classes = { "estimate-section" }, HorizontalAlignment = HorizontalAlignment.Stretch });
        ProjectList.Children.Add(body);
    }

    private Button CalendarNavigation(string icon, string label, int direction)
    {
        var button = new Button { Content = Icons.Glyph(icon, 18), Classes = { "icon-action" } };
        AutomationProperties.SetName(button, label); ToolTip.SetTip(button, label);
        button.Click += (_, _) => { _projectCalendarMonth = _projectCalendarMonth.AddMonths(direction); _projectCalendarDay = _projectCalendarMonth; RenderProjects(); };
        return button;
    }

    private static TextBlock CalendarHint(string text) => new() { Text = text, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

    private Control BuildCalendarProject(ProjectSummary project, string root)
    {
        var text = new StackPanel { Spacing = 4 };
        text.Children.Add(new TextBlock { Text = project.Title(), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(CalendarHint(project.Reference + " · " + project.ClientTitle()));
        var facts = new WrapPanel { ItemSpacing = 12, LineSpacing = 6 };
        if (project.Profile?.DueDate is { } due) facts.Children.Add(CalendarHint(due.ToString("dd/MM/yyyy") + " · " + ProjectDeadlineLabel(project).Text));
        facts.Children.Add(CreateStatusBadge(project.StatusLabel));
        if (project.Priority != ProjectPriority.None) facts.Children.Add(ProjectPriorityVisual.Label(project.Priority));
        text.Children.Add(facts);
        var actions = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        var open = new Button { Content = "Ouvrir le projet", Tag = project, Classes = { "ghost" } };
        AutomationProperties.SetName(open, "Ouvrir le projet : " + project.Title()); open.Click += ProjectEntry_Click; actions.Children.Add(open);
        var plan = new Button { Content = project.Profile is null ? "Compléter la fiche" : project.Profile.DueDate is null ? "Planifier" : "Modifier l’échéance", Classes = { "ghost" } };
        AutomationProperties.SetName(plan, (project.Profile?.DueDate is null ? "Planifier : " : "Modifier l’échéance : ") + project.Title());
        plan.Click += async (_, _) =>
        {
            if (!ProjectCalendarContextCurrent(root) || _projectDeadlineBusy) return;
            if (project.Profile is null) { EditProject_Click(new Button { Tag = project }, new RoutedEventArgs()); return; }
            await new ProjectDeadlineWindow(project.Title(), project.Profile.DueDate ?? _projectCalendarDay,
                date => SaveProjectDeadlineAsync(root, project, date)).ShowDialog<bool>(this);
        };
        actions.Children.Add(plan);
        if (project.Profile?.DueDate is not null)
        {
            var remove = new Button { Content = Icons.Glyph("IconClose", 16), Classes = { "icon-action" } };
            AutomationProperties.SetName(remove, "Retirer l’échéance : " + project.Title()); ToolTip.SetTip(remove, "Retirer l’échéance");
            remove.Click += async (_, _) =>
            {
                if (!ProjectCalendarContextCurrent(root) || _projectDeadlineBusy) return;
                if (!await new ConfirmRemovalWindow("Échéance de " + project.Title(), "La date sera retirée du calendrier, de la fiche projet et des alertes du tableau de bord. Les tâches et les fichiers seront conservés.").ShowDialog<bool>(this)) return;
                var error = await SaveProjectDeadlineAsync(root, project, null);
                if (error is not null) Notify(error, error: true);
            };
            actions.Children.Add(remove);
        }
        text.Children.Add(actions);
        return new Border { Child = text, Padding = new Thickness(12), BorderBrush = (IBrush?)Application.Current?.FindResource("SubtleBorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    private bool ProjectCalendarContextCurrent(string root) => IsVisible && ProjectsWorkspacePanel.IsVisible && _projectListMode && _projectCalendarView && string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase);

    private async Task<string?> SaveProjectDeadlineAsync(string root, ProjectSummary project, DateOnly? date)
    {
        if (!ProjectCalendarContextCurrent(root) || project.Profile is null) return "Le contexte du projet a changé. Revenez au calendrier.";
        if (_projectDeadlineBusy) return "Un enregistrement est déjà en cours.";
        _projectDeadlineBusy = true;
        ProjectList.IsEnabled = false; ProjectFilterBar.IsEnabled = false; ProjectViewTabs.IsEnabled = false;
        try
        {
            var updated = await Task.Run(() => new ProjectProfileStore(new WorkspaceMetadataPaths(root)).Update(project.Profile with { DueDate = date }, project.Profile.UpdatedAt));
            if (ProjectCalendarContextCurrent(root))
            {
                _allProjects = _allProjects.Select(p => p.Profile?.Id == updated.Id ? p with { Profile = updated } : p).ToArray();
                if (date is { } selected) { _projectCalendarDay = selected; _projectCalendarMonth = new(selected.Year, selected.Month, 1); }
                RenderProjects();
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { return e.Message + " Actualisez les projets avant de réessayer."; }
        finally
        {
            _projectDeadlineBusy = false;
            ProjectList.IsEnabled = true; ProjectFilterBar.IsEnabled = !_projectCatalogUnavailable; ProjectViewTabs.IsEnabled = true;
        }
    }
}
