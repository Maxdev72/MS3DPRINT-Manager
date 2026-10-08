using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.App.Views;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Search;
using CollectionView = MS3DPRINT.Manager.App.Views.CollectionView;

namespace MS3DPRINT.Manager.Core.Tests.Views;

[Collection("Responsive layout UI")]
public sealed class CompactTablesTests
{
    [Fact]
    public void ClientAndProjectRowsRenderReadableBusinessLabels()
    {
        ThemeTestResources.RunSta(() =>
        {
            foreach (var kind in new[] { "clients", "projects" })
            {
                var (page, name, row, _) = Fixture(kind);
                Assert.IsType<ListView>(page.FindName(name)).ItemsSource = new[] { row };
                page.Measure(new Size(1100, 600));
                page.Arrange(new Rect(0, 0, 1100, 600));
                page.UpdateLayout();
                var texts = Descendants(page).OfType<TextBlock>().Select(text => text.Text).ToArray();
                Assert.Contains(kind == "clients" ? "Fiche à compléter" : "À compléter", texts);
                Assert.DoesNotContain("True", texts);
                Assert.DoesNotContain("False", texts);
            }
        });
    }

    [Theory]
    [InlineData("clients")]
    [InlineData("projects")]
    [InlineData("collections")]
    [InlineData("search")]
    public void SelectionDoesNotOpen_ExplicitButtonAndEnterOpenSelectedRow(string kind)
    {
        ThemeTestResources.RunSta(() =>
        {
            var (page, name, row, subscribe) = Fixture(kind);
            var list = Assert.IsType<ListView>(page.FindName(name));
            var opens = 0;
            subscribe(() => opens++);
            list.ItemsSource = new[] { row };
            list.SelectedItem = row;
            Assert.Equal(0, opens);
            Assert.Same(row, list.SelectedItem);
            var button = Assert.IsType<Button>(page.FindName("OpenButton"));
            Assert.True(button.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(1, opens);
            using var source = new HwndSource(new HwndSourceParameters("Table keyboard fixture") { Width = 1, Height = 1, WindowStyle = 0 });
            list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.Equal(2, opens);
            Assert.Same(row, list.SelectedItem);
            page.Measure(new Size(900, 650));
            page.Arrange(new Rect(0, 0, 900, 650));
            page.UpdateLayout();
            list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
            Assert.Equal(2, opens);
            var container = Assert.IsType<ListViewItem>(list.ItemContainerGenerator.ContainerFromItem(row));
            list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = container });
            Assert.Equal(3, opens);
        });
    }

    [Theory]
    [InlineData("clients")]
    [InlineData("projects")]
    [InlineData("collections")]
    public void LegacySelectionEnablesOpenEditTrash_AndExplainsUnavailableIdentityActions(string kind)
    {
        ThemeTestResources.RunSta(() =>
        {
            var (page, name, row, _) = Fixture(kind);
            var list = Assert.IsType<ListView>(page.FindName(name));
            var open = Assert.IsType<Button>(page.FindName("OpenButton"));
            Assert.False(open.IsEnabled);
            list.ItemsSource = new[] { row };
            list.SelectedItem = row;
            Assert.True(open.IsEnabled);
            Assert.True(Assert.IsType<Button>(page.FindName("EditButton")).IsEnabled);
            Assert.True(Assert.IsType<Button>(page.FindName("TrashButton")).IsEnabled);
            foreach (var action in new[] { "RenameButton", "MoveButton" })
            {
                var button = Assert.IsType<Button>(page.FindName(action));
                Assert.Equal(kind == "collections", button.IsEnabled);
                if (!button.IsEnabled) Assert.NotNull(button.ToolTip);
            }
        });
    }

    [Fact]
    public void HeaderClicksSortNumbersNumerically_ThenReverseAndKeepSelection()
    {
        ThemeTestResources.RunSta(() =>
        {
            var helper = typeof(ClientsView).Assembly.GetType("MS3DPRINT.Manager.App.Controls.CompactTable");
            Assert.NotNull(helper);
            var list = new ListView();
            var grid = new GridView();
            grid.Columns.Add(new GridViewColumn { Header = "Projets", Width = 160, DisplayMemberBinding = new Binding("ProjectCount") });
            list.View = grid;
            var a = new ClientSummary("A", "A", "A", "A", null, null, 10);
            var b = new ClientSummary("B", "B", "B", "B", null, null, 2);
            list.ItemsSource = new[] { a, b };
            helper.GetMethod("Configure", new[] { typeof(ListView) })!.Invoke(null, new object[] { list });
            list.SelectedItem = a;
            list.Measure(new Size(300, 180));
            list.Arrange(new Rect(0, 0, 300, 180));
            list.UpdateLayout();
            var header = Descendants(list).OfType<GridViewColumnHeader>().Single(h => h.Column == grid.Columns[0]);
            header.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(new[] { 2, 10 }, list.Items.Cast<ClientSummary>().Select(item => item.ProjectCount));
            header.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(new[] { 10, 2 }, list.Items.Cast<ClientSummary>().Select(item => item.ProjectCount));
            Assert.Same(a, list.SelectedItem);
            list.UpdateLayout();
            var container = Assert.IsType<ListViewItem>(list.ItemContainerGenerator.ContainerFromItem(a));
            Assert.InRange(container.ActualHeight, 36, 40);
            grid.Columns[0].Width = 420;
            list.UpdateLayout();
            Assert.Equal(420, grid.Columns[0].ActualWidth);
            Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetHorizontalScrollBarVisibility(list));
        });
    }

    [Fact]
    public void FilamentAndTrashTablesKeepSelectionForTheirExistingActions()
    {
        ThemeTestResources.RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ms3d-table-" + Guid.NewGuid().ToString("N"));
            var filament = new FilamentsView(root);
            var list = Assert.IsType<ListView>(filament.FindName("FilamentsList"));
            Assert.IsType<GridView>(list.View);
            var now = DateTimeOffset.UtcNow;
            var row = new MS3DPRINT.Manager.Core.Filaments.FilamentProfile(Guid.NewGuid(), "Acme", "Test", "PLA", 24.991m, false, now, now);
            ((FilamentsViewModel)filament.DataContext).ApplyProfiles(new[] { row });
            filament.Measure(new Size(1000, 600));
            filament.Arrange(new Rect(0, 0, 1000, 600));
            filament.UpdateLayout();
            list.SetCurrentValue(Selector.SelectedItemProperty, row);
            Assert.Same(row, ((FilamentsViewModel)filament.DataContext).SelectedFilament);
            var trash = new TrashView(root);
            var trashList = Assert.IsType<ListView>(trash.FindName("TrashList"));
            Assert.IsType<GridView>(trashList.View);
            var restore = Assert.IsType<Button>(trash.FindName("RestoreButton"));
            Assert.False(restore.IsEnabled);
            var entry = new MS3DPRINT.Manager.Core.Workspace.TrashEntry(Guid.NewGuid(), "Deleted", now);
            trashList.ItemsSource = new[] { entry };
            trashList.SelectedItem = entry;
            Assert.True(restore.IsEnabled);
        });
    }

    [Fact]
    public void FilamentSelectionBindingSurvivesSortingAndExplicitRowOpening()
    {
        ThemeTestResources.RunSta(() =>
        {
            var page = new FilamentsView(Path.Combine(Path.GetTempPath(), "ms3d-filament-selection-" + Guid.NewGuid().ToString("N")));
            var now = DateTimeOffset.UtcNow;
            var first = new MS3DPRINT.Manager.Core.Filaments.FilamentProfile(Guid.NewGuid(), "A", "First", "PLA", 10m, false, now, now);
            var second = first with { Id = Guid.NewGuid(), Brand = "B", Name = "Second" };
            var model = (FilamentsViewModel)page.DataContext;
            model.ApplyProfiles(new[] { first, second });
            page.Measure(new Size(1000, 600)); page.Arrange(new Rect(0, 0, 1000, 600)); page.UpdateLayout();
            var list = Assert.IsType<ListView>(page.FindName("FilamentsList"));
            list.SetCurrentValue(Selector.SelectedItemProperty, first);
            Assert.Same(first, model.SelectedFilament);
            var header = Descendants(list).OfType<GridViewColumnHeader>().First(h => h.Column is not null);
            header.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            list.SetCurrentValue(Selector.SelectedItemProperty, second);
            Assert.Same(second, model.SelectedFilament);
            MS3DPRINT.Manager.App.Controls.CompactTable.BindOpen(list, () => { });
            list.UpdateLayout();
            var row = Assert.IsType<ListViewItem>(list.ItemContainerGenerator.ContainerFromItem(second));
            list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = row });
            list.SetCurrentValue(Selector.SelectedItemProperty, first);
            Assert.Same(first, model.SelectedFilament);
        });
    }

    private static (UserControl Page, string List, object Row, Action<Action> Subscribe) Fixture(string kind)
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3d-table-fixture-" + Guid.NewGuid().ToString("N"));
        var clients = new ClientCatalog(new(new(root)));
        switch (kind)
        {
            case "clients":
                var clientPage = new ClientsView(new(clients, root));
                return (clientPage, "ClientsList", new ClientSummary(root, "ACME", "Acme", "ACM", null, null, 0), action => clientPage.ClientSelected += _ => action());
            case "projects":
                var projectPage = new ProjectsView(new(new(new(new(root))), root));
                return (projectPage, "ProjectsList", new ProjectSummary("ACME", root, root, "ACM-2026-001", "ACM-2026-001_TEST", null), action => projectPage.ProjectSelected += _ => action());
            case "collections":
                var collectionPage = new CollectionView(new(new(), root, "06_FOURNISSEURS", "Fournisseurs", "Documents"));
                return (collectionPage, "ItemsList", new CollectionItemSummary("Acme", root, DateTimeOffset.UtcNow), action => collectionPage.ItemSelected += _ => action());
            default:
                var searchPage = new SearchView(new(clients, new(new(new(root)))), root);
                return (searchPage, "ResultsList", new GlobalSearchResult(GlobalSearchResultKind.File, "Test", "Path", root), action => searchPage.ResultSelected += _ => action());
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
