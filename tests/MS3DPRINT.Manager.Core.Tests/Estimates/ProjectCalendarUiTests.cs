using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Desktop;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Calendar_plans_moves_and_removes_the_same_deadline_without_changing_tasks() => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root,"Fabrice","Calendrier","FC");
        var client=new WorkspaceClientReader().Load(_root).Single();
        new WorkspaceProjectCreator().CreateTracked(_root,client,2026,"Pièce vélo",ProjectPriority.High);
        var store=new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        var p=ProjectTasks.Add(store,store.LoadAll().Single(),"Faire valider",null);
        var owner=await Open();owner.Show();
        try
        {
            Invoke(owner,"ShowWorkspace","Projets");await (Task)Invoke(owner,"LoadProjectsAsync")!;
            owner.FindControl<Button>("ProjectCalendarViewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            var list=owner.FindControl<StackPanel>("ProjectList")!;
            Button Action(string name)=>list.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)==name);
            Assert.Equal(42,list.GetVisualDescendants().OfType<Button>().Count(b=>b.Classes.Contains("project-calendar-day")));
            Action("Planifier : Pièce vélo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ProjectDeadlineWindow>(owner);
            var body=owner.FindControl<Border>("SidePanelBody")!;
            var calendar=body.GetVisualDescendants().OfType<Calendar>().Single();
            var day=DateTime.Today.AddDays(3);
            await Task.Delay(280);CaptureDraftUi(owner,"project-calendar-date-picker");
            calendar.SelectedDate=day;DialogAction(owner,"Enregistrer l’échéance");
            for(var i=0;i<100 && store.LoadAll().Single().DueDate!=DateOnly.FromDateTime(day);i++)await Task.Delay(20);
            Assert.Equal(DateOnly.FromDateTime(day),store.LoadAll().Single().DueDate);
            await Task.Delay(80);Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            Action("Vue à venir des échéances").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            Action("Modifier l’échéance : Pièce vélo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ProjectDeadlineWindow>(owner);
            body=owner.FindControl<Border>("SidePanelBody")!;
            calendar=body.GetVisualDescendants().OfType<Calendar>().Single();
            Assert.Equal(day,calendar.SelectedDate);
            calendar.SelectedDate=day.AddDays(4);DialogAction(owner,"Annuler");
            Assert.Equal(DateOnly.FromDateTime(day),store.LoadAll().Single().DueDate);
            Action("Modifier l’échéance : Pièce vélo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ProjectDeadlineWindow>(owner);
            body=owner.FindControl<Border>("SidePanelBody")!;
            calendar=body.GetVisualDescendants().OfType<Calendar>().Single();
            calendar.SelectedDate=day.AddDays(4);DialogAction(owner,"Enregistrer l’échéance");
            for(var i=0;i<100 && store.LoadAll().Single().DueDate!=DateOnly.FromDateTime(day.AddDays(4));i++)await Task.Delay(20);
            Assert.Equal(DateOnly.FromDateTime(day.AddDays(4)),store.LoadAll().Single().DueDate);
            await Task.Delay(80);Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            Action("Retirer l’échéance : Pièce vélo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ConfirmRemovalWindow>(owner);DialogAction(owner,"Annuler");
            Assert.NotNull(store.LoadAll().Single().DueDate);
            Action("Retirer l’échéance : Pièce vélo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ConfirmRemovalWindow>(owner);DialogAction(owner,"Confirmer le retrait");
            for(var i=0;i<100 && store.LoadAll().Single().DueDate is not null;i++)await Task.Delay(20);
            Assert.Null(store.LoadAll().Single().DueDate);
            Assert.Equal("Faire valider",Assert.Single(store.LoadAll().Single().Tasks!).Title);
            Assert.Equal(ProjectStatus.Quote,store.LoadAll().Single().Status);
        }
        finally {CloseInvoiceTestPanel(owner);owner.Close();}
    });

    [Fact]
    public Task Calendar_displays_multiple_deadlines_changes_month_and_keeps_filters() => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root,"Camille","Planning","CP");
        var client=new WorkspaceClientReader().Load(_root).Single();
        var store=new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        foreach(var name in new[]{"Pièce vélo","Support imprimante","Sans date"})new WorkspaceProjectCreator().CreateTracked(_root,client,2026,name);
        var today=DateOnly.FromDateTime(DateTime.Today);
        foreach(var p in store.LoadAll().Where(p=>p.ProjectName!="Sans date"))store.Update(p with{DueDate=today},p.UpdatedAt);
        var owner=await Open();owner.Show();
        try
        {
            Invoke(owner,"ShowWorkspace","Projets");await (Task)Invoke(owner,"LoadProjectsAsync")!;
            owner.FindControl<Button>("ProjectCalendarViewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            var list=owner.FindControl<StackPanel>("ProjectList")!;
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="2 échéances");
            foreach(var width in new[]{960d,1440d}){owner.Width=width;await Task.Delay(280);CaptureDraftUi(owner,"project-calendar-"+width);}
            list.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Mois suivant").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(today.AddMonths(1).Month,Field<DateOnly>(owner,"_projectCalendarMonth").Month);
            owner.FindControl<TextBox>("ProjectFilterBox")!.Text="Support";Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            list.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Vue à venir des échéances").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            Assert.DoesNotContain(list.GetVisualDescendants().OfType<Button>(),b=>AutomationProperties.GetName(b)=="Ouvrir le projet : Pièce vélo");
            Assert.Contains(list.GetVisualDescendants().OfType<Button>(),b=>AutomationProperties.GetName(b)=="Ouvrir le projet : Support imprimante");
        }
        finally{owner.Close();}
    });

    [Fact]
    public Task Calendar_keeps_the_chosen_date_when_a_concurrent_project_edit_blocks_saving() => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root,"Camille","Conflit calendrier","CC");
        var client=new WorkspaceClientReader().Load(_root).Single();
        new WorkspaceProjectCreator().CreateTracked(_root,client,2026,"Support");
        var store=new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        var owner=await Open();owner.Show();
        try
        {
            Invoke(owner,"ShowWorkspace","Projets");await (Task)Invoke(owner,"LoadProjectsAsync")!;
            owner.FindControl<Button>("ProjectCalendarViewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            var list=owner.FindControl<StackPanel>("ProjectList")!;
            list.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Planifier : Support").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ProjectDeadlineWindow>(owner);
            var body=owner.FindControl<Border>("SidePanelBody")!;
            var calendar=body.GetVisualDescendants().OfType<Calendar>().Single();
            calendar.SelectedDate=DateTime.Today.AddDays(8);
            var original=store.LoadAll().Single();
            store.Update(original with {Notes="Modifié ailleurs"},original.UpdatedAt);
            DialogAction(owner,"Enregistrer l’échéance");await Task.Delay(180);
            Assert.Equal(DateTime.Today.AddDays(8),calendar.SelectedDate);
            Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(),t=>t.IsVisible && t.Text?.Contains("modifiée depuis son ouverture")==true);
            Assert.Null(store.LoadAll().Single().DueDate);
            Assert.Equal("Modifié ailleurs",store.LoadAll().Single().Notes);
            DialogAction(owner,"Annuler");
        }
        finally{CloseInvoiceTestPanel(owner);owner.Close();}
    });
}
