using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Core.Revenues;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private Control BuildProjectOverview(ProjectSummary project)
    {
        var overviewRoot = WorkspaceRoot;
        var content = new StackPanel { Spacing = 16 };
        // The name, reference and client are already the page heading: the sheet starts with the facts.

        // Each fact has its label, so the column reads as a form and not as a row of loose words.
        static Control Fact(string label, Control value)
        {
            var name = new TextBlock { Text = label, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
            value.VerticalAlignment = VerticalAlignment.Center; value.HorizontalAlignment = HorizontalAlignment.Left;
            if (value is Button) value.Margin = new Thickness(-Ui.Space.S2, 0, 0, 0); // the text of a button lines up with the plain values
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("96,*"), MinHeight = 36, Children = { name, value } };
            Grid.SetColumn(value, 1);
            return row;
        }
        var facts = new StackPanel { Spacing = 4 };
        facts.Children.Add(Fact("Statut", CreateProjectStatusCell(project)));
        facts.Children.Add(Fact("Priorité", CreateProjectPriorityButton(project, label: true)));
        var due = new Button { Content = project.Profile?.DueDate is { } date ? $"{date:dd/MM/yyyy}" : "Définir", Tag = project, Classes = { "ghost" }, Padding = new Thickness(8, 4) };
        due.Click += EditProject_Click;
        Avalonia.Automation.AutomationProperties.SetName(due, project.Profile?.DueDate is null ? "Définir une échéance" : "Modifier l’échéance");
        facts.Children.Add(Fact("Échéance", due));
        if (project.Profile is { } sheet)
            facts.Children.Add(Fact("Créé le", new TextBlock { Text = $"{sheet.CreatedAt.ToLocalTime():dd/MM/yyyy}" }));
        content.Children.Add(facts);
        content.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(project.Profile?.Description)
                ? project.Profile is null ? "Complétez la fiche pour préciser le suivi de ce projet." : "Décrivez la pièce et les attentes du client depuis Modifier."
                : project.Profile.Description,
            TextWrapping = TextWrapping.Wrap, FontSize = Ui.Text.Body, LineHeight = 24, Classes = { "muted" }
        });

        var estimate = new Button { Content = Icons.Label("IconEstimates", "Préparer une estimation"), Classes = { "primary-action" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        estimate.Click += async (_, _) => await StartProjectEstimateAsync(project);
        var folder = new Button { Content = Icons.Label("IconOpen", "Ouvrir le dossier"), Tag = project.ProjectPath };
        folder.Click += OpenFolder_Click;
        var collect = new Button { Content = Icons.Label("IconBook", "Enregistrer un encaissement"), HorizontalAlignment = HorizontalAlignment.Stretch };
        collect.Click += async (_, _) => await RecordProjectCollectionAsync(project);
        // Two actions that move the project forward stay visible; everything else sits in one menu.
        var edit = new Button { Content = Icons.Label("IconEdit", "Modifier la fiche"), Tag = project };
        edit.Click += EditProject_Click;
        var duplicate = new Button { Content = Icons.Label("IconCopy", "Dupliquer le projet"), Tag = project };
        duplicate.Click += DuplicateProject_Click;
        var more = CreateOverflowActions([edit, folder, .. CreateProjectCopyActions(project), duplicate]);
        Avalonia.Automation.AutomationProperties.SetName(more, "Autres actions du projet");
        ToolTip.SetTip(more, "Modifier la fiche, ouvrir le dossier, copier, dupliquer");
        var secondary = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { collect, more } };
        Grid.SetColumn(more, 1);
        content.Children.Add(new StackPanel { Spacing = 8, Children = { estimate, secondary } });

        if (!string.IsNullOrWhiteSpace(project.Profile?.Notes))
            content.Children.Add(EstimateSection("Notes de l’atelier", "Informations internes au projet", new TextBlock { Text = project.Profile.Notes, TextWrapping = TextWrapping.Wrap, LineHeight = 22 }));
        if (BuildProjectTasksSection(project) is { } tasks) content.Children.Add(tasks);

        content.Children.Add(BuildClassificationSection(project));
        content.Children.Add(BuildProjectBalanceSection(project));

        var history = new StackPanel { Spacing = 4 };
        try
        {
            var quotes = new EstimateStore(WorkspaceRoot).LoadReadable().Where(q => q.ProjectReference == project.Reference).OrderByDescending(q => q.CreatedAt).ToArray();
            if (quotes.Length > 0)
            {
                foreach (var quote in quotes)
                {
                    var open = new Button { Content = $"{quote.Name} · {EstimateMoney(quote.Result.FinalPrice)}", Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
                    open.Content = new TextBlock { Text = (string)open.Content, TextWrapping = TextWrapping.Wrap };
                    open.Click += async (_, _) =>
                    {
                        if (!string.Equals(overviewRoot, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return;
                        ShowWorkspace("Estimations");
                        PageTitle.Text = "Estimations";
                        PageSubtitle.Text = $"Estimation enregistrée · {project.Reference}";
                        if (!await LoadEstimateFilamentsAsync()) return;
                        LoadSavedEstimate_Click(new Button { Tag = quote }, new RoutedEventArgs());
                    };
                    history.Children.Add(open);
                }
                var linked = (Expander)EstimateSection($"Estimations liées ({quotes.Length})", "Retrouver les prix enregistrés pour ce projet", history);
                linked.IsExpanded = false;
                content.Children.Add(linked);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        {
            content.Children.Add(new TextBlock { Text = "Les estimations liées sont indisponibles. Retrouvez-les dans Estimations après vérification de l’espace de travail.", TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
        }
        return new Border { Classes = { "project-overview" }, Child = content };
    }

    /// <summary>Opens the revenue entry with the project's client, reference and the price of its latest quote as proposals.</summary>
    private async Task RecordProjectCollectionAsync(ProjectSummary project)
    {
        decimal? suggested = null;
        try
        {
            var root = WorkspaceRoot;
            suggested = await Task.Run(() => new EstimateStore(root).LoadReadable().Where(q => q.ProjectReference == project.Reference)
                .OrderByDescending(q => q.CreatedAt).Select(q => (decimal?)q.Result.FinalPrice).FirstOrDefault());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException) { }
        await OpenRevenueEntryAsync(null, new RevenueSuggestion(project.ClientTitle(), project.ClientPath, project.Reference, project.Title(), suggested));
    }

    private async Task StartProjectEstimateAsync(ProjectSummary project)
    {
        if (!MS3DPRINT.Manager.Core.Workspace.WorkspaceEntityPaths.IsInside(WorkspaceRoot, project.ProjectPath)) return;
        ShowWorkspace("Estimations");
        PageTitle.Text = "Estimations";
        PageSubtitle.Text = $"Préparation pour {project.Reference} · {project.ClientTitle()}";
        if (!await LoadEstimateFilamentsAsync()) return;
        if (!ResetEstimate_Click(this, new RoutedEventArgs())) return;
        EstimateProject.SelectedItem = (EstimateProject.ItemsSource as IEnumerable<ProjectSummary>)?.FirstOrDefault(p => p.Reference == project.Reference);
        EstimateName.Text = project.Title();
    }
}
