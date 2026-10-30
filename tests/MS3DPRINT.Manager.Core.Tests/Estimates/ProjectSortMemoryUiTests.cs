using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Desktop;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Theory]
    [InlineData(4, true)]
    [InlineData(4, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public Task Project_sort_is_saved_on_selection_and_restored_after_reopening(int column, bool ascending) => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root, "Camille", "Tri", "CT");
        var client = new WorkspaceClientReader().Load(_root).Single();
        new WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Zèbre", priority: ProjectPriority.High);
        new WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Alpha", priority: ProjectPriority.Low);
        var owner = await Open(); owner.Show();
        string[] expected;
        Button SortButton(MainWindow window) => column == 4 ? Field<Button>(window, "_projectPrioritySortButton")
            : window.FindControl<StackPanel>("ProjectList")!.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("sort-head")).ElementAt(column);
        string[] Order(MainWindow window) => window.FindControl<StackPanel>("ProjectList")!.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("table-row")).Select(b => b.Tag).OfType<ProjectSummary>().Select(p => p.Reference).ToArray();
        try
        {
            Invoke(owner, "ShowWorkspace", "Projets"); await (Task)Invoke(owner, "LoadProjectsAsync")!;
            SortButton(owner).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!ascending) SortButton(owner).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            // The preference must already be on disk, before closing the window.
            var path = Path.Combine(_root, "window.json"); Assert.True(File.Exists(path));
            using var saved = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(column, saved.RootElement.GetProperty("ProjectSortColumn").GetInt32());
            Assert.Equal(ascending, saved.RootElement.GetProperty("ProjectSortAscending").GetBoolean());
            expected = Order(owner); Assert.Equal(2, expected.Length);
            Invoke(owner, "ShowWorkspace", "Accueil");
            Invoke(owner, "ShowWorkspace", "Projets"); await (Task)Invoke(owner, "LoadProjectsAsync")!;
            Assert.Equal(expected, Order(owner));
        }
        finally { owner.Close(); }
        var reopened = await Open(); reopened.Show();
        try
        {
            Invoke(reopened, "ShowWorkspace", "Projets"); await (Task)Invoke(reopened, "LoadProjectsAsync")!;
            Assert.Equal(column, Field<int>(reopened, "_projectSortColumn"));
            Assert.Equal(ascending, Field<bool>(reopened, "_projectSortAscending"));
            Assert.Equal(expected, Order(reopened));
            if (column == 4) Assert.Contains("selected", SortButton(reopened).Classes);
            else Assert.Contains("active", SortButton(reopened).Classes);
        }
        finally { reopened.Close(); }
    });

    [Theory]
    [InlineData("")]
    [InlineData(",\"ProjectSortColumn\":99,\"ProjectSortAscending\":false")]
    public Task Project_sort_defaults_remain_usable_with_old_or_invalid_preferences(string extra) => Run(async () =>
    {
        File.WriteAllText(Path.Combine(_root, "window.json"), "{\"Width\":1200,\"Height\":760,\"Maximized\":false" + extra + "}");
        var owner = await Open();
        try
        {
            Assert.Equal(-1, Field<int>(owner, "_projectSortColumn"));
            Assert.True(Field<bool>(owner, "_projectSortAscending"));
        }
        finally { owner.Close(); }
    });
}
