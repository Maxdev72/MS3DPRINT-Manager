using Avalonia.Automation;
using Avalonia.Controls;
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
        Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
        return owner;
    }

    private static async Task<ConfirmRemovalWindow> WaitForOwnedConfirmation(MainWindow owner)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (owner.OwnedWindows.OfType<ConfirmRemovalWindow>().FirstOrDefault() is { } dialog) return dialog;
            await Task.Delay(10);
        }
        throw new TimeoutException("Confirmation attendue : ConfirmRemovalWindow");
    }

    private static void Settle(MainWindow owner) { Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); }
    private static StackPanel TaskListOf(MainWindow owner) => owner.FindControl<StackPanel>("TaskList")!;
    private static Button TaskView(MainWindow owner, string label) => owner.FindControl<StackPanel>("TaskViewList")!.Children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == label);

    [Fact]
    public Task Quick_add_reads_the_text_and_files_the_task_in_the_open_view() => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root, "Fabrice", "Romby", "FR");
        new WorkspaceProjectCreator().CreateTracked(_root, new WorkspaceClientReader().Load(_root).Single(), 2026, "Pièce vélo");
        var store = new TaskStore(new WorkspaceMetadataPaths(_root));
        var owner = await OpenTasksPage();
        try
        {
            var input = owner.FindControl<TextBox>("NewTaskBox")!;
            input.Text = "Commander PLA noir demain !haute #Achats";
            await (Task)Invoke(owner, "AddTaskAsync")!;
            var task = Assert.Single(store.LoadActive());
            Assert.Equal(("Commander PLA noir", DateOnly.FromDateTime(DateTime.Today).AddDays(1), ProjectPriority.High, "Achats", (Guid?)null),
                (task.Title, task.DueDate!.Value, task.Priority, task.List, task.ProjectId));
            Assert.Equal("", input.Text);
            Settle(owner);
            // The new list shows on the left; typing there files the task in it, without date.
            TaskView(owner, "Achats").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(owner);
            input.Text = "Filament TPU";
            await (Task)Invoke(owner, "AddTaskAsync")!;
            Assert.Equal("Achats", store.LoadActive().Single(t => t.Title == "Filament TPU").List);
            // « Aujourd’hui » gives today's date to a task typed there.
            TaskView(owner, "Aujourd’hui").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(owner);
            input.Text = "Nettoyer le plateau";
            await (Task)Invoke(owner, "AddTaskAsync")!;
            Assert.Equal(DateOnly.FromDateTime(DateTime.Today), store.LoadActive().Single(t => t.Title == "Nettoyer le plateau").DueDate);
            Settle(owner);
            Assert.Contains(TaskListOf(owner).GetVisualDescendants().OfType<CheckBox>(), c => AutomationProperties.GetName(c) == "Terminer : Nettoyer le plateau");
            TaskView(owner, "Toutes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(owner);
            foreach (var width in new[] { 960d, 1440d }) { owner.Width = width; await Task.Delay(280); CaptureDraftUi(owner, "tasks-page-" + width); }

            // A checked task stays in its view, struck through under « Terminées », and is listed in the « Terminées » view.
            TaskListOf(owner).GetVisualDescendants().OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "Terminer : Filament TPU").IsChecked = true;
            // Wait for the page to show the saved state, not a fixed delay: the suite runs under load.
            for (var i = 0; i < 250 && !TaskListOf(owner).GetVisualDescendants().OfType<CheckBox>().Any(c => AutomationProperties.GetName(c) == "Rouvrir : Filament TPU"); i++) { await Task.Delay(20); Settle(owner); }
            Assert.NotNull(store.LoadActive().Single(t => t.Title == "Filament TPU").CompletedAt);
            Assert.Contains(TaskListOf(owner).GetVisualDescendants().OfType<CheckBox>(), c => AutomationProperties.GetName(c) == "Rouvrir : Filament TPU");
            Assert.Contains(TaskListOf(owner).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Terminées · 1");
            TaskView(owner, "Terminées").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(owner);
            Assert.Contains(TaskListOf(owner).GetVisualDescendants().OfType<CheckBox>(), c => AutomationProperties.GetName(c) == "Rouvrir : Filament TPU");
        }
        finally { CloseInvoiceTestPanel(owner); owner.Close(); }
    });

    [Fact]
    public Task Task_form_links_a_project_adds_steps_and_removal_asks_first() => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root, "Camille", "Suivi", "CS");
        new WorkspaceProjectCreator().CreateTracked(_root, new WorkspaceClientReader().Load(_root).Single(), 2026, "Support");
        var project = new WorkspaceProjectReader().Load(_root).Single().Profile!;
        var store = new TaskStore(new WorkspaceMetadataPaths(_root));
        store.Create(WorkTask.New("Relancer", 0));
        var owner = await OpenTasksPage();
        try
        {
            TaskListOf(owner).GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Modifier : Relancer").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<TaskWindow>(owner);
            var body = owner.FindControl<Border>("SidePanelBody")!;
            IdentityControl<TextBox>(body, "Tâche").Text = "Relancer le client";
            IdentityControl<ComboBox>(body, "Projet").SelectedIndex = 1;
            Assert.False(IdentityControl<ComboBox>(body, "Client").IsEnabled);
            var step = IdentityControl<TextBox>(body, "Nouvelle sous-tâche");
            step.Text = "Envoyer le devis";
            step.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
            DialogAction(owner, "Enregistrer");
            for (var i = 0; i < 100 && store.LoadActive().SingleOrDefault()?.ProjectId is null; i++) await Task.Delay(20);
            var saved = store.LoadActive().Single();
            Assert.Equal(("Relancer le client", (Guid?)project.Id, (Guid?)project.ClientId), (saved.Title, saved.ProjectId, saved.ClientId));
            Assert.Equal("Envoyer le devis", Assert.Single(saved.Steps!).Title);
            await Task.Delay(80); Settle(owner);
            // The project now has its own view on the left.
            TaskView(owner, "Support").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(owner);

            TaskListOf(owner).GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Modifier : Relancer le client").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<TaskWindow>(owner);
            body = owner.FindControl<Border>("SidePanelBody")!;
            body.GetVisualDescendants().OfType<Button>().Single(b => b.Content is StackPanel c && c.Children.OfType<TextBlock>().Any(t => t.Text == "Retirer")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            // The task form is already in the side panel: the confirmation opens as its own window.
            (await WaitForOwnedConfirmation(owner)).Close(false); await Task.Delay(50);
            Assert.Single(store.LoadActive());
            body.GetVisualDescendants().OfType<Button>().Single(b => b.Content is StackPanel c && c.Children.OfType<TextBlock>().Any(t => t.Text == "Retirer")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            (await WaitForOwnedConfirmation(owner)).Close(true);
            for (var i = 0; i < 100 && store.LoadActive().Count != 0; i++) await Task.Delay(20);
            Assert.Empty(store.LoadActive());
            Assert.True(Assert.Single(store.LoadAll()).IsDeleted);
        }
        finally { CloseInvoiceTestPanel(owner); owner.Close(); }
    });

    [Fact]
    public Task Task_form_keeps_the_draft_when_the_task_changed_elsewhere() => Run(async () =>
    {
        var store = new TaskStore(new WorkspaceMetadataPaths(_root));
        var original = store.Create(WorkTask.New("Relancer", 0));
        var owner = await OpenTasksPage();
        try
        {
            TaskListOf(owner).GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Modifier : Relancer").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<TaskWindow>(owner);
            var body = owner.FindControl<Border>("SidePanelBody")!;
            IdentityControl<TextBox>(body, "Tâche").Text = "Relancer vendredi";
            store.Update(original with { Notes = "Autre modification" });
            DialogAction(owner, "Enregistrer"); await Task.Delay(180);
            Assert.Equal("Relancer vendredi", IdentityControl<TextBox>(body, "Tâche").Text);
            Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), t => t.IsVisible && t.Text?.Contains("modifiée ailleurs") == true);
            Assert.Equal(("Relancer", "Autre modification"), (store.Find(original.Id)!.Title, store.Find(original.Id)!.Notes));
            DialogAction(owner, "Annuler");
        }
        finally { CloseInvoiceTestPanel(owner); owner.Close(); }
    });
}
