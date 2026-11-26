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
            var projectMenu = (MenuFlyout)owner.FindControl<Button>("NewTaskProjectButton")!.Flyout!;
            projectMenu.Items.OfType<MenuItem>().Single(i => (i.Header as string)!.Contains("Pièce vélo")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            input.Text = "Modéliser le support";
            await (Task)Invoke(owner, "AddTaskAsync")!;
            Assert.Equal(project.Id, store.LoadActive().Single(t => t.Title == "Modéliser le support").ProjectId);
            Settle(owner);

            var headers = TaskListOf(owner).Children.OfType<Button>().Select(AutomationProperties.GetName).ToArray();
            Assert.Equal(["Replier En retard", "Replier Aujourd’hui"], headers);
            foreach (var width in new[] { 1100d, 1440d }) { owner.Width = width; await Task.Delay(280); CaptureDraftUi(owner, "tasks-page-" + width); }

            // Ticking moves it to « Terminées », folded by default.
            Named<CheckBox>(TaskListOf(owner), "Terminer : Envoyer le devis").IsChecked = true;
            await Until(owner, () => TaskListOf(owner).GetVisualDescendants().OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Déplier Terminées"));
            Assert.NotNull(store.LoadActive().Single(t => t.Title == "Envoyer le devis").CompletedAt);
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
            Assert.Contains(TaskListOf(owner).Children.OfType<Button>(), b => AutomationProperties.GetName(b) == "Replier Cette semaine");

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
