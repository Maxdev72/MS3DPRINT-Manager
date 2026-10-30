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
    public Task Project_tracking_adds_checks_edits_reorders_and_reopens_tasks() => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root, "Fabrice", "Romby", "FR");
        var client = new WorkspaceClientReader().Load(_root).Single();
        new WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Pièce vélo", ProjectPriority.High);
        var p = new WorkspaceProjectReader().Load(_root).Single();
        var store = new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        var profile = ProjectTasks.Add(store, p.Profile!, "Modéliser la pièce", null);
        ProjectTasks.Add(store, profile, "Envoyer le modèle au client", "Attendre son accord avant impression");
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "ShowWorkspace", "Projets"); owner.FindControl<TextBlock>("PageTitle")!.Text="Projets"; await (Task)Invoke(owner, "LoadProjectsAsync")!;
            owner.FindControl<Button>("ProjectTrackingViewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var list = owner.FindControl<StackPanel>("ProjectList")!;
            Expander Card() => list.GetVisualDescendants().OfType<Expander>().Single(e => e.Classes.Contains("project-task-group"));
            Card().IsExpanded = true;
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var input = list.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "QuickProjectTask");
            input.Text = "Livrer les pièces";
            list.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Ajouter une tâche à Pièce vélo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForProjectTaskCount(store, 3);
            await Task.Delay(100); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var first = list.GetVisualDescendants().OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "Terminer : Modéliser la pièce");
            list.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "QuickProjectTask").Text = "Brouillon à garder";
            first.IsChecked = true;
            for (var i=0;i<60 && !store.LoadAll().Single().Tasks![0].IsCompleted;i++) await Task.Delay(20);
            Assert.True(store.LoadAll().Single().Tasks![0].IsCompleted);
            await Task.Delay(100); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            Assert.Equal("Brouillon à garder",list.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name=="QuickProjectTask").Text);
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Prochaine tâche : Envoyer le modèle au client");
            foreach (var width in new[]{960d,1440d}) { owner.Width=width; await Task.Delay(280); CaptureDraftUi(owner,"project-tracking-"+width); }
            list.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Modifier : Envoyer le modèle au client").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dialog = await WaitForDialog<ProjectTaskWindow>(owner);
            var body=owner.FindControl<Border>("SidePanelBody")!;
            IdentityControl<TextBox>(body,"Texte de la tâche").Text="Faire valider le modèle";
            DialogAction(owner,"Enregistrer");
            for(var i=0;i<60 && store.LoadAll().Single().Tasks![1].Title!="Faire valider le modèle";i++) await Task.Delay(20);
            Assert.Equal("Faire valider le modèle",store.LoadAll().Single().Tasks![1].Title);
            Assert.Equal("Attendre son accord avant impression",store.LoadAll().Single().Tasks![1].Notes);
            await Task.Delay(100); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var moving=(Task<bool>)Invoke(owner,"MoveProjectTaskAsync",new WorkspaceProjectReader().Load(_root).Single(),store.LoadAll().Single().Tasks![2],-1)!;
            Assert.True(await moving);
            Assert.Equal("Livrer les pièces",store.LoadAll().Single().Tasks![1].Title);
            await (Task)Invoke(owner,"LoadProjectsAsync")!;
            Assert.Equal(3,store.LoadAll().Single().Tasks!.Count);
            Assert.Equal(ProjectStatus.Quote,store.LoadAll().Single().Status);
        }
        finally { CloseInvoiceTestPanel(owner); owner.Close(); }
    });

    private static async Task WaitForProjectTaskCount(ProjectProfileStore store,int count)
    {
        for(var i=0;i<100 && store.LoadAll().Single().Tasks?.Count!=count;i++) await Task.Delay(20);
        Assert.Equal(count,store.LoadAll().Single().Tasks!.Count);
    }

    [Fact]
    public Task Project_tracking_searches_notes_hides_completed_and_confirms_removal() => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root,"Camille","Suivi","CS");
        var client=new WorkspaceClientReader().Load(_root).Single();
        new WorkspaceProjectCreator().CreateTracked(_root,client,2026,"Support",ProjectPriority.Medium);
        new WorkspaceProjectCreator().CreateTracked(_root,client,2026,"Autre projet");
        var store=new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        var profile=ProjectTasks.Add(store,store.LoadAll().Single(p=>p.ProjectName=="Support"),"Relancer","Prévoir mardi");
        profile=ProjectTasks.Add(store,profile,"Déjà fait",null);
        ProjectTasks.SetCompleted(store,profile,profile.Tasks![1].Id,true);
        var owner=await Open();owner.Show();
        try
        {
            Invoke(owner,"ShowWorkspace","Projets");await (Task)Invoke(owner,"LoadProjectsAsync")!;
            owner.FindControl<Button>("ProjectTrackingViewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            owner.FindControl<TextBox>("ProjectFilterBox")!.Text="mardi";
            Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            var list=owner.FindControl<StackPanel>("ProjectList")!;
            list.GetVisualDescendants().OfType<Expander>().Single().IsExpanded=true;
            Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            list.GetVisualDescendants().OfType<CheckBox>().Single(c=>c.Content as string=="Masquer les tâches terminées").IsChecked=true;
            Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            Assert.DoesNotContain(list.GetVisualDescendants().OfType<Button>(),b=>AutomationProperties.GetName(b)=="Modifier : Déjà fait");
            var menu=list.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Actions de la tâche : Relancer");
            var flyout=Assert.IsType<Flyout>(menu.Tag);
            var remove=((StackPanel)((Border)flyout.Content!).Child!).Children.OfType<Button>().Single(b=>b.Content is StackPanel content && content.Children.OfType<TextBlock>().Any(t=>t.Text=="Retirer la tâche"));
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ConfirmRemovalWindow>(owner);
            Assert.Equal(2,store.LoadAll().Single(p=>p.ProjectName=="Support").Tasks!.Count);
            DialogAction(owner,"Annuler");await Task.Delay(50);
            Assert.Equal(2,store.LoadAll().Single(p=>p.ProjectName=="Support").Tasks!.Count);
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ConfirmRemovalWindow>(owner);DialogAction(owner,"Confirmer le retrait");
            for(var i=0;i<100 && store.LoadAll().Single(p=>p.ProjectName=="Support").Tasks!.Count!=1;i++)await Task.Delay(20);
            Assert.Single(store.LoadAll().Single(p=>p.ProjectName=="Support").Tasks!);
        }
        finally { CloseInvoiceTestPanel(owner);owner.Close(); }
    });

    [Fact]
    public Task Task_editor_keeps_the_draft_when_another_edit_changes_the_project() => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root,"Camille","Conflit","CC");
        var client=new WorkspaceClientReader().Load(_root).Single();
        new WorkspaceProjectCreator().CreateTracked(_root,client,2026,"Support");
        var store=new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        var original=ProjectTasks.Add(store,store.LoadAll().Single(),"Relancer",null);
        var owner=await Open();owner.Show();
        try
        {
            Invoke(owner,"ShowWorkspace","Projets");await (Task)Invoke(owner,"LoadProjectsAsync")!;
            owner.FindControl<Button>("ProjectTrackingViewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var list=owner.FindControl<StackPanel>("ProjectList")!;list.GetVisualDescendants().OfType<Expander>().Single().IsExpanded=true;
            Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            list.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Modifier : Relancer").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ProjectTaskWindow>(owner);
            var body=owner.FindControl<Border>("SidePanelBody")!;
            IdentityControl<TextBox>(body,"Texte de la tâche").Text="Relancer vendredi";
            store.Update(original with { Notes="Autre modification" },original.UpdatedAt);
            DialogAction(owner,"Enregistrer");await Task.Delay(180);
            Assert.Equal("Relancer vendredi",IdentityControl<TextBox>(body,"Texte de la tâche").Text);
            Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(),t=>t.IsVisible && t.Text?.Contains("modifiée depuis son ouverture")==true);
            Assert.Equal("Relancer",store.LoadAll().Single().Tasks![0].Title);
            Assert.Equal("Autre modification",store.LoadAll().Single().Notes);
            DialogAction(owner,"Annuler");
        }
        finally { CloseInvoiceTestPanel(owner);owner.Close(); }
    });
}
