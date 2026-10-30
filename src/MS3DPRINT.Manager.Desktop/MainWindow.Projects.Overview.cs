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
        var content = new StackPanel { Spacing = 18 };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        heading.Children.Add(new TextBlock { Text = "Suivi du projet", FontSize = 24, FontWeight = FontWeight.SemiBold, Classes = { "brand-heading" }, TextWrapping = TextWrapping.Wrap });
        var edit = new Button { Content = Icons.Label("IconEdit", "Modifier"), Tag = project, Classes = { "ghost" } };
        edit.Click += EditProject_Click; Grid.SetColumn(edit, 1); heading.Children.Add(edit);
        content.Children.Add(heading);

        var facts = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 24, LineSpacing = 12 };
        facts.Children.Add(CreateProjectStatusCell(project));
        facts.Children.Add(CreateProjectPriorityButton(project, label: true));
        var due = new Button { Content = project.Profile?.DueDate is { } date ? $"Échéance · {date:dd/MM/yyyy}" : "Définir une échéance", Tag = project, Classes = { "ghost" }, Padding = new Thickness(8, 4) };
        due.Click += EditProject_Click;
        facts.Children.Add(due);
        facts.Children.Add(new TextBlock { Text = project.ClientTitle(), Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(facts);
        content.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(project.Profile?.Description)
                ? project.Profile is null ? "Complétez la fiche pour préciser le suivi de ce projet." : "Décrivez la pièce et les attentes du client depuis Modifier."
                : project.Profile.Description,
            TextWrapping = TextWrapping.Wrap, FontSize = 15, LineHeight = 24, Classes = { "muted" }
        });
        if (!string.IsNullOrWhiteSpace(project.Profile?.Notes))
            content.Children.Add(EstimateSection("Notes de l’atelier", "Informations internes au projet", new TextBlock { Text = project.Profile.Notes, TextWrapping = TextWrapping.Wrap, LineHeight = 22 }));

        var actions = new WrapPanel { ItemSpacing = 10, LineSpacing = 10 };
        var estimate = new Button { Content = Icons.Label("IconEstimates", "Préparer une estimation"), Classes = { "primary-action" } };
        estimate.Click += async (_, _) => await StartProjectEstimateAsync(project);
        var folder = new Button { Content = Icons.Label("IconOpen", "Ouvrir le dossier"), Tag = project.ProjectPath };
        folder.Click += OpenFolder_Click;
        var collect = new Button { Content = Icons.Label("IconBook", "Enregistrer un encaissement") };
        collect.Click += async (_, _) => await RecordProjectCollectionAsync(project);
        actions.Children.Add(estimate); actions.Children.Add(collect); actions.Children.Add(folder); content.Children.Add(actions);
        var duplicate = new Button { Content = Icons.Label("IconCopy", "Dupliquer"), Tag = project };
        duplicate.Click += DuplicateProject_Click; actions.Children.Add(duplicate);
        var copy = CreateOverflowActions(CreateProjectCopyActions(project));
        copy.Content = Icons.Label("IconCopy", "Copier");
        Avalonia.Automation.AutomationProperties.SetName(copy, "Copier les informations du projet");
        ToolTip.SetTip(copy, "Copier le nom, la référence ou le chemin du projet");
        actions.Children.Add(copy);

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
