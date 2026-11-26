using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Tasks;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Desktop;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    private async Task<MainWindow> OpenTasksPage()
    {
        var owner = await Open(); owner.Show();
        Invoke(owner, "ShowWorkspace", "Tâches"); await (Task)Invoke(owner, "LoadTasksAsync")!;
        Settle(owner);
        return owner;
    }

    private static void Settle(MainWindow owner) { Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); }
    private static StackPanel TaskListOf(MainWindow owner) => owner.FindControl<StackPanel>("TaskList")!;
    private static StackPanel TaskDetailOf(MainWindow owner) => owner.FindControl<StackPanel>("TaskDetail")!;
    private static StackPanel TaskViewsOf(MainWindow owner) => owner.FindControl<StackPanel>("TaskViewList")!;
    private static Button TaskView(MainWindow owner, string label) => TaskViewsOf(owner).Children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == label);
    private static T Named<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
    private static void Press(Control target, Key key) => target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = target });
    private static void ClickNamed(Control root, string name) => Named<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Button RemoveTaskButton(MainWindow owner) => TaskDetailOf(owner).GetVisualDescendants().OfType<Button>()
        .Single(b => b.Content is StackPanel c && c.Children.OfType<TextBlock>().Any(t => t.Text == "Retirer la tâche"));

    private static async Task Until(MainWindow owner, Func<bool> ready)
    {
        for (var i = 0; i < 250 && !ready(); i++) { await Task.Delay(20); Settle(owner); }
        Assert.True(ready());
    }

    private static Button RowButton(MainWindow owner, string name) => Named<Button>(TaskListOf(owner), name);
    private static TextBox PickerSearch(Button owner) => (TextBox)((StackPanel)((Flyout)owner.Flyout!).Content!).Children[0];
    private static string[] PickerChoices(Button owner) => ((StackPanel)((StackPanel)((Flyout)owner.Flyout!).Content!).Children[2]).Children
        .Select(c => AutomationProperties.GetName(c)!).ToArray();

    [Fact]
    public Task Project_picker_offers_recent_projects_then_searches_reference_name_and_client() => Run(async () =>
    {
        var clients = new WorkspaceClientCreator();
        clients.CreateIndividual(_root, "Fabrice", "Romby", "FR");
        clients.CreateIndividual(_root, "Paul", "Écrin", "PE");
        var all = new WorkspaceClientReader().Load(_root);
        for (var i = 1; i <= 11; i++) new WorkspaceProjectCreator().CreateTracked(_root, all.Single(c => c.ClientCode == "FR"), 2026, $"Support {i:00}");
        new WorkspaceProjectCreator().CreateTracked(_root, all.Single(c => c.ClientCode == "PE"), 2026, "Coque téléphone");
        var projects = new WorkspaceProjectReader().Load(_root);
        var coque = projects.Single(p => p.Profile!.ProjectName == "Coque téléphone").Profile!;
        var store = new TaskStore(new WorkspaceMetadataPaths(_root));
        store.Create(WorkTask.New("Imprimer la coque", 0) with { ProjectId = coque.Id, ClientId = coque.ClientId });
        var owner = await OpenTasksPage();
        try
        {
            var add = owner.FindControl<Button>("NewTaskProjectButton")!;
            // Nothing typed: the recent projects only (the one with a task first), then « Aucun projet ».
            var recent = PickerChoices(add);
            Assert.Equal(TaskProjectPicker.Recent + 1, recent.Length);
            Assert.Contains("Coque téléphone", recent[0]);
            Assert.Equal("Projet : Aucun projet", recent[^1]);

            var search = PickerSearch(add);
            search.Text = "support"; Settle(owner);
            Assert.Equal(TaskProjectPicker.Matches + 1, PickerChoices(add).Length); // capped, still short with 200 projects
            search.Text = "ecrin"; Settle(owner); // the client's name, without its accent
            Assert.Contains("Coque téléphone", Assert.Single(PickerChoices(add).SkipLast(1)));

            // The filter only offers projects that still have something to do.
            var filter = owner.FindControl<Button>("TaskProjectFilterButton")!;
            Assert.Contains("Coque téléphone · Paul Ecrin", Assert.Single(PickerChoices(filter).SkipLast(1))); // the name and the client, not a bare code
            var filterSearch = PickerSearch(filter);
            filterSearch.Text = "coque";
            Press(filterSearch, Key.Enter);
            await Until(owner, () => owner.FindControl<TextBlock>("TaskViewTitle")!.Text == "Coque téléphone");
        }
        finally { CloseInvoiceTestPanel(owner); owner.Close(); }
    });

    [Fact]
    public Task One_line_adds_a_task_for_today_and_the_sheet_groups_it_by_date() => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root, "Fabrice", "Romby", "FR");
        new WorkspaceProjectCreator().CreateTracked(_root, new WorkspaceClientReader().Load(_root).Single(), 2026, "Pièce vélo");
        var project = new WorkspaceProjectReader().Load(_root).Single().Profile!;
        var store = new TaskStore(new WorkspaceMetadataPaths(_root));
        var today = DateOnly.FromDateTime(DateTime.Today);
        store.Create(WorkTask.New("Envoyer le devis", 0) with { DueDate = today.AddDays(-2) });
        var owner = await OpenTasksPage();
        try
        {
            Assert.Equal("Tâches", owner.FindControl<TextBlock>("TaskViewTitle")!.Text);
            // No syntax to learn: the text is the task, dated today.
            var input = owner.FindControl<TextBox>("NewTaskBox")!;
            input.Text = "Commander PLA noir demain";
            Press(input, Key.Enter);
            await Until(owner, () => store.LoadActive().Count == 2);
            var added = store.LoadActive().Single(t => t.Title == "Commander PLA noir demain");
            Assert.Equal((today, (Guid?)null, ProjectPriority.None, (string?)null), (added.DueDate!.Value, added.ProjectId, added.Priority, added.List));
            Assert.Equal("", input.Text);

            // The project chip files the next task in that project.
            var search = PickerSearch(owner.FindControl<Button>("NewTaskProjectButton")!);
            search.Text = "velo"; // accents ignored
            Press(search, Key.Enter);
            input.Text = "Modéliser le support";
            await (Task)Invoke(owner, "AddTaskAsync")!;
            Assert.Equal(project.Id, store.LoadActive().Single(t => t.Title == "Modéliser le support").ProjectId);
            Settle(owner);

            // One block per project, its name first and the client under it; the tasks without project come last.
            var headings = TaskListOf(owner).GetVisualDescendants().OfType<Button>().Select(AutomationProperties.GetName).Where(n => n!.StartsWith("Replier ")).ToArray();
            Assert.Equal(["Replier Pièce vélo", "Replier Sans projet"], headings);
            Assert.Contains(TaskListOf(owner).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Fabrice Romby · FR-2026-001");
            foreach (var width in new[] { 1100d, 1440d }) { owner.Width = width; await Task.Delay(280); CaptureDraftUi(owner, "tasks-page-" + width); }

            // Ticking strikes it through where it is, like on paper; it only joins « Terminées » the next day.
            Named<CheckBox>(TaskListOf(owner), "Terminer : Envoyer le devis").IsChecked = true;
            await Until(owner, () => store.LoadActive().Single(t => t.Title == "Envoyer le devis").CompletedAt is not null);
            await Until(owner, () => TaskListOf(owner).GetVisualDescendants().OfType<CheckBox>().Any(c => AutomationProperties.GetName(c) == "Rouvrir : Envoyer le devis"));
            Assert.DoesNotContain(TaskListOf(owner).Children.OfType<Button>(), b => AutomationProperties.GetName(b)!.EndsWith("Terminées"));

            // A project shown alone has a way back, by its button or Échap.
            ClickNamed(TaskListOf(owner), "Projet Fabrice Romby · Pièce vélo"); Settle(owner);
            Assert.Equal("Pièce vélo", owner.FindControl<TextBlock>("TaskViewTitle")!.Text);
            var back = owner.FindControl<Button>("TaskBackButton")!;
            Assert.True(back.IsVisible);
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(owner);
            Assert.Equal("Tâches", owner.FindControl<TextBlock>("TaskViewTitle")!.Text);
            Assert.False(back.IsVisible);
        }
        finally { CloseInvoiceTestPanel(owner); owner.Close(); }
    });

    [Fact]
    public Task Each_line_edits_in_place_flag_date_title_notes_and_removal_asks_first() => Run(async () =>
    {
        var store = new TaskStore(new WorkspaceMetadataPaths(_root));
        var today = DateOnly.FromDateTime(DateTime.Today);
        var original = store.Create(WorkTask.New("Relancer", 0) with { DueDate = today });
        var owner = await OpenTasksPage();
        try
        {
            RowButton(owner, "Important : Relancer").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(owner, () => store.Find(original.Id)!.Priority == ProjectPriority.High);

            var date = (Flyout)RowButton(owner, "Date : Relancer").Flyout!;
            ((StackPanel)((StackPanel)date.Content!).Children[0]).Children.OfType<Button>().Single(b => b.Content as string == "Demain").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(owner, () => store.Find(original.Id)!.DueDate == today.AddDays(1));
            Assert.Contains(TaskListOf(owner).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Demain");

            RowButton(owner, "Modifier : Relancer").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(owner);
            var title = Named<TextBox>(TaskListOf(owner), "Texte de la tâche");
            title.Text = "Relancer le client";
            Press(title, Key.Enter);
            await Until(owner, () => store.Find(original.Id)!.Title == "Relancer le client");
            // Earlier edits are kept: each change applies to the latest saved copy.
            Assert.Equal((ProjectPriority.High, today.AddDays(1)), (store.Find(original.Id)!.Priority, store.Find(original.Id)!.DueDate!.Value));

            var menu = (MenuFlyout)RowButton(owner, "Actions : Relancer le client").Flyout!;
            menu.Items.OfType<MenuItem>().Single(i => i.Header as string == "Ajouter une note").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Settle(owner);
            var notes = Named<TextBox>(TaskListOf(owner), "Notes : Relancer le client");
            notes.Text = "Devis 12 pièces";
            notes.Focus(); Settle(owner); owner.FindControl<TextBox>("NewTaskBox")!.Focus(); // leaving the box saves it
            await Until(owner, () => store.Find(original.Id)!.Notes == "Devis 12 pièces");

            var removing = (Task)Invoke(owner, "RemoveTaskAsync", store.Find(original.Id)!)!;
            await WaitForDialog<ConfirmRemovalWindow>(owner);
            DialogAction(owner, "Annuler"); await removing;
            Assert.Single(store.LoadActive());
            removing = (Task)Invoke(owner, "RemoveTaskAsync", store.Find(original.Id)!)!;
            await WaitForDialog<ConfirmRemovalWindow>(owner); DialogAction(owner, "Confirmer le retrait"); await removing;
            Assert.True(Assert.Single(store.LoadAll()).IsDeleted);
        }
        finally { CloseInvoiceTestPanel(owner); owner.Close(); }
    });
}
