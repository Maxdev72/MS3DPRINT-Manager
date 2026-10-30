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
    public Task Dashboard_next_task_opens_the_matching_tracking_and_updates_after_completion() => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root,"Fabrice","Romby","FR");
        var client=new WorkspaceClientReader().Load(_root).Single();
        new WorkspaceProjectCreator().CreateTracked(_root,client,2026,"Pièce vélo",ProjectPriority.High);
        var store=new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        var p=ProjectTasks.Add(store,store.LoadAll().Single(),"Modéliser la pièce",null);
        p=ProjectTasks.Add(store,p,"Envoyer le modèle au client pour validation avant de lancer la fabrication et vérifier les cotes de montage",null);
        p=ProjectTasks.Add(store,p,"Livrer les pièces",null);
        p=ProjectTasks.SetCompleted(store,p,p.Tasks![0].Id,true);
        store.Update(p with { DueDate=DateOnly.FromDateTime(DateTime.Today).AddDays(1) },p.UpdatedAt);
        var owner=await Open();owner.Show();
        try
        {
            await owner.PreloadAsync(force:true);
            var queue=owner.FindControl<StackPanel>("RecentProjectsList")!;
            var row=queue.Children.OfType<Button>().Single();
            var action=row.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Classes.Contains("queue-next-action"));
            Assert.Equal("Prochaine tâche : "+p.Tasks![1].Title,action.Text);
            Assert.Contains(p.Tasks![1].Title,AutomationProperties.GetName(row));
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="1 / 3 tâches");
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="Demain");
            foreach(var width in new[]{960d,1440d}) {owner.Width=width;await Task.Delay(280);CaptureDraftUi(owner,"dashboard-project-tasks-"+width);}
            // Stale list filters must not hide the project opened from the dashboard.
            owner.FindControl<TextBox>("ProjectFilterBox")!.Text="introuvable";
            Field<ComboBox>(owner,"_projectPriorityFilterBox").SelectedIndex=3;
            row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for(var i=0;i<100 && !owner.FindControl<StackPanel>("ProjectList")!.GetVisualDescendants().OfType<Expander>().Any(e=>e.Classes.Contains("project-task-group") && e.IsExpanded);i++) await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            var list=owner.FindControl<StackPanel>("ProjectList")!;
            Assert.True(list.GetVisualDescendants().OfType<Expander>().Single(e=>e.Classes.Contains("project-task-group")).IsExpanded);
            Assert.Equal(p.Reference,owner.FindControl<TextBox>("ProjectFilterBox")!.Text);
            var check=list.GetVisualDescendants().OfType<CheckBox>().Single(c=>AutomationProperties.GetName(c)=="Terminer : "+p.Tasks![1].Title);
            check.IsChecked=true;
            for(var i=0;i<100 && !store.LoadAll().Single().Tasks![1].IsCompleted;i++)await Task.Delay(20);
            Assert.True(store.LoadAll().Single().Tasks![1].IsCompleted);
            Invoke(owner,"ShowWorkspace","Accueil");await owner.PreloadAsync(force:true);
            Assert.Contains(queue.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="Prochaine tâche : Livrer les pièces");
            Assert.Equal(ProjectStatus.Quote,store.LoadAll().Single().Status);
        }
        finally {owner.Close();}
    });

    [Fact]
    public Task Dashboard_distinguishes_finished_tasks_from_projects_without_a_checklist() => Run(async () =>
    {
        var owner=await Open();owner.Show();
        try
        {
            var now=DateTimeOffset.UtcNow;
            var profile=new ProjectProfile(Guid.NewGuid(),Guid.NewGuid(),"CT","CT-2026-001","Projet","Projet test",ProjectStatus.InProgress,now,null,null,null,now);
            var allDone=profile with { Tasks=[new ProjectTask(Guid.NewGuid(),"Livrer",IsCompleted:true)] };
            ProjectSummary Summary(ProjectProfile p)=>new("Client",_root,_root,p.Reference,p.FolderName,p);
            var projects=new[]{Summary(allDone),Summary(profile with { Id=Guid.NewGuid(),Reference="CT-2026-002" })};
            var dashboard=new WorkspaceDashboard(1,2,0,2,0,projects,[],0,0){WorkQueue=projects};
            Invoke(owner,"BuildWorkQueue",dashboard);
            Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            var rows=owner.FindControl<StackPanel>("RecentProjectsList")!.Children.OfType<Button>().ToArray();
            Assert.Contains(rows[0].GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="Toutes les tâches sont terminées · vérifier le projet");
            Assert.Contains(rows[1].GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="Poursuivre la fabrication");
        }
        finally {owner.Close();}
    });
}
