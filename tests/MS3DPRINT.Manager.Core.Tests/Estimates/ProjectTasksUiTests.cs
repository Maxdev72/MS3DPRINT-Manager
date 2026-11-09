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

    [Fact]
    public Task Quick_add_previews_what_it_understood_and_files_the_task_in_the_open_view() => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root, "Fabrice", "Romby", "FR");
        new WorkspaceProjectCreator().CreateTracked(_root, new WorkspaceClientReader().Load(_root).Single(), 2026, "Pièce vélo");
        var project = new WorkspaceProjectReader().Load(_root).Single().Profile!;
        var store = new TaskStore(new WorkspaceMetadataPaths(_root));
        var owner = await OpenTasksPage();
        try
        {
            Assert.Equal("Ma journée", owner.FindControl<TextBlock>("TaskViewTitle")!.Text);
            var input = owner.FindControl<TextBox>("NewTaskBox")!;
            input.Text = "Commander PLA noir demain !haute #Achats @velo"; Settle(owner);
            var chips = owner.FindControl<WrapPanel>("TaskQuickPreview")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Equal(["Demain", "Haute priorité", "#Achats (nouvelle liste)", "Pièce vélo"], chips);
            await (Task)Invoke(owner, "AddTaskAsync")!;
            var task = Assert.Single(store.LoadActive());
            Assert.Equal(("Commander PLA noir", DateOnly.FromDateTime(DateTime.Today).AddDays(1), ProjectPriority.High, "Achats", (Guid?)project.Id),
                (task.Title, task.DueDate!.Value, task.Priority, task.List, task.ProjectId));
            Assert.Equal("", input.Text);

            // An unknown project is refused and the text stays to be corrected.
            input.Text = "Relancer @inconnu";
            await (Task)Invoke(owner, "AddTaskAsync")!;
            Assert.Single(store.LoadActive());
            Assert.Equal("Relancer @inconnu", input.Text);

            // « Ma journée » gives today's date; a list view files the task in it.
            input.Text = "Nettoyer le plateau";
            await (Task)Invoke(owner, "AddTaskAsync")!;
            Assert.Equal(DateOnly.FromDateTime(DateTime.Today), store.LoadActive().Single(t => t.Title == "Nettoyer le plateau").DueDate);
            Settle(owner);
            TaskView(owner, "Achats").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(owner);
            input.Text = "Filament TPU";
            await (Task)Invoke(owner, "AddTaskAsync")!;
            Assert.Equal("Achats", store.LoadActive().Single(t => t.Title == "Filament TPU").List);

            TaskView(owner, "Toutes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(owner);
            ClickNamed(TaskListOf(owner), "Modifier : Commander PLA noir"); Settle(owner);
            foreach (var width in new[] { 1100d, 1440d }) { owner.Width = width; await Task.Delay(280); CaptureDraftUi(owner, "tasks-page-" + width); }

            // A checked task stays in its view, under « Terminées », folded by default.
            Named<CheckBox>(TaskListOf(owner), "Terminer : Filament TPU").IsChecked = true;
            await Until(owner, () => TaskListOf(owner).GetVisualDescendants().OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Déplier Terminées"));
            Assert.NotNull(store.LoadActive().Single(t => t.Title == "Filament TPU").CompletedAt);
            ClickNamed(TaskListOf(owner), "Déplier Terminées"); Settle(owner);
            Assert.Contains(TaskListOf(owner).GetVisualDescendants().OfType<CheckBox>(), c => AutomationProperties.GetName(c) == "Rouvrir : Filament TPU");
        }
        finally { CloseInvoiceTestPanel(owner); owner.Close(); }
    });

    [Fact]
    public Task Task_detail_saves_each_field_as_it_is_edited_and_removal_asks_first() => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root, "Camille", "Suivi", "CS");
        new WorkspaceProjectCreator().CreateTracked(_root, new WorkspaceClientReader().Load(_root).Single(), 2026, "Support");
        var project = new WorkspaceProjectReader().Load(_root).Single().Profile!;
        var store = new TaskStore(new WorkspaceMetadataPaths(_root));
        var original = store.Create(WorkTask.New("Relancer", 0) with { DueDate = DateOnly.FromDateTime(DateTime.Today) });
        var owner = await OpenTasksPage();
        try
        {
            ClickNamed(TaskListOf(owner), "Modifier : Relancer"); Settle(owner);
            Assert.True(owner.FindControl<Border>("TaskDetailHost")!.IsVisible);

            var title = Named<TextBox>(TaskDetailOf(owner), "Tâche");
            title.Text = "Relancer le client";
            Press(title, Key.Enter);
            await Until(owner, () => store.Find(original.Id)!.Title == "Relancer le client");

            TaskDetailOf(owner).GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Demain").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(owner, () => store.Find(original.Id)!.DueDate == DateOnly.FromDateTime(DateTime.Today).AddDays(1));

            Named<ComboBox>(TaskDetailOf(owner), "Projet").SelectedIndex = 1;
            await Until(owner, () => store.Find(original.Id)!.ProjectId == project.Id);
            Assert.Equal(project.ClientId, store.Find(original.Id)!.ClientId);
            await Until(owner, () => !Named<ComboBox>(TaskDetailOf(owner), "Client").IsEnabled);

            var step = Named<TextBox>(TaskDetailOf(owner), "Nouvelle sous-tâche");
            step.Text = "Envoyer le devis";
            Press(step, Key.Enter);
            await Until(owner, () => store.Find(original.Id)!.Steps?.Count == 1);
            // Earlier edits are kept: each change applies to the latest saved copy.
            Assert.Equal(("Relancer le client", "Envoyer le devis"), (store.Find(original.Id)!.Title, store.Find(original.Id)!.Steps![0].Title));

            await Until(owner, () => TaskDetailOf(owner).GetVisualDescendants().OfType<Button>().Any(b => b.Content is StackPanel c && c.Children.OfType<TextBlock>().Any(t => t.Text == "Retirer la tâche")));
            RemoveTaskButton(owner).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ConfirmRemovalWindow>(owner);
            DialogAction(owner, "Annuler"); await Task.Delay(50);
            Assert.Single(store.LoadActive());
            RemoveTaskButton(owner).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ConfirmRemovalWindow>(owner); DialogAction(owner, "Confirmer le retrait");
            await Until(owner, () => store.LoadActive().Count == 0);
            Assert.True(Assert.Single(store.LoadAll()).IsDeleted);
            await Until(owner, () => !owner.FindControl<Border>("TaskDetailHost")!.IsVisible);
        }
        finally { CloseInvoiceTestPanel(owner); owner.Close(); }
    });

    [Fact]
    public Task Keyboard_moves_checks_and_asks_before_removing() => Run(async () =>
    {
        var store = new TaskStore(new WorkspaceMetadataPaths(_root));
        var today = DateOnly.FromDateTime(DateTime.Today);
        store.Create(WorkTask.New("Premier", 0) with { DueDate = today, Priority = ProjectPriority.High });
        var second = store.Create(WorkTask.New("Second", 1) with { DueDate = today });
        var owner = await OpenTasksPage();
        try
        {
            Press(TaskListOf(owner), Key.Down); Settle(owner);
            Press(TaskListOf(owner), Key.Down); Settle(owner);
            Assert.Equal("Second", Named<TextBox>(TaskDetailOf(owner), "Tâche").Text);
            Press(TaskListOf(owner), Key.Space);
            await Until(owner, () => store.Find(second.Id)!.IsCompleted);
            Press(TaskListOf(owner), Key.Delete);
            await WaitForDialog<ConfirmRemovalWindow>(owner); DialogAction(owner, "Annuler"); await Task.Delay(50);
            Assert.Equal(2, store.LoadActive().Count);
            Press(TaskListOf(owner), Key.Escape); Settle(owner);
            Assert.False(owner.FindControl<Border>("TaskDetailHost")!.IsVisible);
        }
        finally { CloseInvoiceTestPanel(owner); owner.Close(); }
    });

    [Fact]
    public Task Lists_can_be_created_empty_and_removed_without_losing_their_tasks() => Run(async () =>
    {
        var store = new TaskStore(new WorkspaceMetadataPaths(_root));
        var lists = new TaskListStore(new WorkspaceMetadataPaths(_root));
        store.Create(WorkTask.New("Graisser", 0) with { List = "Entretien" });
        var owner = await OpenTasksPage();
        try
        {
            ClickNamed(TaskViewsOf(owner), "Nouvelle liste"); Settle(owner);
            var name = Named<TextBox>(TaskViewsOf(owner), "Nom de la nouvelle liste");
            name.Text = "Salons";
            Press(name, Key.Enter);
            await Until(owner, () => lists.ActiveNames().Contains("Salons"));
            await Until(owner, () => owner.FindControl<TextBlock>("TaskViewTitle")!.Text == "Salons");

            var removing = (Task)Invoke(owner, "RemoveTaskListAsync", "Entretien")!;
            await WaitForDialog<ConfirmRemovalWindow>(owner);
            DialogAction(owner, "Confirmer le retrait");
            await removing;
            Assert.Null(store.LoadActive().Single().List);
            Assert.Equal("Graisser", store.LoadActive().Single().Title);
            Assert.Equal(["Salons"], lists.ActiveNames());
        }
        finally { CloseInvoiceTestPanel(owner); owner.Close(); }
    });
}
