using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Task_grip_drags_before_another_task_and_escape_cancels() => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root,"Camille","Ordre","CO");
        var client=new WorkspaceClientReader().Load(_root).Single();
        new WorkspaceProjectCreator().CreateTracked(_root,client,2026,"Support");
        var store=new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
        var p=store.LoadAll().Single();
        foreach(var name in new[]{"Modélisation","Livraison","Paiement"})p=ProjectTasks.Add(store,p,name,null);
        var owner=await Open();owner.Show();owner.Height=1000;
        try
        {
            Invoke(owner,"ShowWorkspace","Projets");await (Task)Invoke(owner,"LoadProjectsAsync")!;
            owner.FindControl<Button>("ProjectTrackingViewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var list=owner.FindControl<StackPanel>("ProjectList")!;list.GetVisualDescendants().OfType<Expander>().Single().IsExpanded=true;
            Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            Control Grip(string name)=>list.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Déplacer la tâche : "+name);
            Border Row(string title)=>list.GetVisualDescendants().OfType<Border>().Single(b=>b.Classes.Contains("project-task-row") && b.GetVisualDescendants().OfType<Button>().Any(x=>AutomationProperties.GetName(x)=="Modifier : "+title));
            var start=Grip("Paiement").TranslatePoint(new Point(15,15),owner)!.Value;
            var end=Row("Livraison").TranslatePoint(new Point(70,8),owner)!.Value;
            owner.MouseDown(start,MouseButton.Left);owner.MouseMove(end);Dispatcher.UIThread.RunJobs();
            Assert.Contains("drop-before",Row("Livraison").Classes);
            CaptureDraftUi(owner,"project-task-drag");
            owner.MouseUp(end,MouseButton.Left);
            for(var i=0;i<100 && store.LoadAll().Single().Tasks![1].Title!="Paiement";i++)await Task.Delay(20);
            Assert.Equal(new[]{"Modélisation","Paiement","Livraison"},store.LoadAll().Single().Tasks!.Select(t=>t.Title));
            await Task.Delay(100);Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            start=Grip("Livraison").TranslatePoint(new Point(15,15),owner)!.Value;
            end=Row("Modélisation").TranslatePoint(new Point(70,8),owner)!.Value;
            owner.MouseDown(start,MouseButton.Left);owner.MouseMove(end);
            owner.KeyPress(Key.Escape,default,PhysicalKey.Escape,null);owner.KeyRelease(Key.Escape,default,PhysicalKey.Escape,null);
            owner.MouseUp(end,MouseButton.Left);await Task.Delay(80);
            Assert.Equal("Modélisation",store.LoadAll().Single().Tasks![0].Title);
            Grip("Modélisation").Focus();
            for(var position=1;position<=2;position++)
            {
                owner.KeyPress(Key.Down,RawInputModifiers.Alt,PhysicalKey.ArrowDown,null);
                owner.KeyRelease(Key.Down,RawInputModifiers.Alt,PhysicalKey.ArrowDown,null);
                for(var i=0;i<100 && store.LoadAll().Single().Tasks![position].Title!="Modélisation";i++)await Task.Delay(20);
                await Task.Delay(80);Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
                Assert.Equal("Modélisation",store.LoadAll().Single().Tasks![position].Title);
                Assert.True(Grip("Modélisation").IsFocused);
            }
        }
        finally{owner.Close();}
    });
}
