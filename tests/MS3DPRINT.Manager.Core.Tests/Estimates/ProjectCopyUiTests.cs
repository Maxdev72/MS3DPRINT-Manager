using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Desktop;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Project_copy_menu_copies_each_value_to_the_clipboard_from_the_sheet() => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root, "Camille", "Copie", "CC");
        var client = new WorkspaceClientReader().Load(_root).Single();
        new WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Pièce spéciale");
        var project = new WorkspaceProjectReader().Load(_root).Single();
        var owner = await Open(); owner.Show();
        try
        {
            var clipboard = Assert.IsAssignableFrom<IClipboard>(owner.Clipboard);
            var sheet = Assert.IsType<Border>(Invoke(owner, "BuildProjectOverview", project));
            var copy = Assert.Single(sheet.GetVisualDescendants().OfType<Button>()
                .Where(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Copier les informations du projet"));
            var flyout = Assert.IsType<Flyout>(copy.Tag);
            var menu = Assert.IsAssignableFrom<Control>(flyout.Content);
            var buttons = menu.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Equal(4, buttons.Length);
            var expected = new[] { project.Title(), project.Reference, project.Title() + " — " + project.Reference, project.ProjectPath };
            for (var index = 0; index < buttons.Length; index++)
            {
                await clipboard.SetTextAsync("à remplacer");
                buttons[index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var attempt = 0; attempt < 50 && await clipboard.TryGetTextAsync() != expected[index]; attempt++)
                { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
                Assert.Equal(expected[index], await clipboard.TryGetTextAsync());
            }
            Invoke(owner, "ShowWorkspace", "Projets");
            await (Task)Invoke(owner, "LoadProjectsAsync")!;
            var listMenu = owner.FindControl<StackPanel>("ProjectList")!.GetVisualDescendants().OfType<Button>()
                .Where(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Actions")
                .Select(b => b.Tag).OfType<Flyout>().First();
            foreach (var button in buttons)
            {
                var label = Avalonia.Automation.AutomationProperties.GetName(button);
                Assert.Contains(((Control)listMenu.Content!).GetVisualDescendants().OfType<Button>(),
                    b => Avalonia.Automation.AutomationProperties.GetName(b) == label);
            }
            var projectList = owner.FindControl<StackPanel>("ProjectList")!;
            projectList.Children.Clear(); projectList.Children.Add(sheet);
            foreach (var width in new[] { 960d, 1400d })
            {
                owner.Width = width; owner.UpdateLayout();
                copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(180); Dispatcher.UIThread.RunJobs();
                CaptureDraftUi(owner, "project-copy-menu-" + width);
                flyout.Hide();
            }
        }
        finally { owner.Close(); }
    });
}
